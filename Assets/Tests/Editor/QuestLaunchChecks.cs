using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Quest board -> mission scenes: each mission type has a scene in Build Settings, and Return can be sent
// back to the quest board.
public static class QuestLaunchChecks
{
    public static void Run(Action<bool, string> check)
    {
        var holder = new GameObject("Quest Launch Check");
        try
        {
            var hook = holder.AddComponent<SceneMissionStartHook>();
            string koth = hook.SceneFor(QuestMissionType.KingOfTheHill), escort = hook.SceneFor(QuestMissionType.Escort),
                elimination = hook.SceneFor(QuestMissionType.Elimination);
            check(koth == "Assets/Scenes/KOTHTest.unity" && escort == "Assets/Scenes/EscortTest.unity"
                && elimination == "Assets/Scenes/EliminationTest.unity" && hook.SceneFor(QuestMissionType.Retrieval) == null,
                "The quest board's mission hook maps KOTH, Escort and Elimination to their scenes");
            var built = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList();
            check(built.Contains(koth) && built.Contains(escort) && built.Contains(elimination) && built.Contains("Assets/Scenes/PreMissionScene.unity"),
                "The three mission scenes and the quest board are in Build Settings");

            var outcome = holder.AddComponent<MissionOutcomeController>();
            bool defaultMenu = outcome.ReturnScene == "Assets/Scenes/MainMenuScene.unity";
            outcome.ReturnScene = "Assets/Scenes/PreMissionScene.unity";
            check(defaultMenu && outcome.ReturnScene == "Assets/Scenes/PreMissionScene.unity",
                "A mission returns to the main menu by default, or to the quest board when launched from it");

            QuestMissionLaunch.Clear();
            check(!QuestMissionLaunch.Active && !QuestMissionLaunch.Report(QuestMissionOutcome.Succeeded),
                "With no quest launched, a mission scene reports nothing to the quest board");
        }
        finally { Object.DestroyImmediate(holder); QuestMissionLaunch.Clear(); }
    }
}
