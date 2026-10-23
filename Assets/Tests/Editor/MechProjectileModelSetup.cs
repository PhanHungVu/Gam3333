using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class MechProjectileModelSetup
{
    public const string BoltPath = "Assets/Prefabs/Combat/Projectiles/CrossbowBolt.prefab";
    public const string AxePath = "Assets/Prefabs/Combat/Projectiles/ThrowingAxe.prefab";

    public static void BuildBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Disposable project only.");
        try
        {
            Folder("Assets/Prefabs/Combat"); Folder("Assets/Prefabs/Combat/Projectiles");
            var player = PrefabUtility.LoadPrefabContents(MechAnimatedLegsSetup.PlayerPath);
            GameObject bolt, axe;
            try
            {
                var boltReference = player.GetComponentsInChildren<MeshFilter>(true).Single(m => m.sharedMesh != null && m.sharedMesh.name == "CrossbowBoltMesh" && m.GetComponent<Renderer>().enabled);
                var axeReference = player.GetComponentsInChildren<MeshFilter>(true).Where(m => m.sharedMesh != null && m.sharedMesh.name == "ThrowingAxeMesh")
                    .Single(m => m.GetComponent<Renderer>().enabled);
                bolt = BuildModel(BoltPath, "Assets/Models/Golem1Models/CrossbowBolt.glb", boltReference, false);
                axe = BuildModel(AxePath, "Assets/Models/Golem1Models/ThrowingAxe.glb", axeReference, true);
                var combat = new SerializedObject(player.GetComponent<MechCombat>());
                combat.FindProperty("crossbowProjectilePrefab").objectReferenceValue = bolt;
                combat.FindProperty("throwingAxeProjectilePrefab").objectReferenceValue = axe;
                combat.ApplyModifiedPropertiesWithoutUndo();
                ConfigureParts(player);
                PrefabUtility.SaveAsPrefabAsset(player, MechAnimatedLegsSetup.PlayerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
            foreach (string name in new[] { "PartData_Shoulder_LargeCrossbow", "PartData_Arm_SmallCrossbow", "PartData_Arm_ThrowingAxe" })
            {
                var data = AssetDatabase.LoadAssetAtPath<PartData>("Assets/PartCatalogue/" + name + ".asset");
                data.projectilePrefab = name.EndsWith("ThrowingAxe", StringComparison.Ordinal) ? axe : bolt;
                if (name == "PartData_Arm_SmallCrossbow") data.subtype = PartSubtype.Projectile;
                EditorUtility.SetDirty(data);
            }
            var set = AssetDatabase.LoadAssetAtPath<MechWeaponAnimationSet>("Assets/Anim/Weapons/ArmCrossbow.asset");
            set.animationOnly = false; EditorUtility.SetDirty(set);
            foreach (string path in new[] {
                "Assets/Prefabs/Mech/Base/Parts/shoulder_crossbow.prefab",
                "Assets/Prefabs/Mech/Base/Parts/arm_crossbow.prefab",
                "Assets/Prefabs/Mech/Base/Parts/arm_throwingaxe.prefab",
                "Assets/Prefabs/Enemies/Enemy_crossbow.prefab" })
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    ConfigureParts(root);
                    var enemy = root.GetComponent<EnemyAttack>();
                    if (enemy != null)
                    {
                        var settings = new SerializedObject(enemy);
                        settings.FindProperty("projectilePrefab").objectReferenceValue = bolt;
                        settings.ApplyModifiedPropertiesWithoutUndo();
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("PROJECTILE MODELS BUILD COMPLETE"); EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    static GameObject BuildModel(string path, string modelPath, MeshFilter reference, bool spins)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        var mesh = source.GetComponentsInChildren<MeshFilter>(true).Single().sharedMesh;
        var root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(path));
        try
        {
            var visual = new GameObject("Projectile Visual"); visual.transform.SetParent(root.transform, false);
            var art = new GameObject("Model"); art.transform.SetParent(visual.transform, false);
            art.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = art.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = reference.GetComponent<Renderer>().sharedMaterials;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            float scale = reference.transform.TransformVector(Vector3.forward).magnitude;
            art.transform.localPosition = -mesh.bounds.center * scale;
            art.transform.localScale = Vector3.one * scale;
            // glTFast's Unity mesh has the bolt tip along +Z. Verify the narrow
            // end from the vertices so a future source change cannot reverse it.
            if (!spins && EndWidth(mesh, true) > EndWidth(mesh, false))
                visual.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var projectile = root.AddComponent<MechProjectile>();
            var data = new SerializedObject(projectile);
            data.FindProperty("visual").objectReferenceValue = visual.transform;
            data.FindProperty("angularVelocity").vector3Value = spins ? new Vector3(720f, 0f, 0f) : Vector3.zero;
            data.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("PROJECTILE MODEL " + path + " mesh=" + mesh.name + " scale=" + scale
                + " bounds=" + mesh.bounds + " positiveEnd=" + EndWidth(mesh, true) + " negativeEnd=" + EndWidth(mesh, false));
            return PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { Object.DestroyImmediate(root); }
    }

    static float EndWidth(Mesh mesh, bool positive)
    {
        float end = positive ? mesh.bounds.max.z : mesh.bounds.min.z;
        var vertices = mesh.vertices.Where(v => Mathf.Abs(v.z - end) < mesh.bounds.size.z * 0.04f).ToArray();
        return vertices.Length > 0 ? vertices.Max(v => new Vector2(v.x, v.y).magnitude) : 0f;
    }

    static void ConfigureParts(GameObject root)
    {
        foreach (var part in root.GetComponentsInChildren<Part>(true))
        {
            if (part.data == null) continue;
            var driver = part.GetComponent<MechWeaponAnimator>();
            if (driver == null) continue;
            if (part.data.name == "PartData_Arm_SmallCrossbow" && part.muzzle == null)
            {
                var mesh = part.GetComponentsInChildren<MeshFilter>(true).Single(m => m.sharedMesh != null && m.sharedMesh.name == "CrossbowArmMesh");
                var bone = driver.Animator.transform.Find("CrossBowArmRig/WeapRoot/CrossBowArmBody");
                var marker = new GameObject("Muzzle").transform; marker.SetParent(bone, false);
                marker.position = mesh.transform.TransformPoint(mesh.sharedMesh.bounds.center + Vector3.forward * mesh.sharedMesh.bounds.extents.z);
                part.muzzle = marker;
            }
            if (part.data.name == "PartData_Arm_ThrowingAxe" && driver.Placement != null) part.muzzle = driver.Placement;
            string meshName = part.data.name == "PartData_Arm_ThrowingAxe" ? "ThrowingAxeMesh"
                : part.data.name == "PartData_Shoulder_LargeCrossbow" ? "CrossbowBoltMesh" : null;
            if (meshName == null) continue;
            var renderers = part.GetComponentsInChildren<MeshFilter>(true).Where(m => m.sharedMesh != null && m.sharedMesh.name == meshName)
                .Select(m => m.GetComponent<Renderer>()).Where(r => r != null).ToArray();
            var settings = new SerializedObject(driver);
            var array = settings.FindProperty("loadedProjectileRenderers"); array.arraySize = renderers.Length;
            for (int i = 0; i < renderers.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
            settings.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    static void Folder(string path)
    {
        if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'), System.IO.Path.GetFileName(path));
    }
}
