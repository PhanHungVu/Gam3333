using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Reconcile in a disposable project; never replace the merged hierarchy wholesale.
public static class MechMerge2RecoverySetup
{
    const string Player = "Assets/Prefabs/Mech/Base/FullPlayer_mech_base.prefab";
    const string Backup = "Assets/Prefabs/Mech/Base/FullPlayer_mech_base Folder Back Up Merge 2.prefab";
    const string Scene = "Assets/Scenes/Merge Test.unity";
    const string SceneBackup = "Assets/Scenes/Merge Test Back Up Merge 2.unity";

    public static void RecoverBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Disposable project only.");
        try
        {
            RecoverPlayer();
            RecoverScene();
            AssetDatabase.SaveAssets();
            Debug.Log("MERGE2 RECOVERY COMPLETE");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    static void RecoverPlayer()
    {
        var player = PrefabUtility.LoadPrefabContents(Player);
        var backup = PrefabUtility.LoadPrefabContents(Backup);
        try
        {
            string movement = EditorJsonUtility.ToJson(player.GetComponent<MechMovementController>());
            string combat = EditorJsonUtility.ToJson(player.GetComponent<MechCombatController>());
            int particles = player.GetComponentsInChildren<ParticleSystem>(true).Length;
            var parts = player.GetComponentsInChildren<Part>(true);
            var savedParts = backup.GetComponentsInChildren<Part>(true);
            var sockets = player.GetComponentsInChildren<Socket>(true);
            // Receiver pivots can be reused because the model and sockets still
            // have the tested calibration. Fail instead of applying a wrong rig.
            foreach (var socket in sockets)
            {
                var saved = backup.GetComponentsInChildren<Socket>(true).Single(s => s.socketType == socket.socketType);
                if ((socket.transform.localPosition - saved.transform.localPosition).sqrMagnitude > 0.00001f
                    || Quaternion.Angle(socket.transform.localRotation, saved.transform.localRotation) > 0.01f)
                    throw new InvalidOperationException("Socket calibration changed: " + socket.socketType);
            }
            RestoreComponent<MechEquipmentLocomotion>(backup, player);
            RestoreComponent<MechLocomotionDebug>(backup, player);
            RestoreComponent<MechUpperBodyAnimation>(backup, player);
            RestoreComponent<CombatHealth>(backup, player);

            var legs = parts.Single(p => p.partType == PartCategory.Legs);
            if (legs.GetComponent<MechLegAnimator>() == null)
            {
                var socket = legs.transform.parent;
                Object.DestroyImmediate(legs.gameObject);
                var module = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Locomotion/BipedFast_Module.prefab");
                var animated = (GameObject)PrefabUtility.InstantiatePrefab(module, socket);
                animated.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                animated.transform.localScale = Vector3.one;
                animated.GetComponent<Part>().isStartingPart = true;
            }
            var torso = parts.Single(p => p.partType == PartCategory.Torso);
            var visual = torso.transform.Find("Animated Torso Visual");
            if (visual == null)
            {
                visual = new GameObject("Animated Torso Visual").transform;
                visual.SetParent(torso.transform, false);
                visual.gameObject.AddComponent<MeshFilter>().sharedMesh = torso.GetComponent<MeshFilter>().sharedMesh;
                EditorUtility.CopySerialized(torso.GetComponent<MeshRenderer>(), visual.gameObject.AddComponent<MeshRenderer>());
            }
            visual.GetComponent<MeshRenderer>().enabled = true;
            torso.GetComponent<MeshRenderer>().enabled = false;
            RestoreReceiver(savedParts.Single(p => p.partType == PartCategory.Torso), torso, torso.transform.Cast<Transform>()
                .Where(t => t == visual || t.GetComponent<Socket>() != null && t.GetComponent<Socket>().socketType != PartCategory.Legs).ToArray());
            var head = parts.Single(p => p.partType == PartCategory.Head);
            RestoreReceiver(savedParts.Single(p => p.partType == PartCategory.Head), head, head.transform.Cast<Transform>().ToArray());

            var settings = AssetDatabase.LoadAssetAtPath<MechWeaponAnimationImportSettings>(MechWeaponAnimationImport.SettingsPath);
            if (settings == null || settings.source == null) throw new InvalidOperationException("Saved weapon animation references are missing.");
            foreach (var part in parts.Where(p => p != null && (p.partType == PartCategory.ArmL || p.partType == PartCategory.ArmR
                || p.partType == PartCategory.ShoulderL || p.partType == PartCategory.ShoulderR)))
            {
                if (part.GetComponent<MechWeaponAnimator>() != null) continue;
                var entry = settings.weapons.Single(e => e.part == part.data);
                MechWeaponAnimationSetup.BuildRig(part, settings.source, entry);
            }
            if (player.GetComponent<PartSwapHUDScript>() == null || player.GetComponentInChildren<PickupDetector>().highlightElementPrefab == null)
                throw new InvalidOperationException("Teammate's selection UI must survive recovery.");
            if (EditorJsonUtility.ToJson(player.GetComponent<MechMovementController>()) != movement
                || EditorJsonUtility.ToJson(player.GetComponent<MechCombatController>()) != combat
                || player.GetComponentsInChildren<ParticleSystem>(true).Length != particles)
                throw new InvalidOperationException("Recovery changed teammate controller tuning or VFX.");
            PrefabUtility.SaveAsPrefabAsset(player, Player);
            Debug.Log("MERGE2 PLAYER RESTORED; teammate movement/combat tuning, sockets, selection UI and particles preserved");
        }
        finally { PrefabUtility.UnloadPrefabContents(player); PrefabUtility.UnloadPrefabContents(backup); }
    }

    static void RecoverScene()
    {
        var scene = EditorSceneManager.OpenScene(Scene);
        var savedScene = EditorSceneManager.OpenScene(SceneBackup, OpenSceneMode.Additive);
        try
        {
            foreach (string name in new[] { "Heavy Legs Pickup", "Mace Animation Pickup", "Arm Crossbow Animation Pickup" })
            {
                if (scene.GetRootGameObjects().Any(go => go.name == name)) continue;
                var saved = savedScene.GetRootGameObjects().Single(go => go.name == name);
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(saved));
                var pickup = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
                pickup.name = name;
                pickup.transform.SetPositionAndRotation(saved.transform.position, saved.transform.rotation);
                pickup.transform.localScale = saved.transform.localScale;
                pickup.GetComponent<Part>().isStartingPart = false;
            }
        }
        finally { EditorSceneManager.CloseScene(savedScene, true); }
        var player = scene.GetRootGameObjects().Single(go => go.GetComponent<MechMovementController>() != null);
        if (player.GetComponent<CombatHealth>() == null || player.GetComponent<MechEquipmentLocomotion>() == null)
            throw new InvalidOperationException("Scene does not inherit the restored player systems.");
        if (scene.GetRootGameObjects().Count(go => go.GetComponent<EnemySpawnerManager>() != null) != 2
            || GameObject.Find("Canvas/PartSwapHUD") == null || GameObject.Find("Canvas/PartSwapPopup") == null)
            throw new InvalidOperationException("Merged spawners or selection canvas are missing.");
        EditorSceneManager.MarkSceneDirty(scene);
        MechHealthHudSetup.PlaceInScene(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("MERGE2 SCENE RESTORED; merged camera, canvas, spawner prefabs and course retained");
    }

    static void RestoreComponent<T>(GameObject saved, GameObject target) where T : Component
    {
        var source = saved.GetComponent<T>();
        if (source == null) throw new InvalidOperationException("Backup lacks " + typeof(T).Name);
        EditorUtility.CopySerialized(source, target.GetComponent<T>() ?? target.AddComponent<T>());
    }

    static void RestoreReceiver(Part saved, Part target, Transform[] targets)
    {
        RestoreComponent<MechPartPoseReceiver>(saved.gameObject, target.gameObject);
        var data = new SerializedObject(target.GetComponent<MechPartPoseReceiver>());
        var array = data.FindProperty("targets"); array.arraySize = targets.Length;
        for (int i = 0; i < targets.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = targets[i];
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    public static void InspectBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Disposable project only.");
        try
        {
            foreach (string path in new[] { Player, Backup })
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Debug.Log("MERGE2 ROOT " + path + " " + string.Join(",", root.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name)));
                    foreach (var part in root.GetComponentsInChildren<Part>(true))
                        Debug.Log("MERGE2 PART " + part.name + " path=" + AnimationUtility.CalculateTransformPath(part.transform, root.transform)
                            + " type=" + part.partType + " data=" + (part.data != null ? part.data.name : "NULL")
                            + " starting=" + part.isStartingPart + " equipped=" + part.isEquipped
                            + " local=" + part.transform.localPosition + " rotation=" + part.transform.localEulerAngles
                            + " scale=" + part.transform.localScale + " authored=" + part.authoredScale
                            + " mesh=" + string.Join(",", part.GetComponentsInChildren<MeshFilter>(true).Where(m => m.sharedMesh != null && m.GetComponentInParent<Part>() == part).Select(m => m.sharedMesh.name)));
                    foreach (var socket in root.GetComponentsInChildren<Socket>(true))
                        Debug.Log("MERGE2 SOCKET " + AnimationUtility.CalculateTransformPath(socket.transform, root.transform)
                            + " type=" + socket.socketType + " local=" + socket.transform.localPosition + " rotation=" + socket.transform.localEulerAngles + " scale=" + socket.transform.localScale);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            foreach (var path in new[] { Scene, SceneBackup })
            {
                var scene = EditorSceneManager.OpenScene(path);
                foreach (var root in scene.GetRootGameObjects())
                    Debug.Log("MERGE2 SCENE ROOT " + path + " " + root.name + " pos=" + root.transform.position
                        + " source=" + PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root));
                foreach (var spawner in Object.FindObjectsByType<EnemySpawnerManager>())
                    Debug.Log("MERGE2 SPAWNER " + path + " " + spawner.name + " " + JsonUtility.ToJson(spawner));
                foreach (var motor in Object.FindObjectsByType<MechMovementController>())
                    Debug.Log("MERGE2 MOTOR " + path + " " + motor.name + " " + JsonUtility.ToJson(motor));
            }
            Debug.Log("MERGE2 INSPECTION COMPLETE");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
