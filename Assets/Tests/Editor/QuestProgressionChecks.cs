using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Data invariants plus real objective completion and scene-return integration.
// Invoked only by the disposable-project batch runner.
public sealed class QuestProgressionChecks
{
    readonly Action<bool, string> check;
    readonly Action<string> capture;
    readonly IEnumerator routine;
    public QuestProgressionChecks(Action<bool, string> check, Action<string> capture)
    { this.check = check; this.capture = capture; routine = Run(); }
    public bool Step() => !routine.MoveNext();

    public static void CheckData(Action<bool, string> check)
    {
        var catalogue = ScriptableObject.CreateInstance<QuestBoardCatalogue>();
        var quests = Enumerable.Range(0, 7).Select(i =>
        {
            var q = ScriptableObject.CreateInstance<QuestDefinition>();
            q.questId = "progression-" + i; q.category = i == 0 ? QuestCategory.Main : QuestCategory.Side; return q;
        }).ToArray();
        var torso = ScriptableObject.CreateInstance<PartData>(); torso.partId = "torso"; torso.partType = PartType.Torso;
        var excluded = new[] { QuestMissionType.Retrieval, QuestMissionType.Defense }.SelectMany(type => new[] { QuestCategory.Side, QuestCategory.Main }.Select(category =>
        {
            var q = ScriptableObject.CreateInstance<QuestDefinition>(); q.questId = "excluded-" + type + "-" + category;
            q.missionType = type; q.category = category; return q;
        })).ToArray();
        catalogue.quests.AddRange(quests); catalogue.starterTorsos.Add(torso);
        try
        {
            var run = new QuestProgressionSession(catalogue, 41);
            var ui = new QuestSelectionSession(catalogue, 41, run);
            var same = new QuestProgressionSession(catalogue, 41);
            check(run.Offers.Count == 3 && run.BigQuest == null && run.SuccessfulSmallCompletions == 0
                && run.Offers.All(o => o.Quest.category == QuestCategory.Side)
                && run.Offers.Select(o => o.Quest).SequenceEqual(same.Offers.Select(o => o.Quest)), "M1: fresh seeded progression has three small offers and no main");
            ui.SelectQuest(ui.Offers[0]); ui.AcceptQuest(); ui.TryRequestLaunch(out _, out _);
            ui.Back(); ui.Back();
            check(run.ActiveAttempt == null && run.SuccessfulSmallCompletions == 0, "M1: selection, accept, confirm and navigation never award progress or begin gameplay");
            check(!run.TryBeginMission(quests[0], torso, out _) && !run.TryBeginMission(run.Offers[0].Quest, null, out _)
                && !run.ReportMissionResult("foreign", QuestMissionOutcome.Succeeded), "M1: unoffered quests, missing torsos and unknown results are rejected");
            run.TryBeginMission(run.Offers[0].Quest, torso, out var failed);
            var before = run.Offers.ToArray();
            check(!run.TryBeginMission(run.Offers[1].Quest, torso, out _) && !run.ReportMissionResult(failed.Id, (QuestMissionOutcome)99)
                && run.ReportMissionResult(failed.Id, QuestMissionOutcome.Failed) && run.Offers.SequenceEqual(before)
                && run.SuccessfulSmallCompletions == 0, "M1: concurrent/invalid results are rejected; failure retains offers and progress");
            run.TryBeginMission(run.Offers[0].Quest, torso, out var abandoned);
            check(run.ReportMissionResult(abandoned.Id, QuestMissionOutcome.Abandoned) && !run.ReportMissionResult(abandoned.Id, QuestMissionOutcome.Succeeded)
                && run.SuccessfulSmallCompletions == 0, "M1: abandoning or a late success after abandonment cannot count");
            int arrivals = 0; QuestProgressionChange change = null;
            run.Changed += c => { change = c; if (c.AddedBigQuest != null) arrivals++; };
            for (int win = 1; win <= 4; win++)
            {
                var completed = run.Offers.First(o => o.Quest.category == QuestCategory.Side);
                var unrelated = run.Offers.Where(o => o != completed).ToArray();
                ui.SynchronizeOffers(); ui.SelectQuest(unrelated.First(o => o.Quest.category == QuestCategory.Side).Quest); ui.AcceptQuest();
                var selectedId = ui.SelectedOfferId;
                check(run.TryBeginMission(completed.Quest, torso, out var attempt) && run.SuccessfulSmallCompletions == win - 1,
                    "M1: beginning a tracked attempt does not count, win " + win);
                check(!run.ReportMissionResult(failed.Id, QuestMissionOutcome.Succeeded) && run.ActiveAttempt == attempt
                    && run.ReportMissionResult(attempt.Id, QuestMissionOutcome.Succeeded) && !run.ReportMissionResult(attempt.Id, QuestMissionOutcome.Succeeded),
                    "M1: only the current unique attempt can succeed exactly once, win " + win);
                ui.SynchronizeOffers();
                check(run.SuccessfulSmallCompletions == win && run.Offers.Count(o => o.Quest.category == QuestCategory.Side) == 3
                    && change.RemovedOffer == completed && change.ReplacementOffer.Slot == completed.Slot && change.ReplacementOffer.Quest != completed.Quest
                    && unrelated.All(o => run.FindOffer(o.Id) == o) && ui.SelectedOfferId == selectedId && ui.View == PreMissionView.Torso,
                    "M1: replenish one slot and preserve unrelated offers and active selection, win " + win);
                check(run.BigQuestUnlocked == (win >= 3) && (run.BigQuest != null) == (win >= 3) && arrivals == (win >= 3 ? 1 : 0),
                    "M1: big quest arrives once at the threshold, win " + win);
                ui.Back(); ui.Back();
            }
            var big = run.BigQuest;
            run.TryBeginMission(big.Quest, torso, out var mainAttempt);
            check(run.ReportMissionResult(mainAttempt.Id, QuestMissionOutcome.Succeeded) && run.BigQuest == big
                && run.SuccessfulSmallCompletions == 4 && arrivals == 1, "M1: main success never increments small progress or duplicates/removes the unlocked main");
            ui.SynchronizeOffers(); ui.SelectQuest(ui.Offers.First(q => q.category == QuestCategory.Side));
            run.TryBeginMission(ui.SelectedQuest, torso, out var selectedAttempt); run.ReportMissionResult(selectedAttempt.Id, QuestMissionOutcome.Succeeded); ui.SynchronizeOffers();
            check(ui.View == PreMissionView.Board && ui.SelectedQuest == null && ui.SelectedOfferId == null, "M1: a completed selected occurrence is invalidated even if its definition is reusable");

            catalogue.smallOfferCount = 4; catalogue.smallCompletionsToUnlockBig = 1; catalogue.quests.Clear(); catalogue.quests.Add(quests[1]);
            var sparse = new QuestProgressionSession(catalogue, 3); var first = sparse.Offers[0];
            sparse.TryBeginMission(first.Quest, torso, out var sparseAttempt); sparse.ReportMissionResult(sparseAttempt.Id, QuestMissionOutcome.Succeeded);
            check(sparse.Offers.Count == 1 && sparse.Offers[0].Quest == first.Quest && sparse.Offers[0].Id != first.Id
                && sparse.BigQuestUnlocked && sparse.BigQuest == null, "M1: scarce pools recycle definitions as new occurrences without simultaneous duplicates; missing main is safe");
            catalogue.quests.Add(quests[0]); sparse.RefreshAvailability(); var arrived = sparse.BigQuest; sparse.RefreshAvailability();
            check(arrived != null && sparse.BigQuest == arrived && sparse.Offers.Count == 2, "M1: eligible main added later appears once without discarding the run");
            catalogue.smallOfferCount = 0; catalogue.smallCompletionsToUnlockBig = 0;
            var clamped = new QuestProgressionSession(catalogue, 0);
            check(clamped.SmallOfferCount == 1 && clamped.RequiredSmallCompletions == 1, "M1: invalid programmatic configuration is clamped safely");

            catalogue.quests.Clear(); catalogue.quests.AddRange(quests); catalogue.quests.AddRange(excluded);
            catalogue.smallOfferCount = 6; catalogue.smallCompletionsToUnlockBig = 3;
            bool onlyAllowed = true, fullBoards = true, mainUnlocked = true;
            for (int seed = 0; seed < 64; seed++)
            {
                var filtered = new QuestProgressionSession(catalogue, seed);
                for (int win = 0; win < 6; win++)
                {
                    onlyAllowed &= filtered.Offers.All(o => catalogue.AllowsMissionType(o.Quest.missionType));
                    fullBoards &= filtered.Offers.Count(o => o.Quest.category == QuestCategory.Side) == 6;
                    filtered.TryBeginMission(filtered.Offers.First(o => o.Quest.category == QuestCategory.Side).Quest, torso, out var result);
                    filtered.ReportMissionResult(result.Id, QuestMissionOutcome.Succeeded);
                    mainUnlocked &= (filtered.BigQuest != null) == (win >= 2);
                }
                onlyAllowed &= filtered.Offers.All(o => catalogue.AllowsMissionType(o.Quest.missionType));
            }
            check(onlyAllowed, "Only enabled mission types enter initial offers, replacements and main draws, including legacy definitions in the pool");
            check(fullBoards && mainUnlocked, "Mission-type filtering retains six distinct side offers and the three-completion main unlock across 64 seeds");
            catalogue.offeredMissionTypes.Clear();
            check(new QuestProgressionSession(catalogue, 0).Offers.Count == 0, "An empty enabled-type list safely offers no missions");
        }
        finally
        {
            foreach (var quest in quests) Object.DestroyImmediate(quest);
            foreach (var quest in excluded) Object.DestroyImmediate(quest);
            Object.DestroyImmediate(torso); Object.DestroyImmediate(catalogue);
        }
    }

