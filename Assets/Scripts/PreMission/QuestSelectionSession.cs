using System;
using System.Collections.Generic;

public enum PreMissionView { Board, Details, Torso }

// Owns one board's offers and navigation independently of UI GameObjects.
// Construct a new session only for an explicitly fresh board, never on panel enable.
public sealed class QuestSelectionSession
{
    readonly List<QuestDefinition> offers = new List<QuestDefinition>();
    readonly List<PartData> torsos = new List<PartData>();
    int torsoIndex;
    public IReadOnlyList<QuestDefinition> Offers { get; }
    public IReadOnlyList<PartData> Torsos { get; }
    public PreMissionView View { get; private set; }
    public QuestDefinition SelectedQuest { get; private set; }
    public PartData SelectedTorso => torsos.Count > 0 ? torsos[torsoIndex] : null;
    public int SelectedTorsoIndex => torsos.Count > 0 ? torsoIndex : -1;
    public bool LaunchRequested { get; private set; }

    public QuestSelectionSession(QuestBoardCatalogue catalogue, int seed)
    {
        Offers = offers.AsReadOnly();
        Torsos = torsos.AsReadOnly();
        if (catalogue == null) return;

        var candidates = new List<QuestDefinition>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (catalogue.quests != null)
            foreach (var quest in catalogue.quests)
                if (quest != null && quest.available && !string.IsNullOrWhiteSpace(quest.questId) && ids.Add(quest.questId))
                    candidates.Add(quest);

        var random = new Random(seed);
        // Shuffle a copy, leaving the authored catalogue and Unity's gameplay RNG untouched.
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            var other = candidates[j]; candidates[j] = candidates[i]; candidates[i] = other;
        }
        int min = Math.Min(candidates.Count, Math.Max(1, catalogue.minimumOffers));
        int max = Math.Min(candidates.Count, Math.Max(min, catalogue.maximumOffers));
        int count = min == max ? min : random.Next(min, max + 1);
        if (catalogue.includeMainQuestWhenAvailable && count > 0)
        {
            int main = candidates.FindIndex(q => q.category == QuestCategory.Main);
            if (main >= 0) { offers.Add(candidates[main]); candidates.RemoveAt(main); }
        }
        for (int i = 0; offers.Count < count; i++) offers.Add(candidates[i]);

        ids.Clear();
        if (catalogue.starterTorsos != null)
            foreach (var torso in catalogue.starterTorsos)
                if (torso != null && torso.partType == PartType.Torso && !string.IsNullOrWhiteSpace(torso.partId) && ids.Add(torso.partId))
                    torsos.Add(torso);
    }

    public bool SelectQuest(QuestDefinition quest)
    {
        if (View != PreMissionView.Board || quest == null || !offers.Contains(quest)) return false;
        SelectedQuest = quest;
        LaunchRequested = false;
        View = PreMissionView.Details;
        return true;
    }

    public bool AcceptQuest()
    {
        if (View != PreMissionView.Details || SelectedQuest == null) return false;
        View = PreMissionView.Torso;
        return true;
    }

    public bool BrowseTorso(int direction)
    {
        if (View != PreMissionView.Torso || torsos.Count < 2 || direction == 0 || LaunchRequested) return false;
        torsoIndex = (torsoIndex + (direction > 0 ? 1 : -1) + torsos.Count) % torsos.Count;
        return true;
    }

    public bool Back()
    {
        if (View == PreMissionView.Board) return false;
        LaunchRequested = false;
        if (View == PreMissionView.Torso) View = PreMissionView.Details;
        else { View = PreMissionView.Board; SelectedQuest = null; }
        return true;
    }

    public bool TryRequestLaunch(out QuestDefinition quest, out PartData torso)
    {
        quest = null; torso = null;
        if (View != PreMissionView.Torso || SelectedQuest == null || SelectedTorso == null || LaunchRequested) return false;
        quest = SelectedQuest; torso = SelectedTorso;
        LaunchRequested = true;
        return true;
    }

    // A hook that throws can be retried without rebuilding or losing the board.
    public void CancelLaunchRequest() => LaunchRequested = false;
}
