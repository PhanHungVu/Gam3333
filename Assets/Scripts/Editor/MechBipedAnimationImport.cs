using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Saved walking clips are authoritative; other motions refresh from the GLB.
public static class MechBipedAnimationImport
{
    public const string Source = "Assets/Anim/golem2_v2.glb";
    public const string Folder = "Assets/Anim/Locomotion/V2";

    [MenuItem("Mech/Animation/Refresh Biped v2 Clips")]
    public static void RefreshClips()
    {
        var clips = AssetDatabase.LoadAllAssetsAtPath(Source).OfType<AnimationClip>().ToArray();
        AnimationClip Find(params string[] names) => names.Select(n => clips.FirstOrDefault(c => c.name == n)).FirstOrDefault(c => c != null);
        var walk = Find("Fast_Walk", "Biped_Run", "Biped_Walk", "Walk");
        var heavy = Find("Heavy_Walk", "Heavy_Walk.001");
        var boost = Find("Fast_Boost", "Boost", "Biped_Boost");
        var stop = Find("Fast_BoostStop", "BoostStop", "Biped_BoostStop");
        ValidateWalk(walk, "Biped_Run");
        ValidateWalk(heavy, "Heavy_Walk");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Anim/Locomotion", "V2");
        // Bootstrap only. A later export must never overwrite the surviving Unity gait,
        // even when Blender still exports an empty or unrelated action with the same name.
        if (!HasSavedClip("Biped_Run")) CopyClip(walk, "Biped_Run", true);
        if (!HasSavedClip("Heavy_Walk")) CopyClip(heavy, "Heavy_Walk", true, "Heavy_Walk.001");
        CopyClip(boost, "Boost", true);
        CopyClip(stop, "BoostStop", false);
        // These placeholders are deliberately curve-free. Never erase a hand-authored placeholder.
        CopyClip(Find("Fast_Neutral", "Biped_Neutral", "Neutral"), "Biped_Neutral", true);
        CopyClip(Find("Fast_Jump", "Biped_Jump", "Jump"), "Biped_Jump", false);
        CopyHeavyClip(Find("Heavy_Boost"), "Heavy_Boost", "Boost", true);
        CopyHeavyClip(Find("Heavy_BoostStop"), "Heavy_BoostStop", "BoostStop", false);
        CopyHeavyClip(Find("Heavy_Neutral"), "Heavy_Neutral", "Biped_Neutral", true);
        CopyHeavyClip(Find("Heavy_Jump"), "Heavy_Jump", "Biped_Jump", false);
        AssetDatabase.SaveAssets();
        Debug.Log("BIPED V2 CLIPS REFRESHED: saved walking clips retained; available Fast/Heavy actions updated; absent actions retain their saved clips.");
    }

    static void CopyHeavyClip(AnimationClip source, string file, string initialFallback, bool loop)
    {
        // Older exports shared these actions. Bootstrap that behavior only once;
        // never replace an existing Heavy action with Fast when a later export omits it.
        if (source == null && !HasSavedClip(file))
            source = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/" + initialFallback + ".anim");
        CopyClip(source, file, loop);
    }

    public static AnimationClip ActionClip(string family, string state)
    {
        string file = family == "Heavy" ? "Heavy_" + state
            : state == "Neutral" || state == "Jump" ? "Biped_" + state : state;
        return AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/" + file + ".anim");
    }

    static bool HasSavedClip(string file) => AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/" + file + ".anim") != null;

    static void ValidateWalk(AnimationClip source, string file)
    {
        if (source == null && !HasSavedClip(file))
            throw new InvalidOperationException("No saved " + file + ".anim or matching walking clip in " + Source
                + ". Restore the saved .anim asset and its .meta file from backup.");
    }

    static void CopyClip(AnimationClip source, string file, bool loop, string clipName = null)
    {
        string path = Folder + "/" + file + ".anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
        else if (source == null) return;
        if (source != null) EditorUtility.CopySerialized(source, clip);
        clip.name = clipName ?? file;
        clip.hideFlags = HideFlags.None;
        clip.legacy = false;
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
    }
}

public sealed class MechBipedAnimationPostprocessor : AssetPostprocessor
{
    static bool queued;
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (queued || !imported.Contains(MechBipedAnimationImport.Source)) return;
        queued = true;
        EditorApplication.delayCall += Refresh;
    }

    static void Refresh()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        { EditorApplication.delayCall += Refresh; return; }
        queued = false;
        try { MechBipedAnimationImport.RefreshClips(); }
        catch (Exception e) { Debug.LogError("Biped animation refresh failed: " + e.Message); }
    }
}
