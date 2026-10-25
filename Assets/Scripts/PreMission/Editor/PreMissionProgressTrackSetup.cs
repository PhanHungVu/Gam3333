using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

// Explicit P2 prototype upgrade. Existing board slots, camera framing and art stay intact.
public static class PreMissionProgressTrackSetup
{
    [MenuItem("Tools/Mech/Pre-Mission/Upgrade P2 Physical Progress Track")]
    public static void UpgradeAssets()
    {
        if(Application.isPlaying) throw new InvalidOperationException("Stop Play mode before upgrading the board.");
        PreMissionStonePhysicalitySetup.UpgradeAssets();
        var root=PrefabUtility.LoadPrefabContents(PreMissionStoneBoardSetup.BoardPath);
        try
        {
            var track=root.GetComponentInChildren<PreMissionProgressTrack>(true);
            if(track==null)
            {
                string folder=PreMissionStoneBoardSetup.DirectoryPath;
                var pending=AssetDatabase.LoadAssetAtPath<Material>(folder+"/Board Frame.mat");
                var complete=AssetDatabase.LoadAssetAtPath<Material>(folder+"/Selection Brass.mat");
                var markerMaterial=AssetDatabase.LoadAssetAtPath<Material>(folder+"/Main Stone.mat");
                var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/century-gothic-bold-4_ufonts.asset");
                var parent=new GameObject("Physical Progress Track"); parent.layer=26;
                parent.transform.SetParent(root.transform.Find("Board Surface"),false); parent.transform.localPosition=new Vector3(-7.95f,0.10f,0);
                track=parent.AddComponent<PreMissionProgressTrack>();
                var visual=new GameObject("Track Visual").transform; visual.SetParent(parent.transform,false); visual.gameObject.layer=26;
                Cube("Track Plinth",visual,new Vector3(0,0,0),new Vector3(0.85f,0.12f,6.9f),pending);
                Cube("Progress Rail",visual,new Vector3(-0.08f,0.065f,0),new Vector3(0.035f,0.018f,5f),complete);
                var nodes=new GameObject("Progress Points").transform; nodes.SetParent(visual,false); nodes.gameObject.layer=26;
                var marker=new GameObject("Progress Marker",typeof(MeshFilter),typeof(MeshRenderer)).transform;
                marker.SetParent(visual,false); marker.gameObject.layer=26; marker.localScale=new Vector3(0.22f,0.34f,0.22f);
                marker.GetComponent<MeshFilter>().sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(folder+"/PrototypeRock.asset");
                marker.GetComponent<MeshRenderer>().sharedMaterial=markerMaterial;
                Label("Progress Heading",visual,"MAIN\nPROGRESS",new Vector3(0,0.10f,3.05f),font,1.3f,new Vector2(0.78f,0.55f));
                var counter=Label("Progress Count",visual,"0 / 3",new Vector3(0,0.10f,-3.05f),font,2f,new Vector2(0.78f,0.4f));
                var fields=new SerializedObject(track);
                fields.FindProperty("visual").objectReferenceValue=visual; fields.FindProperty("nodeParent").objectReferenceValue=nodes;
                fields.FindProperty("marker").objectReferenceValue=marker; fields.FindProperty("counter").objectReferenceValue=counter;
                fields.FindProperty("labelFont").objectReferenceValue=font; fields.FindProperty("pendingMaterial").objectReferenceValue=pending;
                fields.FindProperty("completeMaterial").objectReferenceValue=complete; fields.ApplyModifiedPropertiesWithoutUndo();
            }
            var motion=new SerializedObject(root.GetComponent<PreMissionStoneMotion>());
            motion.FindProperty("progressTrack").objectReferenceValue=track; motion.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root,PreMissionStoneBoardSetup.BoardPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets(); Debug.Log("PRE-MISSION P2 PHYSICAL PROGRESS TRACK READY");
    }
    static void Cube(string name,Transform parent,Vector3 position,Vector3 scale,Material material)
    {
        var cube=GameObject.CreatePrimitive(PrimitiveType.Cube); cube.name=name; cube.layer=26;
        cube.transform.SetParent(parent,false); cube.transform.localPosition=position; cube.transform.localScale=scale;
        cube.GetComponent<MeshRenderer>().sharedMaterial=material; Object.DestroyImmediate(cube.GetComponent<Collider>());
    }
    static TMP_Text Label(string name,Transform parent,string text,Vector3 position,TMP_FontAsset font,float size,Vector2 bounds)
    {
        var label=new GameObject(name,typeof(TextMeshPro)).GetComponent<TextMeshPro>(); label.gameObject.layer=26;
        label.transform.SetParent(parent,false); label.transform.localPosition=position; label.transform.localRotation=Quaternion.Euler(90,0,0);
        label.font=font; label.fontSize=size; label.rectTransform.sizeDelta=bounds; label.alignment=TextAlignmentOptions.Center;
        label.text=text; label.raycastTarget=false; label.GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.Off;
        return label;
    }
}
