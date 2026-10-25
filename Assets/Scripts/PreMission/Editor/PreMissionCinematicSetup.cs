using System;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

// Explicit P3 prototype upgrade. Does not change UI prefabs, authored quests or gameplay.
public static class PreMissionCinematicSetup
{
    [MenuItem("Tools/Mech/Pre-Mission/Upgrade P3 Cinematic Briefing")]
    public static void UpgradeAssets()
    {
        if(Application.isPlaying) throw new InvalidOperationException("Stop Play mode before upgrading the board.");
        PreMissionProgressTrackSetup.UpgradeAssets();
        var root=PrefabUtility.LoadPrefabContents(PreMissionStoneBoardSetup.BoardPath);
        try
        {
            var miniature=root.GetComponentInChildren<PreMissionMissionMiniature>(true);
            bool created=miniature==null;
            if(created)
            {
                string folder=PreMissionStoneBoardSetup.DirectoryPath;
                var frame=AssetDatabase.LoadAssetAtPath<Material>(folder+"/Board Frame.mat");
                var stone=AssetDatabase.LoadAssetAtPath<Material>(folder+"/Small Stone.mat");
                var objective=AssetDatabase.LoadAssetAtPath<Material>(folder+"/Selection Brass.mat");
                var core=AssetDatabase.LoadAssetAtPath<Material>(folder+"/Main Stone.mat");
                var rock=AssetDatabase.LoadAssetAtPath<Mesh>(folder+"/PrototypeRock.asset");
                var parent=new GameObject("Mission Miniature"); parent.layer=26; parent.transform.SetParent(root.transform,false);
                miniature=parent.AddComponent<PreMissionMissionMiniature>();
                var display=new GameObject("Miniature Display").transform; display.SetParent(parent.transform,false); display.gameObject.layer=26;
                Primitive("Objective Plinth",display,PrimitiveType.Cylinder,new Vector3(0,0.025f,0),new Vector3(1.95f,0.025f,1.3f),frame);
                var types=(QuestMissionType[])Enum.GetValues(typeof(QuestMissionType));
                var fields=new SerializedObject(miniature); fields.FindProperty("display").objectReferenceValue=display;
                var variants=fields.FindProperty("variants"); variants.arraySize=types.Length;
                for(int i=0;i<types.Length;i++)
                {
                    var group=new GameObject(types[i]+" Objective").transform; group.SetParent(display,false); group.gameObject.layer=26;
                    Build(types[i],group,frame,stone,objective,core,rock);
                    var variant=variants.GetArrayElementAtIndex(i); variant.FindPropertyRelative("missionType").enumValueIndex=i;
                    variant.FindPropertyRelative("root").objectReferenceValue=group; group.gameObject.SetActive(false);
                }
                fields.ApplyModifiedPropertiesWithoutUndo(); display.gameObject.SetActive(false);
            }
            var motion=new SerializedObject(root.GetComponent<PreMissionStoneMotion>());
            motion.FindProperty("missionMiniature").objectReferenceValue=miniature;
            if(created)
            {
                if(Mathf.Approximately(motion.FindProperty("inspectDuration").floatValue,0.72f)) motion.FindProperty("inspectDuration").floatValue=0.52f;
                if(Mathf.Approximately(motion.FindProperty("returnDuration").floatValue,0.64f)) motion.FindProperty("returnDuration").floatValue=0.44f;
            }
            motion.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.SaveAsPrefabAsset(root,PreMissionStoneBoardSetup.BoardPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets(); Debug.Log("PRE-MISSION P3 CINEMATIC BRIEFING READY");
    }
    static void Build(QuestMissionType type,Transform parent,Material frame,Material stone,Material objective,Material core,Mesh rock)
    {
        switch(type)
        {
            case QuestMissionType.Retrieval:
                Primitive("Destination",parent,PrimitiveType.Cylinder,new Vector3(0.45f,0.07f,0),new Vector3(0.5f,0.02f,0.5f),objective);
                Rock("Recoverable Object",parent,new Vector3(0.45f,0.1f,0),new Vector3(0.20f,0.35f,0.20f),core,rock);
                Rock("Traveller",parent,new Vector3(-0.5f,0.07f,0),new Vector3(0.16f,0.25f,0.16f),stone,rock);
                Primitive("Route",parent,PrimitiveType.Cube,new Vector3(-0.05f,0.065f,0),new Vector3(0.55f,0.02f,0.055f),objective);
                break;
            case QuestMissionType.Elimination:
                Primitive("Target Base",parent,PrimitiveType.Cylinder,new Vector3(0,0.07f,0),new Vector3(0.9f,0.02f,0.9f),frame);
                Primitive("Target Marker",parent,PrimitiveType.Cylinder,new Vector3(0,0.25f,0),new Vector3(0.30f,0.18f,0.30f),stone);
                Primitive("Target Crossbar X",parent,PrimitiveType.Cube,new Vector3(0,0.45f,0),new Vector3(0.6f,0.045f,0.045f),objective);
                Primitive("Target Crossbar Z",parent,PrimitiveType.Cube,new Vector3(0,0.45f,0),new Vector3(0.045f,0.045f,0.6f),objective);
                break;
            case QuestMissionType.KingOfTheHill:
                Primitive("Capture Area",parent,PrimitiveType.Cylinder,new Vector3(0,0.075f,0),new Vector3(1.15f,0.025f,0.85f),objective);
                Primitive("Capture Post",parent,PrimitiveType.Cylinder,new Vector3(0,0.32f,0),new Vector3(0.07f,0.25f,0.07f),stone);
                Primitive("Capture Flag",parent,PrimitiveType.Cube,new Vector3(0.18f,0.48f,0),new Vector3(0.36f,0.20f,0.045f),stone);
                break;
            case QuestMissionType.Escort:
                Primitive("Escort Route",parent,PrimitiveType.Cube,new Vector3(0,0.065f,0),new Vector3(1.25f,0.02f,0.07f),objective);
                Rock("Escort Token",parent,new Vector3(-0.35f,0.08f,0),new Vector3(0.22f,0.34f,0.22f),stone,rock);
                Primitive("Destination",parent,PrimitiveType.Cylinder,new Vector3(0.58f,0.08f,0),new Vector3(0.32f,0.025f,0.32f),objective);
                break;
            case QuestMissionType.Defense:
                Rock("Protected Core",parent,new Vector3(0,0.08f,0),new Vector3(0.30f,0.45f,0.30f),core,rock);
                for(int i=0;i<3;i++) Primitive("Guard Wall "+i,parent,PrimitiveType.Cube,new Vector3((i-1)*0.43f,0.20f,0.34f),new Vector3(0.34f,0.26f,0.12f),stone);
                break;
        }
    }
    static void Primitive(string name,Transform parent,PrimitiveType kind,Vector3 position,Vector3 scale,Material material)
    {
        var item=GameObject.CreatePrimitive(kind); item.name=name; item.layer=26; item.transform.SetParent(parent,false);
        item.transform.localPosition=position; item.transform.localScale=scale; item.GetComponent<MeshRenderer>().sharedMaterial=material;
        Object.DestroyImmediate(item.GetComponent<Collider>());
    }
    static void Rock(string name,Transform parent,Vector3 position,Vector3 scale,Material material,Mesh mesh)
    {
        var item=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer)); item.layer=26; item.transform.SetParent(parent,false);
        item.transform.localPosition=position; item.transform.localScale=scale; item.GetComponent<MeshFilter>().sharedMesh=mesh; item.GetComponent<MeshRenderer>().sharedMaterial=material;
    }
}
