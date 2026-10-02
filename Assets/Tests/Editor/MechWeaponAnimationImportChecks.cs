using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class MechWeaponAnimationImportChecks
{
    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Disposable project only.");
        try
        {
            var settings = AssetDatabase.LoadAssetAtPath<MechWeaponAnimationImportSettings>(MechWeaponAnimationImport.SettingsPath);
            var entry = settings.weapons[0];
            var originalFire = entry.fire; var originalReload = entry.reload;
            var savedFire = entry.target.fire; var savedReload = entry.target.reload;
            string fireGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(savedFire));
            string fireData = File.ReadAllText(AssetDatabase.GetAssetPath(savedFire));
            string reloadData = File.ReadAllText(AssetDatabase.GetAssetPath(savedReload));
            string fastPath = MechBipedAnimationImport.Folder + "/Biped_Run.anim";
            string heavyPath = MechBipedAnimationImport.Folder + "/Heavy_Walk.anim";
            string fast = File.ReadAllText(fastPath), heavy = File.ReadAllText(heavyPath);
            AnimationClip renamed = null;
            try
            {
                entry.fire = entry.reload = null;
                MechWeaponAnimationImport.RefreshClips();
                Check(entry.target.fire == savedFire && entry.target.reload == savedReload
                    && File.ReadAllText(AssetDatabase.GetAssetPath(savedFire)) == fireData
                    && File.ReadAllText(AssetDatabase.GetAssetPath(savedReload)) == reloadData,
                    "Missing selected actions retain saved weapon clips and their contents");
                renamed = Object.Instantiate(originalFire); renamed.name = "An_Entirely_Different_Action_Name";
                entry.fire = renamed;
                MechWeaponAnimationImport.RefreshClips();
                Check(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(entry.target.fire)) == fireGuid
                    && AnimationUtility.GetCurveBindings(entry.target.fire).Length == AnimationUtility.GetCurveBindings(originalFire).Length,
                    "Explicitly assigned renamed action refreshes the same saved clip without name lookup");
                Check(File.ReadAllText(fastPath) == fast && File.ReadAllText(heavyPath) == heavy,
                    "Weapon refresh never edits surviving locomotion gait assets");
            }
            finally
            {
                entry.fire = originalFire; entry.reload = originalReload;
                MechWeaponAnimationImport.RefreshClips();
                if (renamed != null) Object.DestroyImmediate(renamed);
            }
            Check(settings.weapons.Count(e => e.target.reload != null) == 3,
                "Only the three actually exported Reload clips are configured");
            Debug.Log("WEAPON IMPORT CHECKS COMPLETE"); EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    static void Check(bool ok, string message)
    { if (!ok) throw new Exception(message); Debug.Log("WEAPON IMPORT PASS: " + message); }
}
