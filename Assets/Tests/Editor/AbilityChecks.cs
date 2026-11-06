using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Part abilities: damage modifiers, timed effects, and the default torso's Damage Amp.
public static class AbilityChecks
{
    public static void Run(Action<bool, string> check)
    {
        var modifiers = new DamageModifiers();
        object a = new object(), b = new object();
        modifiers.Set(a, 1.5f); modifiers.Set(b, 2f);
        bool stacked = Mathf.Approximately(modifiers.Total, 3f);
        modifiers.Set(a, 1.2f);
        bool replaced = Mathf.Approximately(modifiers.Total, 2.4f);
        modifiers.Remove(a); modifiers.Remove(b);
        check(stacked && replaced && modifiers.Total == 1f, "Damage modifiers multiply together, a source replaces its own, and removing them restores normal damage");

        var timer = new TimedEffect();
        timer.Start(20f);
        bool running = !timer.Tick(19f) && timer.Active;
        check(running && timer.Tick(1.5f) && !timer.Active, "A timed ability lasts its duration and reports when it ends");

        var torso = AssetDatabase.LoadAssetAtPath<PartData>("Assets/PartCatalogue/PartData_Torso.asset");
        // Checks the setup, not the tuning: any power, cooldown and duration above zero.
        check(torso != null && torso.ability == PartAbility.DamageAmp && torso.GetStat(StatType.AbilityPower) > 0f
            && torso.GetStat(StatType.AbilityCooldown) > 0f && torso.GetStat(StatType.AbilityDuration) > 0f,
            "The default torso's ability is Damage Amp, with a power, a cooldown and a duration set");

        var mech = new GameObject("Ability Check Mech");
        try
        {
            var combat = mech.AddComponent<MechCombat>();
            var context = new MechAbilityContext { Combat = combat, Mech = mech };
            var effect = MechAbilities.Create(PartAbility.DamageAmp);
            effect.Begin(context, 50f);
            bool amped = Mathf.Approximately(combat.DamageModifiers.Total, 1.5f);
            effect.End(context);
            check(amped && combat.DamageModifiers.Total == 1f && MechAbilities.Create(PartAbility.None) == null,
                "Damage Amp raises the mech's weapon damage by its power while active, then restores it");
        }
        finally { Object.DestroyImmediate(mech); }

        RunOnPlayer(check);
    }

    static readonly System.Reflection.FieldInfo StrikeDamage =
        typeof(MeleeAttack).GetField("strikeDamage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    // The whole path on the real player prefab: request -> amp on -> cooldown -> amplified melee -> ends.
    static void RunOnPlayer(Action<bool, string> check)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TestMech.PlayerPath);
        var player = Object.Instantiate(prefab, new Vector3(-30000f, 1.6f, 0f), Quaternion.identity);
        var spare = new GameObject("Ability Check Spare Torso");
        try
        {
            TestMech.StopPlayerInput(player.GetComponent<MechPlayerInput>());
            var abilities = player.GetComponent<MechAbilities>();
            var combat = player.GetComponent<MechCombat>();
            var equipment = player.GetComponent<EquipManager>();
            var torso = equipment != null ? equipment.GetEquipped(PartCategory.Torso) : null;
            check(abilities != null && torso != null && torso.Stats.ability == PartAbility.DamageAmp,
                "The player prefab has Mech Abilities and a torso with Damage Amp");
            if (abilities == null || torso == null) return;

            float power = torso.Stats.GetStat(StatType.AbilityPower), duration = torso.Stats.GetStat(StatType.AbilityDuration);
            float amp = 1f + power / 100f;
            bool started = abilities.Request(PartCategory.Torso, torso);
            check(started && Mathf.Approximately(combat.DamageModifiers.Total, amp) && PartAbilityCooldown.For(torso).IsCoolingDown
                && !abilities.Request(PartCategory.Torso, torso),
                "Using the torso ability amplifies the mech's damage, starts its cooldown, and can't be reused while cooling down");

            PartCategory melee = PartCategory.Head; Part sword = null;
            foreach (var socket in new[] { PartCategory.ArmL, PartCategory.ArmR })
            {
                var part = equipment.GetEquipped(socket);
                if (part != null && part.Stats.subtype == PartSubtype.Melee) { melee = socket; sword = part; break; }
            }
            if (sword != null)
            {
                TestMech.Advance(player, 5f);
                bool swung = combat.TryAttack(melee).Started;
                float dealt = (float)StrikeDamage.GetValue(combat.Melee);
                check(swung && Mathf.Approximately(dealt, sword.Stats.GetStat(StatType.Damage) * amp),
                    "A melee swing during Damage Amp deals the amplified damage; swung=" + swung + "; strike=" + dealt);
            }

            abilities.Tick(duration + 0.1f);
            check(combat.DamageModifiers.Total == 1f && !abilities.IsActive(PartCategory.Torso),
                "Damage Amp ends after its duration and damage returns to normal");

            // A torso that isn't the equipped one (as if it had just been swapped off) ends at once.
            var other = spare.AddComponent<Part>();
            other.data = torso.Stats;
            bool otherStarted = abilities.Request(PartCategory.Torso, other);
            abilities.Tick(0.01f);
            check(otherStarted && combat.DamageModifiers.Total == 1f && !abilities.IsActive(PartCategory.Torso),
                "Taking the torso off ends its Damage Amp at once");
        }
        finally { Object.DestroyImmediate(player); Object.DestroyImmediate(spare); }
    }
}
