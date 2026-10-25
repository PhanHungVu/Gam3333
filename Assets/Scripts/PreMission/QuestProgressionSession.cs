using System;
using System.Collections.Generic;
using System.Linq;

public enum QuestMissionOutcome { Succeeded, Failed, Abandoned }

// One occurrence of a reusable quest definition, independently of its UI/model.
public sealed class QuestOffer
{
    public string Id { get; }
    public QuestDefinition Quest { get; }
    public int Slot { get; }
    internal QuestOffer(string id, QuestDefinition quest, int slot) { Id = id; Quest = quest; Slot = slot; }
}

public sealed class QuestMissionAttempt
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string ProgressionId { get; }
    public QuestOffer Offer { get; }
    public PartData StartingTorso { get; }
    public QuestCategory Category { get; }
    internal QuestMissionAttempt(string progressionId, QuestOffer offer, PartData torso)
    { ProgressionId = progressionId; Offer = offer; StartingTorso = torso; Category = offer.Quest.category; }
}

public sealed class QuestProgressionChange
{
    public QuestMissionOutcome? Outcome { get; }
    public QuestOffer RemovedOffer { get; }
    public QuestOffer ReplacementOffer { get; }
    public QuestOffer AddedBigQuest { get; }
    internal QuestProgressionChange(QuestMissionOutcome? outcome, QuestOffer removed, QuestOffer replacement, QuestOffer big)
    { Outcome = outcome; RemovedOffer = removed; ReplacementOffer = replacement; AddedBigQuest = big; }
}

// Availability/progression only. No scenes, UI objects, cameras or equipment logic.
public sealed class QuestProgressionSession
{
    readonly List<QuestOffer> offers = new List<QuestOffer>();
    readonly Random random;
    int nextOffer;
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public QuestBoardCatalogue Catalogue { get; }
    public int Seed { get; }
    public int SmallOfferCount { get; }
    public int RequiredSmallCompletions { get; }
    public int SuccessfulSmallCompletions { get; private set; }
    public bool BigQuestUnlocked { get; private set; }
    public QuestOffer BigQuest { get; private set; }
    public QuestMissionAttempt ActiveAttempt { get; private set; }
    public int Version { get; private set; }
    public IReadOnlyList<QuestOffer> Offers { get; }
    public string PreferredTorsoId { get; set; }
    public event Action<QuestProgressionChange> Changed;

    public QuestProgressionSession(QuestBoardCatalogue catalogue, int seed)
    {
        Catalogue = catalogue; Seed = seed; random = new Random(seed);
        SmallOfferCount = Math.Max(1, catalogue != null ? catalogue.smallOfferCount : 3);
        RequiredSmallCompletions = Math.Max(1, catalogue != null ? catalogue.smallCompletionsToUnlockBig : 3);
        Offers = offers.AsReadOnly();
        FillSmallSlots();
    }
    IEnumerable<QuestDefinition> Eligible(QuestCategory category)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (Catalogue == null || Catalogue.quests == null) yield break;
        foreach (var quest in Catalogue.quests)
            if (quest != null && quest.available && !string.IsNullOrWhiteSpace(quest.questId) && seen.Add(quest.questId) && quest.category == category)
                yield return quest;
    }
    QuestOffer CreateOffer(QuestDefinition quest, int slot) => new QuestOffer(Id + ":" + (++nextOffer), quest, slot);
    QuestOffer DrawSmall(int slot, QuestDefinition previous = null)
    {
        var used = new HashSet<string>(offers.Select(o => o.Quest.questId), StringComparer.Ordinal);
        var eligible = Eligible(QuestCategory.Side).Where(q => !used.Contains(q.questId)).ToList();
        var different = eligible.Where(q => q != previous).ToList();
        if (different.Count > 0) eligible = different;
        return eligible.Count > 0 ? CreateOffer(eligible[random.Next(eligible.Count)], slot) : null;
    }
    void FillSmallSlots()
    {
        for (int slot = 0; slot < SmallOfferCount; slot++)
        {
            if (offers.Any(o => o.Slot == slot && o.Quest.category == QuestCategory.Side)) continue;
            var offer = DrawSmall(slot);
            if (offer != null) offers.Add(offer);
        }
        offers.Sort((a, b) => a.Slot.CompareTo(b.Slot));
    }
    QuestOffer AddBigQuest()
    {
        if (!BigQuestUnlocked || BigQuest != null) return null;
        var candidates = Eligible(QuestCategory.Main).ToList();
        if (candidates.Count == 0) return null;
        BigQuest = CreateOffer(candidates[random.Next(candidates.Count)], SmallOfferCount);
        offers.Add(BigQuest); return BigQuest;
    }
    public QuestOffer FindOffer(string id) => offers.Find(o => o.Id == id);
    public QuestOffer FindOffer(QuestDefinition quest) => offers.Find(o => o.Quest == quest);
    public bool TryBeginMission(QuestDefinition quest, PartData torso, out QuestMissionAttempt attempt)
    {
        attempt = null;
        var offer = FindOffer(quest);
        if (ActiveAttempt != null || offer == null || torso == null || torso.partType != PartType.Torso
            || string.IsNullOrWhiteSpace(torso.partId) || Catalogue == null || Catalogue.starterTorsos == null || !Catalogue.starterTorsos.Contains(torso)) return false;
        attempt = ActiveAttempt = new QuestMissionAttempt(Id, offer, torso);
        return true;
    }
    public bool ReportMissionResult(string attemptId, QuestMissionOutcome outcome)
    {
        if (ActiveAttempt == null || ActiveAttempt.Id != attemptId || ActiveAttempt.ProgressionId != Id
            || !Enum.IsDefined(typeof(QuestMissionOutcome), outcome)) return false;
        var attempt = ActiveAttempt; ActiveAttempt = null;
        QuestOffer removed = null, replacement = null, big = null;
        if (outcome == QuestMissionOutcome.Succeeded && attempt.Category == QuestCategory.Side && FindOffer(attempt.Offer.Id) != null)
        {
            SuccessfulSmallCompletions++;
            removed = attempt.Offer; offers.Remove(removed);
            replacement = DrawSmall(removed.Slot, removed.Quest);
            if (replacement != null) offers.Add(replacement);
            FillSmallSlots();
            if (SuccessfulSmallCompletions >= RequiredSmallCompletions) BigQuestUnlocked = true;
            big = AddBigQuest();
        }
        Version++;
        Changed?.Invoke(new QuestProgressionChange(outcome, removed, replacement, big));
        return true;
    }
    // Allows a previously empty eligible pool to be configured while retaining the run.
    public void RefreshAvailability()
    {
        int before = offers.Count; FillSmallSlots(); var big = AddBigQuest();
        if (before == offers.Count && big == null) return;
        Version++; Changed?.Invoke(new QuestProgressionChange(null, null, null, big));
    }
}
