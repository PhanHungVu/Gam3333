using System;
using UnityEditor;
using UnityEngine;

// Explicit, idempotent upgrade of the owned board prefab; no scene regeneration.
public static class PreMissionBoardTerrainSetup
{
    [MenuItem("Tools/Mech/Pre-Mission/Upgrade Rocky Board Terrain")]
    public static void UpgradeAssets()
    {
        if(Application.isPlaying) throw new InvalidOperationException("Stop Play mode before upgrading the board.");
        var root=PrefabUtility.LoadPrefabContents(PreMissionStoneBoardSetup.BoardPath);
        try
        {
            var terrain=root.GetComponentInChildren<PreMissionBoardTerrain>(true);
            if(terrain==null)
            {
                var parent=new GameObject("Board Terrain"); parent.layer=root.layer;
                parent.transform.SetParent(root.transform.Find("Board Surface") ?? root.transform,false);
                terrain=parent.AddComponent<PreMissionBoardTerrain>();
                var fields=new SerializedObject(terrain);
                fields.FindProperty("rockMaterial").objectReferenceValue=AssetDatabase.LoadAssetAtPath<Material>(PreMissionStoneBoardSetup.DirectoryPath+"/Small Stone.mat");
                fields.ApplyModifiedPropertiesWithoutUndo();
            }
            var motion=new SerializedObject(root.GetComponent<PreMissionStoneMotion>());
            motion.FindProperty("terrain").objectReferenceValue=terrain; motion.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root,PreMissionStoneBoardSetup.BoardPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets(); Debug.Log("PRE-MISSION ROCKY TERRAIN READY");
    }
}
