using UnityEngine;

// A spawn point that puts each enemy on a ring around its own position (the chunk's centre), just
// outside the chunk's edge, on ground enemies can stand on. An objective (KOTH_Zone) creates one for
// its chunk when the chunk has no spawn points of its own.
public sealed class ChunkRingSpawner : SpawnPoint
{
    [Tooltip("Enemies appear this far from the chunk's centre.")]
    [SerializeField, Min(1f)] float radius = 50f;
    [Tooltip("Random spots on the ring tried before skipping this spawn (e.g. all on cliffs).")]
    [SerializeField, Min(1)] int attempts = 12;
    [Tooltip("Left empty, the scene's NavGrid is used.")]
    [SerializeField] NavGrid grid;

    public float Radius => radius;
    public void Configure(float ringRadius) => radius = Mathf.Max(1f, ringRadius);

    public override bool TryGetSpawnPose(out Vector3 position, out Quaternion rotation)
    {
        if (grid == null) grid = FindAnyObjectByType<NavGrid>();
        return SpawnRing.TryPick(transform.position, radius, attempts, grid, out position, out rotation);
    }

    protected override void OnDrawGizmos() => SpawnRing.DrawGizmo(transform.position, radius);
}
