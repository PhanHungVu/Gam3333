using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Replace only the three supplied mission previews. Source art/import settings,
// quest data, slab variants, scene wiring and the empty launch hook stay intact.
public static class PreMissionMissionLogoSetup
{
    public const string SourceDirectory = "Assets/Models/Logos";
    const float Diameter = 1.15f;
    static readonly QuestMissionType[] Types = { QuestMissionType.KingOfTheHill, QuestMissionType.Escort, QuestMissionType.Elimination };
    [MenuItem("Tools/Mech/Pre-Mission/Use Authored Mission Logos")]
    public static void UpgradeAssets()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before replacing mission artwork.");
        var sources = Enumerable.Range(1,3).Select(i => AssetDatabase.LoadAssetAtPath<GameObject>(SourceDirectory+"/Logo"+i+".glb")).ToArray();
        var textures = Enumerable.Range(1,3).Select(i => AssetDatabase.LoadAssetAtPath<Texture2D>(SourceDirectory+"/Logo"+i+"Tex.png")).ToArray();
        if (sources.Any(s => s == null) || textures.Any(t => t == null)) throw new InvalidOperationException("Import Logo1–3 GLBs and their matching textures first.");
        var root = PrefabUtility.LoadPrefabContents(PreMissionStoneBoardSetup.BoardPath);
        try
        {
            var miniature = root.GetComponentInChildren<PreMissionMissionMiniature>(true);
            if (miniature == null) throw new InvalidOperationException("The board's existing mission preview is required.");
            var fields = new SerializedObject(miniature); var display = (Transform)fields.FindProperty("display").objectReferenceValue;
            var plinth = display.Find("Objective Plinth");
            fields.FindProperty("prototypePlinth").objectReferenceValue = plinth != null ? plinth.gameObject : null;
            var variants = fields.FindProperty("variants");
            for (int n=0;n<Types.Length;n++)
            {
                var entry = Enumerable.Range(0,variants.arraySize).Select(i => variants.GetArrayElementAtIndex(i))
                    .Single(v => v.FindPropertyRelative("missionType").enumValueIndex == (int)Types[n]);
                var parent = (Transform)entry.FindPropertyRelative("root").objectReferenceValue;
                while (parent.childCount > 0) Object.DestroyImmediate(parent.GetChild(0).gameObject);
                var fitted = new GameObject("Authored Logo "+(n+1)).transform; fitted.SetParent(parent,false);
                var orientation = new GameObject("Reading Orientation").transform; orientation.SetParent(fitted,false);
                orientation.localRotation = Quaternion.Euler(0,-90,0);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(sources[n], orientation);
                foreach (var collider in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
                foreach (var part in fitted.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = root.layer;
                var material = Material(n+1,textures[n]);
                foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
                {
                    renderer.sharedMaterials = Enumerable.Repeat(material,renderer.sharedMaterials.Length).ToArray();
                    renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
                }
                var bounds = LocalBounds(fitted);
                float scale = Diameter/Mathf.Max(bounds.size.x,bounds.size.z);
                fitted.localScale = Vector3.one*scale;
                fitted.localPosition = new Vector3(-bounds.center.x,-bounds.min.y,-bounds.center.z)*scale;
                entry.FindPropertyRelative("authoredLogo").boolValue = true;
            }
            fields.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.SaveAsPrefabAsset(root,PreMissionStoneBoardSetup.BoardPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets(); Debug.Log("MISSION LOGOS READY: Logo1=KOTH, Logo2=Escort, Logo3=Elimination");
    }
    static Material Material(int index, Texture2D texture)
    {
        string path = PreMissionStoneBoardSetup.DirectoryPath+"/Mission Logo "+index+".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material,path); }
        material.color = Color.white; material.SetTexture("_BaseMap",texture);
        material.SetFloat("_Metallic",0); material.SetFloat("_Smoothness",0.5f);
        material.SetFloat("_Cull",(float)CullMode.Off); material.doubleSidedGI = true;
        EditorUtility.SetDirty(material); return material;
    }
    static Bounds LocalBounds(Transform root)
    {
        var bounds = new Bounds(); bool found = false;
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            var local = filter.sharedMesh.bounds; var matrix = root.worldToLocalMatrix*filter.transform.localToWorldMatrix;
            for(int x=-1;x<=1;x+=2) for(int y=-1;y<=1;y+=2) for(int z=-1;z<=1;z+=2)
            {
                var point = matrix.MultiplyPoint3x4(local.center+Vector3.Scale(local.extents,new Vector3(x,y,z)));
                if (!found) { bounds = new Bounds(point,Vector3.zero); found = true; } else bounds.Encapsulate(point);
            }
        }
        if (!found || Mathf.Max(bounds.size.x,bounds.size.z) < 0.0001f) throw new InvalidOperationException("Logo has no usable render mesh.");
        return bounds;
    }
}
