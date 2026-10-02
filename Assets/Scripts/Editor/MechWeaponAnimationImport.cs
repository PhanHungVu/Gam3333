using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class MechWeaponAnimationImport
{
    public const string SettingsPath = "Assets/Anim/Weapons/Editor/ImportSettings.asset";
    [MenuItem("Mech/Animation/Refresh Weapon Clips")]
    public static void RefreshClips()
    {
        var settings = AssetDatabase.LoadAssetAtPath<MechWeaponAnimationImportSettings>(SettingsPath);
        if (settings == null || settings.source == null) throw new InvalidOperationException("Assign a weapon animation source and clip selections in " + SettingsPath);
        foreach (var entry in settings.weapons)
        {
            if (entry.target == null || string.IsNullOrEmpty(entry.rigPath)) continue;
            entry.target.neutral = Save(entry, entry.neutral, "Neutral", entry.target.neutral, true);
            entry.target.fire = Save(entry, entry.fire, "Fire", entry.target.fire, false);
            entry.target.reload = Save(entry, entry.reload, "Reload", entry.target.reload, false);
            EditorUtility.SetDirty(entry.target);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("WEAPON CLIPS REFRESHED: explicit selections imported; absent actions retain saved clips.");
    }

    static AnimationClip Save(MechWeaponAnimationImportSettings.Entry entry, AnimationClip source, string state, AnimationClip saved, bool loop)
    {
        if (source == null) return saved;
        var bindings = AnimationUtility.GetCurveBindings(source);
        if (bindings.Length == 0 || bindings.Any(b => b.type != typeof(Transform)
            || !(b.path == entry.rigPath || b.path.StartsWith(entry.rigPath + "/", StringComparison.Ordinal))))
            throw new InvalidOperationException(source.name + " contains curves outside its selected weapon rig " + entry.rigPath);
        if (AnimationUtility.GetObjectReferenceCurveBindings(source).Length > 0)
            throw new InvalidOperationException("Unexpected object curves in " + source.name);
        if (saved == null)
        {
            saved = new AnimationClip();
            string folder = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(entry.target)).Replace('\\', '/');
            AssetDatabase.CreateAsset(saved, folder + "/" + entry.target.name + "_" + state + ".anim");
        }
        EditorUtility.CopySerialized(source, saved);
        saved.name = entry.target.name + "_" + state;
        saved.hideFlags = HideFlags.None;
        saved.legacy = false;
        var clipSettings = AnimationUtility.GetAnimationClipSettings(saved);
        clipSettings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(saved, clipSettings);
        EditorUtility.SetDirty(saved);
        return saved;
    }
}

public sealed class MechWeaponAnimationPostprocessor : AssetPostprocessor
{
    static bool queued;
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (queued) return;
        var settings = AssetDatabase.LoadAssetAtPath<MechWeaponAnimationImportSettings>(MechWeaponAnimationImport.SettingsPath);
        if (settings == null || settings.source == null || !imported.Contains(AssetDatabase.GetAssetPath(settings.source))) return;
        queued = true;
        EditorApplication.delayCall += Refresh;
    }
    static void Refresh()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Refresh; return; }
        queued = false;
        try { MechWeaponAnimationImport.RefreshClips(); }
        catch (Exception e) { Debug.LogError("Weapon animation refresh failed; saved clips retained where possible: " + e.Message); }
    }
}
