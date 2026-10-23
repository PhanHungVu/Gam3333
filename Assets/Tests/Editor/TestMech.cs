using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// Builds part-based test mechs for the combat checks: test parts with fixed stats, so checks stay
// independent of real part balance. The default weapons match the old built-in loadout's numbers.
public static class TestMech
{
    static readonly FieldInfo ReadyAtField =
        typeof(Part).GetField("<ReadyAt>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo TickCombatTimers =
        typeof(MechCombatController).GetMethod("TickCooldowns", BindingFlags.Instance | BindingFlags.NonPublic);

    // A ranged part. rangeGolems x 4 m = effective range; maxRange x effective = maximum range.
    public static PartData Ranged(string name, PartType type, PartSubtype subtype, MechPartWeapon weapon, float damage,
        int ammo, float reload, float rangeGolems, float speed, float size, float splash = 0f, float maxRange = 1.5f)
    {
        var data = Data(name, type, subtype, weapon);
        data.maximumRangeMultiplier = maxRange;
        data.cannonExplosionRadius = splash;
        Stat(data, StatType.Damage, damage); Stat(data, StatType.Ammo, ammo); Stat(data, StatType.CooldownReload, reload);
        Stat(data, StatType.Range, rangeGolems); Stat(data, StatType.MaxRangeMultiplier, maxRange);
        Stat(data, StatType.ProjectileSpeed, speed); Stat(data, StatType.ProjectileSize, size);
        if (splash > 0f) Stat(data, StatType.SplashRadius, splash);
        return data;
    }
    public static PartData Axe() => Ranged("Throwing axe", PartType.Arm, PartSubtype.Projectile, MechPartWeapon.ThrowingAxe, 50, 30, 0.5f, 3, 100, 0.18f);
    public static PartData Cannon() => Ranged("Cannon", PartType.Shoulder, PartSubtype.Explosive, MechPartWeapon.Cannon, 100, 20, 3, 5, 100, 0.3f, splash: 6);
    public static PartData Crossbow() => Ranged("Large crossbow", PartType.Shoulder, PartSubtype.Projectile, MechPartWeapon.Crossbow, 30, 50, 1, 10, 160, 0.07f);

    // A melee part. Range 1 gives the unscaled melee box.
    public static PartData Sword(float damage = 100f, float cooldown = 0.7f)
    {
        var data = Data("Sword", PartType.Arm, PartSubtype.Melee, MechPartWeapon.Sword);
        Stat(data, StatType.Damage, damage); Stat(data, StatType.CooldownReload, cooldown); Stat(data, StatType.Range, 1f);
        return data;
    }

    // Adds sockets, parts, EquipManager and the combat bridge to a player that is still inactive.
    // The caller activates the player afterwards, which equips the parts as on the real prefab.
    public static Part[] Equip(GameObject inactivePlayer, params (PartCategory socket, PartData data)[] loadout)
    {
        Debug.Assert(!inactivePlayer.activeSelf, "TestMech.Equip expects an inactive player");
        var parts = new Part[loadout.Length];
        for (int i = 0; i < loadout.Length; i++)
        {
            var socket = new GameObject(loadout[i].socket + " Socket");
            socket.transform.SetParent(inactivePlayer.transform, false);
            socket.AddComponent<Socket>().socketType = loadout[i].socket;
            var part = new GameObject(loadout[i].data.partName).AddComponent<Part>();
            part.transform.SetParent(socket.transform, false);
            part.data = loadout[i].data;
            part.isStartingPart = true;
            parts[i] = part;
        }
        if (inactivePlayer.GetComponent<EquipManager>() == null) inactivePlayer.AddComponent<EquipManager>();
        if (inactivePlayer.GetComponent<MechEquipmentCombat>() == null) inactivePlayer.AddComponent<MechEquipmentCombat>();
        return parts;
    }

    // Simulates time passing between attacks: counts down combat's own short timers (melee camera hold,
    // hit flash) by the given seconds, and skips every part's reload so the next attack is allowed.
    public static void Advance(GameObject player, float seconds)
    {
        var combat = player.GetComponent<MechCombatController>();
        if (combat != null) TickCombatTimers.Invoke(combat, new object[] { seconds });
        foreach (var part in player.GetComponentsInChildren<Part>(true)) ReadyAtField.SetValue(part, 0f);
    }

    // Reload times are measured against Time.time, so compare them with a small tolerance.
    public static bool Near(float value, float expected) => Mathf.Abs(value - expected) < 0.001f;

    static PartData Data(string name, PartType type, PartSubtype subtype, MechPartWeapon weapon)
    {
        var data = ScriptableObject.CreateInstance<PartData>();
        data.partName = name; data.partType = type; data.subtype = subtype; data.weapon = weapon;
        data.stats = new List<StatEntry>();
        return data;
    }
    static void Stat(PartData data, StatType type, float value) => data.stats.Add(new StatEntry { type = type, value = value });
}
