using UnityEngine;
using System.Collections.Generic;

// A melee swing from a Melee part: damage and swing duration from the part's stats, the shared hit box
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
    GreyboxArmWeapon prototype;
    readonly HashSet<CombatHealth> strikeVictims = new HashSet<CombatHealth>();
    float sequenceAge;
    int remainingPunches;
    public bool SequenceActive => remainingPunches > 0;

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
        if (Pending || SequenceActive || part.ReadyAt > Time.time) return false;
        // Hitstop freezes the visual swing while the wall clock still advances.
        // Do not cut that swing off with held attack input when time resumes.
        var animation = part.GetComponent<MechWeaponAnimator>();
        if (animation != null && animation.MeleeSwingPlaying) return false;
        activePart = part;
        prototype = part.GetComponent<GreyboxArmWeapon>();
        if (prototype != null && !prototype.isActiveAndEnabled) prototype = null;
        activeSocket = socket;
        sequenceAge = 0;
        remainingPunches = prototype != null ? prototype.MeleeHits - 1 : 0;
        strikeDamage = Mathf.Max(0f, part.Stats.GetStat(StatType.Damage)) * combat.DamageModifiers.Total;
        cooldownTimer = Mathf.Max(0.01f, part.Stats.ShotInterval);
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
        if (SequenceActive && deltaTime > 0f)
        {
            if (activePart == null || combat.GetMeleePart(activeSocket) != activePart || prototype == null || !prototype.isActiveAndEnabled)
            { Cancel(); return; }
            sequenceAge += deltaTime;
            float spacing = prototype.PunchInterval(activePart);
            while (remainingPunches > 0 && sequenceAge >= spacing)
            { sequenceAge -= spacing; remainingPunches--; Resolve(); }
            return;
        }
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
        prototype = null; remainingPunches = 0; sequenceAge = 0;
        Pending = false;
        Extension = CameraTimer = flashTimer = 0f;
    }

    // Box geometry, also read by the melee box preview.
    public Vector3 BoxCenter(float extension, Quaternion rotation) => owner.position
        + rotation * (prototype != null && activePart != null
            ? new Vector3(0, .5f, BoxSize.z * .5f + extension)
            : Vector3.Scale(combat.MeleeCenter, AbsScale()) + Vector3.forward * extension);
    public Vector3 BoxSize => prototype != null && activePart != null
        ? prototype.MeleeSize(activePart, combat.MetresPerGolem) : Vector3.Scale(combat.MeleeSize, AbsScale());

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // Captured from the actual queries, not reconstructed from the Range stat.
    public readonly struct HitboxSnapshot
    {
        public readonly Vector3 Center, Size, SplashCenter;
        public readonly Quaternion Rotation;
        public readonly float SplashRadius;
        public HitboxSnapshot(Vector3 center, Vector3 size, Quaternion rotation, Vector3 splashCenter, float splashRadius)
        { Center = center; Size = size; Rotation = rotation; SplashCenter = splashCenter; SplashRadius = splashRadius; }
    }
    public event System.Action<HitboxSnapshot> HitboxesResolved;

    public HitboxSnapshot PreviewHitboxes(Part part, Quaternion rotation)
    {
        var config = part != null ? part.GetComponent<GreyboxArmWeapon>() : null;
        if (config != null && !config.isActiveAndEnabled) config = null;
        Vector3 size = config != null ? config.MeleeSize(part, combat.MetresPerGolem)
            : Vector3.Scale(combat.MeleeSize, BoxScale(owner.lossyScale, part, combat.MetresPerGolem));
        Vector3 center = owner.position + rotation * (config != null ? new Vector3(0, .5f, size.z * .5f)
            : Vector3.Scale(combat.MeleeCenter, BoxScale(owner.lossyScale, part, combat.MetresPerGolem)));
        float radius = config != null && part.Stats != null ? part.Stats.GetStat(StatType.SplashRadius) : 0;
        return new HitboxSnapshot(center, size, rotation, center + rotation * Vector3.forward * (size.z * .5f), radius);
    }
#endif

    void Resolve()
    {
        flashTimer = 0.15f;
        strikeVictims.Clear();
        Vector3 directCenter = BoxCenter(Extension, StrikeRotation), directSize = BoxSize;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Vector3 splashCenter = default; float splashRadius = 0;
#endif
        var result = combat.Blast.ResolveBox(directCenter, directSize * 0.5f, StrikeRotation, strikeDamage,
            owner, combat.HitLayers, combat.MeleeWallOrigin, prototype != null ? strikeVictims : null, source: activePart,
            confirmedDamage: prototype != null && prototype.weapon == GreyboxArmWeapon.Weapon.Scythe ? Lifesteal : null);
        if (prototype != null && activePart.Stats.GetStat(StatType.SplashRadius) > 0)
        {
            Vector3 impact = BoxCenter(Extension, StrikeRotation) + StrikeRotation * Vector3.forward * (BoxSize.z * .5f);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            splashCenter = impact; splashRadius = activePart.Stats.GetStat(StatType.SplashRadius);
#endif
            var splash = combat.Blast.ResolveMeleeSphere(impact, activePart.Stats.GetStat(StatType.SplashRadius), StrikeRotation,
                Mathf.Max(0, activePart.Stats.GetStat(StatType.SplashDamage)) * combat.DamageModifiers.Total, owner, combat.HitLayers,
                combat.MeleeWallOrigin, strikeVictims, activePart, prototype.weapon == GreyboxArmWeapon.Weapon.Scythe ? Lifesteal : null);
            result = new DamageResult(result.Hits + splash.Hits, result.Kills + splash.Kills);
        }
        Pending = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        HitboxesResolved?.Invoke(new HitboxSnapshot(directCenter, directSize, StrikeRotation, splashCenter, splashRadius));
#endif
        combat.RaiseMeleePerformed(result.Hits);
    }

    void Lifesteal(CombatHealth victim, float actualDamage)
    {
        if (prototype == null || victim.Team != CombatTeam.Enemy || actualDamage <= 0) return;
        var health = owner.GetComponent<CombatHealth>();
        if (health != null) health.Heal(actualDamage * Mathf.Clamp01(prototype.lifesteal));
    }

    // Reach assistance translates only the attack box. It never steers or teleports the movement
    // root, and never changes target or direction after the swing starts.
    float FindApproach()
    {
        // These greyboxes use their exact stat reach; there is no authored lunge.
        if (prototype != null) return 0;
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
