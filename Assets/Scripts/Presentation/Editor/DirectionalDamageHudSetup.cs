using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class DirectionalDamageHudSetup
{
    public const string PrefabPath = "Assets/Prefabs/UI/Combat HUD/Directional Damage HUD.prefab";
    public const string HudPath = "Assets/Prefabs/UI/Combat HUD/CombatHUD.prefab";
    [MenuItem("Tools/Mech/UI/Connect Directional Damage HUD")]
    public static void Apply()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before updating HUD assets.");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            var root = new GameObject("Directional Damage HUD", typeof(RectTransform), typeof(DirectionalDamageHud)); root.layer = 5;
            try
            {
                var rect = (RectTransform)root.transform;
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
                var fields = new SerializedObject(root.GetComponent<DirectionalDamageHud>());
                var pool = fields.FindProperty("indicators"); pool.arraySize = 8;
                for (int i = 0; i < pool.arraySize; i++)
                {
                    var child = new GameObject("Direction " + (i + 1), typeof(RectTransform), typeof(CanvasRenderer), typeof(DamageDirectionGraphic));
                    child.layer = 5; child.transform.SetParent(root.transform, false);
                    ((RectTransform)child.transform).sizeDelta = new Vector2(140, 44);
                    var graphic = child.GetComponent<DamageDirectionGraphic>();
                    graphic.color = new Color(1, .035f, .025f, .95f); graphic.raycastTarget = false; graphic.maskable = false; graphic.enabled = false;
                    pool.GetArrayElementAtIndex(i).objectReferenceValue = graphic;
                }
                fields.ApplyModifiedPropertiesWithoutUndo();
                prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }
        // A nested Canvas only controls draw order; it still uses the shared
        // parent Canvas/camera and needs no raycaster or independent screen UI.
        var effectRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var canvas = effectRoot.GetComponent<Canvas>();
            bool changed = canvas == null;
            if (canvas == null)
            {
                canvas = effectRoot.AddComponent<Canvas>(); canvas.sortingOrder = 50;
            }
            var layerFields = new SerializedObject(canvas);
            layerFields.FindProperty("m_OverrideSorting").boolValue = true;
            changed |= layerFields.ApplyModifiedPropertiesWithoutUndo();
            if (changed) PrefabUtility.SaveAsPrefabAsset(effectRoot, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(effectRoot); }
        prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var hud = PrefabUtility.LoadPrefabContents(HudPath);
        try
        {
            if (hud.GetComponentInChildren<DirectionalDamageHud>(true) == null)
            {
                PrefabUtility.InstantiatePrefab(prefab, hud.transform);
                PrefabUtility.SaveAsPrefabAsset(hud, HudPath);
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(hud); }
        AssetDatabase.SaveAssets(); Debug.Log("DIRECTIONAL DAMAGE HUD CONNECTED: bounded camera-relative indicators in the shared HUD.");
    }
}
