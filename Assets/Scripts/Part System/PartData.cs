using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Weapon stats: Damage per hit; Ammo = total shots; MagSize = shots before a reload (0 = never reloads);
// FireRate = shots or swings per second; Reload = seconds to refill the magazine; Range in golem lengths;
// SplashRadius and SplashDamage for Splash weapons.
public enum StatType
{
    Health, MoveSpeed,
    Damage, MagSize, Ammo, Reload, Range, SingleShot, AutoAim,
    // Appended so stats already saved in part assets keep their meaning.
    MaxRangeMultiplier, ProjectileSpeed, ProjectileSize, SplashRadius,
    FireRate, SplashDamage
}

// How a part attacks, which decides how its damage is checked. None for parts that don't attack
// (head, legs, torso). Melee: a hit box. Projectile: a shot that hits what it touches. Splash: a shot
// that also damages everything within its Splash Radius.
public enum PartSubtype { None, Projectile, Splash, Melee }


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
    [Tooltip("How the part attacks.")]
    public PartSubtype subtype;
    public List<StatEntry> stats;
    [Header("Combat (Range stat is in golem lengths; metres per golem is in MechRulesConfig)")]
    [Tooltip("Optional projectile prefab. Contains visual meshes and MechProjectile, with no physics colliders.")]
    public GameObject projectilePrefab;
    [Tooltip("Projectile colour (the plain sphere used when no projectile prefab is set, and its trail).")]
    public Color projectileColor = Color.white;

    public float GetStat(StatType type)
    {
        if (stats == null) return 0f;
        foreach (var s in stats)
            if (s.type == type) return s.value;
        return 0f;
    }

    public bool GetBool(StatType type) => GetStat(type) > 0.5f;

    // Seconds between shots or swings: 1 / Fire Rate (1 s if no Fire Rate is set).
    public float ShotInterval { get { float rate = GetStat(StatType.FireRate); return rate > 0f ? 1f / rate : 1f; } }
    // Shots per magazine; 0 means the weapon never needs reloading.
    public int MagazineSize => Mathf.Max(0, Mathf.RoundToInt(GetStat(StatType.MagSize)));

#if UNITY_EDITOR
    void OnValidate()
    {
        if (string.IsNullOrEmpty(partId)) return;

        string[] guids = AssetDatabase.FindAssets("t:PartData");
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var other = AssetDatabase.LoadAssetAtPath<PartData>(path);
            if (other != null && other != this && other.partId == partId)
            {
                Debug.LogWarning($"Duplicate partId '{partId}' found on {other.name} and {name}", this);
            }
        }
    }
#endif
}
