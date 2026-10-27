using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Explicit art + physical shockwave upgrade. Supplied source art stays unchanged.
public static class PreMissionShockwaveSetup
{
    [MenuItem("Tools/Mech/Pre-Mission/Upgrade Sliding Shockwave and Art")]
    public static void UpgradeAssets()
    {
        if(Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before upgrading the board.");
        PreMissionArtTableSetup.UpgradeAssets();
        string path=PreMissionStoneBoardSetup.DirectoryPath+"/Shockwave Dust.mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null)
        {
            material=new Material(AssetDatabase.LoadAssetAtPath<Material>(PreMissionStoneBoardSetup.DirectoryPath+"/Impact Dust.mat")) { name="Shockwave Dust",renderQueue=2991 };
            material.SetFloat("_Cull",0); AssetDatabase.CreateAsset(material,path);
        }
        var root=PrefabUtility.LoadPrefabContents(PreMissionStoneBoardSetup.BoardPath);
        try
        {
            var wave=root.GetComponentInChildren<PreMissionShockwave>(true);
            if(wave==null)
            {
                var parent=new GameObject("Traveling Tabletop Shockwave"); parent.layer=26; parent.transform.SetParent(root.transform.Find("Board Surface"),false);
                wave=parent.AddComponent<PreMissionShockwave>();
                var ring=new GameObject("Traveling Dust Front",typeof(MeshFilter),typeof(MeshRenderer)); ring.layer=26; ring.transform.SetParent(parent.transform,false);
                var renderer=ring.GetComponent<MeshRenderer>(); renderer.sharedMaterial=material; renderer.enabled=false;
                renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
                var fields=new SerializedObject(wave); fields.FindProperty("dustRing").objectReferenceValue=renderer; fields.ApplyModifiedPropertiesWithoutUndo();
            }
            var motion=new SerializedObject(root.GetComponent<PreMissionStoneMotion>());
            motion.FindProperty("shockwave").objectReferenceValue=wave; motion.ApplyModifiedPropertiesWithoutUndo();
            var feedback=root.GetComponent<QuestBoardImpactFeedback>(); var settings=new SerializedObject(feedback);
            if(settings.FindProperty("impactVisualVersion").intValue<1)
            {
                var small=(ParticleSystem)settings.FindProperty("smallDust").objectReferenceValue;
                var heavy=(ParticleSystem)settings.FindProperty("heavyDust").objectReferenceValue;
                var source=heavy.main; var target=small.main;
                target.startLifetime=source.startLifetime; target.startSpeed=source.startSpeed; target.startSize=source.startSize;
                target.startColor=source.startColor; target.gravityModifier=source.gravityModifier; target.maxParticles=256;
                var shape=small.shape; shape.radius=heavy.shape.radius; shape.angle=heavy.shape.angle;
                var color=small.colorOverLifetime; color.enabled=true; color.color=heavy.colorOverLifetime.color;
                var size=small.sizeOverLifetime; size.enabled=true; size.size=heavy.sizeOverLifetime.size;
                var main=heavy.main; main.maxParticles=384; main.startLifetime=new ParticleSystem.MinMaxCurve(0.65f,1.2f);
                main.startSpeed=new ParticleSystem.MinMaxCurve(2.2f,4f); main.startSize=new ParticleSystem.MinMaxCurve(0.22f,0.55f);
                var heavyShape=heavy.shape; heavyShape.radius=1.8f; heavyShape.angle=85;
                var chips=(ParticleSystem)settings.FindProperty("impactChips").objectReferenceValue;
                var chipMain=chips.main; chipMain.maxParticles=160; chipMain.startSize=new ParticleSystem.MinMaxCurve(0.055f,0.14f);
                var plume=new GameObject("Heavy Impact Plume",typeof(ParticleSystem)).GetComponent<ParticleSystem>();
                plume.transform.SetParent(root.transform,false); plume.gameObject.layer=26;
                plume.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                var pm=plume.main; pm.playOnAwake=false; pm.loop=false; pm.useUnscaledTime=true; pm.simulationSpace=ParticleSystemSimulationSpace.World;
                pm.duration=1.5f; pm.maxParticles=96; pm.startLifetime=new ParticleSystem.MinMaxCurve(0.9f,1.4f);
                pm.startSpeed=new ParticleSystem.MinMaxCurve(0.4f,1.0f); pm.startSize=new ParticleSystem.MinMaxCurve(0.45f,0.95f);
                pm.startColor=new Color(0.69f,0.64f,0.51f,0.5f);
                var emission=plume.emission; emission.enabled=false;
                var ps=plume.shape; ps.shapeType=ParticleSystemShapeType.Cone; ps.radius=1.2f; ps.angle=35; ps.rotation=new Vector3(-90,0,0);
                var pc=plume.colorOverLifetime; pc.enabled=true; pc.color=heavy.colorOverLifetime.color;
                var pr=plume.GetComponent<ParticleSystemRenderer>(); pr.sharedMaterial=heavy.GetComponent<ParticleSystemRenderer>().sharedMaterial;
                pr.shadowCastingMode=ShadowCastingMode.Off; pr.receiveShadows=false;
                settings.FindProperty("heavyPlume").objectReferenceValue=plume;
                settings.FindProperty("smallDustCount").intValue=64; settings.FindProperty("heavyDustCount").intValue=220;
                settings.FindProperty("impactVisualVersion").intValue=1;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
            PrefabUtility.SaveAsPrefabAsset(root,PreMissionStoneBoardSetup.BoardPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets(); Debug.Log("PRE-MISSION SLIDING SHOCKWAVE, BREAKABLE TERRAIN AND ART READY");
    }
}
