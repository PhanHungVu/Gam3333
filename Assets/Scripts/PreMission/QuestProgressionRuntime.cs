using System;
using System.Collections.Generic;
using UnityEngine;

// One play-session store, reset on a genuinely new run or new Play session.
// This deliberately does not write ScriptableObjects, PlayerPrefs or a save file.
public static class QuestProgressionRuntime
{
    static readonly HashSet<string> presentedOffers = new HashSet<string>();
    // Presentation watermarks only; availability/results still belong to Current.
    static bool progressTrackRevealed;
    static int lastShownProgressStep;
    static bool terrainRevealed;
    public static QuestProgressionSession Current { get; private set; }
    public static event Action CurrentChanged;
    public static bool HasPresentedOffers => presentedOffers.Count > 0;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetPlaySession() { Current = null; presentedOffers.Clear(); progressTrackRevealed=false; lastShownProgressStep=0; terrainRevealed=false; CurrentChanged = null; }
    public static QuestProgressionSession GetOrCreate(QuestBoardCatalogue catalogue, int seed)
    {
        if (Current == null || Current.Catalogue != catalogue) StartNewProgression(catalogue, seed);
        return Current;
    }
    public static QuestProgressionSession StartNewProgression(QuestBoardCatalogue catalogue, int seed)
    {
        Current = new QuestProgressionSession(catalogue, seed); presentedOffers.Clear(); progressTrackRevealed=false; lastShownProgressStep=0; terrainRevealed=false;
        CurrentChanged?.Invoke(); return Current;
    }
    public static bool WasPresented(string offerId) => presentedOffers.Contains(offerId);
    public static bool WasTerrainRevealed(QuestProgressionSession run) => Current==run && run!=null && terrainRevealed;
    public static void RememberTerrain(QuestProgressionSession run) { if(Current==run && run!=null) terrainRevealed=true; }
    public static void MarkPresented(QuestProgressionSession run, string offerId)
    { if (Current == run && run.FindOffer(offerId) != null) presentedOffers.Add(offerId); }
    public static bool WasProgressTrackRevealed(QuestProgressionSession run) => Current==run && progressTrackRevealed;
    public static int LastShownProgressStep(QuestProgressionSession run) => Current==run ? lastShownProgressStep : 0;
    public static void RememberProgressPresentation(QuestProgressionSession run, int step)
    {
        if (Current!=run || run==null) return;
        progressTrackRevealed=true;
        lastShownProgressStep=Math.Max(lastShownProgressStep,Math.Max(0,Math.Min(step,Math.Min(run.SuccessfulSmallCompletions,run.RequiredSmallCompletions))));
    }
    public static bool TryBeginMission(QuestDefinition quest, PartData torso, out QuestMissionAttempt attempt)
    {
        attempt = null; return Current != null && Current.TryBeginMission(quest, torso, out attempt);
    }
    public static bool ReportMissionResult(string attemptId, QuestMissionOutcome outcome) => Current != null && Current.ReportMissionResult(attemptId, outcome);
}
