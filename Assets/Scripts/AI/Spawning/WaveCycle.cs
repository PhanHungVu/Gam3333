using System;
using System.Collections.Generic;

// A wave's spend-and-refill clock as a plain class (no Unity types), so it ports to the custom engine.
// Tick says when it's time to try a spawn; the caller then reports Spent (an enemy appeared),
// Skipped (no spawn point free this time) or OutOfResource (nothing affordable).
public sealed class WaveCycle
{
    public float SpawnInterval { get; }
    public float TimerReset { get; }
    public int ResourceTotal { get; private set; }
    readonly int baseResourceTotal;
    public int Resource { get; private set; }
    public bool Refilling { get; private set; }
    public float ResetRemaining { get; private set; }
    float untilNextSpawn;

    public WaveCycle(float spawnInterval, float timerReset, int resourceTotal)
    {
        SpawnInterval = Math.Max(0.01f, spawnInterval);
        TimerReset = Math.Max(0f, timerReset);
        ResourceTotal = baseResourceTotal = Math.Max(1, resourceTotal);
        Restart();
    }

    // Scales the wave's resource (e.g. 1.25 = 25% more enemies) from its next refill on.
    public void ScaleTotal(float scale) => ResourceTotal = Math.Max(1, (int)Math.Round(baseResourceTotal * Math.Max(0f, scale)));

    // Full resource, first spawn straight away.
    public void Restart()
    {
        Resource = ResourceTotal;
        Refilling = false;
        ResetRemaining = 0f;
        untilNextSpawn = 0f;
    }

    // Advances time. Returns true when it's time to try spawning one enemy.
    public bool Tick(float deltaTime)
    {
        deltaTime = Math.Max(0f, deltaTime);
        if (Refilling)
        {
            ResetRemaining -= deltaTime;
            if (ResetRemaining > 0f) return false;
            Resource = ResourceTotal;
            Refilling = false;
            untilNextSpawn = 0f;
        }
        untilNextSpawn -= deltaTime;
        return untilNextSpawn <= 0f;
    }

    public void Spent(int cost)
    {
        Resource = Math.Max(0, Resource - Math.Max(0, cost));
        untilNextSpawn = SpawnInterval;
    }

    public void Skipped() => untilNextSpawn = SpawnInterval;

    // Gives back a culled member's cost (never above the total), so it can be spent again.
    public void Refund(int cost) => Resource = Math.Min(ResourceTotal, Resource + Math.Max(0, cost));

    public void OutOfResource()
    {
        Refilling = true;
        ResetRemaining = TimerReset;
    }

    // While refilling, shortens the wait to at most this many seconds (e.g. the wave's enemies were all
    // cleared early). Never lengthens it, and does nothing outside a refill.
    public void ShortenRefill(float seconds)
    {
        if (Refilling) ResetRemaining = Math.Min(ResetRemaining, Math.Max(0f, seconds));
    }

    // Weighted pick among the entries the resource can still pay for; -1 when none can be afforded.
    // random01 is a number from 0 up to (not including) 1.
    public static int Pick(IReadOnlyList<int> costs, IReadOnlyList<float> weights, int resource, float random01)
    {
        float total = 0f;
        for (int i = 0; i < costs.Count; i++)
            if (Affordable(costs[i], weights[i], resource)) total += weights[i];
        if (total <= 0f) return -1;
        float roll = Math.Max(0f, Math.Min(random01, 0.999999f)) * total;
        for (int i = 0; i < costs.Count; i++)
        {
            if (!Affordable(costs[i], weights[i], resource)) continue;
            if (roll < weights[i]) return i;
            roll -= weights[i];
        }
        return -1;
    }

    static bool Affordable(int cost, float weight, int resource) => cost > 0 && cost <= resource && weight > 0f;
}
