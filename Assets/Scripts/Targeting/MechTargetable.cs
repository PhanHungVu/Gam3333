using UnityEngine;

public class MechTargetable : MonoBehaviour
{
    [SerializeField] Transform aimPoint;
    internal Transform PhysicsAim { get; set; }

    public Vector3 AimPosition => AimTransform.position;
    public Transform AimTransform => PhysicsAim != null ? PhysicsAim : aimPoint != null ? aimPoint : transform;
}
