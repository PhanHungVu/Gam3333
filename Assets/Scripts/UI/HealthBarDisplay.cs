using UnityEngine;

// Floating health line above a CombatHealth owner (used on enemies). Presentation only:
// it reads health and never changes it.
[DisallowMultipleComponent]
[RequireComponent(typeof(CombatHealth))]
public sealed class HealthBarDisplay : MonoBehaviour
{
    [SerializeField] Material material;
    [SerializeField] float height = 1.4f;
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
        bar.startWidth = bar.endWidth = 0.1f;
        bar.startColor = bar.endColor = new Color(0.3f, 1f, 0.35f);
        bar.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        bar.receiveShadows = false;
    }

    void LateUpdate()
    {
        if (bar == null) return;
        if (view == null) view = Camera.main;
        Vector3 right = view != null ? view.transform.right : Vector3.right;
        Vector3 start = transform.position + Vector3.up * height - right;
        bar.SetPosition(0, start);
        bar.SetPosition(1, start + right * (2f * health.CurrentHealth / health.MaximumHealth));
    }
}
