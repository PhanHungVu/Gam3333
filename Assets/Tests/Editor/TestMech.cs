using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Builds part-based test mechs for the combat checks: test parts with fixed stats, so checks stay
// independent of real part balance. The default weapons match the old built-in loadout's numbers.
public static class TestMech
{
    // Assets the checks load.
    public const string PlayerPath = "Assets/Prefabs/Mech/Base/FullPlayer_mech_base.prefab";
    public const string LocomotionFolder = "Assets/Prefabs/Mech/Locomotion";
    public const string LegsModel = "Assets/Anim/golem2_v2.glb";
    public const string WeaponAnimationModel = "Assets/Anim/golem2_v3.glb";
    public const string HealthHudPath = "Assets/Prefabs/UI/Player Health HUD.prefab";
    public const string CursorPath = "Assets/Prefabs/Mech Cursor Canvas.prefab";
    public const string BoltPath = "Assets/Prefabs/Combat/Projectiles/CrossbowBolt.prefab";
    public const string AxePath = "Assets/Prefabs/Combat/Projectiles/ThrowingAxe.prefab";
    public static readonly string[] WeaponSounds = { "StoneSword_attack", "ThrowingAxe_attack", "Cannon_attack", "Crossbow_attack", "Mace_attack", "CrossbowArm_attack" };
    public static AudioClip Sound(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/SFX/" + name + ".wav");

    static readonly FieldInfo ReadyAtField =
        typeof(Part).GetField("<ReadyAt>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo ReloadEndsAtField = typeof(Part).GetField("reloadEndsAt", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo TickCombatTimers =
        typeof(MechCombat).GetMethod("TickCooldowns", BindingFlags.Instance | BindingFlags.NonPublic);

    // A ranged part. rangeGolems x 4 m = how far its shots fly (full damage all the way).
    // reload is the wait between shots: Fire Rate = 1 / reload, with no magazine (never reloads).
    public static PartData Ranged(string name, PartType type, PartSubtype subtype, float damage,
        int ammo, float reload, float rangeGolems, float speed, float size, float splash = 0f)
    {
        var data = Data(name, type, subtype);
        Stat(data, StatType.Damage, damage); Stat(data, StatType.Ammo, ammo); Stat(data, StatType.FireRate, 1f / reload);
        Stat(data, StatType.Range, rangeGolems);
        Stat(data, StatType.ProjectileSpeed, speed); Stat(data, StatType.ProjectileSize, size);
        if (splash > 0f) Stat(data, StatType.SplashRadius, splash);
        return data;
    }
    // Gives a test weapon a magazine: magazine shots, then reload seconds to refill from its ammo.
    public static PartData WithMagazine(PartData data, int magazine, float reload)
    {
        Stat(data, StatType.MagSize, magazine); Stat(data, StatType.Reload, reload);
        return data;
    }
    public static PartData Axe() => Ranged("Throwing axe", PartType.Arm, PartSubtype.Projectile, 50, 30, 0.5f, 3, 100, 0.18f);
    // A Splash weapon: 100 on a direct hit plus 100 splash, so everything inside its 6 m blast takes 100.
    public static PartData Cannon()
    {
        var data = Ranged("Cannon", PartType.Shoulder, PartSubtype.Splash, 100, 20, 3, 5, 100, 0.3f, splash: 6);
        Stat(data, StatType.SplashDamage, 100);
        return data;
    }
    public static PartData Crossbow() => Ranged("Large crossbow", PartType.Shoulder, PartSubtype.Projectile, 30, 50, 1, 10, 160, 0.07f);

    // A melee part. Range 1 gives the unscaled melee box.
    public static PartData Sword(float damage = 100f, float cooldown = 0.7f)
    {
        var data = Data("Sword", PartType.Arm, PartSubtype.Melee);
        Stat(data, StatType.Damage, damage); Stat(data, StatType.FireRate, 1f / cooldown); Stat(data, StatType.Range, 1f);
        return data;
    }

    // Adds sockets, parts and EquipManager to a player that is still inactive.
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
        return parts;
    }

    // Simulates time passing between attacks: counts down combat's own short timers (melee camera hold,
    // hit flash) by the given seconds, and skips every part's wait and magazine reload so the next attack is allowed.
    public static void Advance(GameObject player, float seconds)
    {
        var combat = player.GetComponent<MechCombat>();
        if (combat != null) TickCombatTimers.Invoke(combat, new object[] { seconds });
        foreach (var part in player.GetComponentsInChildren<Part>(true)) { ReadyAtField.SetValue(part, 0f); ReloadEndsAtField.SetValue(part, 0f); }
    }

    // Stops the player input on a mech so a check can drive its systems directly.
    public static void StopPlayerInput(Component mech)
    {
        var input = mech.GetComponent<MechPlayerInput>();
        if (input != null) input.enabled = false;
    }

    // Reload times are measured against Time.time, so compare them with a small tolerance.
    public static bool Near(float value, float expected) => Mathf.Abs(value - expected) < 0.001f;

    static PartData Data(string name, PartType type, PartSubtype subtype)
    {
        var data = ScriptableObject.CreateInstance<PartData>();
        data.partName = name; data.partType = type; data.subtype = subtype;
        data.stats = new List<StatEntry>();
        return data;
    }
    static void Stat(PartData data, StatType type, float value) => data.stats.Add(new StatEntry { type = type, value = value });
}
