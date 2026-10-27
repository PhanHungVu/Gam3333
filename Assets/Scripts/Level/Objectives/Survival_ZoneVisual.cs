using UnityEngine;

// Placeholder look for the Survival chunk: an outline of the chunk's footprint, coloured by state.
// Yellow while waiting, blue while surviving, yellow flicker during the grace period, red flash on
// failure, green once complete. Only reads Survival_Zone, so real VFX can replace it later.
[DisallowMultipleComponent]
[RequireComponent(typeof(Survival_Zone))]
public sealed class Survival_ZoneVisual : MonoBehaviour
{
    [Header("State colours")]
    [SerializeField] Color waitingColor = new Color(1f, 0.85f, 0.2f);
    [SerializeField] Color survivingColor = new Color(0.25f, 0.55f, 1f);
    [SerializeField] Color failedColor = new Color(1f, 0.25f, 0.2f);
    [SerializeField] Color completeColor = new Color(0.3f, 0.95f, 0.45f);
    [Tooltip("Colour the outline blinks to during the warning and failed flashes.")]
    [SerializeField] Color offColor = new Color(0.2f, 0.2f, 0.2f, 0.25f);
    [Tooltip("Blinks per second for the warning flicker and the failed flash.")]
    [SerializeField, Min(0.1f)] float flashRate = 4f;

    [Header("Outline")]
    [Tooltip("Optional. A material that uses vertex colour; left empty, Sprites/Default is used.")]
    [SerializeField] Material lineMaterial;
    [SerializeField, Min(0.01f)] float lineWidth = 0.4f;
    [Tooltip("Lift above the chunk's root so the outline sits on top of the ground pad.")]
    [SerializeField] float heightOffset = 0.15f;
    [Tooltip("Pull the outline this far in from the footprint edge, in metres.")]
    [SerializeField, Min(0f)] float inset = 0.5f;

    Survival_Zone zone;
    LineRenderer outline;
    bool drawn;

    void Awake()
    {
        zone = GetComponent<Survival_Zone>();
        if (lineMaterial == null) lineMaterial = new Material(Shader.Find("Sprites/Default"));
        var child = new GameObject("Chunk Outline");
        child.transform.SetParent(transform, false);
        outline = child.AddComponent<LineRenderer>();
        outline.useWorldSpace = true;
        outline.loop = true;
        outline.widthMultiplier = lineWidth;
        outline.sharedMaterial = lineMaterial;
        outline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        outline.receiveShadows = false;
        outline.positionCount = 0;
    }

    void LateUpdate()
    {
        if (!drawn) drawn = DrawOutline();
        bool blinkOn = Mathf.Repeat(Time.time * flashRate, 1f) < 0.5f;
        Color color = zone.State switch
        {
            Survival_ZoneState.Surviving => survivingColor,
            Survival_ZoneState.Warning => blinkOn ? waitingColor : offColor,
            Survival_ZoneState.Failed => blinkOn ? failedColor : offColor,
            Survival_ZoneState.Complete => completeColor,
            _ => waitingColor,
        };
        outline.startColor = outline.endColor = color;
    }

    // Corners of the chunk footprint from ChunkDefinition, in the chunk's orientation.
    bool DrawOutline()
    {
        var chunk = zone.Chunk;
        if (chunk == null) return false;
        Vector2 half = chunk.SizeMetres * 0.5f - new Vector2(inset, inset);
        Transform root = chunk.transform;
        outline.positionCount = 4;
        outline.SetPosition(0, root.TransformPoint(new Vector3(-half.x, heightOffset, -half.y)));
        outline.SetPosition(1, root.TransformPoint(new Vector3(-half.x, heightOffset, half.y)));
        outline.SetPosition(2, root.TransformPoint(new Vector3(half.x, heightOffset, half.y)));
        outline.SetPosition(3, root.TransformPoint(new Vector3(half.x, heightOffset, -half.y)));
        return true;
    }
}
