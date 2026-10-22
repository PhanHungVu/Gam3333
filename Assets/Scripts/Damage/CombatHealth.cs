using System;
using UnityEngine;

// Shared by the player and enemies: one health pool for the whole body. Takes damage,
// reports it, and handles death. Maximum health can be set from outside (MechStats on the player).
[DisallowMultipleComponent]
public class CombatHealth : MonoBehaviour
{
    [SerializeField, Min(1f)] float maximumHealth = 100f;
    [SerializeField] float currentHealth;
    public float CurrentHealth { get => currentHealth; private set => currentHealth = value; }
    public float MaximumHealth => maximumHealth;
    public bool IsDead => CurrentHealth <= 0f;
    public event Action<float> Damaged;
    public event Action Died;
    public event Action<bool> AvailabilityChanged;

    void Awake() => CurrentHealth = maximumHealth;
    void OnEnable() => AvailabilityChanged?.Invoke(true);
    void OnDisable() => AvailabilityChanged?.Invoke(false);

    // Changes capacity. Without refill, lost health is not restored and a lower maximum cuts
    // current health to fit; refill is for the start, when the mech begins at full health.
    public void SetMaximum(float maximum, bool refill)
    {
        maximumHealth = Mathf.Max(1f, maximum);
        CurrentHealth = refill ? maximumHealth : Mathf.Min(CurrentHealth, maximumHealth);
    }

    public bool ApplyDamage(float amount)
    {
        if (!isActiveAndEnabled || IsDead || amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return false;
        float applied = Mathf.Min(CurrentHealth, amount);
        CurrentHealth -= applied;
        Damaged?.Invoke(applied);
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
