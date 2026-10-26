using UnityEngine;

// The spawn point that follows the player: each enemy appears on a circle Radius away from them,
// on ground enemies can stand on (an open NavGrid cell; outside the grid counts as blocked, so
// nothing appears off the map). The SpawnDirector runs the Roaming wave on it. One per scene.
public sealed class RoamingSpawner : SpawnPoint
{
    [Tooltip("Enemies appear this far from the player, on a circle around them.")]
    [SerializeField, Min(1f)] float radius = 30f;
    [Tooltip("Random spots on the circle tried before skipping this spawn (e.g. all behind walls).")]
    [SerializeField, Min(1)] int attempts = 12;
    [Tooltip("Left empty, the object tagged Player is followed.")]
    [SerializeField] Transform target;
    [Tooltip("Left empty, the scene's NavGrid is used.")]
    [SerializeField] NavGrid grid;

    float nextSearch;

    public float Radius => radius;

    public Transform Target
    {
        get
        {
            if (target == null && Time.unscaledTime >= nextSearch)
            {
                nextSearch = Time.unscaledTime + 0.5f;
                var found = GameObject.FindWithTag("Player");
                if (found != null) target = found.transform;
            }
            return target;
        }
    }

    public override bool TryGetSpawnPose(out Vector3 position, out Quaternion rotation)
    {
        position = default; rotation = Quaternion.identity;
        var player = Target;
        if (player == null) return false;
        if (grid == null) grid = FindAnyObjectByType<NavGrid>();
        Vector3 centre = player.position;
        for (int i = 0; i < attempts; i++)
        {
            float angle = Random.value * Mathf.PI * 2f;
            Vector3 candidate = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
            if (grid != null && grid.IsBlockedWorld(candidate)) continue;
            position = candidate;
            Vector3 toPlayer = centre - candidate; toPlayer.y = 0f;
            rotation = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
            return true;
        }
        return false;
    }

    // Keeps the object on the player so its gizmo shows the circle where they are.
    void LateUpdate()
    {
        var player = Target;
        if (player != null) transform.position = player.position;
    }

    protected override void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.4f, 0.1f);
        Vector3 centre = target != null ? target.position : transform.position;
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
