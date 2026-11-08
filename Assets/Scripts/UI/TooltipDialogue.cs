using System;
using System.Text;
using UnityEngine;

// Drives the QuestTooltip read-out from the level's mission. Sits on the same GameObject as
// MissionTracker, reads the copy from a JSON file and pushes two strings into the tooltip:
//   header  - the type of quest the level is running (KOTH, Elimination, Escort...), read from the
//             level's Chunk_<type> objects unless a type is forced here;
//   subtext - the objective that still has to be completed, chosen by the mission state.
// Templates may use {type} {state} {done} {total} {left} {active} {timer}; see the example JSON.
[DisallowMultipleComponent]
[DefaultExecutionOrder(220)]
public sealed class TooltipDialogue : MonoBehaviour
{
    [Header("Content")]
    [Tooltip("JSON copy per quest type and mission state. Tokens: {type} {state} {done} {total} {left} {active} {timer}.")]
    [SerializeField] TextAsset dialogueFile;
    [Tooltip("Forces a quest type instead of reading it from the level's Chunk_<type> objects.")]
    [SerializeField] string questTypeOverride = string.Empty;
    [Header("Targets")]
    [Tooltip("Tooltip to drive. Found in the scene when left empty; it lives on the player's HUD canvas.")]
    [SerializeField] QuestTooltip tooltip;
    [Header("Quest type")]
    [Tooltip("Level objects named after the quest type: Chunk_KOTH gives KOTH.")]
    [SerializeField] string chunkPrefix = "Chunk_";
    [Tooltip("Chunks that are part of the level but do not name the quest type.")]
    [SerializeField] string[] ignoredChunks = { "Chunk_Start", "Chunk_End" };
    [Tooltip("Seconds to keep looking for the level's quest type while it is still being generated.")]
    [SerializeField, Min(0f)] float typeSearchSeconds = 8f;

    // The JSON shape: { "defaults": {...}, "quests": [ { "type": "KOTH", "header": "KING OF THE HILL",
    // "states": [ { "state": "InProgress", "subtext": "Zones held: {done}/{total} · timer {timer}" } ] } ] }
    [Serializable] public sealed class Entry { public string state; public string subtext; }
    [Serializable] public sealed class Quest { public string type; public string header; public Entry[] states; }
    [Serializable] public sealed class Book { public Quest defaults; public Quest[] quests; }

    MissionTracker tracker;
    Book book;
    string questType = string.Empty;
    float typeSearchUntil;
    float nextTypeSearch;
    float nextViewSearch;
    string pushedSignature;

    public string QuestType => questType;
    public string LastHeader { get; private set; } = string.Empty;
    public string LastSubtext { get; private set; } = string.Empty;

    void Awake()
    {
        tracker = GetComponent<MissionTracker>();
        Load();
    }

    void Start()
    {
        typeSearchUntil = Time.unscaledTime + typeSearchSeconds;
        if (!string.IsNullOrEmpty(questTypeOverride)) questType = questTypeOverride.Trim();
        Refresh(true);
    }

    void LateUpdate() => Refresh(false);

    // Reads the JSON copy. Called on Awake, and again whenever the designer swaps the file at runtime.
    public void Load()
    {
        book = null;
        if (dialogueFile == null)
        {
            Debug.LogWarning("Tooltip dialogue has no JSON file assigned.", this);
            return;
        }
        try
        {
            book = JsonUtility.FromJson<Book>(dialogueFile.text);
        }
        catch (Exception error)
        {
            Debug.LogError($"Tooltip dialogue {dialogueFile.name} could not be read: {error.Message}", this);
        }
        if (book != null && (book.quests == null || book.quests.Length == 0) && book.defaults == null)
        {
            Debug.LogWarning($"Tooltip dialogue {dialogueFile.name} lists no quests and no defaults.", this);
            book = null;
        }
        pushedSignature = null;
    }

