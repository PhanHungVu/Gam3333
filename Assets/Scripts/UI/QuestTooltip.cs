using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The quest read-out: the type of quest the player is in goes in the header, the state of its
// current objective in the subtext. The quest system is not in the game yet, so this class is the
// view only: SetQuest(missionType, objective) is the one call a quest source makes when the player
// enters a quest or its objective moves on. The copy per objective state lives with that source,
// for example an escort quest:
//   just started        -> "Escort the convoy to the drop zone."
//   convoy delivered    -> "Convoy is through. Head to the extraction point and extract."
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

    // ---- Quest source: empty until the quest system exists ----
    // Reads the type of quest the player is in and the state of its current objective, then calls
    // SetQuest(missionType, objective). Left empty on purpose: the player's active quest, its type
    // and its objective steps are not available yet.
    public void Refresh()
    {
    }
}
