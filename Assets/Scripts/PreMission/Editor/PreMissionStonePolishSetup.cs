using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Explicit M3/M4 upgrade of our board assets; no art-team or gameplay assets change.
public static class PreMissionStonePolishSetup
{
    const string Folder = PreMissionStoneBoardSetup.DirectoryPath;
    [MenuItem("Tools/Mech/Pre-Mission/Upgrade M3 and M4 Stone Presentation")]
    public static void UpgradeAssets()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play mode before upgrading the board.");
        PreMissionStoneBoardSetup.UpgradeAssets();
        var material = DustMaterial();
        var root = PrefabUtility.LoadPrefabContents(PreMissionStoneBoardSetup.BoardPath);
        try
        {
            var motion = root.GetComponent<PreMissionStoneMotion>(); if (motion == null) motion = root.AddComponent<PreMissionStoneMotion>();
            var feedback = root.GetComponent<QuestBoardImpactFeedback>(); if (feedback == null) feedback = root.AddComponent<QuestBoardImpactFeedback>();
            var inlays = root.transform.Find("Impact Inlays");
            if (inlays == null)
            {
                inlays = new GameObject("Impact Inlays").transform; inlays.SetParent(root.transform,false); inlays.gameObject.layer=26;
                foreach (var mesh in root.GetComponentsInChildren<MeshFilter>(true))
                    if (mesh.name == "Reserved Main Stone Inlay") mesh.transform.SetParent(inlays,true);
            }
            var small = Dust(root.transform,"Small Impact Dust",material,false);
            var heavy = Dust(root.transform,"Heavy Impact Dust",material,true);
            var source = root.transform.Find("Stone Impact Audio")?.GetComponent<AudioSource>();
            if (source == null)
            {
                source = new GameObject("Stone Impact Audio",typeof(AudioSource)).GetComponent<AudioSource>(); source.transform.SetParent(root.transform,false);
                source.playOnAwake=false; source.loop=false; source.spatialBlend=0; source.volume=1;
            }
            var fields = new SerializedObject(feedback);
            fields.FindProperty("smallDust").objectReferenceValue=small; fields.FindProperty("heavyDust").objectReferenceValue=heavy;
            fields.FindProperty("impactAudio").objectReferenceValue=source;
            fields.FindProperty("inlays").objectReferenceValue=inlays; fields.ApplyModifiedPropertiesWithoutUndo();
            var motionFields=new SerializedObject(motion); motionFields.FindProperty("feedback").objectReferenceValue=feedback; motionFields.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root,PreMissionStoneBoardSetup.BoardPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        var flow=Object.FindAnyObjectByType<PreMissionFlowController>();
        var controller=new SerializedObject(flow);
        var skip=(Button)controller.FindProperty("skipPostingButton").objectReferenceValue;
        var label=skip.GetComponentInChildren<TMP_Text>(true); label.text="Skip Arrival (Space)";
        PrefabUtility.RecordPrefabInstancePropertyModifications(label);
        EditorSceneManager.SaveScene(flow.gameObject.scene); AssetDatabase.SaveAssets();
        Debug.Log("PRE-MISSION M3/M4 STONE PRESENTATION READY");
    }
    static ParticleSystem Dust(Transform root,string name,Material material,bool heavy)
    {
        var system=root.Find(name)?.GetComponent<ParticleSystem>(); if(system!=null) return system;
        system=new GameObject(name,typeof(ParticleSystem)).GetComponent<ParticleSystem>(); system.transform.SetParent(root,false); system.gameObject.layer=26;
        system.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=system.main; main.playOnAwake=false; main.loop=false; main.duration=1.2f; main.useUnscaledTime=true; main.simulationSpace=ParticleSystemSimulationSpace.World;
        main.maxParticles=128; main.startLifetime=new ParticleSystem.MinMaxCurve(heavy?0.55f:0.3f,heavy?0.95f:0.55f);
        main.startSpeed=new ParticleSystem.MinMaxCurve(0.15f,heavy?0.85f:0.4f); main.startSize=new ParticleSystem.MinMaxCurve(heavy?0.08f:0.045f,heavy?0.22f:0.11f);
        main.startColor=new Color(0.69f,0.64f,0.51f,0.55f); main.gravityModifier=0.01f;
        var emission=system.emission; emission.enabled=false;
        var shape=system.shape; shape.shapeType=ParticleSystemShapeType.Cone; shape.angle=75; shape.radius=heavy?1.35f:0.6f; shape.rotation=new Vector3(-90,0,0);
        var color=system.colorOverLifetime; color.enabled=true;
        var gradient=new Gradient(); gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
            new[]{new GradientAlphaKey(0.7f,0),new GradientAlphaKey(0.45f,0.25f),new GradientAlphaKey(0,1)}); color.color=gradient;
        var size=system.sizeOverLifetime; size.enabled=true; size.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,0.65f),new Keyframe(0.5f,1.2f),new Keyframe(1,1.6f)));
        var renderer=system.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial=material;
        renderer.renderMode=ParticleSystemRenderMode.Billboard; renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
        return system;
    }
    static Material DustMaterial()
    {
        string path=Folder+"/Impact Dust.mat"; var existing=AssetDatabase.LoadAssetAtPath<Material>(path); if(existing!=null) return existing;
        string texturePath=Folder+"/Dust Puff.asset"; var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if(texture==null)
        {
            texture=new Texture2D(32,32,TextureFormat.RGBA32,false) { name="Prototype Dust Puff",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear };
            for(int y=0;y<32;y++) for(int x=0;x<32;x++)
            { float px=(x-15.5f)/15.5f,py=(y-15.5f)/15.5f; texture.SetPixel(x,y,new Color(1,1,1,Mathf.Pow(Mathf.Max(0,1-px*px-py*py),2))); }
            texture.Apply(); AssetDatabase.CreateAsset(texture,texturePath);
        }
        var shader=Shader.Find("Universal Render Pipeline/Particles/Unlit"); if(shader==null) throw new Exception("URP particle shader unavailable");
        var result=new Material(shader) { name="Impact Dust",renderQueue=(int)RenderQueue.Transparent };
        result.SetTexture("_BaseMap",texture); result.SetColor("_BaseColor",Color.white); result.SetFloat("_Surface",1);
        result.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha); result.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha); result.SetFloat("_ZWrite",0);
        result.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); result.SetShaderPassEnabled("ShadowCaster",false);
        AssetDatabase.CreateAsset(result,path); return result;
    }
}
