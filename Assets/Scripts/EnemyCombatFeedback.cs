using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CombatHealth))]
public sealed class EnemyCombatFeedback : MonoBehaviour
{
    [SerializeField] CombatFeedback feedback;
    [SerializeField] bool hitFlash = true;
    [SerializeField] bool deathEffect = true;
    [SerializeField, Range(0.02f, 0.3f)] float hitFlashDuration = 0.08f;
    [SerializeField, ColorUsage(false, true)] Color hitColor = new Color(2, 1.3f, 0.6f);
    CombatHealth health;
    Renderer[] renderers;
    MaterialPropertyBlock[] original;
    MaterialPropertyBlock[] flash;
    float flashRemaining;
    bool flashing;
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    static readonly int ColorProperty = Shader.PropertyToID("_Color");
    CombatFeedback Feedback => feedback != null ? feedback : CombatFeedback.Current;

    void Awake()
    {
        health = GetComponent<CombatHealth>();
        // Exclude health-bar lines and particle renderers from mesh tinting.
        var found = GetComponentsInChildren<Renderer>(true);
        int count = 0;
        foreach (var renderer in found) if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer) count++;
        renderers = new Renderer[count];
        original = new MaterialPropertyBlock[count];
        flash = new MaterialPropertyBlock[count];
        int index = 0;
        foreach (var renderer in found)
        {
            if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
            renderers[index] = renderer;
            original[index] = new MaterialPropertyBlock();
            flash[index++] = new MaterialPropertyBlock();
        }
    }

    void OnEnable() { health.Damaged += OnDamaged; health.Died += OnDied; }
    void OnDisable() { health.Damaged -= OnDamaged; health.Died -= OnDied; Restore(); }

    void OnDamaged(float amount)
    {
        var receiver = Feedback;
        if (!hitFlash || receiver == null || !receiver.HitFeedbackEnabled) return;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            if (!flashing) renderers[i].GetPropertyBlock(original[i]);
            renderers[i].GetPropertyBlock(flash[i]);
            flash[i].SetColor(BaseColor, hitColor);
            flash[i].SetColor(ColorProperty, hitColor);
            renderers[i].SetPropertyBlock(flash[i]);
        }
        flashing = true;
        flashRemaining = hitFlashDuration;
    }

    void OnDied()
    {
        var receiver = Feedback;
        if (!deathEffect || receiver == null) return;
        var bounds = new Bounds(transform.position, Vector3.one);
        bool found = false;
        foreach (var renderer in renderers)
        {
            if (renderer == null || !renderer.enabled) continue;
            if (!found) { bounds = renderer.bounds; found = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        // The receiver owns the effect at scene root, independently of this enemy.
        receiver.PlayDeath(bounds.center, bounds.extents.magnitude);
    }

    void LateUpdate()
    {
        if (!flashing) return;
        flashRemaining -= Time.deltaTime;
        var receiver = Feedback;
        if (flashRemaining <= 0 || !hitFlash || receiver == null || !receiver.HitFeedbackEnabled) Restore();
    }

    void Restore()
    {
        if (!flashing) return;
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null) renderers[i].SetPropertyBlock(original[i]);
        flashing = false;
    }
}
