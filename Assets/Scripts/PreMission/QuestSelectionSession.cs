using System;
using System.Collections.Generic;

public enum PreMissionView { Board, Details, Torso }

// Navigation wrapper over the persistent progression data, independent of UI objects.
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
    public QuestProgressionSession Progression { get; }
    public string SelectedOfferId { get; private set; }

    public QuestSelectionSession(QuestBoardCatalogue catalogue, int seed, QuestProgressionSession progression = null)
    {
        Offers = offers.AsReadOnly();
        Torsos = torsos.AsReadOnly();
        Progression = progression ?? new QuestProgressionSession(catalogue, seed);
        SynchronizeOffers();
        if (catalogue == null) return;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (catalogue.starterTorsos != null)
            foreach (var torso in catalogue.starterTorsos)
                if (torso != null && torso.partType == PartType.Torso && !string.IsNullOrWhiteSpace(torso.partId) && ids.Add(torso.partId))
                    torsos.Add(torso);
        int preferred = torsos.FindIndex(t => t.partId == Progression.PreferredTorsoId);
        if (preferred >= 0) torsoIndex = preferred;
    }
    public void SynchronizeOffers()
    {
        offers.Clear();
        foreach (var offer in Progression.Offers) offers.Add(offer.Quest);
        if (SelectedOfferId != null && Progression.FindOffer(SelectedOfferId) == null)
        { SelectedQuest = null; SelectedOfferId = null; LaunchRequested = false; View = PreMissionView.Board; }
    }

    public bool SelectQuest(QuestDefinition quest)
    {
        if (View != PreMissionView.Board || quest == null || !offers.Contains(quest) || Progression.FindOffer(quest) == null) return false;
        SelectedQuest = quest;
        SelectedOfferId = Progression.FindOffer(quest)?.Id;
        LaunchRequested = false;
        View = PreMissionView.Details;
        return true;
    }

    public bool AcceptQuest()
    {
        if (View != PreMissionView.Details || SelectedQuest == null || Progression.FindOffer(SelectedOfferId) == null) return false;
        View = PreMissionView.Torso;
        return true;
    }

    public bool BrowseTorso(int direction)
    {
        if (View != PreMissionView.Torso || torsos.Count < 2 || direction == 0 || LaunchRequested) return false;
        torsoIndex = (torsoIndex + (direction > 0 ? 1 : -1) + torsos.Count) % torsos.Count;
        Progression.PreferredTorsoId = SelectedTorso.partId;
        return true;
    }

    public bool Back()
    {
        if (View == PreMissionView.Board) return false;
        LaunchRequested = false;
        if (View == PreMissionView.Torso) View = PreMissionView.Details;
        else { View = PreMissionView.Board; SelectedQuest = null; SelectedOfferId = null; }
        return true;
    }

    public bool TryRequestLaunch(out QuestDefinition quest, out PartData torso)
    {
        quest = null; torso = null;
        if (View != PreMissionView.Torso || SelectedQuest == null || SelectedTorso == null || LaunchRequested || Progression.FindOffer(SelectedOfferId) == null) return false;
        quest = SelectedQuest; torso = SelectedTorso;
        LaunchRequested = true;
        return true;
    }

    // A hook that throws can be retried without rebuilding or losing the board.
    public void CancelLaunchRequest() => LaunchRequested = false;
}
