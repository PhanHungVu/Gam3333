using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class QuickDashPolishSetup
{
    public const string DustPath = "Assets/Prefabs/Combat/Quick Dash Dust.prefab";
    public const string BoardPath = "Assets/PreMission/StoneBoard/QuestStoneBoard.prefab";
    public const string CameraPath = "Assets/Prefabs/Main Camera.prefab";
    public static readonly string[] PlayerPaths = {
        "Assets/Prefabs/Mech/Base/FullPlayer_mech_golem.prefab", "Assets/Prefabs/Mech/Base/FullPlayer_mech_heavy.prefab",
        "Assets/Prefabs/Mech/Specialist/FullPlayer_mech_specialist.prefab"
    };
    [MenuItem("Tools/Mech/Movement/Apply Quick Dash Polish")]
    public static void Apply()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before updating dash assets.");
        var dustPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DustPath);
        if (dustPrefab == null)
        {
            var template = AssetDatabase.LoadAssetAtPath<GameObject>(BoardPath).GetComponentInChildren<QuestBoardImpactFeedback>(true).SmallDust;
            var root = Object.Instantiate(template.gameObject); root.name = "Quick Dash Dust"; root.layer = 0;
            try
            {
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); root.transform.localScale = Vector3.one;
                var system = root.GetComponent<ParticleSystem>(); system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = system.main; main.playOnAwake = false; main.loop = false; main.useUnscaledTime = false;
                main.simulationSpace = ParticleSystemSimulationSpace.World; main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.duration = .65f; main.maxParticles = 128;
                main.startLifetime = new ParticleSystem.MinMaxCurve(.3f, .6f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 4.5f); main.startSize = new ParticleSystem.MinMaxCurve(.22f, .5f);
                main.startColor = new Color(.69f, .64f, .51f, .65f); main.gravityModifier = .25f;
                var emission = system.emission; emission.enabled = false; emission.burstCount = 0;
                var shape = system.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 28; shape.radius = .4f; shape.rotation = new Vector3(-18, 0, 0);
                var velocity = system.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.World;
                velocity.x = velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
                velocity.y = new ParticleSystem.MinMaxCurve(.4f, 1.2f);
                var renderer = system.GetComponent<ParticleSystemRenderer>(); renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                dustPrefab = PrefabUtility.SaveAsPrefabAsset(root, DustPath);
            }
            finally { Object.DestroyImmediate(root); }
        }
        foreach (var path in PlayerPaths)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var fields = new SerializedObject(root.GetComponent<MechMovementController>());
                fields.FindProperty("quickBoostPeakSpeed").floatValue = 50;
                fields.FindProperty("quickBoostDuration").floatValue = .25f;
                fields.FindProperty("quickBoostRecovery").floatValue = .4f;
                fields.FindProperty("quickBoostDistanceMultiplier").floatValue = 1;
                fields.FindProperty("quickBoostDurationMultiplier").floatValue = 1;
                fields.FindProperty("quickBoostExitBlendStart").floatValue = .8f;
                fields.FindProperty("quickBoostCurve").animationCurveValue = new AnimationCurve(
                    new Keyframe(0, .95f), new Keyframe(.1f, 1), new Keyframe(.3f, .95f),
                    new Keyframe(.5f, .75f), new Keyframe(.75f, .4f), new Keyframe(1, 0));
                bool changed = fields.ApplyModifiedPropertiesWithoutUndo();
                var feedback = root.GetComponent<QuickDashFeedback>();
                if (feedback == null) { feedback = root.AddComponent<QuickDashFeedback>(); changed = true; }
                fields = new SerializedObject(feedback); fields.FindProperty("dustPrefab").objectReferenceValue = dustPrefab.GetComponent<ParticleSystem>();
                fields.FindProperty("dustPoolSize").intValue = 12;
                fields.FindProperty("dustCount").intValue = 48;
                fields.FindProperty("dustScale").floatValue = 1.35f;
                fields.FindProperty("dustLifetimeScale").floatValue = 1.25f;
                fields.FindProperty("dustSpacing").floatValue = 1.6f;
                fields.FindProperty("maxDustPuffsPerDash").intValue = 6;
                changed |= fields.ApplyModifiedPropertiesWithoutUndo();
                if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        var cameraRoot = PrefabUtility.LoadPrefabContents(CameraPath);
        try
        {
            var fields = new SerializedObject(cameraRoot.GetComponent<CameraControlThirdPerson>());
            fields.FindProperty("quickBoostFovKick").floatValue = 5;
            fields.FindProperty("quickBoostFovRiseTime").floatValue = .05f;
            fields.FindProperty("quickBoostFovRecoveryTime").floatValue = .25f;
            fields.FindProperty("quickBoostDampingRecovery").floatValue = .12f;
            if (fields.ApplyModifiedPropertiesWithoutUndo()) PrefabUtility.SaveAsPrefabAsset(cameraRoot, CameraPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(cameraRoot); }
        AssetDatabase.SaveAssets(); Debug.Log("QUICK DASH POLISH READY: integrated movement, shared camera/impulses and separate pooled board-derived dust.");
    }
}
