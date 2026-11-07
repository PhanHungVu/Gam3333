using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Reusable world-object presentation. Part owns when a loose part shows this effect.
[DisallowMultipleComponent]
public sealed class PickupHighlight : MonoBehaviour
{
    public const string ResourceName = "PickupHighlight";
    [Tooltip("Object to outline. Left empty, uses this prefab's parent.")]
    [SerializeField] Transform target;
    [SerializeField] Material outlineMaterial;
    [SerializeField] MeshRenderer[] sparkles;
    [Header("Outline")]
    [SerializeField, Range(.5f, 4)] float outlineWidth = 1.5f;
    [SerializeField] Color outlineColor = new Color(1, 1, 1, .32f);
    [Header("White star glint")]
    [SerializeField, Min(.05f)] float sparkleSize = .55f;
    [SerializeField, Min(0)] float heightAboveObject = .12f;
    [SerializeField, Min(.25f)] float pulsePeriod = 2.2f;
    [SerializeField, Range(0, 4)] float brightness = 1.8f;
    [Header("Visibility")]
    [SerializeField, Min(1)] float maximumDistance = 65;
    [SerializeField, Range(0, 32)] float minimumSparklePixels = 18;
    [Tooltip("Optional camera for distance fading. Billboards face each rendering camera automatically.")]
    [SerializeField] Camera viewCamera;

