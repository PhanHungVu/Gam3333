using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using TMPro;

// Fit supplied art in a wrapper; never edits its GLBs, textures or import settings.
public static class PreMissionArtTableSetup
{
    [MenuItem("Tools/Mech/Pre-Mission/Use Art Table and Slab")]
    public static void UpgradeAssets()
    {
        if(Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before upgrading the board.");
        var slab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/slab.glb");
        var table=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/table.glb");
        if(slab==null || table==null) throw new InvalidOperationException("Import Models/slab.glb and Models/table.glb first.");
        var slabMat=Material("Art Slab","SlabTex"); var tableMat=Material("Art Table","TableTex");
        UpgradeQuest(false,slab,slabMat); UpgradeQuest(true,slab,slabMat);
        var root=PrefabUtility.LoadPrefabContents(PreMissionStoneBoardSetup.BoardPath);
        try
        {
            var surface=root.transform.Find("Board Surface");
            if(surface==null) throw new InvalidOperationException("The physical board surface is required.");
            foreach(var renderer in surface.GetComponentsInChildren<MeshRenderer>(true))
                if(renderer.name=="Board Slab" || renderer.name=="Inset Surface" || renderer.name.StartsWith("Frame Side")
                    || renderer.name.StartsWith("Frame End") || renderer.name.StartsWith("Inlay Side") || renderer.name.StartsWith("Inlay End"))
                    renderer.gameObject.SetActive(false);
            // Migrate the previous incorrect assignment: slab is a quest object,
            // and the table itself is the surface receiving terrain and impacts.
            if(surface.Find("Art Slab")!=null) UnityEngine.Object.DestroyImmediate(surface.Find("Art Slab").gameObject);
            if(root.transform.Find("Art Table")!=null) UnityEngine.Object.DestroyImmediate(root.transform.Find("Art Table").gameObject);
            var art=surface.Find("Art Table");
            if(art==null)
            {
                art=Fit("Art Table",table,surface,tableMat,new Vector3(23.2f,1.25f,12.4f),0,90);
                // In the supplied model the broad flat top is mesh-local Y=1;
                // the decorative rim is higher and must not define contact height.
                var filter=art.GetComponentInChildren<MeshFilter>();
                float top=surface.InverseTransformPoint(filter.transform.TransformPoint(Vector3.up)).y;
                art.localPosition+=Vector3.up*(0.02f-top);
            }
            foreach(var renderer in art.GetComponentsInChildren<MeshRenderer>()) Shadows(renderer);
            var track=root.GetComponentInChildren<PreMissionProgressTrack>(true);
            if(track!=null) { var p=track.transform.localPosition; p.x=-9f; track.transform.localPosition=p; }
            var board=new SerializedObject(root.GetComponent<PreMissionStoneBoard>());
            board.FindProperty("wholeTableLayout").boolValue=true;
            board.FindProperty("framingSize").vector3Value=new Vector3(23.5f,3f,11.8f);
            board.FindProperty("framingCenter").vector3Value=new Vector3(0,0.5f,0);
            board.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root,PreMissionStoneBoardSetup.BoardPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets(); Debug.Log("PRE-MISSION ART TABLE AND SLAB READY");
    }
    static void UpgradeQuest(bool main,GameObject slab,Material material)
    {
        string path=PreMissionStoneBoardSetup.DirectoryPath+(main ? "/MainQuestStone.prefab" : "/SmallQuestStone.prefab");
        var root=PrefabUtility.LoadPrefabContents(path);
        try
        {
            var view=new SerializedObject(root.GetComponent<QuestStoneView>());
            var visual=(Transform)view.FindProperty("visual").objectReferenceValue;
            var old=visual.Find("Faceted Rock"); if(old!=null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var art=visual.Find("Art Quest Slab");
            if(art==null) art=Fit("Art Quest Slab",slab,visual,material,main ? new Vector3(4.25f,0.65f,2.65f) : new Vector3(2.65f,0.44f,1.75f),main ? 0.65f : 0.44f,0);
            var renderer=art.GetComponentInChildren<MeshRenderer>(); Shadows(renderer);
            var filter=renderer.GetComponent<MeshFilter>(); filter.sharedMesh=SlabMesh(slab);
            PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
            view.FindProperty("rock").objectReferenceValue=renderer;
            var label=(TMP_Text)view.FindProperty("label").objectReferenceValue;
            label.transform.localPosition=new Vector3(0,main ? 0.67f : 0.46f,0);
            label.rectTransform.sizeDelta=main ? new Vector2(3.75f,2.05f) : new Vector2(2.30f,1.35f);
            view.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root,path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    static void Shadows(MeshRenderer renderer)
    {
        renderer.shadowCastingMode=ShadowCastingMode.On; renderer.receiveShadows=true;
        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
    }
    static Mesh SlabMesh(GameObject source)
    {
        const string path=PreMissionStoneBoardSetup.DirectoryPath+"/Quest Slab Mesh.asset";
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path); if(mesh!=null) return mesh;
        // The source's top and bottom face normals point into the slab. Preserve
        // its exact vertices/UVs and reverse only the owned render copy's faces.
        mesh=UnityEngine.Object.Instantiate(source.GetComponentInChildren<MeshFilter>().sharedMesh);
        mesh.name="Quest Slab - Outward Faces";
        var normals=mesh.normals; for(int i=0;i<normals.Length;i++) normals[i]=-normals[i]; mesh.normals=normals;
        var tangents=mesh.tangents; for(int i=0;i<tangents.Length;i++) tangents[i].w=-tangents[i].w; mesh.tangents=tangents;
        for(int sub=0;sub<mesh.subMeshCount;sub++)
        {
            var indices=mesh.GetIndices(sub);
            for(int i=0;i<indices.Length;i+=3) { int saved=indices[i]; indices[i]=indices[i+1]; indices[i+1]=saved; }
            mesh.SetIndices(indices,MeshTopology.Triangles,sub);
        }
        AssetDatabase.CreateAsset(mesh,path); return mesh;
    }
    static Material Material(string name,string textureName)
    {
        string path=PreMissionStoneBoardSetup.DirectoryPath+"/"+name+".mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material!=null) { TwoSided(material); return material; }
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Materials/TableandSlabTextures/"+textureName+".png");
        if(texture==null) throw new InvalidOperationException("Missing supplied texture "+textureName);
        material=new Material(Shader.Find("Universal Render Pipeline/Lit")) { name=name,color=Color.white };
        material.SetTexture("_BaseMap",texture); material.SetFloat("_Metallic",0); material.SetFloat("_Smoothness",0.18f);
        TwoSided(material);
        AssetDatabase.CreateAsset(material,path); return material;
    }
    static void TwoSided(Material material)
    {
        // Both supplied glTF materials explicitly use doubleSided=true. In
        // particular the slab's upper face must not be culled by a Lit override.
        material.SetFloat("_Cull",(float)CullMode.Off); material.doubleSidedGI=true;
        EditorUtility.SetDirty(material);
    }
    static Transform Fit(string name,GameObject asset,Transform parent,Material material,Vector3 size,float top,float yaw)
    {
        var wrapper=new GameObject(name).transform; wrapper.SetParent(parent,false); wrapper.gameObject.layer=26;
        var model=(GameObject)PrefabUtility.InstantiatePrefab(asset); model.transform.SetParent(wrapper,false);
        model.transform.localRotation=Quaternion.Euler(0,yaw,0);
        foreach(var node in model.GetComponentsInChildren<Transform>(true)) { node.gameObject.layer=26; PrefabUtility.RecordPrefabInstancePropertyModifications(node.gameObject); }
        foreach(var collider in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
        var renderers=model.GetComponentsInChildren<Renderer>(true);
        var bounds=renderers[0].bounds; foreach(var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        model.transform.localPosition-=wrapper.InverseTransformPoint(bounds.center);
        wrapper.localScale=new Vector3(size.x/bounds.size.x,size.y/bounds.size.y,size.z/bounds.size.z);
        wrapper.localPosition=new Vector3(0,top-size.y*0.5f,0);
        foreach(var renderer in renderers)
        {
            renderer.sharedMaterials=Enumerable.Repeat(material,renderer.sharedMaterials.Length).ToArray();
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }
        PrefabUtility.RecordPrefabInstancePropertyModifications(model.transform);
        return wrapper;
    }
}
