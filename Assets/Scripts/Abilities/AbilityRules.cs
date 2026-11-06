using System;
using System.Collections.Generic;

// Damage multipliers that are on while something is active, as plain logic (portable). Each source sets
// its own multiplier (setting again replaces it) and removes it when it ends; they multiply together.
public sealed class DamageModifiers
{
    readonly Dictionary<object, float> multipliers = new Dictionary<object, float>();

    public float Total
    {
        get
        {
            float total = 1f;
            foreach (var m in multipliers.Values) total *= m;
            return total;
        }
    }

    public int Count => multipliers.Count;
    public void Set(object source, float multiplier) { if (source != null) multipliers[source] = Math.Max(0f, multiplier); }
    public void Remove(object source) { if (source != null) multipliers.Remove(source); }
}

// An ability effect that lasts a set time, as plain logic: Start it, Tick it, and it reports when it ends.
public sealed class TimedEffect
{
    public float Remaining { get; private set; }
    public bool Active => Remaining > 0f;

    public void Start(float duration) => Remaining = Math.Max(0f, duration);
    public void Stop() => Remaining = 0f;

    // Returns true on the tick it runs out.
    public bool Tick(float deltaTime)
    {
        if (!Active) return false;
        Remaining -= Math.Max(0f, deltaTime);
        if (Remaining > 0f) return false;
        Remaining = 0f;
        return true;
    }
}
