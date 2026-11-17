using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;
using Object = UnityEngine.Object;

public static class GreyboxZooWeaponSetup
{
    public const string Zoo = "Assets/Scenes/ZooScene.unity";
    public const string DisplayName = "Arm Weapons 5-13 - Ground Pickups";
    [MenuItem("Tools/Mech/Greybox Arm Weapons/Place Individual Pickups in Zoo")]
    public static void PlaceZooPickups()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before placing scene pickups.");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != Zoo)
            throw new InvalidOperationException("Open ZooScene first; this tool only adds pickups to the existing Zoo parts area.");
        var parts = GameObject.Find("parts");
        if (parts == null) throw new InvalidOperationException("Zoo parts display is missing.");
        foreach (var hint in parts.GetComponentsInChildren<TMP_Text>(true))
            if (hint.text == "Swap parts on the ground with F, cycle with V")
            { hint.text = "Swap ground parts with 1 / 2, cycle with V"; EditorUtility.SetDirty(hint); }
        var group = parts.transform.Find(DisplayName);
        if (group == null)
        {
            group = new GameObject(DisplayName).transform; Undo.RegisterCreatedObjectUndo(group.gameObject, "Add Zoo weapon pickups");
            group.SetParent(parts.transform, false); group.SetPositionAndRotation(Vector3.zero, Quaternion.identity); group.localScale = Vector3.one;
        }
        for (int i = 0; i < 9; i++)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GreyboxArmWeaponSetup.PrefabPath(i));
            if (prefab == null) throw new InvalidOperationException("Create the individual greybox weapon prefabs first.");
            var definition = prefab.GetComponent<Part>().data;
            var pickup = group.GetComponentsInChildren<Part>(true).FirstOrDefault(p => p.data == definition);
            if (pickup == null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
                Undo.RegisterCreatedObjectUndo(instance, "Place individual weapon"); pickup = instance.GetComponent<Part>();
            }
            pickup.name = (i + 5).ToString("00") + " " + definition.partName;
            Vector3 cell = new Vector3(25 + i % 3 * 6, 0, -52 + i / 3 * 7);
            var cube = pickup.transform.Find("Cube");
            // These test primitives sit on the same flat ground as the Zoo.
            // Position by their visible cube centers, not their offset socket pivots.
            pickup.transform.SetPositionAndRotation(cell - Vector3.forward * cube.localPosition.z + Vector3.up * (cube.localScale.y * .5f + .08f), Quaternion.identity);
            pickup.transform.localScale = Vector3.one; pickup.isStartingPart = pickup.isEquipped = false;
            pickup.GetComponent<GreyboxArmWeapon>().keepAuthoredStats = true;
            PrefabUtility.RecordPrefabInstancePropertyModifications(pickup.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(pickup);
            PrefabUtility.RecordPrefabInstancePropertyModifications(pickup.GetComponent<GreyboxArmWeapon>());
            var label = group.Find("Label " + (i + 5));
            if (label == null) { label = new GameObject("Label " + (i + 5), typeof(TextMesh)).transform; label.SetParent(group, false); }
            label.SetPositionAndRotation(cell + new Vector3(0, .15f, -2.7f), Quaternion.Euler(50, 0, 0));
            var text = label.GetComponent<TextMesh>(); text.text = (i + 5) + "  " + definition.partName;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.fontSize = 48; text.characterSize = .09f;
            text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center; text.color = new Color(.12f,.15f,.18f);
            text.GetComponent<Renderer>().sharedMaterial = text.font.material;
        }
        EditorSceneManager.MarkSceneDirty(parts.scene); Selection.activeGameObject = group.gameObject;
        Debug.Log("ZOO GROUND WEAPONS COMPLETE: 9 separate prefab pickups, beside the original parts display.");
    }
    public static void PlaceZooForReview()
    {
        if (!Application.isBatchMode || !Application.dataPath.Contains(".quest-review")) throw new InvalidOperationException("Disposable project only.");
        EditorSceneManager.OpenScene(Zoo); PlaceZooPickups();
        // Check repeat application without leaving duplicate physical pickups.
        PlaceZooPickups();
        var parts = GameObject.Find("parts").transform.Find(DisplayName).GetComponentsInChildren<Part>(true);
        if (parts.Length != 9) throw new InvalidOperationException("Zoo pickup placement duplicated weapons.");
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene()); EditorApplication.Exit(0);
    }
    public static void InspectZooForReview()
    {
        if (!Application.isBatchMode || !Application.dataPath.Contains(".quest-review")) throw new InvalidOperationException("Disposable project only.");
        EditorSceneManager.OpenScene(Zoo);
        foreach (var part in Object.FindObjectsByType<Part>(FindObjectsInactive.Include).Where(p => !p.isEquipped && p.GetComponentInParent<CombatHealth>() == null))
            Debug.Log("ZOO PICKUP: " + part.name + " | " + (part.data != null ? part.data.partName : "missing data") + " | pos " + part.transform.position.ToString("F2") + " | scale " + part.transform.lossyScale.ToString("F2") + " | parent " + (part.transform.parent != null ? part.transform.parent.name : "root"));
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()) Debug.Log("ZOO ROOT: " + root.name + " | " + root.transform.position.ToString("F2"));
        Debug.Log("ZOO INSPECTION COMPLETE"); EditorApplication.Exit(0);
    }
}
