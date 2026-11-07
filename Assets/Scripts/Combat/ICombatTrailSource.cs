using System;
using UnityEngine;

// Presentation only: independent projectile implementations share the pooled lingering trail.
public interface ICombatTrailSource
{
    Transform TrailTransform { get; }
    bool IsLive { get; }
    event Action<Vector3> Finished;
}
