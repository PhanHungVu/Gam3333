using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class MechAnimationV2ImportChecks
{
    static readonly Dictionary<string, string> guids = new Dictionary<string, string>();
    static readonly Dictionary<string, byte[]> savedWalks = new Dictionary<string, byte[]>();
    static readonly Dictionary<string, byte[]> absentActions = new Dictionary<string, byte[]>();
    static double deadline;

    // Deliberately damages only a derived clip in a disposable project, then exercises
    // the actual GLB postprocessor rather than calling RefreshClips directly.
    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Isolated batch copy only.");
        guids.Clear();
        savedWalks.Clear();
        foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { MechBipedAnimationImport.Folder }))
            guids.Add(AssetDatabase.GUIDToAssetPath(guid), guid);
        foreach (string name in new[] { "Biped_Run", "Heavy_Walk" })
        {
            string path = MechBipedAnimationImport.Folder + "/" + name + ".anim";
            var walk = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (AnimationUtility.GetCurveBindings(walk).Length == 0) throw new Exception("Saved walking motion is missing: " + name);
            // Distinguish the saved copy from the source to catch accidental overwrites
            // even when this test runs against an export that still includes Walk.
            walk.frameRate = 17f;
            EditorUtility.SetDirty(walk);
        }
        var stop = AssetDatabase.LoadAssetAtPath<AnimationClip>(MechBipedAnimationImport.Folder + "/BoostStop.anim");
        stop.ClearCurves();
        var settings = AnimationUtility.GetAnimationClipSettings(stop);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(stop, settings);
        EditorUtility.SetDirty(stop);
        AssetDatabase.SaveAssets();
        foreach (string name in new[] { "Biped_Run", "Heavy_Walk" })
        {
            string path = MechBipedAnimationImport.Folder + "/" + name + ".anim";
            savedWalks.Add(path, File.ReadAllBytes(path));
        }
        deadline = EditorApplication.timeSinceStartup + 30;
        EditorApplication.update += Check;
        AssetDatabase.ImportAsset(MechBipedAnimationImport.Source, ImportAssetOptions.ForceUpdate);
    }

    // The caller removes only the walking animation entries from the disposable GLB.
    public static void RunWithoutWalkBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Isolated batch copy only.");
        string[] names = { "Fast_Walk", "Biped_Run", "Biped_Walk", "Walk", "Heavy_Walk.001", "Heavy_Walk" };
        if (AssetDatabase.LoadAllAssetsAtPath(MechBipedAnimationImport.Source).OfType<AnimationClip>().Any(c => names.Contains(c.name)))
            throw new Exception("Missing-walk fixture still contains a walking clip.");
        RunBatch();
    }

    public static void RunPartialExportBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Isolated batch copy only.");
        var clips = AssetDatabase.LoadAllAssetsAtPath(MechBipedAnimationImport.Source).OfType<AnimationClip>().ToArray();
        if (clips.Any(c => c.name == "Fast_Boost" || c.name == "Heavy_Boost") || !clips.Any(c => c.name == "Extra_Showcase_Action"))
            throw new Exception("Partial export fixture must omit both Boost clips and include an unrelated extra action.");
        foreach (string file in new[] { "Boost", "Heavy_Boost" })
        {
            string path = MechBipedAnimationImport.Folder + "/" + file + ".anim";
            absentActions.Add(path, File.ReadAllBytes(path));
        }
        RunWithoutWalkBatch();
    }

    static void Check()
    {
        try
        {
            var stop = AssetDatabase.LoadAssetAtPath<AnimationClip>(MechBipedAnimationImport.Folder + "/BoostStop.anim");
            if (AnimationUtility.GetCurveBindings(stop).Length == 0)
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new Exception("GLB reimport did not refresh the derived clip.");
                return;
            }
            if (stop.isLooping) throw new Exception("Reimport lost boost-stop's one-shot setting.");
            foreach (var walk in savedWalks)
                if (!File.ReadAllBytes(walk.Key).SequenceEqual(walk.Value)) throw new Exception("Reimport overwrote the saved gait: " + walk.Key);
            foreach (var action in absentActions)
                if (!File.ReadAllBytes(action.Key).SequenceEqual(action.Value)) throw new Exception("An absent action was overwritten: " + action.Key);
            foreach (var asset in guids)
                if (AssetDatabase.AssetPathToGUID(asset.Key) != asset.Value) throw new Exception("Reimport changed GUID: " + asset.Key);
            foreach (string family in new[] { "Fast", "Heavy" })
            {
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Anim/Locomotion/Biped_" + family + ".controller");
                var walk = AssetDatabase.LoadAssetAtPath<AnimationClip>(MechBipedAnimationImport.Folder
                    + (family == "Fast" ? "/Biped_Run.anim" : "/Heavy_Walk.anim"));
                foreach (var layer in controller.layers)
                {
                    foreach (string state in new[] { "Neutral", "Boost", "BoostStop", "Jump" })
                        if (layer.stateMachine.states.Single(s => s.state.name == state).state.motion != MechBipedAnimationImport.ActionClip(family, state))
                            throw new Exception("Reimport broke the " + family + " " + layer.name + " " + state + " reference.");
                    var tree = (BlendTree)layer.stateMachine.states.Single(s => s.state.name == "Locomotion").state.motion;
                    if (!tree.children.Any(c => c.motion == walk)) throw new Exception("Reimport broke the saved gait reference.");
                }
                foreach (string state in new[] { "Neutral", "Boost", "BoostStop", "Jump" })
                {
                    var source = AssetDatabase.LoadAllAssetsAtPath(MechBipedAnimationImport.Source).OfType<AnimationClip>()
                        .FirstOrDefault(c => c.name == family + "_" + state);
                    if (source == null) continue;
                    var saved = MechBipedAnimationImport.ActionClip(family, state);
                    var bindings = AnimationUtility.GetCurveBindings(source);
                    if (bindings.Length != AnimationUtility.GetCurveBindings(saved).Length
                        || bindings.Any(b => !AnimationUtility.GetEditorCurve(source, b).keys.SequenceEqual(AnimationUtility.GetEditorCurve(saved, b).keys)))
                        throw new Exception("Saved " + family + " " + state + " differs from its authored source motion.");
                }
            }
            Debug.Log("V2 IMPORT CHECKS COMPLETE: saved walking assets unchanged; automatic refresh restores other curves/loop settings and retains GUIDs and controller references.");
            EditorApplication.update -= Check;
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            EditorApplication.update -= Check;
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }
}