    // Rebuilds the copy when the mission, the counts or the timers have moved on.
    public void Refresh(bool force)
    {
        if (tooltip == null && Time.unscaledTime >= nextViewSearch)
        {
            nextViewSearch = Time.unscaledTime + 0.5f;
            tooltip = FindAnyObjectByType<QuestTooltip>(FindObjectsInactive.Include);
        }
        if (tracker == null) tracker = GetComponent<MissionTracker>();
        if (string.IsNullOrEmpty(questType) && Time.unscaledTime < typeSearchUntil && Time.unscaledTime >= nextTypeSearch)
        {
            nextTypeSearch = Time.unscaledTime + 0.5f;
            questType = DetectQuestType();
        }
        if (book == null) return;

        var snapshot = Capture();
        string timer = FormatTimer(snapshot.timer);
        string signature = $"{questType}|{snapshot.state}|{snapshot.done}|{snapshot.total}|{snapshot.active}|{timer}";
        if (!force && signature == pushedSignature) return;
        pushedSignature = signature;

        Quest quest = FindQuest(questType) ?? book.defaults;
        if (quest == null) return;

        LastHeader = Fill(quest.header, snapshot, timer, questType);
        LastSubtext = Fill(ResolveSubtext(quest, snapshot.state), snapshot, timer, questType);
        if (tooltip != null) tooltip.SetContent(LastHeader, LastSubtext);
    }

    // The copy for the mission state we are in, falling back to the first entry if the state is missing.
    static string ResolveSubtext(Quest quest, MissionState state)
    {
        if (quest.states == null || quest.states.Length == 0) return string.Empty;
        string wanted = state.ToString();
        foreach (var entry in quest.states)
            if (entry != null && string.Equals(entry.state, wanted, StringComparison.OrdinalIgnoreCase))
                return entry.subtext ?? string.Empty;
        return quest.states[0] != null ? quest.states[0].subtext ?? string.Empty : string.Empty;
    }

    Quest FindQuest(string type)
    {
        if (string.IsNullOrEmpty(type) || book.quests == null) return null;
        foreach (var quest in book.quests)
            if (quest != null && string.Equals(quest.type, type, StringComparison.OrdinalIgnoreCase))
                return quest;
        return null;
    }

    struct Snapshot
    {
        public MissionState state;
        public int done;
        public int total;
        public int active;
        public float timer;
    }

    // Counts straight from the registered objectives, so the tooltip stays right even without a tracker.
    Snapshot Capture()
    {
        var snapshot = new Snapshot
        {
            state = tracker != null ? tracker.State : MissionState.InProgress,
            total = MissionObjectives.All.Count,
        };
        foreach (var objective in MissionObjectives.All)
        {
            if (objective == null) continue;
            if (objective.IsComplete) { snapshot.done++; continue; }
            snapshot.active++;
        }
        // Copy and feedback show the same active hold/grace clock.
        var feedback = MissionHudFeedbackState.Capture(tracker);
        if (feedback.HasCountdown) snapshot.timer = feedback.Seconds;
        return snapshot;
    }

    static string FormatTimer(float seconds) => seconds > 0f ? seconds.ToString("0.0") + "s" : "—";

    static string Fill(string template, Snapshot snapshot, string timer, string type)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;
        var text = new StringBuilder(template);
        text.Replace("{type}", type ?? string.Empty);
        text.Replace("{state}", snapshot.state.ToString());
        text.Replace("{done}", snapshot.done.ToString());
        text.Replace("{total}", snapshot.total.ToString());
        text.Replace("{left}", Mathf.Max(0, snapshot.total - snapshot.done).ToString());
        text.Replace("{active}", snapshot.active.ToString());
        text.Replace("{timer}", timer);
        return text.ToString();
    }

    // "Chunk_KOTH" (or "Chunk_KOTH(Clone)") names the quest type KOTH.
    string DetectQuestType()
    {
        if (!string.IsNullOrEmpty(questTypeOverride)) return questTypeOverride.Trim();
        foreach (var candidate in FindObjectsByType<Transform>())
        {
            if (candidate == null || !candidate.gameObject.activeInHierarchy) continue;
            string name = TrimClone(candidate.name);
            if (!name.StartsWith(chunkPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (IsIgnoredChunk(name)) continue;
            return name.Substring(chunkPrefix.Length);
        }
        return string.Empty;
    }

    bool IsIgnoredChunk(string name)
    {
        foreach (var ignored in ignoredChunks)
            if (!string.IsNullOrEmpty(ignored) && string.Equals(name, ignored.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    static string TrimClone(string objectName)
    {
        string name = (objectName ?? string.Empty).Trim();
        const string clone = "(Clone)";
        if (name.EndsWith(clone, StringComparison.Ordinal))
            name = name.Substring(0, name.Length - clone.Length).TrimEnd();
        return name;
    }
}
