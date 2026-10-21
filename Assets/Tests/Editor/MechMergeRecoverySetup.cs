using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Operate on a disposable batch project, then review/copy the saved prefab.
public static class MechMergeRecoverySetup
{
    const string Player = "Assets/Prefabs/Mech/Base/FullPlayer_mech_base.prefab";
    public static void RecoverBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Disposable batch project only.");
        GameObject player = null, backup = null;
        try
        {
            player = PrefabUtility.LoadPrefabContents(Player);
            backup = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Mech/Base/FullPlayer_mech_base Folder Back Up.prefab");
            RestoreComponent<MechEquipmentLocomotion>(backup, player);
            RestoreComponent<MechLocomotionDebug>(backup, player);
            RestoreComponent<MechUpperBodyAnimation>(backup, player);
            var parts = player.GetComponentsInChildren<Part>(true);
            var oldLegs = parts.Single(p => p.partType == PartType.Legs);
            if (oldLegs.GetComponent<MechLegAnimator>() == null)
            {
                var socket = oldLegs.transform.parent;
                Object.DestroyImmediate(oldLegs.gameObject);
                var module = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Locomotion/BipedFast_Module.prefab");
                var legs = (GameObject)PrefabUtility.InstantiatePrefab(module, socket);
                legs.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                legs.transform.localScale = Vector3.one;
                legs.GetComponent<Part>().isStartingPart = true;
            }
            var torso = parts.Single(p => p.partType == PartType.Torso);
            var head = parts.Single(p => p.partType == PartType.Head);
            var savedParts = backup.GetComponentsInChildren<Part>(true);
            var savedTorso = savedParts.Single(p => p.partType == PartType.Torso);
            var savedHead = savedParts.Single(p => p.partType == PartType.Head);
            // Socket/model calibration is identical to the tested backup. Keep the
            // merged weapon socket placements and visuals; restore only pose routing.
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
            RestoreReceiver(savedTorso, torso, torso.transform.Cast<Transform>()
                .Where(t => t == visual || t.GetComponent<Socket>() != null && t.GetComponent<Socket>().socketType != PartCategory.Legs).ToArray());
            RestoreReceiver(savedHead, head, head.transform.Cast<Transform>().ToArray());
            PrefabUtility.SaveAsPrefabAsset(player, Player);
            // Both new spawners arrived with empty enemyPrefab fields in the merge.
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Merge Test.unity");
            foreach (var spawner in Object.FindObjectsByType<EnemySpawnerManager>())
            {
                var data = new SerializedObject(spawner);
                if (data.FindProperty("enemyPrefab").objectReferenceValue != null) continue;
                string family = spawner.name.EndsWith("_Mace", StringComparison.Ordinal) ? "mace"
                    : spawner.name.EndsWith("_Crossbow", StringComparison.Ordinal) ? "crossbow" : null;
                if (family == null) throw new InvalidOperationException("Unrecognized empty spawner: " + spawner.name);
                var enemy = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Enemy_" + family + ".prefab");
                if (enemy == null || enemy.GetComponent<EnemyAgent>() == null) throw new InvalidOperationException("Missing enemy agent prefab: " + family);
                data.FindProperty("enemyPrefab").objectReferenceValue = enemy;
                data.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene);
            }
            if (!scene.GetRootGameObjects().Any(go => go.name == "Heavy Legs Pickup"))
            {
                var savedScene = EditorSceneManager.OpenScene("Assets/Scenes/Merge Test Back Up.unity", OpenSceneMode.Additive);
                try
                {
                    var savedPickup = savedScene.GetRootGameObjects().Single(go => go.name == "Heavy Legs Pickup");
                    var heavy = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Locomotion/BipedHeavy_Module.prefab");
                    var pickup = (GameObject)PrefabUtility.InstantiatePrefab(heavy, scene);
                    pickup.name = savedPickup.name;
                    pickup.transform.SetPositionAndRotation(savedPickup.transform.position, savedPickup.transform.rotation);
                    pickup.transform.localScale = savedPickup.transform.localScale;
                    EditorSceneManager.MarkSceneDirty(scene);
                }
                finally { EditorSceneManager.CloseScene(savedScene, true); }
            }
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("MERGE RECOVERY COMPLETE");
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); return; }
        finally
        {
            if (player != null) PrefabUtility.UnloadPrefabContents(player);
            if (backup != null) PrefabUtility.UnloadPrefabContents(backup);
        }
        EditorApplication.Exit(0);
    }

    static void RestoreComponent<T>(GameObject source, GameObject target) where T : Component
    {
        var saved = source.GetComponent<T>();
        if (saved == null) throw new InvalidOperationException("Backup lacks " + typeof(T).Name);
        var component = target.GetComponent<T>() ?? target.AddComponent<T>();
        EditorUtility.CopySerialized(saved, component);
    }

    static void RestoreReceiver(Part saved, Part target, Transform[] targets)
    {
        RestoreComponent<MechPartPoseReceiver>(saved.gameObject, target.gameObject);
        var data = new SerializedObject(target.GetComponent<MechPartPoseReceiver>());
        var array = data.FindProperty("targets");
        array.arraySize = targets.Length;
        for (int i = 0; i < targets.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = targets[i];
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    public static void InspectBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Disposable batch project only.");
        try
        {
            foreach (string path in new[] { Player,
                "Assets/Prefabs/Mech/Base/FullPlayer_mech_base Folder Back Up.prefab",
                "Assets/Prefabs/Mech/Base/FullPlayer_mech_base Scene Back Up.prefab" })
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                Debug.Log("MERGE PREFAB " + path + " root=" + string.Join(",", root.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name)));
                foreach (var part in root.GetComponentsInChildren<Part>(true))
                    Debug.Log("MERGE PART " + AnimationUtility.CalculateTransformPath(part.transform, root.transform)
                        + " category=" + part.partType + " data=" + (part.data != null ? part.data.name : "NULL")
                        + " local=" + part.transform.localPosition + " rotation=" + part.transform.localEulerAngles
                        + " scale=" + part.transform.localScale + " authored=" + part.authoredScale
                        + " components=" + string.Join(",", part.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name)));
                foreach (var socket in root.GetComponentsInChildren<Socket>(true))
                    Debug.Log("MERGE SOCKET " + AnimationUtility.CalculateTransformPath(socket.transform, root.transform)
                        + " type=" + socket.socketType + " local=" + socket.transform.localPosition + " scale=" + socket.transform.localScale);
                PrefabUtility.UnloadPrefabContents(root);
            }
            EditorSceneManager.OpenScene("Assets/Scenes/Merge Test.unity");
            foreach (var motor in Object.FindObjectsByType<MechMovementController>())
                Debug.Log("MERGE SCENE PLAYER " + motor.name + " source=" + PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(motor.gameObject)
                    + " overrides=" + JsonUtility.ToJson(motor));
            foreach (var spawner in Object.FindObjectsByType<EnemySpawnerManager>())
                Debug.Log("MERGE SPAWNER " + spawner.name + " " + JsonUtility.ToJson(spawner));
            Debug.Log("MERGE INSPECTION COMPLETE");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
