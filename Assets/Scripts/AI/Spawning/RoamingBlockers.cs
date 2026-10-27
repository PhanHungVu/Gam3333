using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

// Areas where roaming spawns pause while the player is inside them (objective chunks register here).
// The areas register themselves, so spawning code never depends on the level code.
public static class RoamingBlockers
{
    static readonly Dictionary<Object, Func<Vector3, bool>> areas = new Dictionary<Object, Func<Vector3, bool>>();

    public static void Register(Object owner, Func<Vector3, bool> contains)
    {
        if (owner != null && contains != null) areas[owner] = contains;
    }

    public static void Unregister(Object owner)
    {
        if (owner != null) areas.Remove(owner);
    }

    public static bool Contains(Vector3 position)
    {
        foreach (var contains in areas.Values)
            if (contains(position)) return true;
        return false;
    }
}
