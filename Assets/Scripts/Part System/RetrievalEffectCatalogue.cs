using System;
using UnityEngine;

// Stable family keys; shared across physical copies, never keyed by display name or broad subtype.
public enum RetrievalWeaponFamily { Sword, Mace, Cannon, ThrowingAxe, SmallCrossbow, LargeCrossbow }
public enum RetrievalEffectKind { None, ReturnPath, ArrivalBlast, ArrivalBarrage, ArrivalSpeedBuff, PiercingReturnBolt }

[CreateAssetMenu(fileName = "RetrievalEffects", menuName = "Mech/Retrieval Effects")]
public sealed class RetrievalEffectCatalogue : ScriptableObject
{
    [Serializable] public struct Entry
    {
        public PartData part;
        public RetrievalWeaponFamily family;
        public RetrievalEffectKind effect;
        [Min(0)] public float damageMultiplier;
        [Tooltip("Return-path capsule / explosion / piercing bolt radius, or minimum barrage projectile collision radius.")]
        [Min(.01f)] public float hitRadius;
        [Header("Arrival barrage")]
        [Range(1, 32)] public int projectileCount;
        [Tooltip("Height above player feet for the barrage; above ground under the original drop for a piercing bolt.")]
        [Min(0)] public float spawnHeight;
        [Min(0)] public float spawnRadius;
        [Min(.01f)] public float speedMultiplier;
        [Min(.01f)] public float rangeMultiplier;
        [Header("Piercing return bolt")]
        [Min(.01f)] public float boltVisualScale;
        [Header("Arrival speed buff (refresh, never stack)")]
        [Min(0)] public float buffDuration;
        [Min(1)] public float movementMultiplier;
        [Tooltip("Attack/reload speed for newly started weapon actions, not projectile flight speed.")]
        [Min(1)] public float attackCadenceMultiplier;
    }
    public Entry[] entries = Array.Empty<Entry>();
    public bool TryGet(PartData data, out Entry entry)
    {
        if (data != null && entries != null)
        {
            foreach (var candidate in entries)
                if (candidate.part == data) { entry = candidate; return true; }
            // Runtime stat-roll copies can retain the same canonical identity.
            if (!string.IsNullOrEmpty(data.partId)) foreach (var candidate in entries)
                if (candidate.part != null && string.Equals(candidate.part.partId, data.partId, StringComparison.Ordinal))
                { entry = candidate; return true; }
        }
        entry = default; return false;
    }
}
