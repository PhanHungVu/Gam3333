using UnityEngine;

// Runs after Animator evaluation and existing boost lean. Equipment still owns attachment.
[RequireComponent(typeof(EquipManager))]
[DisallowMultipleComponent]
[DefaultExecutionOrder(80)]
public sealed class MechUpperBodyAnimation : MonoBehaviour
{
    [SerializeField, Min(0f)] float swapBlendDuration = 0.12f;
    EquipManager equipment;
    MechAnimationPoseSource source;
    MechPartPoseReceiver body, head;
    Pose bodyPose = Pose.identity, headPose = Pose.identity;
    Pose fromBody = Pose.identity, fromHead = Pose.identity;
    float blendAge;
    bool hadPose;

    void Awake() => equipment = GetComponent<EquipManager>();
    void OnEnable() { equipment.EquipmentChanged += Refresh; Refresh(); }
    void OnDisable()
    {
        equipment.EquipmentChanged -= Refresh;
        if (body != null) body.Restore();
        if (head != null) head.Restore();
        bodyPose = headPose = Pose.identity;
        body = head = null;
        source = null;
        hadPose = false;
    }

    void Refresh()
    {
        var legs = equipment.GetEquipped(PartCategory.Legs);
        var nextSource = legs != null ? legs.GetComponent<MechAnimationPoseSource>() : null;
        if (source != nextSource)
        {
            fromBody = bodyPose; fromHead = headPose; blendAge = 0f;
            source = nextSource;
        }
        BindReceiver(ref body, PartCategory.Torso);
        BindReceiver(ref head, PartCategory.Head);
    }

    void BindReceiver(ref MechPartPoseReceiver current, PartCategory category)
    {
        var part = equipment.GetEquipped(category);
        var next = part != null ? part.GetComponent<MechPartPoseReceiver>() : null;
        if (current == next) return;
        if (current != null) current.Restore();
        current = next;
    }

    void LateUpdate() => Evaluate(Time.deltaTime);

    public void Evaluate(float deltaTime)
    {
        if (!isActiveAndEnabled) return;
        Pose nextBody = Pose.identity, nextHead = Pose.identity;
        bool hasPose = source != null && source.TryRead(out nextBody, out nextHead);
        if (hasPose != hadPose)
        {
            fromBody = bodyPose; fromHead = headPose; blendAge = 0f; hadPose = hasPose;
        }
        blendAge += Mathf.Max(0f, deltaTime);
        float blend = swapBlendDuration > 0f ? Mathf.Clamp01(blendAge / swapBlendDuration) : 1f;
        bodyPose = Blend(fromBody, nextBody, blend);
        headPose = Blend(fromHead, nextHead, blend);
        if (body != null) body.Apply(bodyPose);
        if (head != null) head.Apply(headPose);
    }

    static Pose Blend(Pose a, Pose b, float t) => new Pose(Vector3.Lerp(a.position, b.position, t), Quaternion.Slerp(a.rotation, b.rotation, t));
}
