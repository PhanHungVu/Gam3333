using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

// A render-only view of existing geometry. Never instantiates Part/player scripts,
// colliders, sockets, animations, or any other behaviour from a model prefab.
[DisallowMultipleComponent, RequireComponent(typeof(RawImage))]
public sealed class StarterTorsoPreview : MonoBehaviour, IDragHandler, IBeginDragHandler
{
    [SerializeField] RawImage image;
    [SerializeField] TMP_Text fallback;
    [SerializeField, Range(256, 1024)] int maximumResolution = 768;
    [SerializeField, Range(0, 31)] int previewLayer = 31;
    [SerializeField, Range(1.05f, 1.6f)] float framingPadding = 1.2f;
    [SerializeField] Color backgroundColor = new Color(0.21f, 0.25f, 0.22f);
    [SerializeField, Range(0.1f, 1f)] float dragSensitivity = 0.4f;
    readonly List<Mesh> bakedMeshes = new List<Mesh>();
    readonly List<Renderer> renderers = new List<Renderer>();
    PartData torso;
    GameObject sourceModel, stage, modelRoot;
    Camera previewCamera;
    RenderTexture texture;
    Vector3 authoredRotation;
    float yaw;
    bool dirty;
    public PartData Torso => torso;
    public bool HasPreview => stage != null && renderers.Count > 0 && texture != null;
    public RenderTexture Texture => texture;
    public float Rotation => yaw;
    public int RenderedMeshCount => renderers.Count;

