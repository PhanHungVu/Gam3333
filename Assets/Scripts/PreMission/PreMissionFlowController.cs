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
    [SerializeField] PreMissionPaperPresentation presentation;
    [Header("Details")]
    [SerializeField] TMP_Text detailsTitle;
    [SerializeField] TMP_Text detailsBody;
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

    void Awake()
    {
        exitButton.onClick.AddListener(Back);
        declineButton.onClick.AddListener(Back);
        torsoBackButton.onClick.AddListener(Back);
        acceptButton.onClick.AddListener(AcceptQuest);
        previousTorsoButton.onClick.AddListener(PreviousTorso);
        nextTorsoButton.onClick.AddListener(NextTorso);
        confirmTorsoButton.onClick.AddListener(ConfirmTorso);
        CreateFreshBoard();
    }

    void OnEnable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        // Re-enabling refreshes the current view, it never generates offers.
        if (Session != null && !IsPosting && !IsTransitioning) RefreshView();
    }

    void OnDisable() { if (Session != null && presentation != null) presentation.Settle(Session.View, Session.SelectedQuest); }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Back();
    }

    public void CreateFreshBoard()
    {
        presentation.Cancel();
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
    }

    public void SelectQuest(QuestDefinition quest)
    {
        if (IsTransitioning) return;
        var notice = notices.Find(n => n.Quest == quest);
        if (notice == null || !notice.IsPosted) return;
        presentation.FinishPosting();
        if (!Session.SelectQuest(quest)) return;
        launchStatus.text = string.Empty;
        RefreshView();
        var scroll = detailsBody.GetComponentInParent<ScrollRect>();
        if (scroll != null) { Canvas.ForceUpdateCanvases(); scroll.StopMovement(); scroll.verticalNormalizedPosition = 1f; }
        presentation.PresentDetails(notice, RefreshView);
    }

    public void AcceptQuest()
    {
        if (IsTransitioning) return;
        if (Session.AcceptQuest()) presentation.SwitchView(PreMissionView.Details, PreMissionView.Torso, RefreshView);
    }

    public void PreviousTorso() => BrowseTorso(-1);
    public void NextTorso() => BrowseTorso(1);
    void BrowseTorso(int direction)
    {
        if (!IsTransitioning && Session.BrowseTorso(direction)) { launchStatus.text = string.Empty; RefreshView(false); }
    }

    public void Back()
    {
        if (IsTransitioning) return;
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
        if (IsTransitioning) return;
        if (missionStartHook == null)
        {
            launchStatus.text = "Assign a Mission Start Hook before confirming.";
            return;
        }
        if (!Session.TryRequestLaunch(out var quest, out var torso)) return;
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

    void RefreshView(bool resetFocus)
    {
        presentation.SetViews(Session.View, Session.SelectedQuest);
        var quest = Session.SelectedQuest;
        if (quest != null)
        {
            detailsTitle.text = quest.title;
            var body = new StringBuilder();
            body.Append(quest.category == QuestCategory.Main ? "MAIN QUEST" : "SIDE QUEST")
                .Append("  /  ").Append(MissionTypeName(quest.missionType))
                .Append("\nEstimated duration: ~").Append(quest.estimatedMinutes).Append(" minutes")
                .Append("\nReward: ").Append(quest.gniReward).Append(" Gni\n\n")
                .Append(quest.description).Append("\n\nAvailable parts: ");
            var partNames = new List<string>();
            if (quest.availableParts != null)
                foreach (var part in quest.availableParts) if (part != null) partNames.Add(part.partName);
            body.Append(partNames.Count > 0 ? string.Join(", ", partNames) : "Not specified");
            var enemies = new List<string>();
            if (quest.availableEnemies != null)
                foreach (var enemy in quest.availableEnemies) if (enemy != null) enemies.Add(enemy.name);
            body.Append("\nAvailable enemies: ").Append(enemies.Count > 0 ? string.Join(", ", enemies) : "Not specified");
            detailsBody.text = body.ToString();
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

    public static string MissionTypeName(QuestMissionType type) => type == QuestMissionType.KingOfTheHill ? "King of the Hill" : type.ToString();
}
