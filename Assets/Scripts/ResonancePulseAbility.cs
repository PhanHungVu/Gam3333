using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class ResonancePulseAbility : MechPartAbility
{
    [Header("Resonance Pulse")]
    [SerializeField, Range(1, 8)] int ringCount = 3;
    [SerializeField, Range(0.5f, 40)] float radius = 12;
    [SerializeField, Range(0.05f, 2)] float width = 0.5f;
    [SerializeField, Range(0.1f, 50)] float speed = 10;
    [SerializeField, Range(0.08f, 5)] float delay = 0.4f;
    [SerializeField, Range(0.1f, 5)] float verticalHalfHeight = 1.5f;
    [Tooltip("Collision-limited horizontal push in metres on surviving enemies, once per pulse.")]
    [SerializeField, Range(0, 3)] float knockback = 0.4f;
    sealed class Ring
    {
        public bool Started, Done;
        public float Radius;
        public CombatEffectLease Visual;
        public readonly HashSet<CombatHealth> Hit = new HashSet<CombatHealth>();
    }
    readonly Ring[] rings = new Ring[8];
    int count;
    float maxRadius, ringWidth, travelSpeed, interval, height, push;
    Action<CombatHealth> applyKnockback;
    protected override int RequiredVisuals => Mathf.Min(Mathf.Clamp(ringCount, 1, 8), DetonationLimit);

    protected override void Begin()
    {
        count = Mathf.Min(Mathf.Clamp(ringCount, 1, 8), DetonationLimit);
        maxRadius = Mathf.Clamp(radius, 0.5f, 40); ringWidth = Mathf.Clamp(width, 0.05f, 2);
        travelSpeed = Mathf.Clamp(speed, 0.1f, 50); interval = Mathf.Clamp(delay, 0.08f, 5);
        height = Mathf.Clamp(verticalHalfHeight, 0.1f, 5); push = Mathf.Clamp(knockback, 0, 3);
        if (applyKnockback == null) applyKnockback = Push;
        for (int i = 0; i < count; i++)
        {
            if (rings[i] == null) rings[i] = new Ring();
            rings[i].Started = rings[i].Done = false; rings[i].Radius = 0; rings[i].Hit.Clear();
            rings[i].Visual = Feedback != null ? Feedback.LeaseAbilityEffect(CombatFeedbackEffect.Kind.Wave, Context.Origin, Tint) : default;
            rings[i].Visual.Hide();
        }
    }

    void Push(CombatHealth health) => Queries.Knockback(health, Context.Origin, push, Context.HitLayers);

    protected override void Advance(float deltaTime)
    {
        int completed = 0;
        for (int i = 0; i < count; i++)
        {
            Ring ring = rings[i];
            if (ring.Done) { ring.Visual.Release(); completed++; continue; }
            if (Age < i * interval) continue;
            if (!ring.Started) { ring.Started = true; if (Feedback != null) Feedback.PlayAbilityAudio(Context.Origin); }
            float nextRadius = Mathf.Min(maxRadius, (Age - i * interval) * travelSpeed);
            if (!ring.Visual.IsValid && Feedback != null)
                ring.Visual = Feedback.LeaseAbilityEffect(CombatFeedbackEffect.Kind.Wave, Context.Origin, Tint);
            ring.Visual.Wave(nextRadius, ringWidth);
            var result = Blast.Resolve(default, Context.Origin, nextRadius + ringWidth * 0.5f, Damage,
                Context.Owner, Context.HitLayers, budget: Budget,
                innerRadius: Mathf.Max(0, ring.Radius - ringWidth * 0.5f), waveHalfHeight: height,
                hitHistory: ring.Hit, onHit: applyKnockback);
            ring.Radius = nextRadius;
            Publish(Context.Origin, nextRadius, result.Hits, result.Kills);
            if (!IsActive) return;
            if (nextRadius >= maxRadius) { ring.Done = true; completed++; }
        }
        // Keep the final visible front for this frame; release on the next tick.
        if (completed == count && allDoneLastTick) Finish();
        allDoneLastTick = completed == count;
    }
    bool allDoneLastTick;

    protected override void Release()
    {
        for (int i = 0; i < count; i++) { rings[i].Visual.Release(); rings[i].Hit.Clear(); }
        allDoneLastTick = false;
    }
}
