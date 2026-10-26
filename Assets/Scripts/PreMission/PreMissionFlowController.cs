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
    [Tooltip("Scene-owned 3D board. Unassigned prefabs retain the legacy paper preview.")]
    [SerializeField] PreMissionStoneBoard stoneBoard;
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
    int displayedVersion = -1;
    public QuestSelectionSession Session { get; private set; }
    public IReadOnlyList<QuestNoticeView> Notices => notices.AsReadOnly();
    public bool IsPosting => presentation.IsPosting || (stoneBoard != null && stoneBoard.IsPosting);
    public bool IsTransitioning => presentation.IsTransitioning || (stoneBoard != null && stoneBoard.IsTransitioning);
    public PreMissionStoneBoard StoneBoard => stoneBoard;
    public bool CanSelectBoard => CanInteract && Session != null && Session.View == PreMissionView.Board && !IsTransitioning;
    bool exitRequested;
    bool confirmationPending;
    bool initialized;
    int skippedPostingFrame = -1;
    bool CanInteract => isActiveAndEnabled && presentation != null && presentation.isActiveAndEnabled && (stoneBoard == null || stoneBoard.IsOperational);

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
        OpenBoard(false);
        if (!CanInteract && presentation != null) presentation.LockInput();
    }

    void OnEnable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (initialized && Session == null && CanInteract) { OpenBoard(false); return; }
        // Re-enabling refreshes the current view, it never generates offers.
        if (Session != null && !IsPosting && !IsTransitioning) RefreshView();
        if (!CanInteract && presentation != null) presentation.LockInput();
    }

    void OnDisable() => RecoverPresentation();

    // Used by this controller and its presenter when either component is disabled.
    public void RecoverPresentation()
    {
        if (initialized && Session == null && CanInteract) { OpenBoard(false); return; }
        if (Session != null && presentation != null) presentation.Settle(Session.View, Session.SelectedQuest);
        if (Session != null && stoneBoard != null) stoneBoard.RecoverView(Session.View, Session.SelectedOfferId);
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
        if (Session == null || Session.Progression != QuestProgressionRuntime.Current) { OpenBoard(false); return; }
        RememberPostedOffers();
        if (displayedVersion != Session.Progression.Version && !IsTransitioning && !IsPosting
            && (Session.View == PreMissionView.Board || (Session.SelectedOfferId != null && Session.Progression.FindOffer(Session.SelectedOfferId) == null)))
            SynchronizeBoard();
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
        if (stoneBoard != null) stoneBoard.FinishArrivals();
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
        OpenBoard(true);
    }

    [ContextMenu("Development/Start New Quest Progression")]
    public void StartNewProgression()
    {
        if (!Application.isPlaying || !CanInteract) return;
        QuestProgressionRuntime.StartNewProgression(catalogue, Environment.TickCount);
        OpenBoard(false);
    }

    void OpenBoard(bool replayPosting)
    {
        if (!CanInteract) return;
        presentation.Cancel();
        presentation.ConfigureFeedback(stoneBoard!=null ? stoneBoard.GetComponent<QuestBoardImpactFeedback>() : null);
        presentation.ConfigureTabletopBriefing(stoneBoard!=null && stoneBoard.Motion!=null && stoneBoard.Motion.Miniature!=null,acceptButton,declineButton,detailsTitle);
        confirmationPending = false;
        skippedPostingFrame = -1;
        foreach (var notice in notices)
            if (notice != null) { notice.gameObject.SetActive(false); Destroy(notice.gameObject); }
        notices.Clear();
        var run = QuestProgressionRuntime.GetOrCreate(catalogue, Environment.TickCount);
        Session = new QuestSelectionSession(catalogue, run.Seed, run);
        exitRequested = false;
        noticeParent.gameObject.SetActive(stoneBoard == null);
        if (stoneBoard != null) stoneBoard.Bind(this, run);
        else foreach (var offer in run.Offers)
        {
            var notice = Instantiate(noticePrefab, noticeParent);
            notice.Bind(offer.Quest, SelectQuest, offer.Id, offer.Slot);
            notices.Add(notice);
        }
        displayedVersion = run.Version;
        RefreshProgress();
        launchStatus.text = string.Empty;
        RefreshView();
        Canvas.ForceUpdateCanvases();
        if (stoneBoard != null) presentation.BeginStoneBoard(() =>
        {
            stoneBoard.StartArrivals(BoardReady); RefreshPostingControls();
        }, replayPosting || !QuestProgressionRuntime.HasPresentedOffers);
        else
        {
            var pending = notices.FindAll(n => replayPosting || !QuestProgressionRuntime.WasPresented(n.OfferId));
            presentation.BeginBoard(notices, run.Seed, BoardReady, true, run.SmallOfferCount, pending,
                replayPosting || !QuestProgressionRuntime.HasPresentedOffers);
        }
        RefreshPostingControls();
    }

    void RememberPostedOffers()
    {
        if (Session == null) return;
        if (stoneBoard != null)
        {
            if(Session.View==PreMissionView.Board && !IsPosting && !IsTransitioning) stoneBoard.Motion?.ProgressTrack?.RememberResting();
            foreach (var stone in stoneBoard.Stones)
                if (stone.IsReady && !stone.IsMoving) QuestProgressionRuntime.MarkPresented(Session.Progression, stone.Offer.Id);
            return;
        }
        foreach (var notice in notices)
            if (notice != null && notice.IsPosted) QuestProgressionRuntime.MarkPresented(Session.Progression, notice.OfferId);
    }
    void BoardReady() { RememberPostedOffers(); RefreshView(); }
    void RefreshProgress()
    {
        var run = Session.Progression;
        boardMessage.text = "Main Quest Progress: " + Math.Min(run.SuccessfulSmallCompletions, run.RequiredSmallCompletions) + " / " + run.RequiredSmallCompletions
            + (run.BigQuest != null ? "  |  Big Quest Unlocked — choose any quest."
                : run.BigQuestUnlocked ? "  |  Big quest unlocked; no eligible main quest configured." : string.Empty)
            + (Session.Offers.Count == 0 ? "  |  No eligible small quests. Check the Board Catalogue." : string.Empty);
    }
    void SynchronizeBoard()
    {
        var run = Session.Progression;
        bool removedSelection = Session.SelectedOfferId != null && run.FindOffer(Session.SelectedOfferId) == null;
        if (removedSelection)
        {
            presentation.Settle(PreMissionView.Board, null); stoneBoard?.RecoverView(PreMissionView.Board, null); confirmationPending = false;
        }
        Session.SynchronizeOffers();
        if (stoneBoard != null)
        {
            stoneBoard.Synchronize(); displayedVersion = run.Version;
            RefreshProgress(); RefreshView(removedSelection); RememberPostedOffers();
            stoneBoard.StartArrivals(BoardReady); RefreshPostingControls();
            return;
        }
        var old = new List<QuestNoticeView>(notices);
        notices.Clear();
        var added = new List<QuestNoticeView>();
        foreach (var offer in run.Offers)
        {
            var notice = old.Find(n => n != null && n.OfferId == offer.Id);
            if (notice != null) old.Remove(notice);
            else
            {
                notice = Instantiate(noticePrefab, noticeParent);
                notice.Bind(offer.Quest, SelectQuest, offer.Id, offer.Slot);
                if (!QuestProgressionRuntime.WasPresented(offer.Id)) added.Add(notice);
            }
            notices.Add(notice);
        }
        foreach (var notice in old)
            if (notice != null) { notice.gameObject.SetActive(false); Destroy(notice.gameObject); }
        displayedVersion = run.Version;
        RefreshProgress();
        RefreshView(removedSelection);
        if (added.Count > 0 || old.Count > 0)
            presentation.BeginBoard(notices, run.Seed, BoardReady, true, run.SmallOfferCount, added, false);
        RefreshPostingControls();
    }

    public void SelectQuest(QuestDefinition quest)
    {
        if (!CanInteract || IsTransitioning || Session.View != PreMissionView.Board || Time.frameCount == skippedPostingFrame) return;
        if (stoneBoard != null)
        {
            var offer = Session.Progression.FindOffer(quest);
            if (!stoneBoard.InputEnabled || offer == null || stoneBoard.FindStone(offer.Id) == null || !Session.SelectQuest(quest)) return;
            stoneBoard.ClearHighlights();
            launchStatus.text = string.Empty;
            RefreshView(false, false);
            PresentStoneInspection(offer);
            ResetBriefingScroll();
            return;
        }
        var notice = notices.Find(n => n.Quest == quest);
        if (notice == null || !notice.IsPosted || Session.Progression.FindOffer(notice.OfferId) == null) return;
        presentation.FinishPosting();
        if (!Session.SelectQuest(quest)) return;
        launchStatus.text = string.Empty;
        // Populate the briefing without disabling the board. Presentation captures
        // the visible hovered paper before handing it to the foreground proxy.
        RefreshView(false, false);
        presentation.PresentDetails(notice, RefreshView);
        ResetBriefingScroll();
    }
    void ResetBriefingScroll()
    {
        var scroll = detailsBody.GetComponentInParent<ScrollRect>(true);
        if (scroll != null) { Canvas.ForceUpdateCanvases(); scroll.StopMovement(); scroll.verticalNormalizedPosition = 1f; }
    }
    void PresentStoneInspection(QuestOffer offer)
    {
        var transitionSession=Session;
        bool stoneReady=false, briefingReady=false, briefingStarted=false, finished=false;
        void Finish()
        {
            if(finished || !stoneReady || !briefingReady || Session!=transitionSession || Session.View!=PreMissionView.Details) return;
            finished=true; RefreshView();
        }
        void BeginBriefing()
        {
            if(briefingStarted || Session!=transitionSession) return;
            briefingStarted=true;
            presentation.PresentStoneDetails(offer.Quest.category==QuestCategory.Main,()=>{ briefingReady=true; Finish(); });
        }
        presentation.PrepareStoneInspection();
        stoneBoard.Inspect(offer.Id,progress=>
        {
            presentation.SetStoneInspectionProgress(progress);
            if(progress>=(stoneBoard.Motion!=null ? stoneBoard.Motion.BriefingRevealAt : 1f)) BeginBriefing();
        },()=>{ stoneReady=true; BeginBriefing(); Finish(); });
    }
    void ReturnStoneInspection(string offerId)
    {
        var transitionSession=Session;
        bool stoneReady=false, briefingReady=false, finished=false;
        void Finish()
        {
            if(finished || !stoneReady || !briefingReady || Session!=transitionSession || Session.View!=PreMissionView.Board) return;
            finished=true; RefreshView(); stoneBoard.FocusOffer(offerId);
        }
        presentation.ReturnFromStoneDetails(()=>{ briefingReady=true; Finish(); });
        stoneBoard.ReturnInspection(presentation.SetStoneInspectionProgress,()=>{ stoneReady=true; Finish(); });
    }

    public void AcceptQuest()
    {
        if (!CanInteract || IsTransitioning) return;
        if (Session.AcceptQuest())
        {
            stoneBoard?.Motion?.Feedback?.PlayCue(QuestBoardImpactFeedback.Cue.Accept);
            presentation.SwitchView(PreMissionView.Details, PreMissionView.Torso, RefreshView);
        }
    }

    public void PreviousTorso() => BrowseTorso(-1);
    public void NextTorso() => BrowseTorso(1);
    void BrowseTorso(int direction)
    {
        if (!CanInteract || IsTransitioning || Session.View != PreMissionView.Torso || Session.LaunchRequested || Session.Torsos.Count < 2) return;
        presentation.BrowseTorso(direction, () =>
        {
            Session.BrowseTorso(direction); launchStatus.text = string.Empty; RefreshView(false, false);
            stoneBoard?.Motion?.Feedback?.PlayCue(QuestBoardImpactFeedback.Cue.Torso);
        }, () => RefreshView(false));
    }

    public void Back()
    {
        if (!CanInteract || IsTransitioning) return;
        var previous = Session.View;
        string returningOffer = Session.SelectedOfferId;
        var notice = notices.Find(n => n.Quest == Session.SelectedQuest);
        if (Session.Back())
        {
            launchStatus.text = string.Empty;
            if (previous == PreMissionView.Details && stoneBoard != null)
                ReturnStoneInspection(returningOffer);
            else if (previous == PreMissionView.Details)
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
        stoneBoard?.Motion?.Feedback?.PlayCue(QuestBoardImpactFeedback.Cue.Confirm);
        RefreshView(false, false);
        presentation.ConfirmTorso(() => CompleteConfirmation(quest, torso));
    }

    void CompleteConfirmation(QuestDefinition quest, PartData torso)
    {
        if (!confirmationPending) return;
        if (Session.Progression != QuestProgressionRuntime.Current || Session.Progression.FindOffer(Session.SelectedOfferId) == null)
        { confirmationPending = false; OpenBoard(false); return; }
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
            if (Session.View == PreMissionView.Board && stoneBoard != null)
            {
                EventSystem.current.SetSelectedGameObject(null); stoneBoard.FocusFirst(); return;
            }
            var target = Session.View == PreMissionView.Board ? (notices.Count > 0 ? notices[0].Button : exitButton)
                : Session.View == PreMissionView.Details ? acceptButton : confirmTorsoButton.interactable ? confirmTorsoButton : torsoBackButton;
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(target.gameObject);
        }
    }

    void OnDestroy()
    {
        if (stoneBoard != null) stoneBoard.DetachOwner(this);
        Unbind(exitButton, Back); Unbind(declineButton, Back); Unbind(torsoBackButton, Back);
        Unbind(acceptButton, AcceptQuest); Unbind(previousTorsoButton, PreviousTorso); Unbind(nextTorsoButton, NextTorso);
        Unbind(confirmTorsoButton, ConfirmTorso); Unbind(skipPostingButton, SkipPosting);
    }
    static void Unbind(Button button, UnityAction action) { if (button != null) button.onClick.RemoveListener(action); }

    public static string MissionTypeName(QuestMissionType type) => type == QuestMissionType.KingOfTheHill ? "King of the Hill" : type.ToString();
}
