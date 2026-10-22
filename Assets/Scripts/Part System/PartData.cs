using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public enum StatType
{
    Health, MoveSpeed,
    Damage, MagSize, Ammo, CooldownReload, Range, SingleShot, AutoAim,
    // Appended so stats already saved in part assets keep their meaning.
    MaxRangeMultiplier, ProjectileSpeed, ProjectileSize, SplashRadius, SpiralBarrage
}

// How a part attacks. None for parts that don't attack (head, legs, torso, ability parts).
public enum PartSubtype { None, Projectile, Explosive, Melee }

public enum MechPartWeapon { None, Sword, ThrowingAxe, Cannon, Crossbow, ArmCrossbow, Mace }
public enum MechPartAbilityKind { None, ResonancePulse, ZigzagBarrage, BloomMissile }

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
    [Header("Combat integration (Range stat is in golem lengths)")]
    public MechPartWeapon weapon;
    public MechPartAbilityKind ability;
    [Tooltip("Optional projectile prefab. Contains visual meshes and MechProjectile, with no physics colliders.")]
    public GameObject projectilePrefab;
    [Tooltip("Projectile colour (the plain sphere used when no projectile prefab is set, and its trail).")]
    public Color projectileColor = Color.white;
    [Min(0.01f)] public float metresPerGolem = 4f;
    [Min(1f)] public float maximumRangeMultiplier = 1.5f;
    [Min(0f)] public float cannonExplosionRadius = 6f;

    public float GetStat(StatType type)
    {
        if (stats == null) return 0f;
        foreach (var s in stats)
            if (s.type == type) return s.value;
        return 0f;
    }

    public bool GetBool(StatType type) => GetStat(type) > 0.5f;

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
