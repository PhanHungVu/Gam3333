using UnityEngine;

// Debug presentation: draws the melee hit box and its ground footprint from MechCombat's melee
// geometry. Never changes gameplay.
[DisallowMultipleComponent]
[DefaultExecutionOrder(301)]
[RequireComponent(typeof(MechCombat))]
public sealed class MeleeBoxPreview : MonoBehaviour
{
    [SerializeField] Material material;
    [SerializeField] bool showMeleeBox = true;
    static readonly int[] BoxCorners = { 0, 1, 3, 2, 0, 4, 5, 1, 5, 7, 3, 7, 6, 2, 6, 4 };
    static readonly int[] GroundCorners = { 0, 1, 5, 4, 0 };
    MechCombat combat;
    CharacterController capsule;
    LineRenderer box, ground;

    void Awake()
    {
        combat = GetComponent<MechCombat>();
        capsule = GetComponent<CharacterController>();
        if (material == null) return;
        box = Line("Melee Hitbox Preview", 0.05f);
        box.positionCount = BoxCorners.Length;
        ground = Line("Melee Ground Reach", 0.055f);
        ground.positionCount = GroundCorners.Length;
    }

    LineRenderer Line(string name, float width)
    {
        var line = new GameObject(name).AddComponent<LineRenderer>();
        line.transform.SetParent(transform, false);
        line.useWorldSpace = true;
        line.sharedMaterial = material;
        line.startWidth = line.endWidth = width;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    void OnDisable()
    {
        if (box != null) box.enabled = false;
        if (ground != null) ground.enabled = false;
    }

    void LateUpdate()
    {
        if (box == null) return;
        var melee = combat.Melee;
        bool visible = showMeleeBox && melee != null && combat.isActiveAndEnabled && combat.HasMelee;
        box.enabled = ground.enabled = visible;
        if (!visible) return;
        Quaternion rotation = melee.Pending || melee.Flashing ? melee.StrikeRotation : transform.rotation;
        Vector3 center = melee.BoxCenter(melee.Extension, rotation);
        Vector3 size = melee.BoxSize;
        // Walk every edge of the box, retracing only the edges needed to connect them.
        for (int i = 0; i < BoxCorners.Length; i++)
        {
            int c = BoxCorners[i];
            var corner = new Vector3((c & 1) == 0 ? -0.5f : 0.5f, (c & 2) == 0 ? -0.5f : 0.5f, (c & 4) == 0 ? -0.5f : 0.5f);
            box.SetPosition(i, center + rotation * Vector3.Scale(corner, size));
        }
        box.startColor = box.endColor = melee.Flashing ? new Color(1f, 0.9f, 0.25f)
            : melee.Pending ? new Color(1f, 0.55f, 0.15f) : new Color(0.2f, 0.8f, 1f, 0.7f);
        ground.startColor = ground.endColor = box.startColor;
        float groundY = capsule != null ? capsule.bounds.min.y + 0.04f : transform.position.y - 1.46f;
        for (int i = 0; i < GroundCorners.Length; i++)
        {
            int c = GroundCorners[i];
            Vector3 point = center + rotation * Vector3.Scale(new Vector3((c & 1) == 0 ? -0.5f : 0.5f, 0f, (c & 4) == 0 ? -0.5f : 0.5f), size);
            point.y = groundY;
            ground.SetPosition(i, point);
        }
    }
}
