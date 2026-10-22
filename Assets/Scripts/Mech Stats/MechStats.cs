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

    void Awake()
    {
        equipment = GetComponent<EquipManager>();
        health = GetComponent<CombatHealth>();
        health.SetMaximum(MaxHealth, refill: true);
    }
    void OnEnable() => equipment.EquipmentChanged += Refresh;
    void OnDisable() => equipment.EquipmentChanged -= Refresh;
    void Refresh() => health.SetMaximum(MaxHealth, refill: false);
}