    static void Set(Object target, string name, object value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
    static PreMissionFlowController Flow => Object.FindAnyObjectByType<PreMissionFlowController>();
    static bool Busy => Flow.IsPosting || Flow.IsTransitioning;
    static string Message => ((TMP_Text)new SerializedObject(Flow).FindProperty("boardMessage").objectReferenceValue).text;
    static string OfferIds(QuestProgressionSession run) => string.Join(";", run.Offers.Select(o => o.Id));
    IEnumerator Run()
    {
        Time.timeScale = 1f;
        var catalogue = QuestProgressionRuntime.Current.Catalogue;
        int savedCount = catalogue.smallOfferCount;
        catalogue.smallOfferCount = 3;
        QuestProgressionRuntime.StartNewProgression(catalogue, 31337);
        yield return null;
        while (Busy) yield return null;
        var run = QuestProgressionRuntime.Current;
        check(Flow.Notices.Count == 3 && Flow.Notices.All(n => n.Quest.category == QuestCategory.Side) && Message.Contains("0 / 3"), "M1 Play: shipped board starts with three small offers and visible 0/3 feedback");
        capture("m1-fresh-three-small");
        var torso = Flow.Session.Torsos[0];
        var allPositions = Flow.Notices.ToDictionary(n => n.OfferSlot, n => n.BoardPosition);
        int bigArrivals = 0; run.Changed += change => { if (change.AddedBigQuest != null) bigArrivals++; };
        for (int win = 1; win <= 3; win++)
        {
            var completed = run.Offers.First(o => o.Quest.category == QuestCategory.Side);
            var unchanged = Flow.Notices.Where(n => n.OfferId != completed.Id).ToArray();
            check(run.TryBeginMission(completed.Quest, torso, out var attempt), "M1 Play: bind real objective to tracked attempt " + win);
            var player = new GameObject("M1 objective test player"); player.tag = "Player";
            var item = new GameObject("M1 objective item"); item.transform.position = Vector3.right * 100f;
            var destination = new GameObject("M1 objective return"); destination.transform.position = Vector3.right * 200f;
            var host = new GameObject("M1 real prototype objective"); host.SetActive(false);
            var objective = host.AddComponent<TempLevelObjective>();
            Set(objective, "item", item.transform); Set(objective, "returnPoint", destination.transform); Set(objective, "pauseOnWin", false);
            var bridge = host.AddComponent<PreMissionObjectiveResultBridge>(); host.SetActive(true);
            yield return null; yield return null;
            check(bridge.AttemptId == attempt.Id && run.SuccessfulSmallCompletions == win - 1 && !objective.IsComplete,
                "M1 Play: scene/objective entry alone never counts " + win);
            player.transform.position = item.transform.position;
            yield return null; yield return null;
            check(!objective.IsComplete && run.SuccessfulSmallCompletions == win - 1, "M1 Play: pickup alone never counts " + win);
            player.transform.position = destination.transform.position;
            yield return null; yield return null;
            while (Busy) yield return null;
            check(objective.IsComplete && run.SuccessfulSmallCompletions == win && !bridge.ReportFailure()
                && !run.ReportMissionResult(attempt.Id, QuestMissionOutcome.Succeeded), "M1 Play: actual return-to-spawn win counts exactly once " + win);
            check(unchanged.All(n => Flow.Notices.Contains(n)) && Flow.Notices.Where(n => n.Quest.category == QuestCategory.Side)
                .All(n => Vector2.Distance(n.BoardPosition, allPositions[n.OfferSlot]) < 0.01f), "M1 Play: replacing one notice and adding main preserve unrelated objects and every small slot " + win);
            check(Message.Contains(win + " / 3") && (Flow.Notices.Any(n => n.Quest.category == QuestCategory.Main) == (win == 3)), "M1 Play: counter and unlock feedback match actual results " + win);
            capture("m1-progress-" + win);
            Object.Destroy(host); Object.Destroy(item); Object.Destroy(destination); Object.Destroy(player);
            yield return null;
            string ids = OfferIds(run);
            SceneManager.LoadScene("MainMenuScene"); yield return null; yield return null;
            check(QuestProgressionRuntime.Current == run && run.SuccessfulSmallCompletions == win, "M1 Play: menu return preserves session progress " + win);
            SceneManager.LoadScene("PreMissionScene"); yield return null; yield return null;
            check(Flow.Session.Progression == run && OfferIds(run) == ids && !Flow.IsPosting && !Flow.IsTransitioning,
                "M1 Play: reopening restores exact offers without reposting seen notices " + win);
        }
        check(bigArrivals == 1 && Flow.Notices.Count == 4 && run.BigQuest != null, "M1 Play: one optional main arrives; three small quests remain available");
        var smallOffer = run.Offers.First(o => o.Quest.category == QuestCategory.Side);
        run.TryBeginMission(smallOffer.Quest, torso, out var failure);
        var failedHost = new GameObject("M1 failure fixture");
        var failedObjective = failedHost.AddComponent<TempLevelObjective>(); failedObjective.enabled = false;
        var failedBridge = failedHost.AddComponent<PreMissionObjectiveResultBridge>();
        check(failedBridge.ReportFailure() && run.SuccessfulSmallCompletions == 3 && run.FindOffer(smallOffer.Id) != null, "M1 Play: explicit failure adapter preserves offer and progress");
        Object.Destroy(failedHost); yield return null;
        run.TryBeginMission(smallOffer.Quest, torso, out var abandonment);
        var abandonedHost = new GameObject("M1 abandoned fixture");
        var abandonedObjective = abandonedHost.AddComponent<TempLevelObjective>(); abandonedObjective.enabled = false;
        abandonedHost.AddComponent<PreMissionObjectiveResultBridge>();
        Object.Destroy(abandonedHost); yield return null; yield return null;
        check(run.ActiveAttempt == null && run.SuccessfulSmallCompletions == 3 && !run.ReportMissionResult(abandonment.Id, QuestMissionOutcome.Succeeded), "M1 Play: destroying an unfinished bound objective abandons once without credit");

        // Results received while inspecting a different quest are deferred until return.
        var inspect = Flow.Notices.First(n => n.Quest.category == QuestCategory.Main);
        Flow.SelectQuest(inspect.Quest); while (Busy) yield return null;
        Flow.AcceptQuest(); while (Busy) yield return null;
        Flow.NextTorso(); while (Busy) yield return null;
        var preferred = Flow.Session.SelectedTorso;
        run.TryBeginMission(smallOffer.Quest, preferred, out var background); run.ReportMissionResult(background.Id, QuestMissionOutcome.Succeeded);
        yield return null; yield return null;
        check(Flow.Session.SelectedQuest == inspect.Quest && Flow.Session.SelectedTorso == preferred && Flow.Session.View == PreMissionView.Torso
            && Flow.Notices.Contains(inspect), "M1 Play: unrelated result preserves current inspection, torso choice and notice");
        Flow.Back(); while (Busy) yield return null;
        Flow.Back(); while (Busy) yield return null;
        yield return null; while (Busy) yield return null;
        check(Flow.Notices.Contains(inspect) && !Flow.Notices.Any(n => n.OfferId == smallOffer.Id) && bigArrivals == 1, "M1 Play: deferred replacement applies on board without another main arrival");
        var selected = Flow.Notices.First(n => n.Quest.category == QuestCategory.Side);
        Flow.SelectQuest(selected.Quest); while (Busy) yield return null;
        run.TryBeginMission(selected.Quest, preferred, out var selectedAttempt); run.ReportMissionResult(selectedAttempt.Id, QuestMissionOutcome.Succeeded);
        yield return null; yield return null; while (Busy) yield return null;
        check(Flow.Session.View == PreMissionView.Board && Flow.Session.SelectedQuest == null && !Flow.Notices.Contains(selected), "M1 Play: completed selected offer safely returns inspection to the board");
        string preservedIds = OfferIds(run);
        SceneManager.LoadScene("MainMenuScene"); yield return null;
        SceneManager.LoadScene("PreMissionScene"); yield return null; yield return null;
        check(Flow.Session.Progression == run && Flow.Session.SelectedTorso == preferred && OfferIds(run) == preservedIds, "M1 Play: torso preference and stable offers survive scene reload");

        run.TryBeginMission(run.Offers.First(o => o.Quest.category == QuestCategory.Side).Quest, torso, out var stale);
        Flow.StartNewProgression(); yield return null;
        check(QuestProgressionRuntime.Current != run && QuestProgressionRuntime.Current.SuccessfulSmallCompletions == 0
            && !QuestProgressionRuntime.ReportMissionResult(stale.Id, QuestMissionOutcome.Succeeded), "M1 Play: explicit new run clears progress and rejects old-run completion tokens");
        while (Busy) yield return null;
        capture("m1-reset-three-small");

        // The same result API also works with no board/controller alive at all.
        var returnedRun = QuestProgressionRuntime.Current;
        var seenUnrelated = returnedRun.Offers.Skip(1).Select(o => o.Id).ToArray();
        SceneManager.LoadScene("MainMenuScene"); yield return null; yield return null;
        for (int i = 0; i < 3; i++)
        {
            returnedRun.TryBeginMission(returnedRun.Offers[0].Quest, torso, out var offscreen);
            check(QuestProgressionRuntime.ReportMissionResult(offscreen.Id, QuestMissionOutcome.Succeeded), "M1 Play: result is recorded while the board scene is absent " + i);
        }
        string offscreenIds = OfferIds(returnedRun);
        SceneManager.LoadScene("PreMissionScene"); yield return null; yield return null;
        check(Flow.Session.Progression == returnedRun && OfferIds(returnedRun) == offscreenIds && Flow.IsPosting
            && Flow.Notices.Where(n => seenUnrelated.Contains(n.OfferId)).All(n => n.IsPosted)
            && ((CanvasGroup)new SerializedObject(new SerializedObject(Flow).FindProperty("presentation").objectReferenceValue)
                .FindProperty("board").objectReferenceValue).alpha == 1f,
            "M1 Play: offscreen unlock posts only the replacement and new main; existing offers stay visible without entry fade");
        while (Busy) yield return null;
        capture("m1-offscreen-unlock");
        catalogue.smallOfferCount = savedCount;
    }
}
