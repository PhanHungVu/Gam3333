using UnityEngine;

// Placeholder look for the King of the Hill zone: a ground ring tinted by state, plus an inner arc
// that fills with capture progress. Only listens to KOTH_HoldZone and never changes it, so the real
// hill VFX can replace this later by reading the same State and Progress.
[DisallowMultipleComponent]
[RequireComponent(typeof(KOTH_HoldZone))]
public sealed class KOTH_HoldZoneVisual : MonoBehaviour
{
    [Header("State colours")]
    [SerializeField] Color idleColor = new Color(1f, 0.85f, 0.2f);      // player outside
    [SerializeField] Color capturingColor = new Color(0.25f, 0.55f, 1f); // filling
    [SerializeField] Color blockedColor = new Color(1f, 0.25f, 0.2f);    // enemy inside with the player
    [SerializeField] Color completeColor = new Color(0.3f, 0.95f, 0.45f);

    [Header("Ring")]
    [Tooltip("Optional. A material that uses vertex colour; left empty, Sprites/Default is used.")]
    [SerializeField] Material lineMaterial;
    [SerializeField, Min(0.01f)] float ringWidth = 0.3f;
    [Tooltip("Lift above the ground so the ring doesn't flicker into the floor.")]
    [SerializeField] float heightOffset = 0.05f;
    [Tooltip("Progress arc radius as a fraction of the zone radius.")]
    [SerializeField, Range(0.1f, 1f)] float progressRadiusFraction = 0.85f;

    const int Segments = 64;
    KOTH_HoldZone zone;
    LineRenderer ring;
    LineRenderer progressArc;
    int shownArcPoints = -1;

    void Awake()
    {
        zone = GetComponent<KOTH_HoldZone>();
        if (lineMaterial == null) lineMaterial = new Material(Shader.Find("Sprites/Default"));
        ring = CreateLine("Zone Ring", true);
        progressArc = CreateLine("Progress Arc", false);
    }

    void Start()
    {
        DrawArc(ring, zone.RadiusMetres, 1f);
        Apply(zone.State);
    }

    void OnEnable()
    {
        if (zone != null) zone.StateChanged += Apply;
    }

    void OnDisable()
    {
        if (zone != null) zone.StateChanged -= Apply;
    }

    void LateUpdate()
    {
        int points = Mathf.RoundToInt(zone.Progress * Segments);
        if (points == shownArcPoints) return;
        shownArcPoints = points;
        DrawArc(progressArc, zone.RadiusMetres * progressRadiusFraction, zone.Progress);
    }

    void Apply(KOTH_ZoneState state)
    {
        Color color = state switch
        {
            KOTH_ZoneState.Capturing => capturingColor,
            KOTH_ZoneState.Blocked => blockedColor,
            KOTH_ZoneState.Complete => completeColor,
            _ => idleColor,
        };
        ring.startColor = ring.endColor = color;
        progressArc.startColor = progressArc.endColor = color;
    }

    LineRenderer CreateLine(string name, bool loop)
    {
        var child = new GameObject(name);
        child.transform.SetParent(transform, false);
        var line = child.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = loop;
        line.widthMultiplier = ringWidth;
        line.sharedMaterial = lineMaterial;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.positionCount = 0;
        return line;
    }

    // Draws a circle (fraction 1) or a partial arc starting at the zone's forward direction.
    void DrawArc(LineRenderer line, float radius, float fraction)
    {
        int points = Mathf.RoundToInt(Mathf.Clamp01(fraction) * Segments);
        bool fullLoop = line.loop;
        int count = fullLoop ? Segments : (points > 0 ? points + 1 : 0);
        line.positionCount = count;
        for (int i = 0; i < count; i++)
        {
            float angle = Mathf.PI * 0.5f - i * Mathf.PI * 2f / Segments;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, heightOffset, Mathf.Sin(angle) * radius));
        }
    }
}