    public void Show(PartData data, StarterTorsoVisual visual)
    {
        var nextModel = visual != null ? visual.model : null;
        var nextRotation = visual != null ? visual.rotation : Vector3.zero;
        bool changed = sourceModel != nextModel || authoredRotation != nextRotation;
        torso = data; sourceModel = nextModel; authoredRotation = nextRotation;
        if (changed) { yaw = 0f; Release(); }
        if (isActiveAndEnabled && stage == null) Build();
    }
    void OnEnable() { if (image == null) image = GetComponent<RawImage>(); Build(); }
    void OnDisable() => Release();
    void OnDestroy() => Release();
    void OnRectTransformDimensionsChange() { dirty = true; }
    void LateUpdate() { if (dirty && stage != null) RenderNow(); }
    public void OnBeginDrag(PointerEventData data) { if (HasPreview && data.button == PointerEventData.InputButton.Left) data.useDragThreshold = false; }
    public void OnDrag(PointerEventData data)
    {
        if (!HasPreview || data.button != PointerEventData.InputButton.Left || !CanInteract()) return;
        yaw = Mathf.Repeat(yaw + data.delta.x * dragSensitivity, 360f); dirty = true;
    }
    bool CanInteract()
    {
        foreach (var group in GetComponentsInParent<CanvasGroup>())
        {
            if (!group.isActiveAndEnabled) continue;
            if (!group.interactable) return false;
            if (group.ignoreParentGroups) break;
        }
        return true;
    }
    void Build()
    {
        if (image == null) image = GetComponent<RawImage>();
        if (stage != null) return;
        if (torso == null || sourceModel == null) { SetFallback(); return; }
        stage = new GameObject("Starter Torso Preview Stage") { hideFlags = HideFlags.HideAndDontSave, layer = previewLayer };
        // Outside the UI scene camera's clipping range; only the preview camera sees this layer here.
        stage.transform.position = new Vector3(20000f, -20000f, 20000f);
        modelRoot = new GameObject("Model"); modelRoot.transform.SetParent(stage.transform, false);
        CopyVisuals(sourceModel.transform, modelRoot.transform);
        if (renderers.Count == 0) { Release(); SetFallback(); return; }
        previewCamera = new GameObject("Preview Camera", typeof(Camera)).GetComponent<Camera>();
        previewCamera.transform.SetParent(stage.transform, false); previewCamera.enabled = false;
        previewCamera.cullingMask = 1 << previewLayer; previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = backgroundColor; previewCamera.orthographic = true;
        previewCamera.nearClipPlane = 0.01f; previewCamera.farClipPlane = 100f;
        previewCamera.allowHDR = false; previewCamera.allowMSAA = false;
        Light("Key", new Vector3(35, -30, 0), 1.1f);
        Light("Fill", new Vector3(20, 145, 0), 0.65f);
        fallback.gameObject.SetActive(false); image.color = Color.white; dirty = true;
        RenderNow();
    }
    void CopyVisuals(Transform source, Transform parent)
    {
        var copy = new GameObject(source.name) { layer = previewLayer };
        copy.transform.SetParent(parent, false);
        copy.transform.localPosition = source.localPosition; copy.transform.localRotation = source.localRotation; copy.transform.localScale = source.localScale;
        Mesh mesh = null; Material[] materials = null;
        var filter = source.GetComponent<MeshFilter>(); var renderer = source.GetComponent<MeshRenderer>();
        if (filter != null && renderer != null && renderer.enabled) { mesh = filter.sharedMesh; materials = renderer.sharedMaterials; }
        var skinned = source.GetComponent<SkinnedMeshRenderer>();
        if (skinned != null && skinned.enabled && skinned.sharedMesh != null)
        {
            mesh = new Mesh(); skinned.BakeMesh(mesh); bakedMeshes.Add(mesh); materials = skinned.sharedMaterials;
        }
        if (mesh != null)
        {
            copy.AddComponent<MeshFilter>().sharedMesh = mesh;
            var view = copy.AddComponent<MeshRenderer>(); view.sharedMaterials = materials;
            view.shadowCastingMode = ShadowCastingMode.Off; view.receiveShadows = false; renderers.Add(view);
        }
        foreach (Transform child in source) if (child.gameObject.activeSelf) CopyVisuals(child, copy.transform);
    }
    void Light(string name, Vector3 rotation, float intensity)
    {
        var light = new GameObject(name, typeof(Light)).GetComponent<Light>();
        light.transform.SetParent(stage.transform, false); light.transform.localRotation = Quaternion.Euler(rotation);
        light.type = LightType.Directional; light.intensity = intensity; light.cullingMask = 1 << previewLayer; light.shadows = LightShadows.None;
    }
    public void RenderNow()
    {
        if (stage == null || renderers.Count == 0 || !isActiveAndEnabled) return;
        var rect = ((RectTransform)transform).rect;
        if (rect.width <= 0 || rect.height <= 0) return;
        float aspect = rect.width / rect.height;
        int width = aspect >= 1f ? maximumResolution : Mathf.Max(128, Mathf.RoundToInt(maximumResolution * aspect));
        int height = aspect >= 1f ? Mathf.Max(128, Mathf.RoundToInt(maximumResolution / aspect)) : maximumResolution;
        if (texture == null || texture.width != width || texture.height != height)
        {
            ReleaseTexture();
            texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { name = "Starter Torso Preview", hideFlags = HideFlags.HideAndDontSave };
            texture.Create(); image.texture = texture; previewCamera.targetTexture = texture;
        }
        modelRoot.transform.localRotation = Quaternion.Euler(authoredRotation + Vector3.up * yaw);
        var cameraRotation = Quaternion.Euler(12f, 25f, 0f);
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        // Fit all eight bounds corners in camera space, including wide replacement torsos.
        var inverse = Quaternion.Inverse(cameraRotation);
        Vector3 extents = Vector3.zero;
        for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = inverse * Vector3.Scale(bounds.extents, new Vector3(x, y, z));
                    extents = Vector3.Max(extents, new Vector3(Mathf.Abs(corner.x), Mathf.Abs(corner.y), Mathf.Abs(corner.z)));
                }
        previewCamera.transform.rotation = cameraRotation;
        previewCamera.transform.position = bounds.center - cameraRotation * Vector3.forward * Mathf.Max(5f, extents.z + 2f);
        previewCamera.aspect = aspect;
        previewCamera.orthographicSize = Mathf.Max(0.01f, Mathf.Max(extents.y, extents.x / aspect) * framingPadding);
        previewCamera.farClipPlane = Mathf.Max(100f, extents.z * 4f + 10f);
        if (GraphicsSettings.currentRenderPipeline != null)
            RenderPipeline.SubmitRenderRequest(previewCamera, new RenderPipeline.StandardRequest { destination = texture });
        else previewCamera.Render();
        dirty = false;
    }
    void SetFallback()
    {
        if (image == null) return;
        image.texture = null; image.color = Color.clear;
        fallback.text = torso == null ? "No starting torso available" : "Model preview unavailable";
        fallback.gameObject.SetActive(true);
    }
    void ReleaseTexture()
    {
        if (previewCamera != null) previewCamera.targetTexture = null;
        if (image != null) image.texture = null;
        if (texture != null) { texture.Release(); Destroy(texture); texture = null; }
    }
    void Release()
    {
        ReleaseTexture();
        if (stage != null) { stage.SetActive(false); Destroy(stage); stage = null; }
        previewCamera = null; modelRoot = null; renderers.Clear();
        foreach (var mesh in bakedMeshes) if (mesh != null) Destroy(mesh);
        bakedMeshes.Clear(); dirty = false;
    }
}
