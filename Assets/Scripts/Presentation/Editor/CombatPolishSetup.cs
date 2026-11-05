using System;
using UnityEditor;
using UnityEngine;

// Updates presentation components only. Does not rebuild characters, HUD layouts or scenes.
public static class CombatPolishSetup
{
    [MenuItem("Tools/Mech/Combat/Apply Weapon Camera and Health Polish")]
    public static void Apply()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before updating presentation assets.");
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Combat/Prototype Combat.mat");
        var shader = Shader.Find("Lithomachia/SoftPuff");
        if (material == null || shader == null) throw new InvalidOperationException("Combat presentation materials are required.");
        const string smokePath = "Assets/Combat/Weapon Smoke.mat";
        var smoke = AssetDatabase.LoadAssetAtPath<Material>(smokePath);
        if (smoke == null) { smoke = new Material(shader) { name = "Weapon Smoke" }; AssetDatabase.CreateAsset(smoke, smokePath); }
        var axe = AssetDatabase.LoadAssetAtPath<PartData>("Assets/PartCatalogue/PartData_Arm_ThrowingAxe.asset");
        var largeBolt = AssetDatabase.LoadAssetAtPath<PartData>("Assets/PartCatalogue/PartData_Shoulder_LargeCrossbow.asset");
        var cannonData = AssetDatabase.LoadAssetAtPath<PartData>("Assets/PartCatalogue/PartData_Shoulder_Cannon.asset");
        foreach (var folder in new[] { "Assets/Prefabs/Mech/Base", "Assets/Prefabs/Enemies" })
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("/Old/") || path.Contains("/Models/")) continue;
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool changed = false;
                foreach (var part in root.GetComponentsInChildren<Part>(true))
                {
                    var stats = part.Stats;
                    if (stats == null || stats.subtype == PartSubtype.None) continue;
                    bool melee = stats.subtype == PartSubtype.Melee, cannon = stats == cannonData;
                    var settings = part.GetComponent<WeaponVisualSettings>();
                    if (settings == null) settings = part.gameObject.AddComponent<WeaponVisualSettings>();
                    settings.material = material;
                    settings.color = melee ? new Color(.4f, .85f, 1, .95f) : stats.projectileColor;
                    settings.meleeTrail = melee; settings.projectileTrail = !melee;
                    settings.muzzleFlash = settings.muzzleSmoke = !melee && stats != axe;
                    settings.muzzleSize = cannon ? 1.2f : stats == largeBolt ? .5f : .35f;
                    settings.muzzleDuration = cannon ? .12f : .08f;
                    settings.trailWidth = melee ? .45f : cannon ? .16f : stats == axe ? .12f : .065f;
                    settings.trailLifetime = melee ? .26f : cannon ? .35f : stats == axe ? .3f : .18f;
                    settings.impactSize = melee ? .75f : cannon ? 1.2f : .5f;
                    settings.firingShake = cannon ? .075f : stats == largeBolt ? .04f : .02f;
                    settings.firingShakeDuration = cannon ? .26f : stats == largeBolt ? .18f : .12f;
                    settings.trailAnchor = melee ? MeleeTip(part) : part.muzzle;
                    settings.swingStart = .12f; settings.swingEnd = .8f;
                    changed = true;
                }
                var feedback = root.GetComponent<CombatFeedback>();
                if (feedback != null)
                {
                    var fields = new SerializedObject(feedback);
                    fields.FindProperty("smokeMaterial").objectReferenceValue = smoke;
                    fields.FindProperty("damageImpulseStrength").floatValue = .16f;
                    fields.FindProperty("damageImpulseDuration").floatValue = .24f;
                    fields.ApplyModifiedPropertiesWithoutUndo();
                    if (root.GetComponent<PlayerDamageVignette>() == null) root.AddComponent<PlayerDamageVignette>();
                    changed = true;
                }
                if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        const string healthPath = "Assets/Prefabs/UI/Player Health HUD.prefab";
        var healthRoot = PrefabUtility.LoadPrefabContents(healthPath);
        try
        {
            var fields = new SerializedObject(healthRoot.GetComponent<MechHealthHud>());
            fields.FindProperty("whiteBarDelay").floatValue = 1;
            fields.FindProperty("whiteBarDrainDuration").floatValue = .65f;
            fields.FindProperty("lowHealthColor").colorValue = fields.FindProperty("healthyColor").colorValue;
            fields.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.SaveAsPrefabAsset(healthRoot, healthPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(healthRoot); }
        AssetDatabase.SaveAssets();
        Debug.Log("COMBAT POLISH ASSETS READY: weapon VFX, camera feedback, local vignette and health damage timing. Ammo/ability UI untouched.");
    }

    static Transform MeleeTip(Part part)
    {
        // Current modular weapons are rigid meshes attached to animated bones.
        // Other rig families may use skinning; support both without changing art.
        MeshFilter rigid = null; float largest = 0;
        foreach (var filter in part.GetComponentsInChildren<MeshFilter>(true))
        {
            var renderer = filter.GetComponent<MeshRenderer>();
            if (filter.sharedMesh == null || renderer == null || !renderer.enabled) continue;
            float size = renderer.bounds.size.sqrMagnitude;
            if (size > largest) { largest = size; rigid = filter; }
        }
        if (rigid != null) return PlaceTip(rigid.transform, rigid.sharedMesh.bounds);
        var skin = part.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (skin == null || skin.sharedMesh == null) return part.muzzle;
        var mesh = skin.sharedMesh; var weights = mesh.boneWeights; var vertices = mesh.vertices;
        var bones = skin.bones; var bindposes = mesh.bindposes;
        if (bones.Length == 0 || weights.Length != vertices.Length) return part.muzzle;
        var totals = new float[bones.Length];
        foreach (var weight in weights)
        {
            totals[weight.boneIndex0] += weight.weight0; totals[weight.boneIndex1] += weight.weight1;
            totals[weight.boneIndex2] += weight.weight2; totals[weight.boneIndex3] += weight.weight3;
        }
        int dominant = 0;
        for (int i = 1; i < totals.Length; i++) if (totals[i] > totals[dominant]) dominant = i;
        if (bones[dominant] == null || dominant >= bindposes.Length) return part.muzzle;
        var bounds = new Bounds(); bool found = false;
        for (int i = 0; i < vertices.Length; i++)
        {
            var weight = weights[i];
            if (weight.boneIndex0 != dominant || weight.weight0 < .5f) continue;
            var point = bindposes[dominant].MultiplyPoint3x4(vertices[i]);
            if (found) bounds.Encapsulate(point); else { bounds = new Bounds(point, Vector3.zero); found = true; }
        }
        if (!found) return part.muzzle;
        return PlaceTip(bones[dominant], bounds);
    }
    static Transform PlaceTip(Transform parent, Bounds bounds)
    {
        int axis = bounds.extents.x > bounds.extents.y ? 0 : 1;
        if (bounds.extents.z > bounds.extents[axis]) axis = 2;
        var tip = bounds.center; tip[axis] += (tip[axis] >= 0 ? 1 : -1) * bounds.extents[axis] * .6f;
        var anchor = parent.Find("VFX Swing Tip");
        if (anchor == null) { anchor = new GameObject("VFX Swing Tip").transform; anchor.SetParent(parent, false); }
        anchor.localPosition = tip; anchor.localRotation = Quaternion.identity; anchor.localScale = Vector3.one;
        return anchor;
    }
}
