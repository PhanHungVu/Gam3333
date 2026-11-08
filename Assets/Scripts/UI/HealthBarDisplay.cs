using UnityEngine;

// Floating health line above a CombatHealth owner (enemies, the escort payload). Presentation only:
// it reads health and never changes it. Length, Width and Colour default to the enemies' bar.
[DisallowMultipleComponent]
[RequireComponent(typeof(CombatHealth))]
public sealed class HealthBarDisplay : MonoBehaviour
{
    [SerializeField] Material material;
    [SerializeField] float height = 1.4f;
    [Tooltip("Length of the full bar, in metres.")]
    [SerializeField, Min(0.1f)] float length = 2f;
    [Tooltip("Thickness of the bar, in metres.")]
    [SerializeField, Min(0.01f)] float width = 0.1f;
    [SerializeField] Color colour = new Color(0.3f, 1f, 0.35f);
    CombatHealth health;
    LineRenderer bar;
    Camera view;

    void Awake()
    {
        health = GetComponent<CombatHealth>();
        if (material == null) return;
        var display = new GameObject("Enemy Health");
        display.transform.SetParent(transform, false);
        bar = display.AddComponent<LineRenderer>();
        bar.sharedMaterial = material;
        bar.positionCount = 2;
        bar.startWidth = bar.endWidth = width;
        bar.startColor = bar.endColor = colour;
        bar.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        bar.receiveShadows = false;
    }

    void LateUpdate()
    {
        if (bar == null) return;
        if (view == null) view = Camera.main;
        Vector3 right = view != null ? view.transform.right : Vector3.right;
        Vector3 start = transform.position + Vector3.up * height - right * (length * 0.5f);
        bar.SetPosition(0, start);
        bar.SetPosition(1, start + right * (length * health.CurrentHealth / health.MaximumHealth));
    }
}
