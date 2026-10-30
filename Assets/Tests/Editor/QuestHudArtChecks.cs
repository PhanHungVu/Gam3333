using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class QuestHudArtChecks
{
    static int checks;
    static void Check(bool valid,string message)
    { if(!valid) throw new Exception(message); checks++; Debug.Log("HUD ART PASS: "+message); }
    public static void RunBatch()
    {
        string root=Directory.GetParent(Application.dataPath).FullName;
        if(!Application.isBatchMode || !Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))
            throw new Exception("Disposable project only");
        for(int i=1;i<=7;i++)
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(PreMissionArtTableSetup.SourceDirectory+"/Slab"+i+".glb").GetComponentInChildren<MeshFilter>().sharedMesh;
            var fitted=AssetDatabase.LoadAssetAtPath<Mesh>(PreMissionStoneBoardSetup.DirectoryPath+"/HUD Slab"+i+" Mesh.asset");
            Check(fitted!=null&&fitted.uv.SequenceEqual(source.uv),"Slab "+i+" retains the supplied UVs");
            var size=source.bounds.size; var scale=new Vector3(1/size.x,1/size.y,1/size.z);
            Check(fitted.vertices.SequenceEqual(source.vertices.Select(v=>Vector3.Scale(v-source.bounds.center,scale)+Vector3.up*0.5f))
                &&Vector3.Distance(fitted.bounds.size,Vector3.one)<0.00001f&&Mathf.Abs(fitted.bounds.min.y)<0.00001f,
                "Slab "+i+" preserves its shape within the shared logical footprint/contact plane");
            float facing=0; var vertices=fitted.vertices; var normals=fitted.normals;
            for(int v=0;v<vertices.Length;v++) facing+=Vector3.Dot(normals[v],vertices[v]-fitted.bounds.center);
            Check(facing>0,"Slab "+i+" has outward lighting normals");
        }
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PreMissionStoneBoardSetup.DirectoryPath+"/SmallQuestStone.prefab");
        var catalogue=AssetDatabase.LoadAssetAtPath<QuestBoardCatalogue>("Assets/PreMission/PrototypeBoardCatalogue.asset");
        var instance=Object.Instantiate(prefab); var stone=instance.GetComponent<QuestStoneView>(); var seen=new HashSet<int>();
        var randomState=JsonUtility.ToJson(UnityEngine.Random.state);
        try
        {
            for(int seed=0;seed<32;seed++)
            {
                var first=new QuestProgressionSession(catalogue,seed); var replay=new QuestProgressionSession(catalogue,seed); bool same=true;
                for(int slot=0;slot<first.Offers.Count;slot++)
                {
                    stone.Bind(first.Offers[slot],seed); int chosen=stone.SlabVariantIndex; seen.Add(chosen);
                    stone.Bind(replay.Offers[slot],seed); same&=chosen==stone.SlabVariantIndex;
                }
                Check(same,"Seed "+seed+" reconstructs the same appearance despite different run/offer GUIDs");
            }
            Check(seen.Count==7,"Seeded offers can choose all seven supplied shapes");
            Check(randomState==JsonUtility.ToJson(UnityEngine.Random.state),"Appearance choices do not consume Unity gameplay RNG");
        }
        finally { Object.DestroyImmediate(instance); }
        Debug.Log("HUD ART CHECKS COMPLETE: "+checks);
    }
}
