using System;
using UnityEngine;
using Object = UnityEngine.Object;

// The ground the AI walks on, when a scene has generated terrain. The level generator registers
// "is this spot a wall (cliff or off the map)?" and "how high is the ground here?", so the AI code
// never depends on the level code. With nothing registered, flat scenes behave exactly as before.
public static class NavTerrain
{
    static Object owner;
    static Func<Vector3, bool> isWall;
    static Func<Vector3, float> groundHeight;

    public static bool Active => owner != null && isWall != null && groundHeight != null;

    public static void Register(Object source, Func<Vector3, bool> wall, Func<Vector3, float> height)
    {
        owner = source; isWall = wall; groundHeight = height;
    }

    public static void Unregister(Object source)
    {
        if (owner != source) return;
        owner = null; isWall = null; groundHeight = null;
    }

    public static bool IsWall(Vector3 position) => Active && isWall(position);

    // Terrain height at this spot, or the fallback when there is no terrain.
    public static float GroundHeight(Vector3 position, float fallback) => Active ? groundHeight(position) : fallback;
}
