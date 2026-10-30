using UnityEngine;

public sealed class EnemySpinnerAttack : EnemyGraphAttack
{
    [SerializeField] Transform visualRoot;
    [SerializeField, Min(1)] float chargeSpeed = 12;
    [SerializeField, Min(0.1f)] float chargeDuration = 0.75f;
    [SerializeField, Min(0.1f)] float contactRadius = 0.8f;
    Vector3 direction, previous;
    Quaternion restRotation;
    public int Contacts { get; private set; }
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
    public override void BeginAttack() { CurrentStage = Stage.Attack; age = 0; previous = transform.position; marker.Hide(); }
    public override bool TickAttack(float dt)
    {
        age += dt; var travel = transform.position - previous;
        var rotation = travel.sqrMagnitude > 0.001f ? Quaternion.LookRotation(Steering.Flat(travel).normalized) : Quaternion.LookRotation(direction);
        var result = blast.ResolveBox((previous + transform.position) * 0.5f + Vector3.up * 0.7f,
            new Vector3(contactRadius, 1.4f, travel.magnitude * 0.5f + contactRadius), rotation, damage, transform, ~0, transform.position + Vector3.up);
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
