using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class PreMissionFlowController : MonoBehaviour
{
    [Header("Data and gameplay handoff")]
    [SerializeField] QuestBoardCatalogue catalogue;
    [SerializeField] MissionStartHook missionStartHook;
    [SerializeField] UnityEvent exitToMenu = new UnityEvent();
    [Header("Three distinct views")]
    [SerializeField] GameObject boardView;
    [SerializeField] GameObject detailsView;
    [SerializeField] GameObject torsoView;
    [Header("Board")]
    [SerializeField] Transform noticeParent;
    [SerializeField] QuestNoticeView noticePrefab;
    [SerializeField] TMP_Text boardMessage;
    [SerializeField] Button exitButton;
    [SerializeField] Button skipPostingButton;
    [SerializeField] TMP_Text boardHint;
    [SerializeField] PreMissionPaperPresentation presentation;
    [Header("Details")]
    [SerializeField] TMP_Text detailsTitle;
    [SerializeField] TMP_Text detailsBody;
    [SerializeField] TMP_Text detailsType;
    [SerializeField] TMP_Text detailsInfo;
    [SerializeField] TMP_Text detailsReward;
    [SerializeField] Button acceptButton;
    [SerializeField] Button declineButton;
    [Header("Starting torso")]
    [SerializeField] TMP_Text torsoQuestTitle;
    [SerializeField] TMP_Text torsoName;
    [SerializeField] TMP_Text torsoDescription;
    [SerializeField] TMP_Text launchStatus;
    [SerializeField] Button previousTorsoButton;
    [SerializeField] Button nextTorsoButton;
    [SerializeField] Button confirmTorsoButton;
    [SerializeField] Button torsoBackButton;
    [SerializeField] TMP_Text torsoCounter;
    [SerializeField] StarterTorsoPreview torsoPreview;

    readonly List<QuestNoticeView> notices = new List<QuestNoticeView>();
    public QuestSelectionSession Session { get; private set; }
    public IReadOnlyList<QuestNoticeView> Notices => notices.AsReadOnly();
    public bool IsPosting => presentation.IsPosting;
    public bool IsTransitioning => presentation.IsTransitioning;
    bool exitRequested;
    bool confirmationPending;
    bool initialized;
    int skippedPostingFrame = -1;
    bool CanInteract => isActiveAndEnabled && presentation != null && presentation.isActiveAndEnabled;

    void Awake()
    {
        exitButton.onClick.AddListener(Back);
        declineButton.onClick.AddListener(Back);
        torsoBackButton.onClick.AddListener(Back);
        acceptButton.onClick.AddListener(AcceptQuest);
        previousTorsoButton.onClick.AddListener(PreviousTorso);
        nextTorsoButton.onClick.AddListener(NextTorso);
        confirmTorsoButton.onClick.AddListener(ConfirmTorso);
        if (skipPostingButton != null) skipPostingButton.onClick.AddListener(SkipPosting);
        initialized = true;
        CreateFreshBoard();
        if (!CanInteract && presentation != null) presentation.LockInput();
    }

    void OnEnable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (initialized && Session == null && CanInteract) { CreateFreshBoard(); return; }
        // Re-enabling refreshes the current view, it never generates offers.
        if (Session != null && !IsPosting && !IsTransitioning) RefreshView();
        if (!CanInteract && presentation != null) presentation.LockInput();
    }

    void OnDisable() => RecoverPresentation();

    // Used by this controller and its presenter when either component is disabled.
    public void RecoverPresentation()
    {
        if (initialized && Session == null && CanInteract) { CreateFreshBoard(); return; }
        if (Session != null && presentation != null) presentation.Settle(Session.View, Session.SelectedQuest);
        if (confirmationPending)
        {
            confirmationPending = false; Session.CancelLaunchRequest(); launchStatus.text = string.Empty;
        }
        if (Session == null || presentation == null) return;
        if (CanInteract) RefreshView(); else presentation.LockInput();
        RefreshPostingControls();
    }

    void Update()
    {
        if (!CanInteract) return;
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame && IsPosting) SkipPosting();
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Back();
    }

    public void SkipPosting()
    {
        if (!CanInteract || Session.View != PreMissionView.Board || !IsPosting || IsTransitioning) return;
        // A Submit binding or second pointer event in this frame must not turn
        // finishing the intro into an accidental quest selection.
        skippedPostingFrame = Time.frameCount;
        presentation.FinishPosting();
    }

    void RefreshPostingControls()
    {
        bool posting = Session != null && Session.View == PreMissionView.Board && IsPosting;
        if (skipPostingButton != null)
        {
            bool appeared = posting && !skipPostingButton.gameObject.activeSelf;
            skipPostingButton.gameObject.SetActive(posting);
            if (appeared && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(skipPostingButton.gameObject);
        }
        if (boardHint != null) boardHint.gameObject.SetActive(!posting);
    }

    public void CreateFreshBoard()
    {
        if (!CanInteract) return;
        presentation.Cancel();
        confirmationPending = false;
        skippedPostingFrame = -1;
        foreach (var notice in notices)
            if (notice != null) { notice.gameObject.SetActive(false); Destroy(notice.gameObject); }
        notices.Clear();
        int seed = Environment.TickCount;
        Session = new QuestSelectionSession(catalogue, seed);
        exitRequested = false;
        foreach (var quest in Session.Offers)
        {
            var notice = Instantiate(noticePrefab, noticeParent);
            notice.Bind(quest, SelectQuest);
            notices.Add(notice);
        }
        boardMessage.text = Session.Offers.Count == 0 ? "No available quests. Check the Board Catalogue."
            : "Choose any quest. Main quests and side quests are equally available.";
        launchStatus.text = string.Empty;
        RefreshView();
        Canvas.ForceUpdateCanvases();
        presentation.BeginBoard(notices, seed, RefreshView);
        RefreshPostingControls();
    }

    public void SelectQuest(QuestDefinition quest)
    {
        if (!CanInteract || IsTransitioning || Session.View != PreMissionView.Board || Time.frameCount == skippedPostingFrame) return;
        var notice = notices.Find(n => n.Quest == quest);
        if (notice == null || !notice.IsPosted) return;
        presentation.FinishPosting();
        if (!Session.SelectQuest(quest)) return;
        launchStatus.text = string.Empty;
        // Populate the briefing without disabling the board. Presentation captures
        // the visible hovered paper before handing it to the foreground proxy.
        RefreshView(false, false);
        presentation.PresentDetails(notice, RefreshView);
        var scroll = detailsBody.GetComponentInParent<ScrollRect>(true);
        if (scroll != null) { Canvas.ForceUpdateCanvases(); scroll.StopMovement(); scroll.verticalNormalizedPosition = 1f; }
    }

    public void AcceptQuest()
    {
        if (!CanInteract || IsTransitioning) return;
        if (Session.AcceptQuest()) presentation.SwitchView(PreMissionView.Details, PreMissionView.Torso, RefreshView);
    }

    public void PreviousTorso() => BrowseTorso(-1);
    public void NextTorso() => BrowseTorso(1);
    void BrowseTorso(int direction)
    {
        if (!CanInteract || IsTransitioning || Session.View != PreMissionView.Torso || Session.LaunchRequested || Session.Torsos.Count < 2) return;
        presentation.BrowseTorso(direction, () =>
        {
            Session.BrowseTorso(direction); launchStatus.text = string.Empty; RefreshView(false, false);
        }, () => RefreshView(false));
    }

    public void Back()
    {
        if (!CanInteract || IsTransitioning) return;
        var previous = Session.View;
        var notice = notices.Find(n => n.Quest == Session.SelectedQuest);
        if (Session.Back())
        {
            launchStatus.text = string.Empty;
            if (previous == PreMissionView.Details)
                presentation.ReturnToBoard(notice, () => { RefreshView(); if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(notice.Button.gameObject); });
            else presentation.SwitchView(previous, Session.View, RefreshView);
        }
        else if (!exitRequested) { exitRequested = true; exitToMenu.Invoke(); }
    }

    public void ConfirmTorso()
    {
        if (!CanInteract || IsTransitioning) return;
        if (missionStartHook == null)
        {
            launchStatus.text = "Assign a Mission Start Hook before confirming.";
            return;
        }
        if (!Session.TryRequestLaunch(out var quest, out var torso)) return;
        confirmationPending = true;
        RefreshView(false, false);
        presentation.ConfirmTorso(() => CompleteConfirmation(quest, torso));
    }

    void CompleteConfirmation(QuestDefinition quest, PartData torso)
    {
        if (!confirmationPending) return;
        confirmationPending = false;
        // No scene loading, equipping, rewards or gameplay are implemented here.
        launchStatus.text = "Selection submitted. Mission launch is not connected yet.\nBack returns to quest details.";
        RefreshView();
        try { missionStartHook.StartMission(quest, torso); }
        catch (Exception exception)
        {
            Session.CancelLaunchRequest();
            launchStatus.text = "Mission start failed. Check the Console, then try again.";
            RefreshView();
            Debug.LogException(exception, missionStartHook);
        }
    }

    void RefreshView()
    {
        RefreshView(true);
    }

    void RefreshView(bool resetFocus, bool updateViews = true)
    {
        if (updateViews) presentation.SetViews(Session.View, Session.SelectedQuest);
        RefreshPostingControls();
        var quest = Session.SelectedQuest;
        if (quest != null)
        {
            detailsTitle.text = quest.title;
            detailsType.text = (quest.category == QuestCategory.Main ? "MAIN QUEST" : "SIDE QUEST") + "  /  " + MissionTypeName(quest.missionType);
            detailsBody.text = quest.description;
            detailsReward.text = "Reward: " + quest.gniReward + " Gni";
            var body = new StringBuilder();
            body.Append("Estimated duration: ~").Append(quest.estimatedMinutes).Append(" minutes\n\nAvailable parts: ");
            var partNames = new List<string>();
            if (quest.availableParts != null)
                foreach (var part in quest.availableParts) if (part != null) partNames.Add(part.partName);
            body.Append(partNames.Count > 0 ? string.Join(", ", partNames) : "Not specified");
            var enemies = new List<string>();
            if (quest.availableEnemies != null)
                foreach (var enemy in quest.availableEnemies) if (enemy != null) enemies.Add(enemy.name);
            body.Append("\nAvailable enemies: ").Append(enemies.Count > 0 ? string.Join(", ", enemies) : "Not specified");
            detailsInfo.text = body.ToString();
            torsoQuestTitle.text = "Selected quest: " + quest.title;
        }
        var torso = Session.SelectedTorso;
        torsoName.text = torso != null ? torso.partName : "No starter torso available";
        torsoDescription.text = torso != null
            ? (string.IsNullOrWhiteSpace(torso.description) ? "The core of your assembled golem." : torso.description)
                + "\n\nHealth contribution: " + torso.GetStat(StatType.Health)
            : "No starter torsos are available.";
        torsoCounter.text = Session.Torsos.Count > 0 ? (Session.SelectedTorsoIndex + 1) + " / " + Session.Torsos.Count : "0 / 0";
        torsoPreview.Show(torso, catalogue != null ? catalogue.FindTorsoVisual(torso) : null);
        previousTorsoButton.interactable = nextTorsoButton.interactable = Session.Torsos.Count > 1 && !Session.LaunchRequested;
        confirmTorsoButton.interactable = torso != null && quest != null && !Session.LaunchRequested;

        // Clear stale selection before focusing a control on the newly active screen.
        if (resetFocus && EventSystem.current != null)
        {
            var target = Session.View == PreMissionView.Board ? (notices.Count > 0 ? notices[0].Button : exitButton)
                : Session.View == PreMissionView.Details ? acceptButton : confirmTorsoButton.interactable ? confirmTorsoButton : torsoBackButton;
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(target.gameObject);
        }
    }

    void OnDestroy()
    {
        Unbind(exitButton, Back); Unbind(declineButton, Back); Unbind(torsoBackButton, Back);
        Unbind(acceptButton, AcceptQuest); Unbind(previousTorsoButton, PreviousTorso); Unbind(nextTorsoButton, NextTorso);
        Unbind(confirmTorsoButton, ConfirmTorso); Unbind(skipPostingButton, SkipPosting);
    }
    static void Unbind(Button button, UnityAction action) { if (button != null) button.onClick.RemoveListener(action); }

    public static string MissionTypeName(QuestMissionType type) => type == QuestMissionType.KingOfTheHill ? "King of the Hill" : type.ToString();
}
