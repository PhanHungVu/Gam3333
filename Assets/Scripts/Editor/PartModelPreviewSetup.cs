using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class PartModelPreviewSetup
{
    public const string PodPath = "Assets/Prefabs/UI/PartSwapSelectPod.prefab";
    [MenuItem("Tools/Mech/UI/Set Up Part Model Previews")]
    public static void Configure()
    {
        var root = PrefabUtility.LoadPrefabContents(PodPath);
        try
        {
            var slot = root.transform.Find("Sprite");
            if (slot == null) throw new InvalidOperationException("Swap card is missing its Sprite preview slot.");
            var placeholder = slot.GetComponent<Image>();
            if (placeholder != null) UnityEngine.Object.DestroyImmediate(placeholder);
            var image = slot.GetComponent<RawImage>() ?? slot.gameObject.AddComponent<RawImage>();
            image.raycastTarget = false;
            image.color = Color.white;
            image.enabled = false;
            if (slot.GetComponent<PartModelPreview>() == null) slot.gameObject.AddComponent<PartModelPreview>();
            PrefabUtility.SaveAsPrefabAsset(root, PodPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    public static void BuildBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Disposable project only.");
        try { Configure(); Debug.Log("PART MODEL PREVIEW SETUP COMPLETE"); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
