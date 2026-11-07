using System.Collections.Generic;
using UnityEngine;

// What an ability effect can reach on its mech.
public sealed class MechAbilityContext
{
    public MechCombat Combat;
    public MechMovementController Movement;
    public CombatHealth Health;
    public GameObject Mech;
}

// One kind of ability effect (Damage Amp, later Heal, Invulnerable...). Begin when the ability fires,
// End when its duration runs out or its part is removed. Power is the part's Ability Power stat.
public interface IAbilityEffect
{
    void Begin(MechAbilityContext context, float power);
    void End(MechAbilityContext context);
}

// An effect that can only start in some situations (e.g. Jump needs ground). When it can't, the request
// is refused and the cooldown isn't spent.
public interface IConditionalAbilityEffect : IAbilityEffect
{
    bool CanBegin(MechAbilityContext context);
}

// Jump (default legs): a leap of Power golem lengths, through the movement controller's own jump.
// Instant (no duration); only from the ground.
public sealed class JumpEffect : IConditionalAbilityEffect
{
    public bool CanBegin(MechAbilityContext context) => context.Movement != null && context.Movement.IsGrounded;
    public void Begin(MechAbilityContext context, float power)
    {
        float metresPerGolem = context.Combat != null ? context.Combat.MetresPerGolem : 4f;
        context.Movement?.RequestJump(Mathf.Max(0f, power) * metresPerGolem);
    }
    public void End(MechAbilityContext context) { }
}

// An effect that also does something every frame while active (e.g. Heal over time).
public interface ITickingAbilityEffect : IAbilityEffect
{
    void Tick(MechAbilityContext context, float deltaTime);
}

// Heal (default head): restores Power% of maximum health per second for the duration.
public sealed class HealEffect : ITickingAbilityEffect
{
    float percentPerSecond;
    public void Begin(MechAbilityContext context, float power) => percentPerSecond = Mathf.Max(0f, power);
    public void Tick(MechAbilityContext context, float deltaTime)
    {
        if (context.Health != null) context.Health.Heal(context.Health.MaximumHealth * percentPerSecond / 100f * Mathf.Max(0f, deltaTime));
    }
    public void End(MechAbilityContext context) { }
}

// Damage Amp (default torso): weapon damage x (1 + Power%) for the duration.
public sealed class DamageAmpEffect : IAbilityEffect
{
    public void Begin(MechAbilityContext context, float power) => context.Combat?.DamageModifiers.Set(this, 1f + Mathf.Max(0f, power) / 100f);
    public void End(MechAbilityContext context) => context.Combat?.DamageModifiers.Remove(this);
}

// Runs the abilities of the mech's head, torso and legs. Listens to MechPlayerInput's ability requests
// (Ctrl torso, Tab head); a part's PartData says which ability it gives and its Cooldown, Duration and
// Power stats tune it. An accepted ability starts its effect and then confirms the part's cooldown
// (PartAbilityCooldown, which the abilities HUD shows). The effect ends after its Duration, or at once if
// the part is taken off. Put it on the player, next to MechPlayerInput.
[DisallowMultipleComponent]
public sealed class MechAbilities : MonoBehaviour
{
    sealed class Running
    {
        public Part Part;
        public PartCategory Socket;
        public IAbilityEffect Effect;
        public readonly TimedEffect Timer = new TimedEffect();
    }

    [Header("Debug (read-only)")]
    [Tooltip("The mech's weapon damage multiplier right now (1.5 while Damage Amp is on).")]
    [SerializeField] float damageMultiplier = 1f;
    [SerializeField] string activeAbilities = "";

    readonly List<Running> running = new List<Running>();
    MechAbilityContext context;
    MechPlayerInput input;
    EquipManager equipment;

    public bool IsActive(PartCategory socket) => running.Exists(r => r.Socket == socket);

    // The effect for each ability kind. A new ability is a new case here and a new effect class.
    public static IAbilityEffect Create(PartAbility ability) => ability switch
    {
        PartAbility.DamageAmp => new DamageAmpEffect(),
        PartAbility.Heal => new HealEffect(),
        PartAbility.Jump => new JumpEffect(),
        _ => null,
    };

    void Awake()
    {
        input = GetComponent<MechPlayerInput>();
        equipment = GetComponent<EquipManager>();
        context = new MechAbilityContext { Combat = GetComponent<MechCombat>(), Health = GetComponent<CombatHealth>(),
            Movement = GetComponent<MechMovementController>(), Mech = gameObject };
    }

    void OnEnable() { if (input != null) input.AbilityRequested += OnAbilityRequested; }
    void OnAbilityRequested(PartCategory socket, Part part) => Request(socket, part);
    void OnDisable()
    {
        if (input != null) input.AbilityRequested -= OnAbilityRequested;
        for (int i = running.Count - 1; i >= 0; i--) Finish(i);
    }

    // Tries to fire the ability of the part in this socket. Returns true if it started.
    public bool Request(PartCategory socket, Part part)
    {
        if (part == null || part.Stats == null || part.Stats.ability == PartAbility.None || IsActive(socket)) return false;
        var cooldown = PartAbilityCooldown.For(part);
        if (cooldown.IsCoolingDown) return false;
        var effect = Create(part.Stats.ability);
        if (effect == null || (effect is IConditionalAbilityEffect conditional && !conditional.CanBegin(context))) return false;
        var run = new Running { Part = part, Socket = socket, Effect = effect };
        effect.Begin(context, part.Stats.GetStat(StatType.AbilityPower));
        run.Timer.Start(part.Stats.GetStat(StatType.AbilityDuration));
        running.Add(run);
        cooldown.ConfirmActivation(Mathf.Max(0f, part.Stats.GetStat(StatType.AbilityCooldown)));
        if (!run.Timer.Active) Finish(running.Count - 1); // an instant ability (no duration) ends at once
        return true;
    }

    void Update() => Tick(Time.deltaTime);

    // Advances running abilities: each ends when its duration runs out or its part is no longer equipped.
    public void Tick(float deltaTime)
    {
        for (int i = running.Count - 1; i >= 0; i--)
        {
            var run = running[i];
            bool removed = run.Part == null || (equipment != null && equipment.GetEquipped(run.Socket) != run.Part);
            if (!removed && run.Effect is ITickingAbilityEffect ticking) ticking.Tick(context, Mathf.Min(deltaTime, run.Timer.Remaining));
            if (run.Timer.Tick(deltaTime) || removed) Finish(i);
        }
        damageMultiplier = context.Combat != null ? context.Combat.DamageModifiers.Total : 1f;
        activeAbilities = string.Join(", ", running.ConvertAll(r => $"{r.Part.Stats.ability} ({r.Timer.Remaining:0.0}s)"));
    }

    void Finish(int index)
    {
        running[index].Effect.End(context);
        running.RemoveAt(index);
    }
}
