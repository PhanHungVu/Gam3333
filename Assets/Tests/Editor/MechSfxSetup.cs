using System;
using UnityEditor;
using UnityEngine;

public static class MechSfxSetup
{
    public static readonly string[] Families = { "Sword", "ThrowingAxe", "Cannon", "Crossbow", "Mace", "ArmCrossbow" };
    public static readonly string[] Sounds = { "StoneSword_attack", "ThrowingAxe_attack", "Cannon_attack", "Crossbow_attack", "Mace_attack", "CrossbowArm_attack" };
    public static AudioClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/SFX/" + name + ".wav");

    public static void BuildBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Isolated project only.");
        try
        {
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/SFX" }))
            {
                var importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                importer.forceToMono = true;
                importer.loadInBackground = false;
                var sample = importer.defaultSampleSettings;
                sample.loadType = AudioClipLoadType.DecompressOnLoad;
                sample.preloadAudioData = true;
                importer.defaultSampleSettings = sample;
                importer.SaveAndReimport();
            }
            for (int i = 0; i < Families.Length; i++)
            {
                var set = AssetDatabase.LoadAssetAtPath<MechWeaponAnimationSet>("Assets/Anim/Weapons/" + Families[i] + ".asset");
                set.attackSound = Clip(Sounds[i]);
                if (set.attackSound == null) throw new InvalidOperationException("Missing SFX " + Sounds[i]);
                EditorUtility.SetDirty(set);
            }
            foreach (string path in new[] { MechAnimatedLegsSetup.PlayerPath,
                "Assets/Prefabs/Enemies/Enemy_crossbow.prefab", "Assets/Prefabs/Enemies/Enemy_mace.prefab" })
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var audio = root.GetComponent<MechMovementAudio>() ?? root.AddComponent<MechMovementAudio>();
                    var settings = new SerializedObject(audio);
                    settings.FindProperty("movementSound").objectReferenceValue = Clip("Golem1Legs_Move");
                    settings.ApplyModifiedPropertiesWithoutUndo();
                    var feedback = root.GetComponent<CombatFeedback>();
                    if (feedback != null)
                    {
                        // Weapon rigs now own attack audio; keep impact/confirmation settings.
                        var combat = new SerializedObject(feedback);
                        combat.FindProperty("fireAudio").boolValue = false;
                        combat.ApplyModifiedPropertiesWithoutUndo();
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("SFX SETUP COMPLETE: six attack clips and grounded movement wired.");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
