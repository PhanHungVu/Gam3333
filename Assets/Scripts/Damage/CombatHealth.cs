using System;
using UnityEngine;

// Shared by the player and enemies: one health pool for the whole body. Takes damage,
// reports it, and handles death. Maximum health can be set from outside (MechStats on the player).
// Team decides who can damage it (see CombatTeam).
[DisallowMultipleComponent]
public class CombatHealth : MonoBehaviour
{
    [SerializeField, Min(1f)] float maximumHealth = 100f;
    [SerializeField] float currentHealth;
    [Tooltip("Attacks never damage their own team. Player on the player, Enemy on enemies, Neutral for dummies and props.")]
    [SerializeField] CombatTeam team;
    public CombatTeam Team { get => team; set => team = value; }
    public float CurrentHealth { get => currentHealth; private set => currentHealth = value; }
    public float MaximumHealth => maximumHealth;
    public bool IsDead => CurrentHealth <= 0f;
    public event Action<float> Damaged;
    // Optional source data; the original Damaged event and ApplyDamage(float) remain compatible.
    public event Action<CombatDamageInfo> DamageReceived;
    public event Action Died;
    public event Action<bool> AvailabilityChanged;
    // Includes damage, capacity changes and explicit refills; presentation reads the final values.
    public event Action Changed;

    void Awake() => CurrentHealth = maximumHealth;
    void OnEnable() { CombatRagdoll.IgnoreActorCollisions(gameObject); AvailabilityChanged?.Invoke(true); }
    void OnDisable() => AvailabilityChanged?.Invoke(false);

    // Changes capacity. Without refill, lost health is not restored and a lower maximum cuts
    // current health to fit; refill is for the start, when the mech begins at full health.
    public void SetMaximum(float maximum, bool refill)
    {
        if (float.IsNaN(maximum) || float.IsInfinity(maximum)) return;
        maximumHealth = Mathf.Max(1f, maximum);
        CurrentHealth = refill ? maximumHealth : Mathf.Min(CurrentHealth, maximumHealth);
        Changed?.Invoke();
    }

    public bool ApplyDamage(float amount) => ApplyDamage(amount, null);
    public bool ApplyDamage(float amount, Vector3? sourcePosition)
    {
        if (!isActiveAndEnabled || IsDead || amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return false;
        float applied = Mathf.Min(CurrentHealth, amount);
        CurrentHealth -= applied;
        Damaged?.Invoke(applied);
        DamageReceived?.Invoke(new CombatDamageInfo(applied, sourcePosition));
        Changed?.Invoke();
        if (IsDead)
        {
            Died?.Invoke();
            // Immediate removal also clears colliders, movement, and lock eligibility.
            gameObject.SetActive(false);
        }
        return true;
    }

    void OnValidate() => maximumHealth = Mathf.Max(1f, maximumHealth);
}
