using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Physics owns a temporary visual/body copy; the same living health remains targetable and resumes its existing AI.
[DisallowMultipleComponent, RequireComponent(typeof(CombatHealth), typeof(EnemyAgent)), DefaultExecutionOrder(-50)]
public sealed class EnemyMeleeReaction : MonoBehaviour
{
    [SerializeField, Min(0)] float impulseMultiplier = 1f;
    [SerializeField, Range(1, 24)] int maximumActive = 12;
    [SerializeField, Range(1, 20)] int maximumBodies = 14;
    CombatHealth health;
    EnemyAgent agent;
    MechTargetable targetable;
    CombatRagdoll ragdoll;
    readonly List<Behaviour> paused = new List<Behaviour>();
    readonly List<Renderer> hidden = new List<Renderer>();
    readonly List<Collider> disabled = new List<Collider>();
    Vector3 rootOffset, originalPosition, fallbackVelocity;
    float age, duration;
    bool active;
    public bool IsKnockedDown => active;
    public CombatRagdoll Ragdoll => ragdoll;
    public int ImpactCount { get; private set; }
    public int RecoveryCount { get; private set; }
    void Awake() { health = GetComponent<CombatHealth>(); agent = GetComponent<EnemyAgent>(); targetable = GetComponent<MechTargetable>(); }

