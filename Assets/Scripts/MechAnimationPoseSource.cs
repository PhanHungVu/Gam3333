using UnityEngine;

// Module-local rig adapter. Receivers do not need to know bone names or rig family.
[DisallowMultipleComponent]
[RequireComponent(typeof(Part))]
public sealed class MechAnimationPoseSource : MonoBehaviour
{
    [System.Serializable]
    public struct BoneChannel
    {
        public Transform bone;
        public Vector3 restPosition;
        public Quaternion restRotation;

        public Pose Read()
        {
            if (bone == null) return Pose.identity;
            var inverse = Quaternion.Inverse(restRotation);
            return new Pose(inverse * (bone.localPosition - restPosition), inverse * bone.localRotation);
        }
    }

    [SerializeField] Animator animator;
    [SerializeField] BoneChannel body;
    [SerializeField] BoneChannel head;

    public bool TryRead(out Pose bodyPose, out Pose headPose)
    {
        bodyPose = headPose = Pose.identity;
        if (!isActiveAndEnabled || animator == null || !animator.isActiveAndEnabled || !animator.isInitialized) return false;
        bodyPose = body.Read();
        headPose = head.Read();
        return true;
    }
}
