using UnityEngine;

// Each part owns its rig. Nothing here writes to gameplay or equipment transforms.
[RequireComponent(typeof(Part))]
[DisallowMultipleComponent]
[DefaultExecutionOrder(60)]
public sealed class MechLegAnimator : MonoBehaviour
{
    [SerializeField] Animator animator;
    [SerializeField, Min(0.01f)] float referenceSpeed = 6f;
    [SerializeField, Min(0f)] float blendResponse = 12f;
    static readonly int MoveBlend = Animator.StringToHash("MoveBlend");
    static readonly int PlaybackSpeed = Animator.StringToHash("PlaybackSpeed");
    MechMovementController owner;
    Part part;
    float blend;

    public Animator Animator => animator;
    public float NormalizedPhase => animator != null && animator.isActiveAndEnabled && animator.isInitialized
        ? Mathf.Repeat(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, 1f) : 0f;

    void Awake() { part = GetComponent<Part>(); if (animator != null) animator.applyRootMotion = false; }
    void OnEnable()
    {
        if (part == null) part = GetComponent<Part>();
        Bind(part.EquippedBy != null ? part.EquippedBy.GetComponent<MechMovementController>() : null);
    }
    void OnDisable() => Bind(null);

    public void Bind(MechMovementController movement, float phase = 0f)
    {
        if (owner == movement && animator != null && animator.enabled == (movement != null)) return;
        owner = movement;
        if (animator == null) return;
        animator.applyRootMotion = false;
        if (!gameObject.activeInHierarchy) { animator.enabled = false; return; }
        animator.enabled = true;
        animator.Rebind();
        blend = owner != null && ShouldWalk(owner.AnimationData) ? 1f : 0f;
        ApplyParameters(0f);
        // All masked body regions share the gait phase, including on a moving swap.
        for (int layer = 0; layer < animator.layerCount; layer++)
            animator.Play(Animator.StringToHash(animator.GetLayerName(layer) + ".Locomotion"), layer, Mathf.Repeat(phase, 1f));
        animator.Update(0f);
        if (owner == null) animator.enabled = false;
    }

    void Update() => ApplyParameters(Time.deltaTime);

    public void ApplyParameters(float deltaTime)
    {
        if (animator == null) return;
        var data = owner != null ? owner.AnimationData : default;
        float target = owner != null && owner.isActiveAndEnabled && ShouldWalk(data) ? 1f : 0f;
        if (deltaTime > 0f) blend = Mathf.Lerp(blend, target, 1f - Mathf.Exp(-blendResponse * deltaTime));
        animator.SetFloat(MoveBlend, blend);
        animator.SetFloat(PlaybackSpeed, target > 0f ? Mathf.Clamp(data.Speed / referenceSpeed, 0.35f, 2f) : 1f);
    }

    static bool ShouldWalk(LocomotionAnimationData data) => data.IsGrounded && data.Speed > 0.1f
        && data.State != MechMovementController.MovementState.QuickBoost
        && data.State != MechMovementController.MovementState.AssaultBoost;
}
