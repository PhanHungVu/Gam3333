using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using TMPro;

// Explicit asset-only migration. Supplied GLBs, textures and import settings are read-only.
public static class PreMissionArtTableSetup
{
    public const string SourceDirectory="Assets/Models/PremissionHudAssets";
    const string OwnedDirectory=PreMissionStoneBoardSetup.DirectoryPath;
    [MenuItem("Tools/Mech/Pre-Mission/Use Art Table and Slab")]
    public static void UpgradeAssets()
    {
        if(Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before upgrading the board.");
        var table=AssetDatabase.LoadAssetAtPath<GameObject>(SourceDirectory+"/table.glb");
        var sources=Enumerable.Range(1,7).Select(i=>AssetDatabase.LoadAssetAtPath<GameObject>(SourceDirectory+"/Slab"+i+".glb")).ToArray();
        if(table==null || sources.Any(s=>s==null)) throw new InvalidOperationException("Import all seven slabs and table from Models/PremissionHudAssets first.");
        var variants=sources.Select(FittedMesh).ToArray();
        UpgradeQuest(false,variants,Material("Art Slab","SlabTex"));
        UpgradeQuest(true,variants,Material("Art Main Slab","BigSlabTex"));
        var tableMaterial=Material("Art Table","TableTex");
        var root=PrefabUtility.LoadPrefabContents(PreMissionStoneBoardSetup.BoardPath);
        try
        {
            var surface=root.transform.Find("Board Surface");
            if(surface==null) throw new InvalidOperationException("The physical board surface is required.");
            foreach(var renderer in surface.GetComponentsInChildren<MeshRenderer>(true))
                if(renderer.name=="Board Slab" || renderer.name=="Inset Surface" || renderer.name.StartsWith("Frame Side")
                    || renderer.name.StartsWith("Frame End") || renderer.name.StartsWith("Inlay Side") || renderer.name.StartsWith("Inlay End")) renderer.gameObject.SetActive(false);
            if(surface.Find("Art Slab")!=null) UnityEngine.Object.DestroyImmediate(surface.Find("Art Slab").gameObject);
            if(root.transform.Find("Art Table")!=null) UnityEngine.Object.DestroyImmediate(root.transform.Find("Art Table").gameObject);
            var art=surface.Find("Art Table");
            if(art!=null)
            {
                var filter=art.GetComponentInChildren<MeshFilter>();
                if(filter==null || AssetDatabase.GetAssetPath(filter.sharedMesh)!=SourceDirectory+"/table.glb")
                { UnityEngine.Object.DestroyImmediate(art.gameObject); art=null; }
            }
            if(art==null)
            {
                art=FitTable(table,surface,tableMaterial);
                // The broad plateau is mesh-local Y=1, below the decorative rim.
                var filter=art.GetComponentInChildren<MeshFilter>();
                float top=surface.InverseTransformPoint(filter.transform.TransformPoint(Vector3.up)).y;
                art.localPosition+=Vector3.up*(0.02f-top);
            }
            foreach(var renderer in art.GetComponentsInChildren<MeshRenderer>())
            { renderer.sharedMaterials=Enumerable.Repeat(tableMaterial,renderer.sharedMaterials.Length).ToArray(); Shadows(renderer); }
            PrefabUtility.SaveAsPrefabAsset(root,PreMissionStoneBoardSetup.BoardPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets(); Debug.Log("PRE-MISSION HUD ART READY: seven stable slab variants, dedicated main texture and new table");
    }
    static void UpgradeQuest(bool main,Mesh[] variants,Material material)
    {
        string path=OwnedDirectory+(main ? "/MainQuestStone.prefab" : "/SmallQuestStone.prefab");
        var root=PrefabUtility.LoadPrefabContents(path);
        try
        {
            var view=new SerializedObject(root.GetComponent<QuestStoneView>());
            var visual=(Transform)view.FindProperty("visual").objectReferenceValue;
            var old=visual.Find("Faceted Rock"); if(old!=null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var art=visual.Find("Art Quest Slab");
            if(art!=null && art.GetComponent<MeshFilter>()==null) { UnityEngine.Object.DestroyImmediate(art.gameObject); art=null; }
            if(art==null) { art=new GameObject("Art Quest Slab",typeof(MeshFilter),typeof(MeshRenderer)).transform; art.SetParent(visual,false); }
            art.gameObject.layer=root.layer; art.localPosition=Vector3.zero; art.localRotation=Quaternion.identity;
            art.localScale=main ? new Vector3(4.25f,0.65f,2.65f) : new Vector3(2.65f,0.44f,1.75f);
            art.GetComponent<MeshFilter>().sharedMesh=variants[0];
            var renderer=art.GetComponent<MeshRenderer>(); renderer.sharedMaterial=material; Shadows(renderer);
            view.FindProperty("rock").objectReferenceValue=renderer;
            var meshes=view.FindProperty("slabVariants"); meshes.arraySize=variants.Length;
            for(int i=0;i<variants.Length;i++) meshes.GetArrayElementAtIndex(i).objectReferenceValue=variants[i];
            var label=(TMP_Text)view.FindProperty("label").objectReferenceValue;
            label.transform.localPosition=new Vector3(0,main ? 0.67f : 0.46f,0);
            label.rectTransform.sizeDelta=main ? new Vector2(3.75f,2.05f) : new Vector2(2.30f,1.35f);
            view.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.SaveAsPrefabAsset(root,path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    internal static Mesh FittedMesh(GameObject source)
    {
        var original=source.GetComponentInChildren<MeshFilter>().sharedMesh;
        var copy=UnityEngine.Object.Instantiate(original); copy.name="HUD "+source.name+" - Fitted";
        var bounds=original.bounds; var vertices=copy.vertices; var normals=copy.normals; var tangents=copy.tangents;
        var scale=new Vector3(1/bounds.size.x,1/bounds.size.y,1/bounds.size.z);
        var normalMatrix=Matrix4x4.Scale(scale).inverse.transpose;
        double normalFacing=0,volume=0;
        for(int i=0;i<vertices.Length;i++) if(i<normals.Length) normalFacing+=Vector3.Dot(normals[i],vertices[i]-bounds.center);
        for(int sub=0;sub<copy.subMeshCount;sub++)
        {
            var indices=copy.GetTriangles(sub);
            for(int i=0;i<indices.Length;i+=3) volume+=Vector3.Dot(vertices[indices[i]],Vector3.Cross(vertices[indices[i+1]],vertices[indices[i+2]]));
        }
        for(int i=0;i<vertices.Length;i++)
        {
            vertices[i]=Vector3.Scale(vertices[i]-bounds.center,scale)+Vector3.up*0.5f;
            if(i<normals.Length) normals[i]=normalMatrix.MultiplyVector(normals[i]*(normalFacing<0 ? -1 : 1)).normalized;
            if(i<tangents.Length)
            {
                var direction=Vector3.Scale(new Vector3(tangents[i].x,tangents[i].y,tangents[i].z),scale).normalized;
                tangents[i]=new Vector4(direction.x,direction.y,direction.z,tangents[i].w*(normalFacing<0 ? -1 : 1));
            }
        }
        copy.vertices=vertices; copy.normals=normals; copy.tangents=tangents;
        if(volume<0) for(int sub=0;sub<copy.subMeshCount;sub++)
        {
            var indices=copy.GetTriangles(sub);
            for(int i=0;i<indices.Length;i+=3) { int saved=indices[i]; indices[i]=indices[i+1]; indices[i+1]=saved; }
            copy.SetTriangles(indices,sub);
        }
        copy.RecalculateBounds();
        string path=OwnedDirectory+"/HUD "+source.name+" Mesh.asset";
        var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(existing==null) { AssetDatabase.CreateAsset(copy,path); return copy; }
        EditorUtility.CopySerialized(copy,existing); UnityEngine.Object.DestroyImmediate(copy); EditorUtility.SetDirty(existing); return existing;
    }
    static Material Material(string name,string textureName)
    {
        string path=OwnedDirectory+"/"+name+".mat";
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(SourceDirectory+"/"+textureName+".png");
        if(texture==null) throw new InvalidOperationException("Missing supplied texture "+textureName);
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null) { material=new Material(Shader.Find("Universal Render Pipeline/Lit")) { name=name }; AssetDatabase.CreateAsset(material,path); }
        material.color=Color.white; material.SetTexture("_BaseMap",texture); material.SetFloat("_Metallic",0); material.SetFloat("_Smoothness",0.18f);
        material.SetFloat("_Cull",(float)CullMode.Off); material.doubleSidedGI=true; EditorUtility.SetDirty(material); return material;
    }
    static void Shadows(MeshRenderer renderer)
    { renderer.shadowCastingMode=ShadowCastingMode.On; renderer.receiveShadows=true; PrefabUtility.RecordPrefabInstancePropertyModifications(renderer); }
    static Transform FitTable(GameObject asset,Transform parent,Material material)
    {
        var wrapper=new GameObject("Art Table").transform; wrapper.SetParent(parent,false); wrapper.gameObject.layer=26;
        var model=(GameObject)PrefabUtility.InstantiatePrefab(asset); model.transform.SetParent(wrapper,false); model.transform.localRotation=Quaternion.Euler(0,90,0);
        foreach(var node in model.GetComponentsInChildren<Transform>(true)) { node.gameObject.layer=26; PrefabUtility.RecordPrefabInstancePropertyModifications(node.gameObject); }
        foreach(var collider in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
        var renderers=model.GetComponentsInChildren<MeshRenderer>(true);
        var bounds=renderers[0].bounds; foreach(var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        model.transform.localPosition-=wrapper.InverseTransformPoint(bounds.center);
        var size=new Vector3(23.2f,1.25f,12.4f); wrapper.localScale=new Vector3(size.x/bounds.size.x,size.y/bounds.size.y,size.z/bounds.size.z);
        wrapper.localPosition=new Vector3(0,-size.y*0.5f,0);
        foreach(var renderer in renderers) { renderer.sharedMaterials=Enumerable.Repeat(material,renderer.sharedMaterials.Length).ToArray(); Shadows(renderer); }
        PrefabUtility.RecordPrefabInstancePropertyModifications(model.transform); return wrapper;
    }
}
