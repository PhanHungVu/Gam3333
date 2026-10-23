using System;
using UnityEngine;

// Equip any concrete ability component on a part. No input, ammo, camera or weapon-slot dependency.
[DefaultExecutionOrder(360)]
public abstract class MechPartAbility : MonoBehaviour
{
    [Header("Shared Ability Limits")]
    [SerializeField, Min(0)] float damage = 35;
    [SerializeField] bool useWeaponDamage;
    [SerializeField, Min(0)] float weaponDamageMultiplier = 0.35f;
    [SerializeField, Min(0)] float maximumTotalDamage = 250;
    [SerializeField, Range(0, 32)] int maximumTargets = 10;
    [SerializeField, Range(1, 32)] int maximumDetonations = 16;
    [SerializeField, Range(0.1f, 30)] float maximumDuration = 10;
    [SerializeField] Color effectColor = new Color(0.25f, 0.8f, 1);
    protected readonly CombatBlast Blast = new CombatBlast();
    protected readonly CombatBlast.Budget Budget = new CombatBlast.Budget();
    protected readonly AbilityPhysics Queries = new AbilityPhysics();
    protected MechAbilityContext Context { get; private set; }
    protected CombatFeedback Feedback { get; private set; }
    protected float Damage { get; private set; }
    protected Color Tint { get; private set; }
    protected int DetonationLimit { get; private set; }
    protected float Age { get; private set; }
    float lifetime;
    int hits, kills;
    public bool IsActive { get; private set; }
    public event Action<MechAbilityResult> Resolved;

    public bool TryActivate(MechAbilityContext context)
    {
        if (!Application.isPlaying || !isActiveAndEnabled || IsActive || context.Owner == null
            || !context.Owner.gameObject.activeInHierarchy) return false;
        Context = context;
        Feedback = context.Feedback != null ? context.Feedback : context.Owner.GetComponent<CombatFeedback>();
        Damage = Mathf.Max(0, useWeaponDamage && context.SourcePart != null && context.SourcePart.Stats != null
            ? context.SourcePart.Stats.GetStat(StatType.Damage) * weaponDamageMultiplier : damage);
        Tint = effectColor;
        DetonationLimit = Mathf.Clamp(maximumDetonations, 1, 32);
        if (Feedback != null && !Feedback.CanReserveAbilityEffects(RequiredVisuals))
        { Context = default; Feedback = null; return false; }
        lifetime = Mathf.Clamp(maximumDuration, 0.1f, 30);
        Budget.Reset(maximumTotalDamage, Mathf.Clamp(maximumTargets, 0, 32));
        Age = 0; hits = kills = 0;
        IsActive = true;
        Begin();
        return true;
    }

    protected Vector3 AimDirection
    {
        get
        {
            Vector3 direction = Context.Direction;
            if (direction.sqrMagnitude < 0.001f && Context.Target != null) direction = Context.Target.AimPosition - Context.Origin;
            return direction.sqrMagnitude > 0.001f ? direction.normalized : Context.Owner.forward;
        }
    }

    void LateUpdate() => Tick(Time.deltaTime);
    // Exposed for deterministic simulation/tests; normal gameplay calls this once per frame.
    public void Tick(float deltaTime)
    {
        if (!IsActive) return;
        if (!isActiveAndEnabled || Context.Owner == null || !Context.Owner.gameObject.activeInHierarchy)
        { Cancel(); return; }
        if (deltaTime <= 0) return;
        float step = Mathf.Min(deltaTime, Mathf.Max(0, lifetime - Age));
        Age += step;
        Advance(step);
        if (IsActive && Age >= lifetime) Cancel();
    }

    protected void Detonate(Vector3 position, float radius, Collider direct = null)
    {
        var result = Blast.Resolve(position, radius, Damage, Context.Owner, Context.HitLayers, direct, Budget);
        if (Feedback != null) Feedback.PlayAbilityExplosion(position, radius, Tint);
        Publish(position, radius, result.Hits, result.Kills);
    }

    protected void Publish(Vector3 position, float radius, int hitCount, int killCount)
    {
        hits += hitCount; kills += killCount;
        Resolved?.Invoke(new MechAbilityResult(position, radius, hitCount, killCount));
    }

    protected void Finish()
    {
        if (!IsActive) return;
        IsActive = false;
        Release();
        Budget.Targets.Clear();
        if (Feedback != null) Feedback.ConfirmAbilityKills(kills);
        var result = new MechAbilityResult(Context.Origin, 0, hits, kills, true);
        Context = default; Feedback = null;
        Resolved?.Invoke(result);
    }

    public void Cancel() => Finish();
    protected virtual void OnDisable() => Cancel();
    protected abstract void Begin();
    protected virtual int RequiredVisuals => 0;
    protected abstract void Advance(float deltaTime);
    protected abstract void Release();
}
