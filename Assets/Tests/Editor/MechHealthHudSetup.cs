using System;
using UnityEditor;
using UnityEngine;

public static class MechHealthHudSetup
{
    public static void BuildBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Disposable project only.");
        var root = PrefabUtility.LoadPrefabContents(MechAnimatedLegsSetup.PlayerPath);
        try
        {
            if (root.GetComponent<CombatHealth>() == null) throw new InvalidOperationException("Player health is missing.");
            if (root.GetComponent<MechHealthHud>() == null) root.AddComponent<MechHealthHud>();
            PrefabUtility.SaveAsPrefabAsset(root, MechAnimatedLegsSetup.PlayerPath);
            Debug.Log("PLAYER HEALTH HUD BUILD COMPLETE"); EditorApplication.Exit(0);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
