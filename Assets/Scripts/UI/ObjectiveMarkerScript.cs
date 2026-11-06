using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One objective marker on the HUD. ObjectiveMarkerManager hands it an objective; every frame the
// marker projects that objective's world position with WorldToScreenPoint and moves its own
// RectTransform onto it. Out of view, or behind the camera, it halves and pins itself to the screen
// edge nearest the objective; while the player stands inside the objective's own zone it fades out.
[DisallowMultipleComponent]
[DefaultExecutionOrder(230)]
public sealed class ObjectiveMarkerScript : MonoBehaviour
{
    [Header("Authored references")]
    [Tooltip("Rect moved onto the objective. Defaults to this object.")]
    [SerializeField] RectTransform rectTransform;
    [Tooltip("Fades the whole marker. Added at runtime when empty.")]
    [SerializeField] CanvasGroup group;
    [Tooltip("Marker art. Defaults to the Image on this object.")]
    [SerializeField] Image icon;
    [Tooltip("Distance read-out. Defaults to the first TMP text underneath.")]
    [SerializeField] TMP_Text distanceLabel;
    [Header("Placement")]
    [Tooltip("Screen border kept free, as a fraction of the screen.")]
    [SerializeField, Range(0f, 0.4f)] float edgeMargin = 0.08f;
    [Tooltip("Scale used while the objective is off screen. 0.5 is half size.")]
    [SerializeField, Range(0.1f, 1f)] float offScreenScale = 0.5f;

    Transform objective;
    Transform player;
    Camera worldCamera;
    Canvas canvas;
    ChunkDefinition zone;
    bool bound;

    public Transform Objective => objective;
    public RectTransform Rect => rectTransform;
    public bool IsBound => bound && objective != null;

    void Awake()
    {
        if (rectTransform == null) rectTransform = (RectTransform)transform;
        if (icon == null) icon = GetComponent<Image>();
        if (distanceLabel == null) distanceLabel = GetComponentInChildren<TMP_Text>(true);
        if (group == null)
        {
            group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        }
        group.blocksRaycasts = false;
        group.interactable = false;
    }

    // What this marker points at, and the camera and canvas to project through.
    public void Assign(Transform target, ChunkDefinition objectiveZone, Camera camera, Canvas owner,
        Transform playerTransform, Sprite sprite)
    {
        objective = target;
        zone = objectiveZone;
        worldCamera = camera;
        canvas = owner;
        player = playerTransform;
        bound = target != null;
        if (icon != null && sprite != null) icon.sprite = sprite;
        SetAlpha(1f);
        gameObject.SetActive(true);
        Project();   // place it in the frame it is made, so it never pops in at the origin
    }

    public void SetAlpha(float alpha)
    {
        if (group != null) group.alpha = alpha;
    }

    public void Release()
    {
        objective = null;
        bound = false;
    }

    void LateUpdate() => Project();

    // WorldToScreenPoint is the whole job: the objective's screen point is where the marker belongs.
    void Project()
    {
        if (!bound || objective == null || worldCamera == null || rectTransform == null) return;

        bool visible = objective.gameObject.activeInHierarchy;
        if (group != null && Mathf.Approximately(group.alpha, 0f) == visible) SetAlpha(visible ? 1f : 0f);
        if (!visible) return;

        Vector3 world = objective.position;
        Vector3 screen = worldCamera.WorldToScreenPoint(world);   // z <= 0 means behind the camera
        float width = Mathf.Max(1f, Screen.width);
        float height = Mathf.Max(1f, Screen.height);
        Vector2 viewport = new Vector2(screen.x / width, screen.y / height);
        bool onScreen = screen.z > 0f
            && viewport.x >= edgeMargin && viewport.x <= 1f - edgeMargin
            && viewport.y >= edgeMargin && viewport.y <= 1f - edgeMargin;

        Vector2 point;
        if (onScreen)
        {
            point = screen;
            transform.localScale = Vector3.one;
        }
        else
        {
            // Behind the camera mirrors viewport x/y, so flip the direction back before using it.
            Vector2 direction = new Vector2(viewport.x - 0.5f, viewport.y - 0.5f);
            if (screen.z <= 0f) direction = -direction;
            if (direction.sqrMagnitude < 1e-6f) direction = screen.z <= 0f ? Vector2.down : Vector2.up;

            // Push the direction out until it meets the screen border that is nearest to it.
            float half = Mathf.Max(0.01f, 0.5f - edgeMargin);
            float reach = Mathf.Max(Mathf.Abs(direction.x) / half, Mathf.Abs(direction.y) / half);
            Vector2 edge = new Vector2(0.5f, 0.5f) + direction / Mathf.Max(reach, 1e-4f);
            point = new Vector2(edge.x * width, edge.y * height);
            transform.localScale = Vector3.one * offScreenScale;
        }

        Camera canvasCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera : null;
        var parentRect = rectTransform.parent as RectTransform;
        if (parentRect != null
            && RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, point, canvasCamera, out Vector2 local))
            rectTransform.localPosition = new Vector3(local.x, local.y, 0f);
        else
            rectTransform.position = new Vector3(point.x, point.y, 0f);

        if (distanceLabel != null)
        {
            Transform from = Player != null ? Player : worldCamera.transform;
            distanceLabel.text = Mathf.Max(0, Mathf.RoundToInt(Vector3.Distance(from.position, world))) + "m";
        }

        // Standing inside the objective's zone there is nothing left to point at, so it fades out.
        if (zone != null && Player != null) SetAlpha(zone.Contains(Player.position) ? 0f : 1f);
    }

    Transform Player
    {
        get
        {
            if (player == null)
            {
                var rig = CombatHudBinding.FindPlayer();
                if (rig != null) player = rig.transform;
            }
            return player;
        }
    }
}
