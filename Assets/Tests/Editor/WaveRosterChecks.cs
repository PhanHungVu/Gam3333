using System;
using System.Linq;

// Per-wave rosters, the leash rule and refunds: plain logic behind the escort wave staying near the payload.
public static class WaveRosterChecks
{
    sealed class Member { public bool alive = true; public float x, z; }

    public static void Run(Action<bool, string> check)
    {
        var roster = new WaveRoster<Member>();
        var near = new Member { x = 10f }; var far = new Member { x = 120f }; var dead = new Member { x = 5f, alive = false };
        roster.Add(near, 1); roster.Add(far, 2); roster.Add(dead, 10);
        roster.Prune(m => m.alive);
        check(roster.Count == 2, "A wave counts only its own living enemies");

        var culled = roster.Cull(m => WaveLeash.Beyond(m.x, m.z, 90f));
        check(culled.Count == 1 && culled[0].member == far && culled[0].cost == 2 && roster.Count == 1,
            "Enemies beyond the leash are picked out with their cost, the rest stay");
        check(!WaveLeash.Beyond(500f, 0f, 0f) && WaveLeash.Beyond(0f, 91f, 90f) && !WaveLeash.Beyond(60f, 60f, 90f),
            "The leash measures flat distance and a leash of 0 is off");

        var cycle = new WaveCycle(0.5f, 10f, 20);
        cycle.Spent(5); cycle.Spent(5); cycle.Refund(2);
        bool refunded = cycle.Resource == 12;
        cycle.Refund(100);
        check(refunded && cycle.Resource == 20, "A culled enemy's cost goes back to its wave, never above the total");
    }
}
