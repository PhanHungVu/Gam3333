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
        return SpawnRing.TryPick(player.position, radius, attempts, grid, out position, out rotation);
    }

    // Keeps the object on the player so its gizmo shows the circle where they are.
    void LateUpdate()
    {
        var player = Target;
        if (player != null) transform.position = player.position;
    }

    protected override void OnDrawGizmos() => SpawnRing.DrawGizmo(target != null ? target.position : transform.position, radius);
}
