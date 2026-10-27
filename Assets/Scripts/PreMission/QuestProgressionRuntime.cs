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
    static bool shockwaveResolved;
    static bool landingCleared;
    static readonly Dictionary<int,Vector2> stoneDisplacements=new Dictionary<int,Vector2>();
    static readonly Dictionary<int,Vector2> terrainDisplacements=new Dictionary<int,Vector2>();
    static readonly HashSet<int> brokenTerrain=new HashSet<int>();
    public static QuestProgressionSession Current { get; private set; }
    public static event Action CurrentChanged;
    public static bool HasPresentedOffers => presentedOffers.Count > 0;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetPlaySession() { Current = null; presentedOffers.Clear(); progressTrackRevealed=false; lastShownProgressStep=0; terrainRevealed=false; ClearShockwave(); CurrentChanged = null; }
    public static QuestProgressionSession GetOrCreate(QuestBoardCatalogue catalogue, int seed)
    {
        if (Current == null || Current.Catalogue != catalogue) StartNewProgression(catalogue, seed);
        return Current;
    }
    public static QuestProgressionSession StartNewProgression(QuestBoardCatalogue catalogue, int seed)
    {
        Current = new QuestProgressionSession(catalogue, seed); presentedOffers.Clear(); progressTrackRevealed=false; lastShownProgressStep=0; terrainRevealed=false;
        ClearShockwave();
        CurrentChanged?.Invoke(); return Current;
    }
    public static bool WasPresented(string offerId) => presentedOffers.Contains(offerId);
    static void ClearShockwave() { shockwaveResolved=landingCleared=false; stoneDisplacements.Clear(); terrainDisplacements.Clear(); brokenTerrain.Clear(); }
    public static bool WasLandingCleared(QuestProgressionSession run) => run!=null && Current==run && landingCleared;
    public static void RememberLandingLayout(QuestProgressionSession run,Dictionary<int,Vector2> offsets)
    {
        if(run==null || Current!=run || landingCleared || shockwaveResolved) return;
        foreach(var item in offsets) stoneDisplacements[item.Key]=item.Value;
        landingCleared=true;
    }
    public static bool HasShockwave(QuestProgressionSession run) => run!=null && Current==run && shockwaveResolved;
    public static Vector2 StoneDisplacement(QuestProgressionSession run,int slot) => Current==run && stoneDisplacements.TryGetValue(slot,out var value) ? value : Vector2.zero;
    public static Vector2 TerrainDisplacement(QuestProgressionSession run,int index) => Current==run && terrainDisplacements.TryGetValue(index,out var value) ? value : Vector2.zero;
    public static bool IsTerrainBroken(QuestProgressionSession run,int index) => Current==run && brokenTerrain.Contains(index);
    public static void RememberShockwave(QuestProgressionSession run,Dictionary<int,Vector2> stones,Dictionary<int,Vector2> terrain,IEnumerable<int> broken)
    {
        if(Current!=run || run==null || shockwaveResolved) return;
        foreach(var item in stones) stoneDisplacements[item.Key]=item.Value;
        foreach(var item in terrain) terrainDisplacements[item.Key]=item.Value;
        foreach(int index in broken) brokenTerrain.Add(index);
        shockwaveResolved=landingCleared=true;
    }
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
