using System;
using UnityEngine;

// Append entries so existing serialized slots keep their meaning.
public enum MechWeaponSlot { ThrowingAxe, Cannon, Crossbow, ArmCrossbow }

[Serializable]
public class MechWeaponStats
{
    public string displayName;
    [Min(0)] public float damage;
    [Min(1)] public int ammunition;
    [Min(0.01f)] public float reloadSeconds;
    [Min(0.01f)] public float effectiveRange;
    [Min(0.01f)] public float maximumRange;
    [Min(0.01f)] public float projectileSpeed;
    [Min(0.01f)] public float projectileRadius;
    [Min(0)] public float explosionRadius;
    public Color color;
    public MechWeaponStats(string name, float damage, int ammo, float reload, float effective,
        float maximum, float speed, float radius, float blast, Color color)
    {
        displayName = name; this.damage = damage; ammunition = ammo; reloadSeconds = reload;
        effectiveRange = effective; maximumRange = maximum; projectileSpeed = speed;
        projectileRadius = radius; explosionRadius = blast; this.color = color;
    }
}
