using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Keep the existing UI Button and navigation, with a real slab in its screen area.
[DisallowMultipleComponent, RequireComponent(typeof(Button))]
public sealed class PreMissionSlabButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    [SerializeField] GameObject modelPrefab;
    [SerializeField] Vector2 modelFootprint = new Vector2(3f, 1.1f);
    Camera view;
    GameObject model;
    Button button;
    RectTransform rect;
    CanvasGroup[] groups;
    MeshRenderer rock;
    TMP_Text text;
    Color baseColor;
    MaterialPropertyBlock properties;
    Material depthMaterial;
    readonly Vector3[] corners = new Vector3[4];
    bool hovered, selected;
    public void BindCamera(Camera camera)
    {
        view = camera;
        if (view == null || modelPrefab == null) return;
        button = GetComponent<Button>(); rect = (RectTransform)transform;
        groups = GetComponentsInParent<CanvasGroup>(true);
        if (model == null)
        {
            model = Instantiate(modelPrefab, view.transform); model.name = "Back Slab";
            rock = model.GetComponentInChildren<MeshRenderer>(); text = model.GetComponentInChildren<TMP_Text>();
            if(depthMaterial != null) Destroy(depthMaterial);
            depthMaterial = new Material(rock.sharedMaterial) { name = "Back Slab - UI Depth" };
            depthMaterial.SetFloat("_Surface", 1);
            depthMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); depthMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            depthMaterial.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); depthMaterial.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            depthMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON"); depthMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            depthMaterial.SetOverrideTag("RenderType", "Transparent"); depthMaterial.renderQueue = (int)RenderQueue.Transparent;
            rock.sharedMaterial = depthMaterial;
            baseColor = rock.sharedMaterial.color; properties = new MaterialPropertyBlock();
        }
        button.targetGraphic.color = Color.clear;
        foreach (var label in GetComponentsInChildren<TMP_Text>(true)) label.gameObject.SetActive(false);
        Refresh();
    }
    void LateUpdate() => Refresh();
    void Refresh()
    {
        if (model == null || view == null) return;
        float alpha = 1;
        foreach (var group in groups) alpha *= group.alpha;
        bool visible = isActiveAndEnabled && view.isActiveAndEnabled && alpha > .001f;
        model.SetActive(visible); if (!visible) return;
        // This closed 3D mesh fades like UI, but must still write depth so its
        // rear triangles cannot paint dark rectangles over the visible top.
        depthMaterial.SetFloat("_ZWrite", 1);
        depthMaterial.SetShaderPassEnabled("DepthOnly", true);
        rect.GetWorldCorners(corners);
        var canvas = GetComponentInParent<Canvas>();
        var rootCanvas = canvas.rootCanvas;
        Vector3 ViewportPoint(Vector3 point)
        {
            if (rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
                return view.ScreenToViewportPoint(RectTransformUtility.WorldToScreenPoint(rootCanvas.worldCamera, point));
            // Normalize in canvas space so Game-view scaling cannot move the
            // physical model off screen when UI and camera pixel sizes differ.
            var rootRect = (RectTransform)rootCanvas.transform; var local = rootRect.InverseTransformPoint(point);
            return new Vector3(Mathf.InverseLerp(rootRect.rect.xMin, rootRect.rect.xMax, local.x),
                Mathf.InverseLerp(rootRect.rect.yMin, rootRect.rect.yMax, local.y), 0);
        }
        Vector3 bottomLeft = ViewportPoint(corners[0]), topRight = ViewportPoint(corners[2]);
        float depth = Mathf.Max(3f, view.nearClipPlane + 1f);
        Vector3 a = view.ViewportToWorldPoint(new Vector3(bottomLeft.x, bottomLeft.y, depth));
        Vector3 b = view.ViewportToWorldPoint(new Vector3(topRight.x, topRight.y, depth));
        model.transform.SetPositionAndRotation((a + b) * .5f,
            Quaternion.LookRotation(view.transform.up, -view.transform.forward) * Quaternion.Euler(12, 0, 0));
        float scale = Mathf.Min(Vector3.Dot(b - a, view.transform.right) / modelFootprint.x,
            Vector3.Dot(b - a, view.transform.up) / modelFootprint.y);
        model.transform.localScale = Vector3.one * scale;
        var color = Color.Lerp(baseColor, new Color(.83f, .76f, .54f), (hovered || selected) && button.interactable ? .3f : 0);
        color.a *= alpha;
        properties.Clear(); properties.SetColor("_BaseColor", color); properties.SetColor("_Color", color); rock.SetPropertyBlock(properties);
        text.alpha = alpha;
    }
    public void OnPointerEnter(PointerEventData _) => hovered = true;
    public void OnPointerExit(PointerEventData _) => hovered = false;
    public void OnSelect(BaseEventData _) => selected = true;
    public void OnDeselect(BaseEventData _) => selected = false;
    void OnDisable() { hovered = selected = false; if (model != null) model.SetActive(false); }
    void OnDestroy() { if (model != null) Destroy(model); if(depthMaterial != null) Destroy(depthMaterial); }
}
