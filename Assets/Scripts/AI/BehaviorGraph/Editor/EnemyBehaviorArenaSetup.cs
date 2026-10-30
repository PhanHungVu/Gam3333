using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class EnemyBehaviorArenaSetup
{
    public const string ScenePath = "Assets/Scenes/EnemyBehaviorPlaytest.unity";
    [MenuItem("Tools/Mech/Enemies/Create Behavior Playtest Arena")]
    public static void CreateScene()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) { Debug.Log("Arena already exists; it is preserved: " + ScenePath); return; }
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EnemyRosterSetup.UpgradeAssets(); EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Prototype ground"; floor.transform.position = new Vector3(0,-.25f,0); floor.transform.localScale = new Vector3(200,.5f,200);
        var light = new GameObject("Sun").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.3f; light.shadows = LightShadows.Soft; light.transform.rotation = Quaternion.Euler(55,-30,0);
        var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PartSwapWorldUiSetup.PlayerPath)); player.transform.position = new Vector3(0,1.6f,0); player.transform.rotation = Quaternion.identity;
        var camera = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Main Camera.prefab"));
        var cameraFields = new SerializedObject(camera.GetComponent<CameraControlThirdPerson>()); cameraFields.FindProperty("target").objectReferenceValue = player.transform; cameraFields.ApplyModifiedPropertiesWithoutUndo();
        var navigation = new GameObject("Shared navigation"); var grid = navigation.AddComponent<NavGrid>(); var flow = navigation.AddComponent<FlowField>();
        var gridFields = new SerializedObject(grid); gridFields.FindProperty("drawOpenCells").boolValue = false; gridFields.FindProperty("drawBlockedCells").boolValue = false; gridFields.ApplyModifiedPropertiesWithoutUndo();
        var flowFields = new SerializedObject(flow); flowFields.FindProperty("drawArrows").boolValue = false; flowFields.ApplyModifiedPropertiesWithoutUndo();
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Pathfinding obstacle"; wall.tag = "Obstacle"; wall.transform.position = new Vector3(0,1,8); wall.transform.localScale = new Vector3(6,2,1);
        var sword = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(EnemyRosterSetup.Roster[0])); sword.transform.position = new Vector3(0,1,16); sword.transform.rotation = Quaternion.LookRotation(Vector3.back);
        var controls = new GameObject("Enemy Behavior Playtest"); var director = controls.AddComponent<SpawnDirector>(); var spawn = controls.AddComponent<RoamingSpawner>();
        var fields = new SerializedObject(controls.AddComponent<EnemyBehaviorPlaytest>());
        fields.FindProperty("player").objectReferenceValue = player.transform; fields.FindProperty("director").objectReferenceValue = director;
        fields.FindProperty("spawnPoint").objectReferenceValue = spawn; fields.FindProperty("roaming").objectReferenceValue = AssetDatabase.LoadAssetAtPath<WaveData>(EnemyRosterSetup.Roaming);
        fields.FindProperty("survival").objectReferenceValue = AssetDatabase.LoadAssetAtPath<WaveData>(EnemyRosterSetup.Survival);
        var roster = fields.FindProperty("roster"); roster.arraySize = EnemyRosterSetup.Roster.Length;
        for (int i=0;i<roster.arraySize;i++) roster.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyRosterSetup.Roster[i]);
        fields.ApplyModifiedPropertiesWithoutUndo(); EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),ScenePath);
        AssetDatabase.SaveAssets(); Debug.Log("ENEMY BEHAVIOR PLAYTEST ARENA READY: " + ScenePath);
    }
}
