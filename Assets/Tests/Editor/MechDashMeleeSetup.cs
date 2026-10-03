using System;
using UnityEditor;
using UnityEngine;

public static class MechDashMeleeSetup
{
    public static void BuildBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Isolated project only.");
        try
        {
            var root = PrefabUtility.LoadPrefabContents(MechAnimatedLegsSetup.PlayerPath);
            try
            {
                var settings = new SerializedObject(root.GetComponent<MechCombatController>());
                settings.FindProperty("meleeCenter").vector3Value = new Vector3(0f, 0f, 2.75f);
                settings.FindProperty("meleeSize").vector3Value = new Vector3(5f, 3.5f, 5.5f);
                settings.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, MechAnimatedLegsSetup.PlayerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Debug.Log("DASH MELEE SETUP COMPLETE"); EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
