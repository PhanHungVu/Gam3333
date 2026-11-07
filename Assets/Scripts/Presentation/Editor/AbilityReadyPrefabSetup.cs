using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Connects ability and weapon pods to the same reusable, prefab-authored ready effect.
public static class AbilityReadyPrefabSetup
{
    public const string BurstPath = "Assets/Prefabs/UI/Combat HUD/Ability Ready Burst.prefab";
    public const string PodPath = "Assets/Prefabs/UI/Combat HUD/AbilitiesHUDPodPrefab.prefab";
    public const string WeaponPodPath = "Assets/Prefabs/UI/Combat HUD/WeaponsHUDPodPrefab.prefab";

    [MenuItem("Tools/Mech/UI/Connect Ability Ready Burst Prefab")]
    public static void Apply()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before updating HUD assets.");
        var burstPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BurstPath);
        if (burstPrefab == null)
        {
            var template = AssetDatabase.LoadAssetAtPath<GameObject>(PodPath).GetComponent<AbilityReadyVfx>();
            var root = new GameObject("Ability Ready Burst", typeof(RectTransform)); root.layer = 5;
            try
            {
                ((RectTransform)root.transform).sizeDelta = new Vector2(90, 90);
                var column = new GameObject("Column", typeof(RectTransform), typeof(CanvasRenderer), typeof(AbilityReadyBurstGraphic));
                column.layer = 5; column.transform.SetParent(root.transform, false);
                ((RectTransform)column.transform).sizeDelta = new Vector2(90, 90);
                var graphic = column.GetComponent<AbilityReadyBurstGraphic>();
                graphic.raycastTarget = false; graphic.maskable = false; graphic.enabled = false;
                var effect = root.AddComponent<AbilityReadyVfx>();
                if (template != null) EditorUtility.CopySerialized(template, effect);
                var fields = new SerializedObject(effect);
                fields.FindProperty("burst").objectReferenceValue = graphic;
                fields.FindProperty("icon").objectReferenceValue = null;
                fields.ApplyModifiedPropertiesWithoutUndo();
                burstPrefab = PrefabUtility.SaveAsPrefabAsset(root, BurstPath);
            }
            finally { Object.DestroyImmediate(root); }
        }
        ConnectPod<AbilitiesHudPod>(PodPath, burstPrefab);
        ConnectPod<WeaponsHudPod>(WeaponPodPath, burstPrefab);
        AssetDatabase.SaveAssets();
        Debug.Log("READY PREFAB CONNECTED: linked, reusable burst inside ability and weapon pods; no runtime object builder.");
    }

    static void ConnectPod<T>(string path, GameObject burstPrefab) where T : Component
    {
        var pod = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var old = pod.GetComponent<AbilityReadyVfx>();
            if (old != null) Object.DestroyImmediate(old);
            var effect = pod.GetComponentInChildren<AbilityReadyVfx>(true);
            if (effect == null)
                effect = ((GameObject)PrefabUtility.InstantiatePrefab(burstPrefab, pod.transform)).GetComponent<AbilityReadyVfx>();
            var fields = new SerializedObject(pod.GetComponent<T>());
            fields.FindProperty("readyVfx").objectReferenceValue = effect;
            fields.ApplyModifiedPropertiesWithoutUndo();
            effect.Configure((Image)fields.FindProperty("icon").objectReferenceValue);
            PrefabUtility.RecordPrefabInstancePropertyModifications(effect);
            PrefabUtility.SaveAsPrefabAsset(pod, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(pod); }
    }
}
