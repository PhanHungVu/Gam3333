using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Asset migration only: the old cannon stays available as an archived authoring asset.
public static class GreyCannonReplacementSetup
{
    public const string LegacyPath = "Assets/Prefabs/Mech/Base/Parts/shoulder_cannon.prefab";
    public const string ActivePath = SpecialistPlayerIntegrationSetup.CannonPath;
    public static readonly List<string> ChangedPaths = new List<string>();
    static GameObject replacement;
    static PartData cannonData;

    [MenuItem("Tools/Mech/Replace Active Cannons With Specialist Model")]
    public static void Apply()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before replacing cannon assets.");
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        replacement = AssetDatabase.LoadAssetAtPath<GameObject>(ActivePath);
        cannonData = replacement.GetComponent<Part>().data;
        ChangedPaths.Clear();
        var sceneSetup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            foreach (var path in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p))
            {
                if (path == LegacyPath || path == ActivePath || path.Contains("/Old/") || !Candidate(path)) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    bool changed = UpgradeTree(root, false, new[] { root });
                    if (changed) { PrefabUtility.SaveAsPrefabAsset(root, path); ChangedPaths.Add(path); }
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            foreach (var path in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p))
            {
                var scene = EditorSceneManager.OpenScene(path); bool changed = false;
                var roots = scene.GetRootGameObjects();
                foreach (var root in roots) changed |= UpgradeTree(root, true, scene.GetRootGameObjects());
                if (changed) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); ChangedPaths.Add(path); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("GREY CANNON REPLACEMENT COMPLETE: " + string.Join(", ", ChangedPaths));
        }
        finally
        {
            if (!Application.isBatchMode && sceneSetup.Length > 0 && sceneSetup.All(s => !string.IsNullOrEmpty(s.path))) EditorSceneManager.RestoreSceneManagerSetup(sceneSetup);
        }
    }

    static bool Candidate(string path)
    {
        string text = File.ReadAllText(path);
        return text.Contains("55c3987ff5a5221488425f98040b8852") || text.Contains("2f1b7738a6e6f3c49884f79c552dc2dc") || text.Contains("16cd307188044ac4a832470ffad5ddd7") || text.Contains("d476b62987313ac46aa043ff618a1075");
    }

    public static bool IsCannon(Part part)
    {
        if (part == null) return false;
        var driver = part.GetComponent<MechWeaponAnimator>();
        return part.data != null && part.data.partId == "backweaponA3" || driver != null && driver.Animations != null
            && (driver.Animations.name == "Cannon" || driver.Animations.name == "Cannon_Specialist");
    }
    public static bool IsLegacy(Part part) => IsCannon(part)
        && !part.GetComponentsInChildren<MeshFilter>(true).Any(f => f.sharedMesh != null && f.GetComponentInParent<Part>(true) == part && AssetDatabase.GetAssetPath(f.sharedMesh).EndsWith("/SHOULDER_Cannon.fbx", StringComparison.OrdinalIgnoreCase));

    static bool UpgradeTree(GameObject root, bool scene, GameObject[] owners)
    {
        bool changed = false;
        foreach (var part in root.GetComponentsInChildren<Part>(true).Where(IsLegacy).ToArray())
        {
            if (scene && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(part.gameObject) == LegacyPath
                && !part.isEquipped && part.transform.parent?.GetComponent<Socket>() == null)
                ReplacePickup(part, owners);
            else UpgradeCarriedCannon(part, owners);
            changed = true;
        }
        changed |= Remap(owners, new Dictionary<Object, Object>());
        return changed;
    }

    static void UpgradeCarriedCannon(Part part, GameObject[] owners)
    {
        if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(part.gameObject) == LegacyPath && PrefabUtility.IsAnyPrefabInstanceRoot(part.gameObject))
            PrefabUtility.UnpackPrefabInstance(part.gameObject, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
        var driver = part.GetComponent<MechWeaponAnimator>();
        if (driver != null) driver.Animations.neutral.SampleAnimation(driver.Animator.gameObject, 0);
        var mount = part.GetComponentsInChildren<Transform>(true).Single(t => t.name == "WeapRoot");
        Vector3 anchor = mount.position, localAnchor = part.transform.InverseTransformPoint(anchor);
        var parentSocket = part.transform.parent != null ? part.transform.parent.GetComponent<Socket>() : null;
        var equipment = part.GetComponentInParent<EquipManager>();
        if (equipment != null)
        {
            foreach (var socket in equipment.GetComponentsInChildren<Socket>(true).Where(s => s.socketType == PartCategory.ShoulderL || s.socketType == PartCategory.ShoulderR))
                AddMountIfMissing(socket, localAnchor);
        }
        else if (parentSocket != null) AddMountIfMissing(parentSocket, localAnchor);

        var oldChildren = part.transform.Cast<Transform>().ToArray();
        var template = Object.Instantiate(replacement); template.SetActive(false);
        try
        {
            var source = template.GetComponent<Part>(); var sourceDriver = template.GetComponent<MechWeaponAnimator>();
            if (part.data == null) part.data = cannonData;
            var map = new Dictionary<Object, Object>();
            if (part.muzzle != null) map[part.muzzle] = source.muzzle;
            if (driver != null && driver.Animator != null) map[driver.Animator] = sourceDriver.Animator;
            foreach (var child in template.transform.Cast<Transform>().ToArray()) child.SetParent(part.transform, false);
            part.muzzle = source.muzzle; part.recoilVisual = source.recoilVisual;
            if (driver == null) driver = part.gameObject.AddComponent<MechWeaponAnimator>();
            var fields = new SerializedObject(driver);
            fields.FindProperty("animator").objectReferenceValue = sourceDriver.Animator;
            fields.FindProperty("animations").objectReferenceValue = sourceDriver.Animations;
            fields.FindProperty("placement").objectReferenceValue = sourceDriver.Placement;
            fields.FindProperty("loadedProjectileRenderers").arraySize = 0;
            fields.ApplyModifiedPropertiesWithoutUndo();
            var visual = part.GetComponent<WeaponVisualSettings>();
            if (visual == null) { visual = part.gameObject.AddComponent<WeaponVisualSettings>(); EditorUtility.CopySerialized(template.GetComponent<WeaponVisualSettings>(), visual); }
            visual.trailAnchor = part.muzzle;
            var box = part.GetComponent<BoxCollider>();
            if (box == null) box = part.gameObject.AddComponent<BoxCollider>();
            EditorUtility.CopySerialized(template.GetComponent<BoxCollider>(), box);
            Remap(owners, map);
            foreach (var child in oldChildren) Object.DestroyImmediate(child.gameObject);
            // New native model's origin is its mount; old rig had a baked translation above the part root.
            part.transform.position = anchor; part.name = replacement.name;
            if (part.isEquipped) foreach (var collider in part.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            EditorUtility.SetDirty(part); EditorUtility.SetDirty(driver); EditorUtility.SetDirty(visual);
        }
        finally { Object.DestroyImmediate(template); }
    }

    static void AddMountIfMissing(Socket socket, Vector3 legacyAnchor)
    {
        var fields = new SerializedObject(socket); var entries = fields.FindProperty("weaponAttachments");
        var animation = replacement.GetComponent<MechWeaponAnimator>().Animations;
        for (int i = 0; i < entries.arraySize; i++) if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("animations").objectReferenceValue == animation) return;
        var old = AssetDatabase.LoadAssetAtPath<GameObject>(LegacyPath).GetComponent<Part>();
        Pose prior = socket.AttachmentFor(old);
        int index = entries.arraySize++; var entry = entries.GetArrayElementAtIndex(index);
        entry.FindPropertyRelative("animations").objectReferenceValue = animation;
        entry.FindPropertyRelative("position").vector3Value = prior.position + prior.rotation * legacyAnchor;
        entry.FindPropertyRelative("eulerAngles").vector3Value = prior.rotation.eulerAngles;
        fields.ApplyModifiedPropertiesWithoutUndo();
    }

    static void ReplacePickup(Part old, GameObject[] owners)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(replacement, old.gameObject.scene);
        instance.transform.SetParent(old.transform.parent, false);
        instance.transform.localPosition = old.transform.localPosition; instance.transform.localRotation = old.transform.localRotation; instance.transform.localScale = old.transform.localScale;
        instance.transform.SetSiblingIndex(old.transform.GetSiblingIndex()); instance.SetActive(old.gameObject.activeSelf);
        var part = instance.GetComponent<Part>(); part.data = old.data; part.isStartingPart = old.isStartingPart; part.isEquipped = old.isEquipped;
        var map = new Dictionary<Object, Object> { [old.gameObject] = instance, [old.transform] = instance.transform, [old] = part };
        foreach (var component in old.GetComponents<Component>().Where(c => c != null && !(c is Transform) && !(c is Part)))
        {
            var target = instance.GetComponent(component.GetType());
            if (target == null) { target = instance.AddComponent(component.GetType()); EditorUtility.CopySerialized(component, target); }
            map[component] = target;
        }
        if (old.muzzle != null) map[old.muzzle] = part.muzzle;
        Remap(owners.Concat(new[] { instance }).Distinct().ToArray(), map);
        PrefabUtility.RecordPrefabInstancePropertyModifications(part);
        PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
        PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
        Object.DestroyImmediate(old.gameObject);
    }

    static bool Remap(GameObject[] roots, Dictionary<Object, Object> map)
    {
        bool changed = false;
        foreach (var component in roots.Where(r => r != null).SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true)).Where(c => c != null).Distinct())
        {
            var fields = new SerializedObject(component); var p = fields.GetIterator();
            while (p.NextVisible(true))
            {
                if (p.propertyType != SerializedPropertyType.ObjectReference || p.objectReferenceValue == null) continue;
                var old = p.objectReferenceValue; Object target;
                if (map.TryGetValue(old, out target)) p.objectReferenceValue = target;
                else if (AssetDatabase.GetAssetPath(old) == LegacyPath)
                {
                    target = old is GameObject ? replacement : old is Component c ? replacement.GetComponent(c.GetType()) : null;
                    if (target == null) throw new InvalidOperationException("Unsupported legacy cannon reference: " + component.name + "." + p.propertyPath);
                    p.objectReferenceValue = target;
                }
            }
            if (fields.ApplyModifiedPropertiesWithoutUndo())
            {
                EditorUtility.SetDirty(component); PrefabUtility.RecordPrefabInstancePropertyModifications(component); changed = true;
            }
        }
        return changed;
    }
}
