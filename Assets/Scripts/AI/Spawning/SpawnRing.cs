using UnityEngine;

// Picks a spot on a circle around a centre, on ground enemies can stand on (an open NavGrid cell;
// outside the grid counts as blocked, so nothing appears off the map), facing the centre.
// Shared by the Roaming Spawner (centred on the player) and the Chunk Ring Spawner (centred on a chunk).
public static class SpawnRing
{
    public static bool TryPick(Vector3 centre, float radius, int attempts, NavGrid grid, out Vector3 position, out Quaternion rotation)
    {
        for (int i = 0; i < Mathf.Max(1, attempts); i++)
        {
            float angle = Random.value * Mathf.PI * 2f;
            Vector3 candidate = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
            if (grid != null && grid.IsBlockedWorld(candidate)) continue;
            position = candidate;
            Vector3 toCentre = centre - candidate; toCentre.y = 0f;
            rotation = Quaternion.LookRotation(toCentre.normalized, Vector3.up);
            return true;
        }
        position = default; rotation = Quaternion.identity;
        return false;
    }

    public static void DrawGizmo(Vector3 centre, float radius)
    {
        Gizmos.color = new Color(1f, 0.4f, 0.1f);
        const int segments = 64;
        Vector3 previous = centre + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 next = centre + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }
}
