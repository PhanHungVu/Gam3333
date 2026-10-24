using UnityEngine;

// Applies a rigid pose around an authored pivot without moving the Part root.
// Torso targets include its mesh and upper sockets, never the locomotion socket.
[RequireComponent(typeof(Part))]
[DisallowMultipleComponent]
public sealed class MechPartPoseReceiver : MonoBehaviour
{
    [SerializeField] Vector3 pivotPosition;
    [SerializeField] Quaternion pivotRotation = Quaternion.identity;
    [SerializeField] Transform[] targets = new Transform[0];
    [SerializeField, Range(0f, 1f)] float weight = 1f;
    Vector3[] restPositions;
    Quaternion[] restRotations;

    public float Weight { get => weight; set => weight = Mathf.Clamp01(value); }

    void Awake() => CaptureRest();
    void OnDisable() => Restore();

    void CaptureRest()
    {
        if (restPositions != null) return;
        restPositions = new Vector3[targets.Length];
        restRotations = new Quaternion[targets.Length];
        for (int i = 0; i < targets.Length; i++)
        {
            // Only direct children are supported: prevents ancestor/descendant double transforms.
            if (targets[i] == null || targets[i].parent != transform) continue;
            restPositions[i] = targets[i].localPosition;
            restRotations[i] = targets[i].localRotation;
        }
    }

    public void Apply(Pose pose)
    {
        if (!isActiveAndEnabled) return;
        CaptureRest();
        Quaternion rotation = pivotRotation * Quaternion.Slerp(Quaternion.identity, pose.rotation, weight)
            * Quaternion.Inverse(pivotRotation);
        Vector3 translation = pivotRotation * (pose.position * weight);
        for (int i = 0; i < targets.Length; i++)
        {
            var target = targets[i];
            if (target == null || target.parent != transform) continue;
            target.localPosition = pivotPosition + translation + rotation * (restPositions[i] - pivotPosition);
            target.localRotation = rotation * restRotations[i];
        }
    }

    public void Restore()
    {
        if (restPositions == null) return;
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null || targets[i].parent != transform) continue;
            targets[i].localPosition = restPositions[i];
            targets[i].localRotation = restRotations[i];
        }
    }
}
