using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Creates only the reusable effect assets. Existing materials/prefab tuning is preserved on rerun.
public static class PickupHighlightSetup
{
    public const string Folder = "Assets/VFX/Pickups";
    public const string PrefabPath = Folder + "/Resources/PickupHighlight.prefab";
    public const string EnemyPrefabPath = Folder + "/Resources/EnemyDropHighlight.prefab";
    public const string OutlinePath = Folder + "/Pickup Outline.mat";
    public const string StarPath = Folder + "/Pickup Star.mat";
    [MenuItem("Tools/Mech/Presentation/Create Pickup Highlight Prefab")]
    public static void Apply()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before creating effect assets.");
        if (!AssetDatabase.IsValidFolder(Folder + "/Resources")) AssetDatabase.CreateFolder(Folder, "Resources");
        var outline = Material(OutlinePath, "Lithomachia/Pickup Outline");
        var star = Material(StarPath, "Lithomachia/Pickup Star");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            var root = new GameObject("PickupHighlight");
            try
            {
                var effect = root.AddComponent<PickupHighlight>();
                var fields = new SerializedObject(effect);
                fields.FindProperty("outlineMaterial").objectReferenceValue = outline;
                var sparkles = fields.FindProperty("sparkles"); sparkles.arraySize = 2;
                for (int i = 0; i < 2; i++)
                {
                    var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    quad.name = i == 0 ? "White Star" : "Small Twinkle";
                    quad.transform.SetParent(root.transform, false);
                    Object.DestroyImmediate(quad.GetComponent<Collider>());
                    var renderer = quad.GetComponent<MeshRenderer>();
                    renderer.sharedMaterial = star; renderer.enabled = false;
                    renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    sparkles.GetArrayElementAtIndex(i).objectReferenceValue = renderer;
                }
                fields.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath) == null)
        {
            // A prefab variant keeps the same reusable star meshes/materials and
            // inherits future pickup-size tuning, with gold sparkles and no outline.
            var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            try
            {
                root.name = "EnemyDropHighlight";
                var effect = root.GetComponent<PickupHighlight>();
                var fields = new SerializedObject(effect);
                fields.FindProperty("drawOutline").boolValue = false;
                fields.FindProperty("sparkleColor").colorValue = new Color(1, .66f, .12f, 1);
                fields.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.RecordPrefabInstancePropertyModifications(effect);
                PrefabUtility.SaveAsPrefabAsset(root, EnemyPrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }
        AssetDatabase.SaveAssets();
        Debug.Log("PICKUP HIGHLIGHT READY: reusable white-star/outline prefab; loose parts connect through Part.SetEquippedPhysics.");
    }
    static Material Material(string path, string shaderName)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        var shader = Shader.Find(shaderName);
        if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Pickup shader failed to import: " + shaderName);
        var material = new Material(shader); material.name = System.IO.Path.GetFileNameWithoutExtension(path);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
