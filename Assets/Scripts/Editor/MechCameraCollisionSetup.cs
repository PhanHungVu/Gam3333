using System;
using UnityEditor;
using UnityEngine;

// Keep collision geometry on our Unity prefabs, outside the imported models.
public static class MechCameraCollisionSetup
{
    public static readonly string[] StructureNames =
    {
        "platform", "building-platform", "building-roof", "building-structure",
        "bridge", "fence", "rocks-low", "rocks-ramp", "stones", "tent", "tree", "tree-high"
    };

    [MenuItem("Tools/Mech/Map/Add Missing Structure Colliders")]
    public static void AddMissingStructureColliders()
    {
        foreach (string name in StructureNames)
        {
            string path = "Assets/Prefabs/Map Elements/" + name + ".prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int added = 0;
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null || filter.GetComponent<MeshRenderer>() == null) continue;
                    bool solid = false;
                    foreach (var collider in filter.GetComponents<Collider>())
                        solid |= !collider.isTrigger && collider.enabled;
                    if (solid) continue;
                    // Non-convex static meshes preserve openings and sloped surfaces;
                    // a bounds box would incorrectly seal the bridge/building interiors.
                    var meshCollider = filter.gameObject.AddComponent<MeshCollider>();
                    meshCollider.sharedMesh = filter.sharedMesh;
                    added++;
                }
                if (added > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    Debug.Log("Added " + added + " solid collider(s) to " + path);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }

    public static void BuildBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run setup in an isolated batch project.");
        try
        {
            AddMissingStructureColliders();
            Debug.Log("CAMERA COLLISION SETUP COMPLETE");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
