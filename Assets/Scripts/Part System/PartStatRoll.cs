using System;
using System.Collections.Generic;
using UnityEngine;

// Numeric loot variation only. PartData assets, weapon identity, icons, abilities,
// projectile references and all non-stat configuration remain authored content.
public static class PartStatRoll
{
    public const double Variation = .10d;

    public static PartData Create(PartData definition, int seed)
    {
        if (definition == null) return null;
        var result = UnityEngine.Object.Instantiate(definition);
        result.name = definition.name + " (Loot Stats)";
        result.hideFlags = HideFlags.DontSave;
        result.stats = new List<StatEntry>();
        var random = new System.Random(seed);
        if (definition.stats == null) return result;
        foreach (var entry in definition.stats)
        {
            float value = entry.value;
            if (value != 0 && !float.IsNaN(value) && !float.IsInfinity(value))
            {
                double first = value * (1d - Variation), second = value * (1d + Variation);
                double lower = Math.Min(first, second), upper = Math.Max(first, second);
                double rolled = value * (1d - Variation + random.NextDouble() * Variation * 2d);
                if (entry.type == StatType.Ammo || entry.type == StatType.MagSize)
                {
                    // Discrete counts must stay inside the advertised bounds.
                    // A one-round magazine cannot become zero or two rounds.
                    double minimumCount = Math.Ceiling(lower), maximumCount = Math.Floor(upper);
                    rolled = minimumCount <= maximumCount
                        ? Math.Max(minimumCount, Math.Min(maximumCount, Math.Round(rolled))) : value;
                }
                value = (float)Math.Max(-float.MaxValue, Math.Min(float.MaxValue, rolled));
            }
            result.stats.Add(new StatEntry { type = entry.type, value = value });
        }
        return result;
    }
}
