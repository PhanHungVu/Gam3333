using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Reuses the current HUD and zoo roster. Does not replace maps, objectives or player combat.
public static class GameplaySceneMaintenance
{
    public const string CursorPath = "Assets/Prefabs/Mech Cursor Canvas.prefab";
    public const string CombatHudPath = "Assets/Prefabs/UI/Combat HUD/CombatHUD.prefab";
    public const string TrainingWavePath = "Assets/LevelCatalogue/WaveData_Training.asset";
    public static readonly string[] PlayerPaths = {
        "Assets/Prefabs/Mech/Base/FullPlayer_mech_heavy.prefab",
        "Assets/Prefabs/Mech/Specialist/FullPlayer_mech_specialist.prefab"
    };
    static readonly string[] Retired = { "3c93e3bc34dac5e4394db937a5e4f772", "c8be8f34120258b4fa2fdab60358411f" };
    static readonly Regex Blocks = new Regex(@"(?ms)^--- !u!(\d+) &([^\r\n ]+)(?: stripped)?\r?\n.*?(?=^--- !u!|\z)");

    [MenuItem("Tools/Mech/Repair Gameplay HUD and Legacy Spawners")]
    public static void Apply()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before repairing scene assets.");
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        ConfigureCursor();
        var cursor = AssetDatabase.LoadAssetAtPath<GameObject>(CursorPath);
        foreach (var playerPath in PlayerPaths)
        {
            var player = PrefabUtility.LoadPrefabContents(playerPath);
            try
            {
                var fields = new SerializedObject(player.GetComponent<MechPlayerRig>());
                fields.FindProperty("cursorPrefab").objectReferenceValue = cursor;
                fields.FindProperty("combatHudPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(CombatHudPath);
                if (fields.ApplyModifiedPropertiesWithoutUndo()) PrefabUtility.SaveAsPrefabAsset(player, playerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
        }

        var sceneSetup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            foreach (var path in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                bool repairedSpawners = RepairRetiredSpawners(path);
                // Main menu and pre-mission use their own UI; gameplay scenes are identified by their actual player/HUD.
                var scene = EditorSceneManager.OpenScene(path);
                var roots = scene.GetRootGameObjects();
                var legacy = roots.SelectMany(r => r.GetComponentsInChildren<MechEnergyHud>(true)).ToArray();
                bool changed = repairedSpawners;
                var oldCanvases = legacy.Select(h => h.GetComponentInParent<Canvas>(true)).Where(c => c != null).Distinct().ToArray();
                var shared = roots.FirstOrDefault(r => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(r) == CursorPath);
                foreach (var oldCanvas in oldCanvases)
                {
                    if (shared == null) shared = (GameObject)PrefabUtility.InstantiatePrefab(cursor, scene);
                    // The gym's prototype objective still writes to a TMP label. Move that
                    // reference to the new quest readout before deleting the retired canvas.
                    foreach (var objective in roots.SelectMany(r => r.GetComponentsInChildren<TempLevelObjective>(true)))
                    {
                        if (objective.toolTipText == null || !objective.toolTipText.transform.IsChildOf(oldCanvas.transform)) continue;
                        var tooltip = shared.GetComponentInChildren<QuestTooltip>(true);
                        tooltip.SetQuest(QuestMissionType.Retrieval, objective.toolTipText.text);
                        foreach (var label in tooltip.GetComponentsInChildren<TMP_Text>(true))
                            PrefabUtility.RecordPrefabInstancePropertyModifications(label);
                        objective.toolTipText = (TMP_Text)new SerializedObject(tooltip).FindProperty("subtext").objectReferenceValue;
                        EditorUtility.SetDirty(objective);
                    }
                    Object.DestroyImmediate(oldCanvas.gameObject); changed = true;
                }
                // The UI-review scene already uses the new HUD; keep its dialogue, objective tooltip and overrides.
                if (repairedSpawners) ConfigureTrainingWaves(scene);
                if (changed) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
            }
            AssetDatabase.SaveAssets();
        }
        finally
        {
            if (!Application.isBatchMode)
            {
                if (sceneSetup.Length > 0 && sceneSetup.All(s => !string.IsNullOrEmpty(s.path)))
                    EditorSceneManager.RestoreSceneManagerSetup(sceneSetup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            }
        }
        Debug.Log("GAMEPLAY SCENE REPAIR COMPLETE: shared cursor/combat HUD, no stamina meters, current enemy waves.");
    }

    static void ConfigureCursor()
    {
        var root = PrefabUtility.LoadPrefabContents(CursorPath);
        try
        {
            bool changed = false;
            foreach (var meter in root.GetComponentsInChildren<MechEnergyHud>(true)) { Object.DestroyImmediate(meter.gameObject); changed = true; }
            if (root.GetComponentInChildren<MechWeaponsHud>(true) == null)
            {
                foreach (var oldHealth in root.GetComponentsInChildren<MechHealthHud>(true)) Object.DestroyImmediate(oldHealth.gameObject);
                var hud = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CombatHudPath), root.transform);
                hud.transform.SetAsFirstSibling();
                changed = true;
            }
            if (changed) PrefabUtility.SaveAsPrefabAsset(root, CursorPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static void ConfigureTrainingWaves(UnityEngine.SceneManagement.Scene scene)
    {
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        var small = all.Single(t => t.name == "Small Enemies").gameObject;
        var elite = all.Single(t => t.name == "Elite Enemies").gameObject;
        // The old gym managers pointed at two tent instances that predate SpawnPoint.
        var tentPath = AssetDatabase.GUIDToAssetPath("fe5a59c42c3aa9347b2c1486e2dc28a6");
        var tents = all.Where(t => PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)
            && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject) == tentPath).ToArray();
        foreach (var tent in tents) if (tent.GetComponent<SpawnPoint>() == null) tent.gameObject.AddComponent<SpawnPoint>();
        foreach (var marker in all.Where(t => t.name == "EnemySpawner_Crossbow" || t.name == "EnemySpawner_Mace"))
            if (marker.GetComponent<SpawnPoint>() == null) marker.gameObject.AddComponent<SpawnPoint>();
        var points = all.Select(t => t.GetComponent<SpawnPoint>()).Where(p => p != null).Distinct().ToArray();
        if (points.Length < 2) throw new InvalidOperationException(scene.name + " needs its two existing training spawn points.");
        var smallPoint = points.FirstOrDefault(p => p.name.EndsWith("_Mace", StringComparison.Ordinal)) ?? points[0];
        var elitePoint = points.FirstOrDefault(p => p.name.EndsWith("_Crossbow", StringComparison.Ordinal)) ?? points.First(p => p != smallPoint);
        var owner = small.transform.parent != null && small.transform.parent == elite.transform.parent
            ? small.transform.parent.gameObject : new GameObject("Training Enemy Waves");
        var waves = owner.GetComponent<ZooEnemyWaves>() ?? owner.AddComponent<ZooEnemyWaves>();
        var fields = new SerializedObject(waves);
        fields.FindProperty("roster").objectReferenceValue = AssetDatabase.LoadAssetAtPath<WaveData>(TrainingWavePath);
        fields.FindProperty("smallEnemies").objectReferenceValue = small;
        fields.FindProperty("eliteEnemies").objectReferenceValue = elite;
        fields.FindProperty("smallSpawnPoint").objectReferenceValue = smallPoint;
        fields.FindProperty("eliteSpawnPoint").objectReferenceValue = elitePoint;
        fields.ApplyModifiedPropertiesWithoutUndo();
    }

    static string Field(string text, string property, string fallback = "0")
    {
        var m = Regex.Match(text, @"propertyPath: " + Regex.Escape(property) + @"\r?\n      value: ([^\r\n]+)");
        return m.Success ? m.Groups[1].Value.Trim() : fallback;
    }
    static bool RepairRetiredSpawners(string path)
    {
        string text = File.ReadAllText(path); bool changed = false; int next = 0;
        foreach (var guid in Retired)
        {
            foreach (var instance in Blocks.Matches(text).Cast<Match>().Where(m => m.Groups[1].Value == "1001" && m.Value.Contains("m_SourcePrefab: {fileID: 100100000, guid: " + guid)).ToArray())
            {
                string instanceId = instance.Groups[2].Value;
                var stripped = Blocks.Matches(text).Cast<Match>().Where(m => m.Value.Contains("m_PrefabInstance: {fileID: " + instanceId + "}")).ToArray();
                string transformId = stripped.FirstOrDefault(m => m.Groups[1].Value == "4")?.Groups[2].Value ?? instanceId;
                string goId = stripped.FirstOrDefault(m => m.Groups[1].Value == "1")?.Groups[2].Value ?? (900000000000001000L + next++).ToString();
                string parent = Regex.Match(instance.Value, @"m_TransformParent: \{fileID: ([0-9-]+)\}").Groups[1].Value;
                string name = guid == Retired[0] ? "Elite Enemies" : "Small Enemies";
                var replacement = new StringBuilder();
                replacement.Append($"--- !u!1 &{goId}\nGameObject:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {{fileID: 0}}\n  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n  serializedVersion: 6\n  m_Component:\n  - component: {{fileID: {transformId}}}\n  m_Layer: 0\n  m_Name: {name}\n  m_TagString: Untagged\n  m_Icon: {{fileID: 0}}\n  m_NavMeshLayer: 0\n  m_StaticEditorFlags: 0\n  m_IsActive: 1\n");
                replacement.Append($"--- !u!4 &{transformId}\nTransform:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {{fileID: 0}}\n  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n  m_GameObject: {{fileID: {goId}}}\n  serializedVersion: 2\n");
                replacement.Append($"  m_LocalRotation: {{x: {Field(instance.Value,"m_LocalRotation.x")}, y: {Field(instance.Value,"m_LocalRotation.y")}, z: {Field(instance.Value,"m_LocalRotation.z")}, w: {Field(instance.Value,"m_LocalRotation.w","1")}}}\n");
                replacement.Append($"  m_LocalPosition: {{x: {Field(instance.Value,"m_LocalPosition.x")}, y: {Field(instance.Value,"m_LocalPosition.y")}, z: {Field(instance.Value,"m_LocalPosition.z")}}}\n");
                replacement.Append($"  m_LocalScale: {{x: 1, y: 1, z: 1}}\n  m_ConstrainProportionsScale: 0\n  m_Children: []\n  m_Father: {{fileID: {parent}}}\n  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}\n");
                text = text.Replace(instance.Value, replacement.ToString());
                foreach (var old in stripped) text = text.Replace(old.Value, "");
                text = text.Replace("  - {fileID: " + instanceId + "}", "  - {fileID: " + transformId + "}");
                changed = true;
            }
        }
        if (changed) { File.WriteAllText(path, text, new UTF8Encoding(false)); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); }
        return changed;
    }
}
