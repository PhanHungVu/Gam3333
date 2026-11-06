using UnityEngine;

public sealed class EnemySpinnerAttack : EnemyGraphAttack
{
    [SerializeField] Transform visualRoot;
    [SerializeField, Min(1)] float chargeSpeed = 12;
    [SerializeField, Min(0.1f)] float chargeDuration = 0.75f;
    [SerializeField, Min(0.1f)] float contactRadius = 0.8f;
    [Header("Accepted-hit knockback")]
    [SerializeField, Min(0)] float knockbackSpeed = 32;
    [SerializeField, Range(.01f, 1)] float knockbackDuration = .65f;
    readonly System.Collections.Generic.HashSet<CombatHealth> chargeVictims = new System.Collections.Generic.HashSet<CombatHealth>();
    Vector3 direction, previous;
    Quaternion restRotation;
    readonly RaycastHit[] laneHits = new RaycastHit[48];
    readonly Collider[] laneOverlaps = new Collider[32];
    public int Contacts { get; private set; }
    public Vector3 ChargeDirection => direction;
    public override bool TargetInReach => base.TargetInReach && CanChargeFrom(transform.position);
    public bool CanChargeFrom(Vector3 position)
    {
        if (agent == null || agent.Target == null) return false;
        Vector3 delta = Steering.Flat(agent.TargetPosition - position);
        float length = Mathf.Min(delta.magnitude, chargeSpeed * chargeDuration);
        if (length < .01f) return true;
        Vector3 heading = delta.normalized;
        int count = Physics.OverlapSphereNonAlloc(position + Vector3.up * .7f, Mathf.Max(contactRadius, agent.BodyRadius),
            laneOverlaps, CombatRagdoll.QueryMask, QueryTriggerInteraction.Ignore);
        if (count == laneOverlaps.Length) return false;
        for (int i = 0; i < count; i++) if (laneOverlaps[i].GetComponentInParent<CombatHealth>() == null) return false;
        count = Physics.SphereCastNonAlloc(position + Vector3.up * .7f, Mathf.Max(contactRadius, agent.BodyRadius),
            heading, laneHits, length, CombatRagdoll.QueryMask, QueryTriggerInteraction.Ignore);
        if (count == laneHits.Length) return false;
        for (int i = 0; i < count; i++)
        {
            var hit = laneHits[i];
            if (hit.collider.GetComponentInParent<CombatHealth>() != null || hit.normal.y > .7f) continue;
            return false;
        }
        // A straight charge needs walkable support, including at the target.
        float feet = position.y - agent.PivotHeight;
        int steps = Mathf.Max(1, Mathf.CeilToInt(length));
        for (int i = 0; i <= steps; i++)
        {
            Vector3 sample = position + heading * length * i / steps;
            count = Physics.RaycastNonAlloc(new Vector3(sample.x, feet + 2, sample.z), Vector3.down,
                laneHits, 4, CombatRagdoll.QueryMask, QueryTriggerInteraction.Ignore);
            if (count == laneHits.Length) return false;
            bool grounded = false;
            for (int h = 0; h < count; h++)
            {
                var hit = laneHits[h];
                if (hit.collider.GetComponentInParent<CombatHealth>() == null && hit.normal.y >= .7f
                    && Mathf.Abs(hit.point.y - feet) <= 1.5f) { grounded = true; break; }
            }
            if (!grounded) return false;
        }
        return true;
    }
    protected override void Awake() { base.Awake(); if (visualRoot != null) restRotation = visualRoot.localRotation; }
    public override bool BeginWindup()
    {
        if (!base.BeginWindup()) return false;
        direction = Steering.Flat(committedTarget - transform.position).normalized;
        if (direction.sqrMagnitude < 0.01f) direction = transform.forward;
        return true;
    }
    public override bool TickWindup(float dt)
    {
        age += dt; float t = Mathf.Clamp01(age / windupDuration);
        if (visualRoot != null) visualRoot.localRotation = restRotation * Quaternion.Euler(0, Mathf.Lerp(0, 720, t * t), 0);
        marker.Lane(transform.position, transform.position + direction * chargeSpeed * chargeDuration, contactRadius * 2, t);
        return age >= windupDuration;
    }
    public override void BeginAttack() { chargeVictims.Clear(); CurrentStage = Stage.Attack; age = 0; previous = transform.position; marker.Hide(); }
    void Knockback(CombatHealth victim)
    {
        if (victim == null || victim.IsDead || !victim.TryGetComponent<MechMovementController>(out var movement)) return;
        var away = Steering.Flat(victim.transform.position - transform.position);
        movement.ApplyKnockback(away.sqrMagnitude > .0001f ? away : direction, knockbackSpeed, knockbackDuration);
    }
    public override bool TickAttack(float dt)
    {
        age += dt; var travel = transform.position - previous;
        var rotation = travel.sqrMagnitude > 0.001f ? Quaternion.LookRotation(Steering.Flat(travel).normalized) : Quaternion.LookRotation(direction);
        var result = blast.ResolveBox((previous + transform.position) * 0.5f + Vector3.up * 0.7f,
            new Vector3(contactRadius, 1.4f, travel.magnitude * 0.5f + contactRadius), rotation, damage, transform, ~0, transform.position + Vector3.up,
            hitHistory: chargeVictims, acceptedHit: Knockback);
        previous = transform.position;
        if (result.Hits > 0) { Contacts += result.Hits; ImpactSound(); }
        if (result.Hits > 0 || age >= chargeDuration || age > dt && agent.LastMotionBlocked)
        {
            Restore(); CurrentStage = Stage.Recovery; age = 0; agent.SetMovementIntent(EnemyMovementIntent.Hold, dt); return true;
        }
        agent.SetDirectedMovement(direction, chargeSpeed);
        if (visualRoot != null) visualRoot.localRotation = restRotation * Quaternion.Euler(0, age * 1200, 0);
        return false;
    }
    void Restore() { if (visualRoot != null) visualRoot.localRotation = restRotation; }
    public override void Cancel() { base.Cancel(); Restore(); }
}
