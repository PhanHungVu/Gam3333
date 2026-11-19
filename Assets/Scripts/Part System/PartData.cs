using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Weapon stats: Damage per hit; Ammo = total shots; MagSize = shots before a reload (0 = never reloads);
// FireRate = shots or swings per second; Reload = seconds to refill the magazine; Range in golem lengths
// (projectile travel limit and close-range accuracy reference; there is no separate EffectiveRange stat);
// SplashRadius and SplashDamage for Splash weapons.
// Each stat keeps a fixed number, because part assets store the number: never renumber one, and give
// a new stat the next unused number. Retired, don't reuse: 7 Single Shot, 8 Auto Aim, 9 Max Range Multiplier.
public enum StatType
{
    Health = 0, MoveSpeed = 1,
    Damage = 2, MagSize = 3, Ammo = 4, Reload = 5, Range = 6,
    ProjectileSpeed = 10, ProjectileSize = 11, SplashRadius = 12,
    FireRate = 13, SplashDamage = 14,
    // Accuracy: head precision from 0 to 100%; 100 removes ranged shot spread. Ability Cooldown and Ability Duration in seconds, and Ability Power
    // (what the ability does, e.g. 50 = +50% damage for Damage Amp): each head, torso and legs for its own ability.
    Accuracy = 15, AbilityCooldown = 16, AbilityDuration = 17, AbilityPower = 18
}

// The ability a head, torso or legs part gives when its button is pressed (Ctrl torso, Tab head).
// Keep the numbers fixed (assets store them); add new abilities at the end.
public enum PartAbility { None = 0, DamageAmp = 1, Heal = 2, Jump = 3 }

// How a part attacks, which decides how its damage is checked. None for parts that don't attack
// (head, legs, torso). Melee: a hit box. Projectile: a shot that hits what it touches. Splash: a shot
// that also damages everything within its Splash Radius. Special: a part-owned action such as blocking.
public enum PartSubtype { None, Projectile, Splash, Melee, Special }


[System.Serializable]
public struct StatEntry
{
    public StatType type;
    public float value;
}

[CreateAssetMenu(fileName = "PartData", menuName = "Mech/Part")]
public class PartData : ScriptableObject
{
    public string partId;
    public string partName;
    [TextArea] public string description;
    [Tooltip("What this part is. Decides which sockets it fits; the socket decides the side.")]
    public PartType partType;
    [Tooltip("Keep this part in its own socket type even when a test rules config allows mixed types.")]
    public bool restrictToPartType;
    [Tooltip("How the part attacks.")]
    public PartSubtype subtype;
    [Tooltip("The part's own picture (e.g. Sprites/PartIcons): shown for weapons in the weapons HUD, and wherever a part is shown.")]
    public Sprite icon;
    [Tooltip("Head, torso and legs: the picture of this part's ability (e.g. Sprites/AbilityIcons), shown in the abilities HUD.")]
    public Sprite abilityIcon;
    [Tooltip("Head, torso and legs: the ability this part gives (its Ability Cooldown, Duration and Power stats tune it).")]
    public PartAbility ability;
    [Tooltip("Head, torso and legs: the effect shown on the mech while its ability is active (e.g. VFX/Abilities/HealVFX). Played by MechAbilityVfx.")]
    public GameObject abilityVfx;
    public List<StatEntry> stats;
    [Header("Combat (Range stat is in golem lengths; metres per golem is in MechRulesConfig)")]
    [Tooltip("Optional projectile prefab. Contains visual meshes and MechProjectile, with no physics colliders.")]
    public GameObject projectilePrefab;
    [Tooltip("Normal shots pass through damageable opponents once each. Solid scenery and friendly bodies still stop them.")]
    public bool piercingProjectile;
    [Tooltip("Maximum distinct targets per piercing shot; 0 means unlimited within its normal range.")]
    [Min(0)] public int maximumPiercedTargets;
    [Tooltip("Projectile colour (the plain sphere used when no projectile prefab is set, and its trail).")]
    public Color projectileColor = Color.white;

    public float GetStat(StatType type)
    {
        if (stats == null) return 0f;
        foreach (var s in stats)
            if (s.type == type) return s.value;
        return 0f;
    }


    // Seconds between shots or swings: 1 / Fire Rate (1 s if no Fire Rate is set).
    public float ShotInterval { get { float rate = GetStat(StatType.FireRate); return rate > 0f ? 1f / rate : 1f; } }
    // Shots per magazine; 0 means the weapon never needs reloading.
    public int MagazineSize => Mathf.Max(0, Mathf.RoundToInt(GetStat(StatType.MagSize)));

#if UNITY_EDITOR
    static bool idValidationQueued, validatingIds;

    void OnValidate()
    {
        // Loading another part from OnValidate can recursively validate its dependencies.
        // Wait until prefab/import deserialization has finished, and coalesce the checks.
        if (string.IsNullOrEmpty(partId) || idValidationQueued || validatingIds) return;
        idValidationQueued = true;
        EditorApplication.delayCall += ValidatePartIds;
    }

    static void ValidatePartIds()
    {
        idValidationQueued = false;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            idValidationQueued = true;
            EditorApplication.delayCall += ValidatePartIds;
            return;
        }
        validatingIds = true;
        try
        {
            var ids = new Dictionary<string, PartData>();
            foreach (var guid in AssetDatabase.FindAssets("t:PartData"))
            {
                var part = AssetDatabase.LoadAssetAtPath<PartData>(AssetDatabase.GUIDToAssetPath(guid));
                if (part == null || string.IsNullOrEmpty(part.partId)) continue;
                if (ids.TryGetValue(part.partId, out var other))
                    Debug.LogWarning($"Duplicate partId '{part.partId}' found on {other.name} and {part.name}", part);
                else ids.Add(part.partId, part);
            }
        }
        finally { validatingIds = false; }
    }
#endif
}
