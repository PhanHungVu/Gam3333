using System;
using UnityEngine;

// Bound to one player's confirmed damage. Camera-relative presentation only; no combat/camera control.
[DisallowMultipleComponent, DefaultExecutionOrder(410)]
public sealed class DirectionalDamageHud : MonoBehaviour
{
    [SerializeField] DamageDirectionGraphic[] indicators = Array.Empty<DamageDirectionGraphic>();
    [SerializeField, Range(1, 8)] int maximumIndicators = 4;
    [SerializeField, Min(0)] float holdDuration = .12f;
    [SerializeField, Min(.01f)] float fadeDuration = .38f;
    [SerializeField, Range(0, 90)] float mergeAngle = 30;
    [SerializeField] Vector2 edgePadding = new Vector2(32, 32);
    [SerializeField] Color tint = new Color(1, .035f, .025f, .95f);
    [SerializeField, Min(.1f)] float playerSearchInterval = .5f;
    struct Cue { public Vector3 direction; public float startedAt; public bool active; }
    Cue[] cues;
    RectTransform area;
    MechPlayerRig player;
    CombatHealth health;
    float nextSearch;
    public MechPlayerRig Player => player;
    public int Capacity => Mathf.Min(maximumIndicators, indicators.Length);
    public int ActiveIndicatorCount
    {
        get { int count = 0; if (cues != null) foreach (var cue in cues) if (cue.active) count++; return count; }
    }
    public DamageDirectionGraphic[] Indicators => indicators;
    void Awake()
    {
        area = (RectTransform)transform; cues = new Cue[indicators.Length];
        // Canvas override sorting is only effective once this prefab is nested
        // beneath the live HUD Canvas; Prefab Mode treats its Canvas as a root.
        var layer = GetComponent<Canvas>(); if (layer != null) layer.overrideSorting = true;
        Clear();
    }
    void OnEnable() { if (Application.isPlaying) Bind(CombatHudBinding.FindPlayer()); }
    public void Bind(MechPlayerRig owner)
    {
        var nextHealth = owner != null ? owner.GetComponent<CombatHealth>() : null;
        if (owner == player && nextHealth == health) return;
        if (health != null) { health.DamageReceived -= OnDamage; health.AvailabilityChanged -= OnAvailability; }
        Clear(); player = owner; health = nextHealth;
        if (health != null) { health.DamageReceived += OnDamage; health.AvailabilityChanged += OnAvailability; }
    }
    void OnDisable() => Bind(null);
    void OnAvailability(bool available) { if (!available) Clear(); }
    void OnDamage(CombatDamageInfo damage)
    {
        if (!isActiveAndEnabled || health == null || health.IsDead || !damage.SourcePosition.HasValue || Capacity == 0) return;
        Vector3 direction = damage.SourcePosition.Value - health.transform.position; direction.y = 0;
        if (direction.sqrMagnitude < .0001f) return;
        direction.Normalize();
        int choice = -1, free = -1, oldest = -1;
        float smallestAngle = mergeAngle, oldestTime = float.PositiveInfinity;
        for (int i = 0; i < Capacity; i++)
        {
            if (indicators[i] == null) continue;
            if (!cues[i].active || Time.unscaledTime - cues[i].startedAt >= holdDuration + fadeDuration) { if (free < 0) free = i; continue; }
            float angle = Vector3.Angle(cues[i].direction, direction);
            if (angle <= smallestAngle) { smallestAngle = angle; choice = i; }
            if (cues[i].startedAt < oldestTime) { oldestTime = cues[i].startedAt; oldest = i; }
        }
        if (choice < 0) choice = free >= 0 ? free : oldest;
        if (choice >= 0) cues[choice] = new Cue { direction = direction, startedAt = Time.unscaledTime, active = true };
    }
    void LateUpdate()
    {
        if (player == null && Time.unscaledTime >= nextSearch)
        { nextSearch = Time.unscaledTime + playerSearchInterval; Bind(CombatHudBinding.FindPlayer()); }
        if (health == null || !health.isActiveAndEnabled || health.IsDead) { Clear(); return; }
        var camera = player.GameplayCamera;
        for (int i = 0; i < indicators.Length; i++)
        {
            var graphic = indicators[i]; if (graphic == null) continue;
            float age = Time.unscaledTime - cues[i].startedAt;
            if (i >= Capacity || !cues[i].active || age >= holdDuration + fadeDuration)
            { cues[i].active = false; graphic.enabled = false; continue; }
            if (camera == null || !TryScreenDirection(camera.transform, cues[i].direction, out var direction))
            { graphic.enabled = false; continue; }
            var rect = graphic.rectTransform;
            float rotation = Vector2.SignedAngle(Vector2.up, direction);
            rect.localRotation = Quaternion.Euler(0, 0, rotation);
            Vector2 halfSize = rect.rect.size * .62f;
            Vector2 extents = new Vector2(Mathf.Abs(direction.y) * halfSize.x + Mathf.Abs(direction.x) * halfSize.y,
                Mathf.Abs(direction.x) * halfSize.x + Mathf.Abs(direction.y) * halfSize.y);
            Vector2 bounds = Vector2.Max(Vector2.zero, area.rect.size * .5f - edgePadding - extents);
            float x = Mathf.Abs(direction.x) > .0001f ? bounds.x / Mathf.Abs(direction.x) : float.PositiveInfinity;
            float y = Mathf.Abs(direction.y) > .0001f ? bounds.y / Mathf.Abs(direction.y) : float.PositiveInfinity;
            rect.anchoredPosition = direction * Mathf.Min(x, y);
            float progress = Mathf.Clamp01((age - holdDuration) / Mathf.Max(.01f, fadeDuration));
            var color = tint; color.a *= 1 - progress * progress * (3 - 2 * progress);
            graphic.color = color; graphic.enabled = true;
        }
    }
    public static bool TryScreenDirection(Transform camera, Vector3 incoming, out Vector2 direction)
    {
        direction = Vector2.zero; incoming.y = 0;
        if (camera == null || incoming.sqrMagnitude < .0001f) return false;
        Vector3 forward = Vector3.ProjectOnPlane(camera.forward, Vector3.up);
        if (forward.sqrMagnitude < .0001f) forward = Vector3.Cross(camera.right, Vector3.up);
        if (forward.sqrMagnitude < .0001f) return false;
        forward.Normalize(); Vector3 right = Vector3.Cross(Vector3.up, forward);
        direction = new Vector2(Vector3.Dot(incoming, right), Vector3.Dot(incoming, forward)).normalized;
        return true;
    }
    public void Clear()
    {
        if (cues != null) Array.Clear(cues, 0, cues.Length);
        foreach (var indicator in indicators) if (indicator != null) indicator.enabled = false;
    }
#if UNITY_EDITOR
    [ContextMenu("Debug/Take 1 Damage From Front")] void DebugFront() => DebugDamage(Vector2.up);
    [ContextMenu("Debug/Take 1 Damage From Behind")] void DebugBack() => DebugDamage(Vector2.down);
    [ContextMenu("Debug/Take 1 Damage From Left")] void DebugLeft() => DebugDamage(Vector2.left);
    [ContextMenu("Debug/Take 1 Damage From Right")] void DebugRight() => DebugDamage(Vector2.right);
    void DebugDamage(Vector2 side)
    {
        if (!Application.isPlaying || player == null || player.GameplayCamera == null || health == null) return;
        Vector3 forward = Vector3.ProjectOnPlane(player.GameplayCamera.transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        health.ApplyDamage(1, health.transform.position + (forward * side.y + right * side.x) * 10);
    }
#endif
}
