using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class MechWeaponAnimationSetup
{
    public const string Source = "Assets/Anim/golem2_v3.glb";
    public static void BuildBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Disposable batch project only.");
        try
        {
            Folder("Assets/Anim/Weapons"); Folder("Assets/Anim/Weapons/Editor");
            var settings = AssetDatabase.LoadAssetAtPath<MechWeaponAnimationImportSettings>(MechWeaponAnimationImport.SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<MechWeaponAnimationImportSettings>();
                settings.source = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
                settings.weapons = new[]
                {
                    Seed("Cannon", "PartData_Shoulder_Cannon", "CanonRig", "Canon", "Cannon", "CanonBody"),
                    Seed("Crossbow", "PartData_Shoulder_LargeCrossbow", "CrossBowRig", "CrossBow", "Crossbow", "CrossBowBody"),
                    Seed("ArmCrossbow", "PartData_Arm_SmallCrossbow", "CrossBowArmRig", "CrossBowArm", "CrossbowArm", "CrossBowArmBody"),
                    Seed("Sword", "PartData_Arm_Sword", "SwordArmRig", "SwordArm", "SwordArm", "SwordArmBody"),
                    Seed("Mace", "PartData_Arm_Mace", "MaceArmRig", "MaceArm", "MaceArm", "MaceArmBody"),
                    Seed("ThrowingAxe", "PartData_Arm_ThrowingAxe", "ThrowingAxeRig", "ThrowingAxe", "ThrowingAxeArm", "ThrowingAxeBody", "Barrel")
                };
                AssetDatabase.CreateAsset(settings, MechWeaponAnimationImport.SettingsPath);
            }
            MechWeaponAnimationImport.RefreshClips();
            var paths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Mech/Base/Parts", "Assets/Prefabs/Enemies" })
                .Select(AssetDatabase.GUIDToAssetPath).Where(p => !p.Contains("/Old/")).ToList();
            paths.Add(MechAnimatedLegsSetup.PlayerPath);
            foreach (var path in paths)
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    bool changed = false;
                    foreach (var part in root.GetComponentsInChildren<Part>(true))
                    {
                        if (part.partType != PartCategory.ArmL && part.partType != PartCategory.ArmR
                            && part.partType != PartCategory.ShoulderL && part.partType != PartCategory.ShoulderR) continue;
                        var meshes = part.GetComponentsInChildren<MeshFilter>(true)
                            .Where(m => m.GetComponentInParent<Part>() == part && m.sharedMesh != null).ToArray();
                        var entry = settings.weapons.SingleOrDefault(e => meshes.Any(m => m.sharedMesh.name == e.referenceMesh + "Mesh"));
                        if (entry != null && part.data != entry.part)
                        {
                            Debug.Log("WEAPON DATA CORRECTED " + path + "/" + part.name + " -> " + entry.part.name);
                            part.data = entry.part; changed = true;
                        }
                        if (entry == null || part.GetComponent<MechWeaponAnimator>() != null) continue;
                        BuildRig(part, settings.source, entry);
                        changed = true;
                    }
                    if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            // World pickups make all six sets reviewable through the ordinary F path.
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Merge Test.unity");
            var player = Object.FindAnyObjectByType<MechMovementController>();
            string[] pickupNames = { "arm_mace", "arm_crossbow" };
            for (int i = 0; i < pickupNames.Length; i++)
            {
                string name = i == 0 ? "Mace Animation Pickup" : "Arm Crossbow Animation Pickup";
                if (GameObject.Find(name) != null) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/" + pickupNames[i] + ".prefab");
                var pickup = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                pickup.name = name;
                pickup.transform.SetPositionAndRotation(player.transform.position + new Vector3(-4f - i * 3f, 2f, 2f), Quaternion.identity);
                pickup.transform.localScale = Vector3.one * player.GetComponent<EquipManager>().partScale;
                pickup.GetComponent<Part>().isStartingPart = false;
            }
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("WEAPON ANIMATION BUILD COMPLETE");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    static MechWeaponAnimationImportSettings.Entry Seed(string name, string partName, string rig, string prefix,
        string mesh, string bone, string marker = null, bool preview = false)
    {
        var set = ScriptableObject.CreateInstance<MechWeaponAnimationSet>();
        set.name = name; set.animationOnly = preview;
        AssetDatabase.CreateAsset(set, "Assets/Anim/Weapons/" + name + ".asset");
        var clips = AssetDatabase.LoadAllAssetsAtPath(Source).OfType<AnimationClip>().ToArray();
        AnimationClip Find(string state) => clips.SingleOrDefault(c => c.name == prefix + "_" + state);
        var entry = new MechWeaponAnimationImportSettings.Entry
        {
            part = AssetDatabase.LoadAssetAtPath<PartData>("Assets/PartCatalogue/" + partName + ".asset"),
            rigPath = rig, referenceMesh = mesh, bodyBone = "WeapRoot/" + bone,
            placementBone = marker != null ? "WeapRoot/" + bone + "/" + marker : null,
            neutral = Find("Neutral"), fire = Find("Fire"), reload = Find("Reload"), target = set
        };
        if (entry.part == null || entry.neutral == null || entry.fire == null) throw new InvalidOperationException("Incomplete weapon set: " + name);
        return entry;
    }

    internal static void BuildRig(Part part, GameObject source, MechWeaponAnimationImportSettings.Entry entry)
    {
        var oldMeshes = part.GetComponentsInChildren<MeshFilter>(true)
            .Where(m => m.sharedMesh != null && m.sharedMesh.name.EndsWith("Mesh", StringComparison.Ordinal)
                && m.GetComponentInParent<Part>() == part).ToArray();
        var main = oldMeshes.SingleOrDefault(m => m.sharedMesh.name == entry.referenceMesh + "Mesh");
        if (main == null) throw new InvalidOperationException("Cannot find " + entry.referenceMesh + "Mesh on " + part.name
            + "; meshes=" + string.Join(",", part.GetComponentsInChildren<MeshFilter>(true)
                .Select(m => m.sharedMesh.name + " owner=" + (m.GetComponentInParent<Part>() != null ? m.GetComponentInParent<Part>().name : "NULL"))));
        var wrapper = new GameObject("Weapon Visual Rig");
        wrapper.transform.SetParent(part.transform, false);
        var imported = Object.Instantiate(source.transform.Find(entry.rigPath).gameObject, wrapper.transform);
        imported.name = source.transform.Find(entry.rigPath).name;
        var reference = imported.GetComponentsInChildren<MeshFilter>(true).Single(m => m.name == entry.referenceMesh);
        entry.target.neutral.SampleAnimation(wrapper, 0f);
        // Map the authored rig into the existing mesh's coordinate frame. Reparent
        // original textured meshes with world pose retained, so neutral never pops.
        var matrix = main.transform.localToWorldMatrix * reference.transform.worldToLocalMatrix * wrapper.transform.localToWorldMatrix;
        wrapper.transform.rotation = matrix.rotation;
        wrapper.transform.localScale = Divide(matrix.lossyScale, part.transform.lossyScale);
        wrapper.transform.position = matrix.GetColumn(3);
        var body = imported.transform.Find(entry.bodyBone);
        var marker = string.IsNullOrEmpty(entry.placementBone) ? null : imported.transform.Find(entry.placementBone);
        if (body == null || !string.IsNullOrEmpty(entry.placementBone) && marker == null) throw new InvalidOperationException("Missing weapon bones: " + entry.rigPath);
        foreach (var renderer in imported.GetComponentsInChildren<Renderer>(true)) Object.DestroyImmediate(renderer.gameObject);
        foreach (var mesh in oldMeshes)
        {
            var target = marker != null && mesh.sharedMesh.name == "ThrowingAxeMesh" ? marker : body;
            mesh.transform.SetParent(target, true);
        }
        if (part.muzzle != null) part.muzzle.SetParent(body, true);
        else if (marker != null) part.muzzle = marker;
        // The shoulder crossbow's loaded bolt follows its weapon body as well.
        // It remains attached here; launch/hide behavior is a later projectile step.
        var animator = wrapper.AddComponent<Animator>();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var driver = part.gameObject.AddComponent<MechWeaponAnimator>();
        var data = new SerializedObject(driver);
        data.FindProperty("animator").objectReferenceValue = animator;
        data.FindProperty("animations").objectReferenceValue = entry.target;
        data.FindProperty("placement").objectReferenceValue = marker;
        data.ApplyModifiedPropertiesWithoutUndo();
        if (part.data.weapon == MechPartWeapon.Cannon) part.recoilVisual = null;
        Debug.Log("WEAPON RIG BUILT " + part.name + " set=" + entry.target.name + " meshes=" + oldMeshes.Length
            + " local=" + wrapper.transform.localPosition + " rot=" + wrapper.transform.localEulerAngles + " scale=" + wrapper.transform.localScale);
    }

    static Vector3 Divide(Vector3 a, Vector3 b) => new Vector3(a.x / b.x, a.y / b.y, a.z / b.z);
    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'), System.IO.Path.GetFileName(path));
    }
    public static void InspectBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Disposable batch project only.");
        try
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
            foreach (var node in model.GetComponentsInChildren<Transform>(true))
                Debug.Log("WEAPON NODE " + AnimationUtility.CalculateTransformPath(node, model.transform)
                    + " pos=" + node.localPosition + " rot=" + node.localEulerAngles + " scale=" + node.localScale
                    + " components=" + string.Join(",", node.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name)));
            foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(Source).OfType<AnimationClip>())
                Debug.Log("WEAPON CLIP " + clip.name + " length=" + clip.length + " paths="
                    + string.Join(",", AnimationUtility.GetCurveBindings(clip).Select(b => b.path).Distinct()));
            foreach (var path in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Mech/Base/Parts" })
                .Select(AssetDatabase.GUIDToAssetPath).Where(p => !p.Contains("/Old/")))
            {
                var part = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (var mesh in part.GetComponentsInChildren<MeshFilter>(true))
                    Debug.Log("WEAPON OLD MESH " + path + " " + AnimationUtility.CalculateTransformPath(mesh.transform, part.transform)
                        + " local=" + mesh.transform.localPosition + " rot=" + mesh.transform.localEulerAngles
                        + " world=" + mesh.transform.position + " worldRot=" + mesh.transform.eulerAngles
                        + " scale=" + mesh.transform.lossyScale + " mesh=" + mesh.sharedMesh.name);
            }
            Debug.Log("WEAPON INSPECTION COMPLETE");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
