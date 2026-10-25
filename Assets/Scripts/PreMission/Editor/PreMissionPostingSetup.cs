using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// An explicit, idempotent addition to the existing notice prefab. No art/material edits.
public static class PreMissionPostingSetup
{
    [MenuItem("Tools/Mech/Pre-Mission/Upgrade Q5.1 Quest Posting")]
    public static void UpgradeAssets()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play mode before upgrading the UI assets.");
        var root = PrefabUtility.LoadPrefabContents(PreMissionPrototypeSetup.NoticePath);
        try
        {
            var fields = new SerializedObject(root.GetComponent<QuestNoticeView>());
            var paper = (RectTransform)fields.FindProperty("paperVisual").objectReferenceValue;
            if (paper == null) throw new InvalidOperationException("Upgrade the Q2/Q3 notice presentation first.");
            var clip = root.transform.Find("Posting Reveal") as RectTransform;
            if (clip == null)
            {
                clip = new GameObject("Posting Reveal", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
                clip.SetParent(root.transform, false); clip.gameObject.layer = paper.gameObject.layer;
                clip.SetSiblingIndex(paper.GetSiblingIndex());
                clip.anchorMin = Vector2.zero; clip.anchorMax = Vector2.one;
                clip.offsetMin = clip.offsetMax = Vector2.zero;
                paper.SetParent(clip, false);
            }
            var mask = clip.GetComponent<RectMask2D>();
            mask.enabled = false; // No settled/hover shadows are clipped.
            fields.FindProperty("postingReveal").objectReferenceValue = mask;
            fields.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PreMissionPrototypeSetup.NoticePath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }

        // Save the current timing defaults for new serialized posting fields;
        // preserve all previously configured transitions and Inspector overrides.
        var ui = PrefabUtility.LoadPrefabContents(PreMissionPrototypeSetup.PrefabPath);
        try { PrefabUtility.SaveAsPrefabAsset(ui, PreMissionPrototypeSetup.PrefabPath); }
        finally { PrefabUtility.UnloadPrefabContents(ui); }
        AssetDatabase.SaveAssets();
        Debug.Log("PRE-MISSION Q5.1 POSTING UPGRADED");
    }
}
