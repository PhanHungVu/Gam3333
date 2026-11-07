using UnityEngine;

// A melee swing from a Melee part: damage and cooldown from the part's stats, the shared hit box
// from MechCombat (its reach scaled by the part's Range), reach assist toward a just-out-of-reach
// locked enemy, and the short melee camera hold. The mech's body never moves or turns.
public sealed class MeleeAttack
{
    readonly MechCombat combat;
    readonly Transform owner;
    Part activePart;
    PartCategory activeSocket;
    float strikeDamage, cooldownTimer, flashTimer, approachTime, approachDistance;
    MechTargetable assistedTarget;

    public bool Pending { get; private set; }
    public float Extension { get; private set; }
    public float CameraTimer { get; private set; }
    public bool Flashing => flashTimer > 0f;
    public Quaternion StrikeRotation { get; private set; }

    public MeleeAttack(MechCombat combat)
    {
        this.combat = combat;
        owner = combat.transform;
        StrikeRotation = owner.rotation;
    }

    public bool TryStart(Part part, PartCategory socket)
    {
        if (Pending || part.ReadyAt > Time.time) return false;
        activePart = part;
        activeSocket = socket;
        strikeDamage = Mathf.Max(0f, part.Stats.GetStat(StatType.Damage)) * combat.DamageModifiers.Total;
        cooldownTimer = Mathf.Max(0.01f, part.Stats.ShotInterval / combat.AttackCadenceMultiplier);
        part.Consume(cooldownTimer, false);
        combat.RaiseAttackStarted(part);
        CameraTimer = combat.MeleeCameraHold;
        StrikeRotation = owner.rotation;
        Extension = 0f;
        approachDistance = FindApproach();
        if (approachDistance > 0.01f)
        {
            Pending = true;
            approachTime = 0f;
        }
        else Resolve();
        return true;
    }

    public void TickTimers(float deltaTime)
    {
        cooldownTimer = Mathf.Max(0f, cooldownTimer - deltaTime);
        flashTimer = Mathf.Max(0f, flashTimer - deltaTime);
        CameraTimer = Mathf.Max(0f, CameraTimer - deltaTime);
        if (!Pending && flashTimer <= 0f) Extension = 0f;
    }

    public void Tick(float deltaTime)
    {
        if (!Pending || deltaTime <= 0f) return;
        var movement = combat.Movement;
        if (assistedTarget == null || !assistedTarget.isActiveAndEnabled
            || !combat.HasMelee || combat.GetMeleePart(activeSocket) != activePart
            || movement != null && (movement.IsQuickBoosting || !movement.IsGrounded))
        {
            Pending = false;
            Extension = 0f;
            combat.RaiseMeleePerformed(0);
            return;
        }
        approachTime += deltaTime;
        Physics.SyncTransforms();
        Extension = LimitApproach(approachDistance * Mathf.Clamp01(approachTime / combat.MeleeApproachDuration));
        if (approachTime >= combat.MeleeApproachDuration) Resolve();
    }

    public void Cancel()
    {
        activePart = null;
        Pending = false;
        Extension = CameraTimer = flashTimer = 0f;
    }

    // Box geometry, also read by the melee box preview.
    public Vector3 BoxCenter(float extension, Quaternion rotation) => owner.position
        + rotation * (Vector3.Scale(combat.MeleeCenter, AbsScale()) + Vector3.forward * extension);
    public Vector3 BoxSize => Vector3.Scale(combat.MeleeSize, AbsScale());

    void Resolve()
    {
        flashTimer = 0.15f;
        var result = combat.Blast.ResolveBox(BoxCenter(Extension, StrikeRotation), BoxSize * 0.5f, StrikeRotation, strikeDamage,
            owner, combat.HitLayers, combat.MeleeWallOrigin, source: activePart);
        Pending = false;
        combat.RaiseMeleePerformed(result.Hits);
    }

    // Reach assistance translates only the attack box. It never steers or teleports the movement
    // root, and never changes target or direction after the swing starts.
    float FindApproach()
    {
        var targeting = combat.Targeting;
        var movement = combat.Movement;
        assistedTarget = targeting != null && !targeting.IsFreeAim ? targeting.CurrentTarget : null;
        if (assistedTarget == null || !assistedTarget.isActiveAndEnabled || combat.MaximumMeleeApproach <= 0f
            || movement != null && (movement.IsQuickBoosting || !movement.IsGrounded)) return 0f;
        Vector3 direction = Vector3.ProjectOnPlane(assistedTarget.AimPosition - owner.position, Vector3.up);
        float angle = Vector3.Angle(owner.forward, direction);
        if (angle >= combat.MaximumMeleeAssistAngle) return 0f;
        float budget = combat.MaximumMeleeApproach * (1f - angle / combat.MaximumMeleeAssistAngle);
        Vector3 halfSize = BoxSize * 0.5f;
        Vector3 center = BoxCenter(0f, StrikeRotation);
        Vector3 front = center + StrikeRotation * Vector3.forward * halfSize.z;
        float closestGap = float.MaxValue;
        var layers = combat.HitLayers;
        foreach (var candidate in assistedTarget.GetComponentsInChildren<Collider>())
        {
            if (!candidate.enabled || candidate.isTrigger || !candidate.gameObject.activeInHierarchy
                || (layers.value & (1 << candidate.gameObject.layer)) == 0) continue;
            var health = candidate.GetComponentInParent<CombatHealth>();
            if (health == null || health.IsDead || !health.isActiveAndEnabled
                || combat.Blast.LineBlocked(combat.MeleeWallOrigin, candidate, owner, layers)) continue;
            Vector3 local = Quaternion.Inverse(StrikeRotation) * (candidate.ClosestPoint(front) - center);
            if (Mathf.Abs(local.x) > halfSize.x || Mathf.Abs(local.y) > halfSize.y) continue;
            closestGap = Mathf.Min(closestGap, local.z - halfSize.z);
        }
        if (closestGap <= 0f || closestGap + 0.03f > budget) return 0f;
        float extension = closestGap + 0.03f;
        return LimitApproach(extension) >= extension - 0.001f ? extension : 0f;
    }

    float LimitApproach(float distance)
    {
        foreach (var hit in Physics.BoxCastAll(BoxCenter(0f, StrikeRotation), BoxSize * 0.5f, StrikeRotation * Vector3.forward,
            StrikeRotation, distance, combat.HitLayers, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(owner) && hit.collider.GetComponentInParent<CombatHealth>() == null)
                distance = Mathf.Min(distance, Mathf.Max(0f, hit.distance - 0.03f));
        return distance;
    }

    // Reach scales with the swinging part's Range (golem lengths); between swings, the first melee arm.
    Vector3 AbsScale()
    {
        var part = activePart != null && (cooldownTimer > 0f || Pending || flashTimer > 0f) ? activePart
            : combat.GetMeleePart(PartCategory.ArmL) ?? combat.GetMeleePart(PartCategory.ArmR);
        return BoxScale(owner.lossyScale, part, combat.MetresPerGolem);
    }

    // Shared by the player and enemies: the melee box follows the body's scale, and its depth
    // follows the part's Range (golem lengths), so a longer weapon reaches further for everyone.
    public static Vector3 BoxScale(Vector3 lossyScale, Part part, float metresPerGolem)
    {
        float reachScale = part != null && part.Stats != null
            ? Mathf.Max(0.01f, part.Stats.GetStat(StatType.Range) * metresPerGolem / 4f) : 1f;
        return new Vector3(Mathf.Abs(lossyScale.x), Mathf.Abs(lossyScale.y), Mathf.Abs(lossyScale.z) * reachScale);
    }
}
