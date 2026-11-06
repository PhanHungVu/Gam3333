using UnityEngine;

// Presentation data for one accepted health loss. No attacker lifetime or global event dependency.
public readonly struct CombatDamageInfo
{
    public readonly float Amount;
    public readonly Vector3? SourcePosition;
    public CombatDamageInfo(float amount, Vector3? sourcePosition)
    {
        Amount = amount;
        SourcePosition = sourcePosition.HasValue && Finite(sourcePosition.Value) ? sourcePosition : null;
    }
    static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x)
        && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
}
