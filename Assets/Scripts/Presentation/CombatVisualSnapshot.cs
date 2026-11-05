using System.Collections.Generic;
using UnityEngine;

// Copies transforms and visible mesh renderers only. Never instantiates gameplay scripts, audio, VFX or colliders.
public sealed class CombatVisualSnapshot
{
    public GameObject Root { get; private set; }
    public readonly Dictionary<Transform, Transform> Transforms = new Dictionary<Transform, Transform>();
    public Bounds Bounds { get; private set; }
    public int RendererCount { get; private set; }
    public static CombatVisualSnapshot Create(Transform source, string name, int layer, bool includeInactiveActor = false)
    {
        var snapshot = new CombatVisualSnapshot();
        snapshot.Root = new GameObject(name); snapshot.Root.layer = layer;
        snapshot.Root.transform.SetPositionAndRotation(source.position, source.rotation);
        snapshot.Root.transform.localScale = source.lossyScale;
        snapshot.Transforms[source] = snapshot.Root.transform;
        foreach (var renderer in source.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled || !Visible(renderer.transform, source, includeInactiveActor)) continue;
            if (renderer is MeshRenderer mesh && mesh.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
            {
                var target = snapshot.Copy(renderer.transform, source, layer);
                target.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var copy = target.gameObject.AddComponent<MeshRenderer>(); copy.sharedMaterials = mesh.sharedMaterials;
                copy.shadowCastingMode = mesh.shadowCastingMode; copy.receiveShadows = mesh.receiveShadows;
            }
            else if (renderer is SkinnedMeshRenderer skin && skin.sharedMesh != null)
            {
                var target = snapshot.Copy(renderer.transform, source, layer);
                var copy = target.gameObject.AddComponent<SkinnedMeshRenderer>(); copy.sharedMesh = skin.sharedMesh; copy.sharedMaterials = skin.sharedMaterials;
                var bones = skin.bones; var mapped = new Transform[bones.Length];
                for (int i = 0; i < bones.Length; i++) mapped[i] = bones[i] != null ? snapshot.Copy(bones[i], source, layer) : null;
                copy.bones = mapped; copy.rootBone = skin.rootBone != null ? snapshot.Copy(skin.rootBone, source, layer) : target;
                copy.localBounds = skin.localBounds; copy.updateWhenOffscreen = true;
                copy.shadowCastingMode = skin.shadowCastingMode; copy.receiveShadows = skin.receiveShadows;
                for (int i = 0; i < skin.sharedMesh.blendShapeCount; i++) copy.SetBlendShapeWeight(i, skin.GetBlendShapeWeight(i));
            }
            else continue;
            snapshot.Bounds = snapshot.RendererCount++ == 0 ? renderer.bounds : Encapsulate(snapshot.Bounds, renderer.bounds);
        }
        return snapshot;
    }
    static Bounds Encapsulate(Bounds a, Bounds b) { a.Encapsulate(b); return a; }
    static bool Visible(Transform node, Transform root, bool includeInactive)
    {
        if (!includeInactive) return node.gameObject.activeInHierarchy;
        for (var p = node; p != null && p != root; p = p.parent) if (!p.gameObject.activeSelf) return false;
        return true;
    }
    Transform Copy(Transform source, Transform root, int layer)
    {
        if (Transforms.TryGetValue(source, out var found)) return found;
        if (source == null || !source.IsChildOf(root)) return Root.transform;
        var node = new GameObject(source.name).transform; node.gameObject.layer = layer;
        node.SetParent(Copy(source.parent, root, layer), false);
        node.localPosition = source.localPosition; node.localRotation = source.localRotation; node.localScale = source.localScale;
        Transforms[source] = node; return node;
    }
}
