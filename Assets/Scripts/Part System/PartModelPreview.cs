using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// Mesh-only snapshot: opening the menu never instantiates gameplay scripts or colliders.
[DisallowMultipleComponent]
[RequireComponent(typeof(RawImage))]
public sealed class PartModelPreview : MonoBehaviour
{
    [SerializeField, Range(64, 1024)] int resolution = 256;
    [Tooltip("Camera angle around the part's local axes.")]
    [SerializeField] Vector2 viewAngles = new Vector2(15f, 135f);
    [SerializeField, Range(1.05f, 2f)] float framingPadding = 1.2f;
    [SerializeField] Color background = new Color(0.055f, 0.07f, 0.09f, 1f);
    const int PreviewLayer = 31;
    static int nextStage;
    readonly List<Mesh> bakedMeshes = new List<Mesh>();
    RawImage image;
    GameObject stage;
    Camera previewCamera;
    RenderTexture texture;
    public Part Source { get; private set; }
    public int MeshCount { get; private set; }

    public void Show(Part part)
    {
        Clear();
        image = GetComponent<RawImage>();
        image.raycastTarget = false;
        image.enabled = false;
        if (part == null || !isActiveAndEnabled) return;
        Source = part;
        stage = new GameObject("Part Preview Stage") { hideFlags = HideFlags.HideAndDontSave };
        // Far away from gameplay, with no physics bodies. Spacing isolates each
        // menu card's geometry and local lights from the other preview cameras.
        stage.transform.position = new Vector3(10000f + (nextStage++ % 100) * 30f, -10000f, 10000f);
        var model = new GameObject("Model").transform;
        model.SetParent(stage.transform, false);
        Bounds bounds = default;
        foreach (var source in part.GetComponentsInChildren<Renderer>(true))
        {
            if (!source.enabled || !source.gameObject.activeInHierarchy || source.GetComponentInParent<Part>() != part) continue;
            Mesh mesh = null;
            if (source is SkinnedMeshRenderer skin && skin.sharedMesh != null)
            {
                mesh = new Mesh();
                skin.BakeMesh(mesh);
                bakedMeshes.Add(mesh);
            }
            else if (source is MeshRenderer)
                mesh = source.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) continue;
            var visual = new GameObject(source.name, typeof(MeshFilter), typeof(MeshRenderer));
            visual.layer = PreviewLayer;
            visual.transform.SetParent(model, false);
            Matrix4x4 pose = part.transform.worldToLocalMatrix * source.transform.localToWorldMatrix;
            visual.transform.localPosition = pose.GetColumn(3);
            visual.transform.localRotation = pose.rotation;
            visual.transform.localScale = pose.lossyScale;
            visual.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = visual.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = source.sharedMaterials;
            var properties = new MaterialPropertyBlock();
            source.GetPropertyBlock(properties);
            renderer.SetPropertyBlock(properties);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            if (MeshCount++ == 0) bounds = renderer.bounds;
            else bounds.Encapsulate(renderer.bounds);
        }
        if (MeshCount == 0) { Clear(); return; }
        float radius = Mathf.Max(0.001f, bounds.extents.magnitude);
        model.localScale = Vector3.one / radius;
        model.localPosition = -(bounds.center - stage.transform.position) / radius;
        var cameraObject = new GameObject("Preview Camera", typeof(Camera));
        cameraObject.transform.SetParent(stage.transform, false);
        previewCamera = cameraObject.GetComponent<Camera>();
        previewCamera.enabled = false;
        previewCamera.cullingMask = 1 << PreviewLayer;
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = background;
        previewCamera.orthographic = true;
        previewCamera.orthographicSize = framingPadding;
        previewCamera.nearClipPlane = 0.1f;
        previewCamera.farClipPlane = 10f;
        previewCamera.allowHDR = false;
        previewCamera.allowMSAA = false;
        previewCamera.useOcclusionCulling = false;
        Quaternion angle = Quaternion.Euler(viewAngles.x, viewAngles.y, 0f);
        cameraObject.transform.localPosition = angle * new Vector3(0f, 0f, -4f);
        cameraObject.transform.localRotation = angle;
        var cameraData = previewCamera.GetUniversalAdditionalCameraData();
        cameraData.renderPostProcessing = false;
        cameraData.renderShadows = false;
        cameraData.volumeLayerMask = 0;
        AddLight("Key Light", new Vector3(-2f, 3f, -3f), 8f);
        AddLight("Fill Light", new Vector3(3f, 1f, 2f), 5f);
        texture = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGB32)
        { name = "Part Model Preview", hideFlags = HideFlags.HideAndDontSave };
        texture.Create();
        image.texture = texture;
        image.color = Color.white;
        image.enabled = true;
        // Start/update happens outside the rendering loop; URP needs a render request.
        if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
        {
            previewCamera.targetTexture = texture;
            if (GraphicsSettings.currentRenderPipeline == null) previewCamera.Render();
            else RenderPipeline.SubmitRenderRequest(previewCamera,
                new UniversalRenderPipeline.SingleCameraRequest { destination = texture });
            previewCamera.targetTexture = null;
        }
        // The finished image persists, while geometry/lights stop participating in rendering.
        stage.SetActive(false);
    }

    void AddLight(string lightName, Vector3 position, float intensity)
    {
        var lightObject = new GameObject(lightName, typeof(Light));
        lightObject.transform.SetParent(stage.transform, false);
        lightObject.transform.localPosition = position;
        var light = lightObject.GetComponent<Light>();
        light.type = LightType.Point;
        light.range = 10f;
        light.intensity = intensity;
        light.cullingMask = 1 << PreviewLayer;
        light.shadows = LightShadows.None;
    }

    void OnDisable() => Clear();
    void OnDestroy() => Clear();
    void Clear()
    {
        if (image != null) { image.texture = null; image.enabled = false; }
        if (stage != null) { stage.SetActive(false); Destroy(stage); }
        foreach (var mesh in bakedMeshes) if (mesh != null) Destroy(mesh);
        bakedMeshes.Clear();
        if (texture != null) { texture.Release(); Destroy(texture); }
        stage = null; previewCamera = null; texture = null;
        Source = null; MeshCount = 0;
    }
}
