using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Temporary scene control. Remove its named UI object after the board review.
[DisallowMultipleComponent]
public sealed class PreMissionQuestTestButton : MonoBehaviour
{
    [SerializeField] PreMissionFlowController flow;
    [SerializeField] Button button;
    [SerializeField] TMP_Text label;
    int lastAdvanceFrame = -1, shownCount = -1, shownRequired = -1;
    bool shownUnlocked;
    public bool CanAdvance
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!isActiveAndEnabled || flow == null || flow.Session == null || !flow.CanSelectBoard
                || flow.IsPosting || flow.IsTransitioning || lastAdvanceFrame == Time.frameCount) return false;
            var run = flow.Session.Progression;
            if (run != QuestProgressionRuntime.Current || run.ActiveAttempt != null || run.BigQuestUnlocked || flow.Session.SelectedTorso == null) return false;
            foreach (var offer in run.Offers) if (offer.Quest.category == QuestCategory.Side) return true;
#endif
            return false;
        }
    }
    void Awake()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        button.onClick.AddListener(AdvanceProgress); Refresh();
#else
        gameObject.SetActive(false);
#endif
    }
    void Update() => Refresh();
    void Refresh()
    {
        if (button == null || label == null) return;
        button.interactable = CanAdvance;
        var run = flow != null && flow.Session != null ? flow.Session.Progression : null;
        int count = run != null ? Mathf.Min(run.SuccessfulSmallCompletions, run.RequiredSmallCompletions) : 0;
        int required = run != null ? run.RequiredSmallCompletions : 3;
        bool unlocked = run != null && run.BigQuestUnlocked;
        if (shownCount == count && shownRequired == required && shownUnlocked == unlocked) return;
        shownCount = count; shownRequired = required; shownUnlocked = unlocked;
        label.text = (unlocked ? "TEST: Big Quest Ready" : "TEST: +1 Quest") + " (" + count + "/" + required + ")";
    }
    public void AdvanceProgress()
    {
        if (!CanAdvance) return;
        var run = flow.Session.Progression;
        foreach (var offer in run.Offers)
        {
            if (offer.Quest.category != QuestCategory.Side) continue;
            if (QuestProgressionRuntime.TryBeginMission(offer.Quest, flow.Session.SelectedTorso, out var attempt)
                && QuestProgressionRuntime.ReportMissionResult(attempt.Id, QuestMissionOutcome.Succeeded))
            { lastAdvanceFrame = Time.frameCount; Refresh(); }
            return;
        }
    }
    void OnDestroy() { if (button != null) button.onClick.RemoveListener(AdvanceProgress); }
}
