using System;
using System.Collections.Generic;
using UnityEngine;

public enum EnemyEncounterLayout { SeededArc, ExplicitPositions }

[Serializable]
public sealed class EnemyEncounterGroup
{
    public EnemyBehaviorRole role;
    [Min(0)] public int count = 1;
    [Tooltip("Zero uses the preset distance; otherwise this group's distance from the player.")]
    [Min(0)] public float distance;
}

[Serializable]
public struct EnemyEncounterSpawn
{
    public EnemyBehaviorRole role;
    [Tooltip("Ground-plane offset from the starting player pose: X right, Y forward.")]
    public Vector2 offset;
    public EnemyEncounterSpawn(EnemyBehaviorRole type, Vector2 position) { role = type; offset = position; }
}

// Test composition and placement only; enemy prefabs remain the source of AI/stats.
[CreateAssetMenu(menuName = "Lithomachia/Enemy Encounter Preset")]
public sealed class EnemyEncounterPreset : ScriptableObject
{
    struct Entry { public EnemyBehaviorRole Role; public float Distance, Phase; public int Order; }
    public const int MaximumActors = 128;
    [SerializeField] string displayName;
    [SerializeField] EnemyEncounterLayout layout;
    [SerializeField] int seed = 4711;
    [SerializeField, Min(3)] float distance = 16;
    [SerializeField, Range(30, 360)] float arc = 210;
    [SerializeField] EnemyEncounterGroup[] groups = Array.Empty<EnemyEncounterGroup>();
    [SerializeField] EnemyEncounterSpawn[] explicitSpawns = Array.Empty<EnemyEncounterSpawn>();
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public int Seed => seed;
    public EnemyEncounterLayout Layout => layout;
    public long RequestedCount
    {
        get
        {
            if (layout == EnemyEncounterLayout.ExplicitPositions) return explicitSpawns?.Length ?? 0;
            long count = 0;
            if (groups != null) foreach (var group in groups) if (group != null) count += Math.Max(0, group.count);
            return count;
        }
    }
    public void BuildSpawnPlan(List<EnemyEncounterSpawn> plan)
    {
        plan.Clear();
        if (layout == EnemyEncounterLayout.ExplicitPositions)
        {
            if (explicitSpawns != null)
                for (int i = 0; i < Mathf.Min(explicitSpawns.Length, MaximumActors); i++) plan.Add(explicitSpawns[i]);
            return;
        }
        int total = (int)Math.Min(RequestedCount, MaximumActors), index = 0;
        var random = new System.Random(seed);
        if (groups == null) return;
        var entries = new List<Entry>(total);
        foreach (var group in groups)
        {
            if (group == null) continue;
            for (int n = 0; n < group.count && entries.Count < total; n++)
                entries.Add(new Entry { Role = group.role, Distance = group.distance, Phase = (n + .5f) / group.count, Order = entries.Count });
        }
        // Distribute each role through the arc instead of putting all shooters
        // together at one end. A single-role preset retains the original layout.
        entries.Sort((a, b) => a.Phase != b.Phase ? a.Phase.CompareTo(b.Phase) : a.Order.CompareTo(b.Order));
        foreach (var entry in entries)
        {
            float angle = (-arc * .5f + arc * (index + .5f) / total + (float)(random.NextDouble() - .5) * 3) * Mathf.Deg2Rad;
            float radius = Mathf.Max(3, entry.Distance > 0 ? entry.Distance : distance) + (float)(random.NextDouble() - .5) * 1.2f;
            plan.Add(new EnemyEncounterSpawn(entry.Role, new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * radius)); index++;
        }
    }
}
