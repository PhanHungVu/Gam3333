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

        var head = AssetDatabase.LoadAssetAtPath<PartData>("Assets/PartCatalogue/PartData_Head.asset");
        check(head != null && head.ability == PartAbility.Heal && head.GetStat(StatType.AbilityPower) > 0f
            && head.GetStat(StatType.AbilityCooldown) > 0f && head.GetStat(StatType.AbilityDuration) > 0f,
            "The default head's ability is Heal, with a power, a cooldown and a duration set");

        var legsData = AssetDatabase.LoadAssetAtPath<PartData>("Assets/PartCatalogue/PartData_Legs_Fast.asset");
        check(torso != null && torso.abilityVfx != null && head != null && head.abilityVfx != null && legsData != null && legsData.abilityVfx != null,
            "The default torso, head and legs each have an Ability Vfx set on their PartData");

        var patient = new GameObject("Heal Check");
        try
        {
            var hp = patient.AddComponent<CombatHealth>();
            hp.SetMaximum(200f, refill: true);
            hp.ApplyDamage(100f);
            float restored = hp.Heal(30f);
            bool partial = Mathf.Approximately(restored, 30f) && Mathf.Approximately(hp.CurrentHealth, 130f);
            float capped = hp.Heal(500f);
            bool full = Mathf.Approximately(capped, 70f) && Mathf.Approximately(hp.CurrentHealth, 200f) && hp.Heal(10f) == 0f;
            hp.ApplyDamage(1000f);
            check(partial && full && hp.Heal(50f) == 0f, "Healing restores health up to the maximum and never heals the dead");
        }
        finally { Object.DestroyImmediate(patient); }

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

    // The whole path on the default player mech (the specialist): Damage Amp (request -> amp on -> cooldown ->
    // amplified melee -> ends) and Heal (request -> heals over its duration -> ends).
    static void RunOnPlayer(Action<bool, string> check)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SpecialistMechChecks.SpecialistPath);
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
                "The default player mech has Mech Abilities and a torso with Damage Amp");
            if (abilities == null || torso == null) return;

            var vfx = player.GetComponent<MechAbilityVfx>();
            if (vfx == null) vfx = player.AddComponent<MechAbilityVfx>(); // until the prefab carries it
            int startedEvents = 0, endedEvents = 0;
            abilities.AbilityStarted += (s, p, d) => startedEvents++;
            abilities.AbilityEnded += (s, p) => endedEvents++;
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

            bool effectOn = vfx.ActiveCount == 1 && player.transform.Find(torso.Stats.abilityVfx.name + "(Clone)") != null;
            abilities.Tick(duration + 0.1f);
            check(combat.DamageModifiers.Total == 1f && !abilities.IsActive(PartCategory.Torso),
                "Damage Amp ends after its duration and damage returns to normal");
            check(effectOn && vfx.ActiveCount == 0 && startedEvents == 1 && endedEvents == 1,
                "The torso's ability effect plays on the mech while Damage Amp is active and stops when it ends");

            // Heal on the equipped head: restores Power% of max HP per second, for its duration only.
            var health = player.GetComponent<CombatHealth>();
            var headPart = equipment.GetEquipped(PartCategory.Head);
            if (health != null && headPart != null && headPart.Stats.ability == PartAbility.Heal)
            {
                float max = health.MaximumHealth, perSecond = max * headPart.Stats.GetStat(StatType.AbilityPower) / 100f;
                float healDuration = headPart.Stats.GetStat(StatType.AbilityDuration);
                health.ApplyDamage(max * 0.9f);
                float start = health.CurrentHealth;
                bool healStarted = abilities.Request(PartCategory.Head, headPart);
                abilities.Tick(1f);
                float afterOne = health.CurrentHealth - start;
                abilities.Tick(healDuration + 5f);
                float total = health.CurrentHealth - start;
                check(healStarted && Mathf.Abs(afterOne - perSecond) < 0.01f
                    && Mathf.Abs(total - Mathf.Min(perSecond * healDuration, max - start)) < 0.05f
                    && !abilities.IsActive(PartCategory.Head) && PartAbilityCooldown.For(headPart).IsCoolingDown,
                    "Using the head ability heals its power % of max HP per second for its duration, then stops and cools down; first second "
                    + afterOne.ToString("0.00") + ", total " + total.ToString("0.00"));
            }
            else check(false, "The default player mech's head has Heal (and the mech has health)");

            // Jump on the equipped legs: refused in mid-air (the test mech is floating), without spending the cooldown.
            var legs = equipment.GetEquipped(PartCategory.Legs);
            var movement = player.GetComponent<MechMovementController>();
            check(legs != null && legs.Stats.ability == PartAbility.Jump && legs.Stats.GetStat(StatType.AbilityPower) > 0f
                && movement != null && !movement.IsGrounded && !abilities.Request(PartCategory.Legs, legs)
                && !PartAbilityCooldown.For(legs).IsCoolingDown && !movement.HasRequestedJump,
                "The default legs' Jump needs ground: in the air it is refused and its cooldown isn't spent");
            check(movement != null && !movement.RequestJump(8f), "The movement controller only accepts a requested jump on the ground");

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
