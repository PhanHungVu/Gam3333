using System.Collections.Generic;
using UnityEngine;

// A place enemies can appear (e.g. a tent model). Registers itself while enabled, so the
// SpawnDirector never needs to know how many exist: hand-placed, inside a chunk prefab, or
// placed by the level generator later all work the same. Which points a wave uses is decided
// by whoever starts the wave (e.g. Survival_Zone sends the points inside its chunk).
[DisallowMultipleComponent]
public class SpawnPoint : MonoBehaviour
{
    static readonly List<SpawnPoint> all = new List<SpawnPoint>();
    public static IReadOnlyList<SpawnPoint> All => all;

    protected virtual void OnEnable() => all.Add(this);
    protected virtual void OnDisable() => all.Remove(this);

    // Where the next enemy appears. Returns false when this point can't spawn right now.
    public virtual bool TryGetSpawnPose(out Vector3 position, out Quaternion rotation)
    {
        position = transform.position;
        rotation = transform.rotation;
        return true;
    }

    protected virtual void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.4f, 0.1f);
        Gizmos.DrawWireSphere(transform.position, 0.75f);
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 1.5f);
    }
}
