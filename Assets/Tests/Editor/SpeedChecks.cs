using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Mech speed: the legs' Speed in m/s, times every other equipped part's Speed percentage.
public static class SpeedChecks
{
    public static void Run(Action<bool, string> check)
    {
        Part removed = null;
        var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TestMech.PlayerPath),
            new Vector3(96000f, 2f, 0f), Quaternion.identity);
        try
        {
            var stats = player.GetComponent<MechStats>();
            var equipment = player.GetComponent<EquipManager>();
            var motor = player.GetComponent<MechMovementController>();
            var legs = equipment.GetEquipped(PartCategory.Legs);
            check(legs != null && Mathf.Approximately(stats.BaseSpeed, legs.Stats.GetStat(StatType.MoveSpeed)) && stats.BaseSpeed > 0f,
                "The legs' Speed is the mech's base speed in metres per second");

            float product = 1f;
            foreach (PartCategory slot in Enum.GetValues(typeof(PartCategory)))
            {
                var part = equipment.GetEquipped(slot);
                if (slot == PartCategory.Legs || part == null || !part.isEquipped || part.Stats == null) continue;
                product *= 1f + part.Stats.GetStat(StatType.MoveSpeed) / 100f;
            }
            check(Mathf.Abs(stats.SpeedMultiplier - product) < 0.0001f && product < 1f,
                "Every other part's Speed is a percentage, and the percentages multiply together");
            check(Mathf.Abs(motor.WalkSpeed - stats.BaseSpeed * stats.SpeedMultiplier) < 0.001f,
                "The mech walks at the legs' speed times the other parts' multiplier");

            var arm = equipment.GetEquipped(PartCategory.ArmR);
            float armFactor = 1f + arm.Stats.GetStat(StatType.MoveSpeed) / 100f;
            float before = motor.WalkSpeed;
            removed = equipment.Unequip(PartCategory.ArmR);
            check(armFactor < 1f && Mathf.Abs(motor.WalkSpeed - before / armFactor) < 0.001f,
                "Removing a part removes its speed modifier");
        }
        finally
        {
            if (removed != null) Object.DestroyImmediate(removed.gameObject);
            Object.DestroyImmediate(player);
        }
    }
}
