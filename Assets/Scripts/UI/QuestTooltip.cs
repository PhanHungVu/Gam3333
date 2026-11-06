using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The quest read-out: the type of quest the player is in goes in the header, the state of its
// current objective in the subtext. This class is the view only; TooltipDialogue (on the Mission
// object, beside MissionTracker) reads the copy from its JSON file and calls SetContent with the two
// strings. SetQuest still works for a hand-written source that knows its QuestMissionType.
[DisallowMultipleComponent]
[DefaultExecutionOrder(380)]
public sealed class QuestTooltip : MonoBehaviour
{
    [Header("Authored references")]
    [Tooltip("Static backing plate behind the copy.")]
    [SerializeField] Image background;
    [Tooltip("Type of quest the player is in.")]
    [SerializeField] TextMeshProUGUI header;
    [Tooltip("Current objective of that quest.")]
    [SerializeField] TextMeshProUGUI subtext;
    [SerializeField] CanvasGroup group;

    public QuestMissionType MissionType { get; private set; }
    public string Objective { get; private set; } = string.Empty;
    public bool IsVisible => group != null && group.alpha > 0f;
    public Image Background => background;
    public string HeaderText => header != null ? header.text : string.Empty;
    public string Subtext => subtext != null ? subtext.text : string.Empty;

    void Awake()
    {
        if (group == null) group = GetComponent<CanvasGroup>();
    }

    // The quest the player is in: its type in the header, its current objective underneath.
    public void SetQuest(QuestMissionType missionType, string objective)
    {
        MissionType = missionType;
        if (header != null) header.text = Label(missionType);
        SetObjective(objective);
    }

    // Raw copy, used by TooltipDialogue: the header text and the objective line, both already filled in.
    public void SetContent(string headerText, string subtextText)
    {
        if (header != null) header.text = headerText ?? string.Empty;
        Objective = subtextText ?? string.Empty;
        if (subtext != null) subtext.text = Objective;
        Show();
    }

    // Same quest, next objective step: the header stays, the subtext changes.
    public void SetObjective(string objective)
    {
        Objective = objective ?? string.Empty;
        if (subtext != null) subtext.text = Objective;
        Show();
    }

    // Mission type names as the HUD prints them. Add a case when a new type is authored.
    public static string Label(QuestMissionType missionType) => missionType switch
    {
        QuestMissionType.KingOfTheHill => "KING OF THE HILL",
        _ => missionType.ToString().ToUpperInvariant(),
    };

    public void Show()
    {
        if (group != null) group.alpha = 1f;
    }

    public void Hide()
    {
        if (group != null) group.alpha = 0f;
    }
}
