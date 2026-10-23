using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// MechStats: the player's one health pool takes its maximum from the equipped parts.
public static class MechStatsChecks
{
    public static void Run(Action<bool, string> check)
    {
        var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TestMech.PlayerPath),
            new Vector3(47000, 1.6f, 0), Quaternion.identity);
        var detached = new List<GameObject>();
        try
        {
            TestMech.StopPlayerInput(player.GetComponent<MechMovementController>());
            var equipment = player.GetComponent<EquipManager>();
            var stats = player.GetComponent<MechStats>();
            var health = player.GetComponent<CombatHealth>();
            float expected = player.GetComponentsInChildren<Part>().Where(p => p.isEquipped && p.Stats != null)
                .Sum(p => Mathf.Max(0f, p.Stats.GetStat(StatType.Health)));
            check(stats != null && stats.MaxHealth == expected && stats.MaxHealth > 0f,
                "Max health totals the equipped parts' Health stats");
            check(health.MaximumHealth == stats.MaxHealth && health.CurrentHealth == stats.MaxHealth, "The mech starts at full health");
            float full = stats.MaxHealth;
            check(health.ApplyDamage(20f) && health.CurrentHealth == full - 20f, "Damage lowers the single health pool");
            var head = equipment.Unequip(PartCategory.Head);
            detached.Add(head.gameObject);
            float headHealth = head.Stats.GetStat(StatType.Health);
            check(stats.MaxHealth == full - headHealth && health.MaximumHealth == full - headHealth,
                "Dropping a part removes its Health from the maximum");
            check(health.CurrentHealth == Mathf.Min(full - 20f, full - headHealth), "Dropping a part never heals");
            float before = health.CurrentHealth;
            check(equipment.TryEquip(head, PartCategory.Head) && health.MaximumHealth == full && health.CurrentHealth == before,
                "Re-equipping restores the maximum without healing");
            detached.Remove(head.gameObject);
        }
        finally
        {
            foreach (var go in detached) if (go != null) Object.DestroyImmediate(go);
            Object.DestroyImmediate(player);
        }
    }
}