    public bool Apply(Vector3 impulse, float seconds)
    {
        if (!isActiveAndEnabled || health.IsDead || impulseMultiplier <= 0 || MissionOutcomeController.BlocksGameplay(gameObject.scene)) return false;
        impulse *= impulseMultiplier;
        duration = Mathf.Clamp(seconds, .1f, 3f); age = 0; ImpactCount++;
        if (!active)
        {
            originalPosition = transform.position;
            CacheControls();
            ragdoll = CombatRagdoll.Spawn(transform, Vector3.zero, maximumActive, maximumBodies, temporary: true);
            // Dense crowds fall back to one physical body before falling back to swept root knockback.
            if (ragdoll == null) ragdoll = CombatRagdoll.Spawn(transform, Vector3.zero, 24, 1, temporary: true);
            active = true;
            if (ragdoll != null)
            {
                rootOffset = transform.position - ragdoll.CoreBody.worldCenterOfMass;
                foreach (var renderer in hidden) if (renderer != null) renderer.enabled = false;
                foreach (var collider in disabled) if (collider != null) collider.enabled = false;
                if (targetable != null) targetable.PhysicsAim = ragdoll.AimPoint;
            }
            foreach (var script in paused) if (script != null) script.enabled = false;
        }
        if (ragdoll != null) ragdoll.ApplyImpulse(impulse);
        else fallbackVelocity = Vector3.ClampMagnitude(fallbackVelocity + Vector3.ProjectOnPlane(impulse, Vector3.up), 22);
        return true;
    }
    void CacheControls()
    {
        paused.Clear(); hidden.Clear(); disabled.Clear();
        foreach (var script in GetComponentsInChildren<Behaviour>(true))
        {
            if (!script.enabled || script.GetComponentInParent<CombatRagdoll>() != null) continue;
            // Suspend controllers only. Health, loot, wave bookkeeping and death listeners keep running.
            if (script is EnemyAgent || script is EnemyAttack || script is EnemyGraphAttack || script is EnemyGraphController
                || script is Unity.Behavior.BehaviorGraphAgent || script is EnemyCoordinationMember || script is EnemyAnimation
                || script is EnemyHopPresentation || script is EnemyWeaponTelegraph || script is EnemyThreatMarker
                || script is MechWeaponAnimator || script is MechWeaponIdle || script is MechAnimationPoseSource
                || script is MechPartPoseReceiver || script is Animator || script is NavMeshAgent) paused.Add(script);
        }
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            if (renderer.enabled && renderer.GetComponentInParent<CombatRagdoll>() == null) hidden.Add(renderer);
        foreach (var collider in GetComponentsInChildren<Collider>(true))
            if (collider.enabled && collider.GetComponentInParent<CombatRagdoll>() == null) disabled.Add(collider);
    }
    void Update()
    {
        if (!active) return;
        if (health.IsDead) return; // Died promotes the same proxy before CombatHealth deactivates the actor.
        if (MissionOutcomeController.BlocksGameplay(gameObject.scene)) { Recover(); return; }
        age += Time.deltaTime;
        if (ragdoll != null)
        {
            SyncRoot();
            var core = ragdoll.CoreBody;
            bool landed = Ground(core.position, out var hit) && core.GetComponent<Collider>().bounds.min.y <= hit.point.y + .3f
                && Mathf.Abs(core.linearVelocity.y) < 2;
            if ((age >= duration && landed) || age >= duration + 1.5f) Recover();
        }
        else
        {
            MoveFallback(Time.deltaTime);
            if (age >= duration) Recover();
        }
    }
    // Move the logical actor with its physical core without translating its simulated bodies a second time.
    void SyncRoot()
    {
        if (ragdoll == null || ragdoll.CoreBody == null) return;
        Vector3 position = ragdoll.CoreBody.worldCenterOfMass + rootOffset;
        // Never translate an ancestor of simulated/interpolated bodies, even temporarily.
        // Preserve world poses while the logical health root catches up, then restore query ownership.
        ragdoll.transform.SetParent(null, true);
        transform.position = position;
        ragdoll.transform.SetParent(transform, true);
    }
    void MoveFallback(float dt)
    {
        Vector3 motion = fallbackVelocity * dt;
        if (motion.sqrMagnitude > .000001f)
        {
            float distance = motion.magnitude;
            foreach (var hit in Physics.SphereCastAll(transform.position + Vector3.up * agent.ProbeHeight, agent.BodyRadius, motion / distance,
                distance, Part.MovementQueryMask & CombatRagdoll.QueryMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(transform) || hit.normal.y > .6f || hit.collider.GetComponentInParent<CombatHealth>() != null) continue;
                distance = Mathf.Min(distance, Mathf.Max(0, hit.distance - .05f));
            }
            transform.position += motion.normalized * distance;
        }
        if (Ground(transform.position, out var ground)) transform.position = new Vector3(transform.position.x, ground.point.y + agent.PivotHeight, transform.position.z);
        fallbackVelocity = Vector3.MoveTowards(fallbackVelocity, Vector3.zero, 22f / Mathf.Max(.1f, duration) * dt);
    }
    bool Ground(Vector3 point, out RaycastHit result)
    {
        result = default; float nearest = float.PositiveInfinity;
        foreach (var hit in Physics.RaycastAll(point + Vector3.up * 4, Vector3.down, 54, Part.MovementQueryMask & CombatRagdoll.QueryMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.distance >= nearest || hit.normal.y < .5f || hit.point.y > point.y + .5f || hit.transform.IsChildOf(transform)
                || hit.collider.GetComponentInParent<CombatHealth>() != null) continue;
            nearest = hit.distance; result = hit;
        }
        return nearest < float.PositiveInfinity;
    }
    bool StandAt(Vector3 candidate, out Vector3 position)
    {
        position = candidate;
        if (!Ground(candidate, out var hit)) return false;
        position.y = hit.point.y + agent.PivotHeight;
        float radius = Mathf.Max(.1f, agent.BodyRadius);
        Vector3 bottom = new Vector3(position.x, hit.point.y + radius + .05f, position.z);
        Vector3 top = new Vector3(position.x, hit.point.y + Mathf.Max(radius + .05f, agent.PivotHeight * 2), position.z);
        foreach (var collider in Physics.OverlapCapsule(bottom, top, radius, Part.MovementQueryMask & CombatRagdoll.QueryMask, QueryTriggerInteraction.Ignore))
            if (!collider.transform.IsChildOf(transform) && collider.GetComponentInParent<CombatHealth>() == null) return false;
        return true;
    }
    Vector3 RecoveryPosition()
    {
        Vector3 desired = transform.position;
        if (StandAt(desired, out var point)) return point;
        for (int ring = 1; ring <= 3; ring++) for (int i = 0; i < 8; i++)
        {
            Vector3 offset = Quaternion.AngleAxis(i * 45, Vector3.up) * Vector3.forward * (agent.BodyRadius * 1.5f * ring);
            if (StandAt(desired + offset, out point)) return point;
        }
        return StandAt(originalPosition, out point) ? point : originalPosition;
    }
    void Recover()
    {
        if (!active) return;
        SyncRoot(); Vector3 position = RecoveryPosition();
        if (ragdoll != null) { ragdoll.Dispose(); ragdoll = null; }
        transform.position = position; Restore(); RecoveryCount++;
    }
    public CombatRagdoll ReleaseForDeath()
    {
        if (!active || ragdoll == null) return null;
        SyncRoot(); var result = ragdoll; result.SynchronizeVisuals(); result.transform.SetParent(null, true); ragdoll = null;
        return result; // OnDisable restores the original pooled actor; the promoted corpse is no longer its child.
    }
    internal void SynchronizeDeathPose() { if (active && ragdoll != null) { SyncRoot(); ragdoll.SynchronizeVisuals(); } }
    void Restore()
    {
        active = false; fallbackVelocity = Vector3.zero;
        if (targetable != null) targetable.PhysicsAim = null;
        foreach (var renderer in hidden) if (renderer != null) renderer.enabled = true;
        foreach (var collider in disabled) if (collider != null) collider.enabled = true;
        if (gameObject.activeInHierarchy) CombatRagdoll.IgnoreActorCollisions(gameObject);
        bool terminal = MissionOutcomeController.BlocksGameplay(gameObject.scene);
        foreach (var script in paused)
        {
            if (script == null) continue;
            if (terminal && (script is EnemyAgent || script is EnemyAttack || script is EnemyGraphAttack || script is EnemyGraphController
                || script is EnemyCoordinationMember || script is Unity.Behavior.BehaviorGraphAgent || script is NavMeshAgent)) continue;
            script.enabled = true;
        }
        paused.Clear(); hidden.Clear(); disabled.Clear();
    }
    void OnDisable()
    {
        if (!active) return;
        if (gameObject.activeInHierarchy && !health.IsDead) Recover();
        else { if (ragdoll != null) ragdoll.Dispose(); ragdoll = null; Restore(); }
    }
}
