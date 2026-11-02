using System;
using UnityEngine;

// Combines the stats of every equipped part into the mech's totals. For now that is maximum
// health: the sum of the equipped parts' Health stats, pushed into CombatHealth. The mech starts
// at full health; swaps change the maximum without healing.
[DisallowMultipleComponent]
[DefaultExecutionOrder(-140)]
[RequireComponent(typeof(EquipManager), typeof(CombatHealth))]
public sealed class MechStats : MonoBehaviour
{
    static readonly PartCategory[] Slots = (PartCategory[])Enum.GetValues(typeof(PartCategory));
    EquipManager equipment;
    CombatHealth health;

    // Total Health of the currently equipped parts.
    public float MaxHealth
    {
        get
        {
            if (equipment == null) equipment = GetComponent<EquipManager>();
            float total = 0f;
            foreach (var slot in Slots)
            {
                var part = equipment.GetEquipped(slot);
                if (part != null && part.isEquipped && part.Stats != null)
                    total += Mathf.Max(0f, part.Stats.GetStat(StatType.Health));
            }
            return total;
        }
    }

    // Mech speed = the legs' Speed (metres per second) x every other equipped part's Speed as a
    // percentage: -20 is x0.8, +10 is x1.1. The percentages multiply, so the speed never reaches zero.
    public float BaseSpeed
    {
        get
        {
            if (equipment == null) equipment = GetComponent<EquipManager>();
            var legs = equipment.GetEquipped(PartCategory.Legs);
            return legs != null && legs.isEquipped && legs.Stats != null ? Mathf.Max(0f, legs.Stats.GetStat(StatType.MoveSpeed)) : 0f;
        }
    }

    public float SpeedMultiplier
    {
        get
        {
            if (equipment == null) equipment = GetComponent<EquipManager>();
            float multiplier = 1f;
            foreach (var slot in Slots)
            {
                if (slot == PartCategory.Legs) continue;
                var part = equipment.GetEquipped(slot);
                if (part != null && part.isEquipped && part.Stats != null)
                    multiplier *= Mathf.Max(0f, 1f + part.Stats.GetStat(StatType.MoveSpeed) / 100f);
            }
            return multiplier;
        }
    }

    [Header("Speed (read-only)")]
    [Tooltip("The legs' Speed in metres per second.")]
    [SerializeField] float baseSpeed;
    [Tooltip("Every other part's Speed percentage, multiplied together.")]
    [SerializeField] float speedMultiplier = 1f;

    void Awake()
    {
        equipment = GetComponent<EquipManager>();
        health = GetComponent<CombatHealth>();
        health.SetMaximum(MaxHealth, refill: true);
        RefreshSpeed();
    }
    void OnEnable() => equipment.EquipmentChanged += Refresh;
    void OnDisable() => equipment.EquipmentChanged -= Refresh;
    void Refresh() { health.SetMaximum(MaxHealth, refill: false); RefreshSpeed(); }
    void RefreshSpeed() { baseSpeed = BaseSpeed; speedMultiplier = SpeedMultiplier; }
}
