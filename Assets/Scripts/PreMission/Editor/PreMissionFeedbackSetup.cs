using System;
using UnityEditor;
using UnityEngine;

// Explicit P4 upgrade; changes only the owned board prefab. Does not regenerate the scene or UI.
public static class PreMissionFeedbackSetup
{
    [MenuItem("Tools/Mech/Pre-Mission/Upgrade P4 Feedback")]
    public static void UpgradeAssets()
    {
        if(Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before upgrading feedback.");
        var root=PrefabUtility.LoadPrefabContents(PreMissionStoneBoardSetup.BoardPath);
        try
        {
            var feedback=root.GetComponent<QuestBoardImpactFeedback>();
            if(feedback==null || root.GetComponentInChildren<PreMissionMissionMiniature>(true)==null)
                throw new InvalidOperationException("Upgrade this board through P3 before adding P4 feedback.");
            var fields=new SerializedObject(feedback);
            var source=(AudioSource)fields.FindProperty("interactionAudio").objectReferenceValue;
            if(source==null) source=root.transform.Find("Stone Interaction Audio")?.GetComponent<AudioSource>();
            if(source==null)
            {
                source=new GameObject("Stone Interaction Audio",typeof(AudioSource)).GetComponent<AudioSource>();
                source.transform.SetParent(root.transform,false); source.playOnAwake=false; source.loop=false; source.spatialBlend=0;
                var impact=(AudioSource)fields.FindProperty("impactAudio").objectReferenceValue;
                if(impact!=null) source.outputAudioMixerGroup=impact.outputAudioMixerGroup;
            }
            if(fields.FindProperty("interactionAudio").objectReferenceValue==null)
                fields.FindProperty("interactionAudio").objectReferenceValue=source;
            // Audio roles intentionally remain empty until the audio owner assigns clips.
            // Repeating setup preserves any clips that owner connects later.
            fields.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root,PreMissionStoneBoardSetup.BoardPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets(); Debug.Log("PRE-MISSION P4 FEEDBACK READY");
    }
}
