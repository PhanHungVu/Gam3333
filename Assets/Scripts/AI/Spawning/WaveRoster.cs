using System;
using System.Collections.Generic;

// The enemies one wave has spawned, with what each cost, as plain logic (portable). The spawn director keeps
// one per running wave: its alive count is the wave's own population (for Max Concurrent Enemies), and it
// can pick out members to cull (e.g. left too far behind by the leash) so their cost can be refunded.
public sealed class WaveRoster<T> where T : class
{
    readonly List<T> members = new List<T>();
    readonly List<int> costs = new List<int>();

    public int Count => members.Count;
    public int TotalCost { get { int t = 0; foreach (int c in costs) t += c; return t; } }

    public void Add(T member, int cost) { members.Add(member); costs.Add(Math.Max(0, cost)); }

    // Drops members that are no longer alive (killed, destroyed or switched off).
    public void Prune(Func<T, bool> alive)
    {
        for (int i = members.Count - 1; i >= 0; i--)
            if (members[i] == null || !alive(members[i])) { members.RemoveAt(i); costs.RemoveAt(i); }
    }

    // Removes every member matching the rule and returns them with their costs (to destroy and refund).
    public List<(T member, int cost)> Cull(Func<T, bool> rule)
    {
        var culled = new List<(T, int)>();
        for (int i = members.Count - 1; i >= 0; i--)
        {
            if (members[i] == null || !rule(members[i])) continue;
            culled.Add((members[i], costs[i]));
            members.RemoveAt(i); costs.RemoveAt(i);
        }
        return culled;
    }
}

// The leash rule, as plain logic: a wave member further than the leash (flat distance) from its wave's
// anchor gets culled. A leash of 0 or less is off.
public static class WaveLeash
{
    public static bool Beyond(float dx, float dz, float leash) => leash > 0f && dx * dx + dz * dz > leash * leash;
}

// A wave owner that says where its wave should stay near (e.g. the escort payload). Waves with a Leash
// Distance cull members that stray further than that from here.
public interface IWaveAnchor
{
    UnityEngine.Vector3 WaveAnchor { get; }
}
