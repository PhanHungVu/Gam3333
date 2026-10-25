using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class PreMissionReliabilitySetup
{
    [MenuItem("Tools/Mech/Pre-Mission/Upgrade Q5.4 Skip Controls")]
    public static void UpgradeAssets()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play mode before upgrading the UI assets.");
        var root = PrefabUtility.LoadPrefabContents(PreMissionPrototypeSetup.PrefabPath);
        try
        {
            var fields = new SerializedObject(root.GetComponent<PreMissionFlowController>());
            var board = ((GameObject)fields.FindProperty("boardView").objectReferenceValue).transform;
            var skip = board.Find("Skip Posting")?.GetComponent<Button>();
            if (skip == null)
            {
                var source = (Button)fields.FindProperty("exitButton").objectReferenceValue;
                skip = UnityEngine.Object.Instantiate(source, board); skip.name = "Skip Posting";
                skip.onClick = new Button.ButtonClickedEvent();
                var rect = (RectTransform)skip.transform;
                rect.anchorMin = new Vector2(0.68f, 0.05f); rect.anchorMax = new Vector2(0.96f, 0.14f);
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                skip.GetComponentInChildren<TMP_Text>(true).text = "Skip Posting (Space)";
            }
            skip.gameObject.SetActive(false);
            fields.FindProperty("skipPostingButton").objectReferenceValue = skip;
            fields.FindProperty("boardHint").objectReferenceValue = board.Find("Board Hint").GetComponent<TMP_Text>();
            fields.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PreMissionPrototypeSetup.PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets(); Debug.Log("PRE-MISSION Q5.4 SKIP CONTROLS UPGRADED");
    }
}
