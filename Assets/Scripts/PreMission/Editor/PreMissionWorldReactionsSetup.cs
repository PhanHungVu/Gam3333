using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// New-plan P2: only owned prefab/material changes. Preserves lighting, audio and tuning.
public static class PreMissionWorldReactionsSetup
{
    public const string MaterialPath=PreMissionStoneBoardSetup.DirectoryPath+"/Contact Shadow.mat";
    [MenuItem("Tools/Mech/Pre-Mission/Upgrade P2 World Reactions")]
    public static void UpgradeAssets()
    {
        if(Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before upgrading the board.");
        var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if(material==null)
        {
            var shader=Shader.Find("Universal Render Pipeline/Unlit");
            if(shader==null) throw new InvalidOperationException("URP Unlit is required for the optional contact shadow.");
            material=new Material(shader) { name="Contact Shadow",renderQueue=(int)RenderQueue.Transparent-10 };
            material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(PreMissionStoneBoardSetup.DirectoryPath+"/Dust Puff.asset"));
            material.SetColor("_BaseColor",new Color(0,0,0,0.2f));
            material.SetFloat("_Surface",1); material.SetFloat("_Blend",0); material.SetFloat("_ZWrite",0); material.SetFloat("_Cull",0);
            material.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha); material.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha",(float)BlendMode.One); material.SetFloat("_DstBlendAlpha",(float)BlendMode.OneMinusSrcAlpha);
            material.SetOverrideTag("RenderType","Transparent"); material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetShaderPassEnabled("ShadowCaster",false); material.SetShaderPassEnabled("DepthOnly",false);
            AssetDatabase.CreateAsset(material,MaterialPath);
        }
        var root=PrefabUtility.LoadPrefabContents(PreMissionStoneBoardSetup.BoardPath);
        try
        {
            var shadow=root.GetComponentInChildren<PreMissionContactShadow>(true);
            if(shadow==null)
            {
                var parent=new GameObject("Approaching Contact Shadow"); parent.layer=root.layer;
                parent.transform.SetParent(root.transform.Find("Board Surface") ?? root.transform,false);
                shadow=parent.AddComponent<PreMissionContactShadow>();
                var quad=GameObject.CreatePrimitive(PrimitiveType.Quad); quad.name="Temporary Shadow Surface"; quad.layer=root.layer;
                quad.transform.SetParent(parent.transform,false); quad.transform.localRotation=Quaternion.Euler(90,0,0);
                UnityEngine.Object.DestroyImmediate(quad.GetComponent<Collider>());
                var renderer=quad.GetComponent<MeshRenderer>(); renderer.sharedMaterial=material;
                renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false; renderer.enabled=false;
                var fields=new SerializedObject(shadow); fields.FindProperty("surface").objectReferenceValue=renderer; fields.ApplyModifiedPropertiesWithoutUndo();
            }
            var motion=new SerializedObject(root.GetComponent<PreMissionStoneMotion>());
            motion.FindProperty("contactShadow").objectReferenceValue=shadow; motion.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root,PreMissionStoneBoardSetup.BoardPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets(); Debug.Log("PRE-MISSION P2 WORLD REACTIONS READY");
    }
}
