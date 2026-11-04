using UnityEngine;
using UnityEngine.Serialization;

// Placeholder look for a zone (KOTH, extract, elimination camp): an outline of its chunk's edge (a ring for a circular
// chunk), coloured by state. By default yellow while waiting, blue while holding, yellow flicker during the
// grace period, red flash on failure, green once complete; hidden while the zone is closed. Each zone can
// set its own colours. Only reads HoldZone, so real VFX can replace it later.
[DisallowMultipleComponent]
public sealed class HoldZoneVisual : MonoBehaviour
{
    [Header("State colours")]
    [SerializeField] Color waitingColor = new Color(1f, 0.85f, 0.2f);
    [FormerlySerializedAs("survivingColor")]
    [SerializeField] Color holdingColor = new Color(0.25f, 0.55f, 1f);
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

    IZoneDisplay zone;
    LineRenderer outline;
    bool drawn;

    void Awake()
    {
        zone = GetComponent<IZoneDisplay>();
        if (zone == null) { Debug.LogWarning("HoldZoneVisual needs a zone (KOTH_Zone, ExtractZone, EliminationZone) on the same object.", this); enabled = false; return; }
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
        outline.enabled = zone.IsOpen;
        if (!zone.IsOpen) return;
        if (!drawn) drawn = DrawOutline();
        bool blinkOn = Mathf.Repeat(Time.time * flashRate, 1f) < 0.5f;
        Color color = zone.State switch
        {
            HoldZoneState.Holding => holdingColor,
            HoldZoneState.Warning => blinkOn ? waitingColor : offColor,
            HoldZoneState.Failed => blinkOn ? failedColor : offColor,
            HoldZoneState.Complete => completeColor,
            _ => waitingColor,
        };
        outline.startColor = outline.endColor = color;
    }

    // The chunk's edge from ChunkDefinition, in the chunk's orientation: a ring for a circle, else its box.
    bool DrawOutline()
    {
        var chunk = zone.Chunk;
        if (chunk == null) return false;
        Transform root = chunk.transform;
        if (chunk.IsCircle)
        {
            const int segments = 72;
            float r = Mathf.Max(0.5f, chunk.RadiusMetres - inset);
            outline.positionCount = segments;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                outline.SetPosition(i, root.TransformPoint(new Vector3(Mathf.Cos(a) * r, heightOffset, Mathf.Sin(a) * r)));
            }
            return true;
        }
        Vector2 half = chunk.SizeMetres * 0.5f - new Vector2(inset, inset);
        outline.positionCount = 4;
        outline.SetPosition(0, root.TransformPoint(new Vector3(-half.x, heightOffset, -half.y)));
        outline.SetPosition(1, root.TransformPoint(new Vector3(-half.x, heightOffset, half.y)));
        outline.SetPosition(2, root.TransformPoint(new Vector3(half.x, heightOffset, half.y)));
        outline.SetPosition(3, root.TransformPoint(new Vector3(half.x, heightOffset, -half.y)));
        return true;
    }
}
