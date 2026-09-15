using UnityEngine;

public class MechTargetable : MonoBehaviour
{
    [SerializeField] Transform aimPoint;

    public Vector3 AimPosition => aimPoint != null ? aimPoint.position : transform.position;
    public Transform AimTransform => aimPoint != null ? aimPoint : transform;
}
