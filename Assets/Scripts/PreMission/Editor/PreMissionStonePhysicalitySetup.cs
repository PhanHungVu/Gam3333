using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Explicit P1 upgrade of owned prototype geometry; preserves existing layout/art tuning.
public static class PreMissionStonePhysicalitySetup
{
    [MenuItem("Tools/Mech/Pre-Mission/Upgrade P1 Stone Physicality")]
    public static void UpgradeAssets()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play mode before upgrading the board.");
        PreMissionStonePolishSetup.UpgradeAssets();
        var root=PrefabUtility.LoadPrefabContents(PreMissionStoneBoardSetup.BoardPath);
        try
        {
            var surface=root.transform.Find("Board Surface");
            if (surface==null)
            {
                surface=new GameObject("Board Surface").transform;
                surface.SetParent(root.transform,false); surface.gameObject.layer=26;
                foreach (var child in root.transform.Cast<Transform>().ToArray())
                    if (child.GetComponent<MeshRenderer>()!=null && child.GetComponent<ParticleSystem>()==null)
                        child.SetParent(surface,true);
            }
            var chips=root.transform.Find("Impact Chips")?.GetComponent<ParticleSystem>();
            if (chips==null)
            {
                chips=new GameObject("Impact Chips",typeof(ParticleSystem)).GetComponent<ParticleSystem>();
                chips.transform.SetParent(root.transform,false); chips.gameObject.layer=26;
                chips.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                var main=chips.main; main.playOnAwake=false; main.loop=false; main.useUnscaledTime=true;
                main.simulationSpace=ParticleSystemSimulationSpace.World; main.duration=0.8f; main.maxParticles=24;
                main.startLifetime=new ParticleSystem.MinMaxCurve(0.24f,0.42f);
                main.startSpeed=new ParticleSystem.MinMaxCurve(0.65f,1.5f);
                main.startSize=new ParticleSystem.MinMaxCurve(0.035f,0.075f); main.gravityModifier=0.7f;
                main.startRotation=new ParticleSystem.MinMaxCurve(0,Mathf.PI*2);
                var emission=chips.emission; emission.enabled=false;
                var shape=chips.shape; shape.shapeType=ParticleSystemShapeType.Cone;
                shape.angle=65; shape.radius=1.8f; shape.radiusThickness=0.15f; shape.rotation=new Vector3(-90,0,0);
                var size=chips.sizeOverLifetime; size.enabled=true;
                size.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,1),new Keyframe(0.65f,1),new Keyframe(1,0)));
                var renderer=chips.GetComponent<ParticleSystemRenderer>(); renderer.renderMode=ParticleSystemRenderMode.Mesh;
                renderer.mesh=AssetDatabase.LoadAssetAtPath<Mesh>(PreMissionStoneBoardSetup.DirectoryPath+"/PrototypeRock.asset");
                renderer.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(PreMissionStoneBoardSetup.DirectoryPath+"/Main Stone.mat");
                renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
            }
            var fields=new SerializedObject(root.GetComponent<QuestBoardImpactFeedback>());
            fields.FindProperty("boardSurface").objectReferenceValue=surface;
            fields.FindProperty("impactChips").objectReferenceValue=chips;
            fields.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root,PreMissionStoneBoardSetup.BoardPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        Debug.Log("PRE-MISSION P1 STONE PHYSICALITY READY");
    }
}