    sealed class Outline
    {
        public Renderer source, copy;
    }
    readonly List<Outline> outlines = new List<Outline>();
    MaterialPropertyBlock properties;
    static readonly int ColorId = Shader.PropertyToID("_Color"), WidthId = Shader.PropertyToID("_Width");
    static readonly int SizeId = Shader.PropertyToID("_Size"), BrightnessId = Shader.PropertyToID("_Brightness");
    static PickupHighlight template;
    Transform builtTarget;
    bool built;
    float phase;
    public Transform Target => target;
    public int OutlineCount => outlines.Count;
    public bool IsVisible { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCache() => template = null;

    public static PickupHighlight Attach(Transform objectToHighlight)
    {
        if (objectToHighlight == null) return null;
        foreach (var existing in objectToHighlight.GetComponentsInChildren<PickupHighlight>(true))
            if (existing.transform.parent == objectToHighlight && (existing.target == null || existing.target == objectToHighlight))
            { existing.Bind(objectToHighlight); return existing; }
        if (template == null) template = Resources.Load<PickupHighlight>(ResourceName);
        if (template == null) return null;
        var effect = Instantiate(template, objectToHighlight, false);
        effect.name = "Pickup Highlight";
        effect.Bind(objectToHighlight);
        return effect;
    }

    public void Bind(Transform objectToHighlight)
    {
        target = objectToHighlight;
        if (Application.isPlaying && (!built || builtTarget != target)) RebuildOutline();
    }

    void OnEnable()
    {
        if (!Application.isPlaying) return;
        if (properties == null) properties = new MaterialPropertyBlock();
        // Keep the seed small before converting to float, so large entity IDs still give different twinkle phases.
        phase = (GetEntityId().GetHashCode() & int.MaxValue) % 1021 / 1021f;
        if (target == null) target = transform.parent;
        if (!built || builtTarget != target) RebuildOutline();
    }

    [ContextMenu("Rebuild Outline (Play Mode)")]
    public void RebuildOutline()
    {
        if (!Application.isPlaying) return;
        ClearOutlines();
        built = true; builtTarget = target;
        if (target == null || outlineMaterial == null) return;
        foreach (var source in target.GetComponentsInChildren<Renderer>(true))
        {
            if (!source.enabled || source.shadowCastingMode == ShadowCastingMode.ShadowsOnly
                || source.GetComponentInParent<PickupHighlight>(true) != null
                || source.sharedMaterial == outlineMaterial) continue;
            Mesh mesh = null;
            if (source is SkinnedMeshRenderer skin) mesh = skin.sharedMesh;
            else if (source is MeshRenderer && source.TryGetComponent<MeshFilter>(out var filter)) mesh = filter.sharedMesh;
            if (mesh == null || mesh.subMeshCount == 0) continue;
            var proxy = new GameObject("Pickup Outline (visual only)") { layer = source.gameObject.layer, hideFlags = HideFlags.DontSave };
            proxy.transform.SetParent(source.transform, false);
            Renderer copy;
            if (source is SkinnedMeshRenderer skinned)
            {
                var animated = proxy.AddComponent<SkinnedMeshRenderer>();
                animated.sharedMesh = mesh; animated.bones = skinned.bones; animated.rootBone = skinned.rootBone;
                animated.localBounds = skinned.localBounds; copy = animated;
            }
            else
            {
                proxy.AddComponent<MeshFilter>().sharedMesh = mesh;
                copy = proxy.AddComponent<MeshRenderer>();
            }
            var materials = new Material[mesh.subMeshCount];
            for (int i = 0; i < materials.Length; i++) materials[i] = outlineMaterial;
            copy.sharedMaterials = materials;
            copy.shadowCastingMode = ShadowCastingMode.Off; copy.receiveShadows = false;
            copy.lightProbeUsage = LightProbeUsage.Off; copy.reflectionProbeUsage = ReflectionProbeUsage.Off;
            copy.renderingLayerMask = source.renderingLayerMask;
            copy.enabled = false;
            outlines.Add(new Outline { source = source, copy = copy });
        }
    }

    void LateUpdate()
    {
        if (builtTarget != target) RebuildOutline();
        if (viewCamera == null || !viewCamera.isActiveAndEnabled) viewCamera = Camera.main;
        if (viewCamera == null || target != null && !target.gameObject.activeInHierarchy) { Hide(); return; }

        var bounds = new Bounds(target != null ? target.position : transform.position, Vector3.zero);
        bool haveBounds = false;
        foreach (var outline in outlines)
            if (outline.source != null && outline.source.enabled && outline.source.gameObject.activeInHierarchy)
            {
                if (haveBounds) bounds.Encapsulate(outline.source.bounds);
                else { bounds = outline.source.bounds; haveBounds = true; }
            }
        // A standalone prefab can still be used as a star marker without an outline target.
        if (target != null && !haveBounds) { Hide(); return; }
        float distance = Vector3.Distance(viewCamera.transform.position, bounds.center);
        float fade = 1 - Mathf.InverseLerp(maximumDistance * .75f, maximumDistance, distance);
        if (fade <= 0) { Hide(); return; }
        IsVisible = true;
        var tint = outlineColor; tint.a *= fade;
        properties.Clear(); properties.SetColor(ColorId, tint); properties.SetFloat(WidthId, outlineWidth);
        foreach (var outline in outlines)
        {
            if (outline.copy == null) continue;
            outline.copy.enabled = outline.source != null && outline.source.enabled && outline.source.gameObject.activeInHierarchy;
            outline.copy.gameObject.SetActive(outline.copy.enabled);
            outline.copy.SetPropertyBlock(properties);
            if (outline.source is SkinnedMeshRenderer skin && outline.copy is SkinnedMeshRenderer copy)
                for (int i = 0; i < skin.sharedMesh.blendShapeCount; i++) copy.SetBlendShapeWeight(i, skin.GetBlendShapeWeight(i));
        }

        float unitsPerPixel = viewCamera.orthographic ? viewCamera.orthographicSize * 2 / Mathf.Max(1, viewCamera.pixelHeight)
            : 2 * distance * Mathf.Tan(viewCamera.fieldOfView * Mathf.Deg2Rad * .5f) / Mathf.Max(1, viewCamera.pixelHeight);
        float size = Mathf.Clamp(unitsPerPixel * minimumSparklePixels, sparkleSize, sparkleSize * 2.2f);
        Vector3 center = new Vector3(bounds.center.x, bounds.max.y + heightAboveObject, bounds.center.z);
        if (sparkles == null) return;
        for (int i = 0; i < sparkles.Length; i++)
        {
            var sparkle = sparkles[i]; if (sparkle == null) continue;
            float wave = .5f + .5f * Mathf.Sin((Time.time / Mathf.Max(.25f, pulsePeriod) + phase + i * .35f) * Mathf.PI * 2);
            float pulse = Mathf.Pow(wave, 4);
            sparkle.enabled = true;
            sparkle.gameObject.SetActive(true);
            sparkle.transform.position = center + (i == 0 ? Vector3.zero : viewCamera.transform.right * size * .32f + Vector3.up * size * .25f);
            sparkle.bounds = new Bounds(sparkle.transform.position, Vector3.one * size);
            properties.Clear();
            properties.SetColor(ColorId, new Color(1, 1, 1, fade * (.16f + .84f * pulse)));
            properties.SetFloat(SizeId, size * (i == 0 ? 1 : .45f) * (.7f + .3f * pulse));
            properties.SetFloat(BrightnessId, brightness);
            sparkle.SetPropertyBlock(properties);
        }
    }

    void Hide()
    {
        IsVisible = false;
        foreach (var outline in outlines)
            if (outline.copy != null) { outline.copy.enabled = false; outline.copy.gameObject.SetActive(false); }
        if (sparkles != null) foreach (var sparkle in sparkles)
            if (sparkle != null) { sparkle.enabled = false; sparkle.gameObject.SetActive(false); }
    }
    void ClearOutlines()
    {
        foreach (var outline in outlines)
            if (outline.copy != null) { outline.copy.enabled = false; Destroy(outline.copy.gameObject); }
        outlines.Clear();
    }
    void OnDisable() => Hide();
    void OnDestroy() => ClearOutlines();
}
