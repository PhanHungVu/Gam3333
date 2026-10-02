using UnityEngine;

// Each part owns its rig. Nothing here writes to gameplay or equipment transforms.
[RequireComponent(typeof(Part))]
[DisallowMultipleComponent]
[DefaultExecutionOrder(60)]
public sealed class MechLegAnimator : MonoBehaviour
{
    public enum PoseState { Neutral, Locomotion, Boost, BoostStop, Jump }
    [SerializeField] Animator animator;
    [SerializeField, Min(0.01f)] float referenceSpeed = 6f;
    [SerializeField, Min(0f)] float blendResponse = 12f;
    [SerializeField] AnimationClip boostStopClip;
    [SerializeField, Min(0f)] float transitionDuration = 0.08f;
    [SerializeField, Min(0f)] float movingStopDuration = 0.18f;
    static readonly int MoveBlend = Animator.StringToHash("MoveBlend");
    static readonly int PlaybackSpeed = Animator.StringToHash("PlaybackSpeed");
    MechMovementController owner;
    Part part;
    float blend;
    float stopAge;
    bool wasBoosting, hasActionStates;

    public Animator Animator => animator;
    public PoseState CurrentPose { get; private set; }
    public float NormalizedPhase
    {
        get
        {
            if (animator == null || !animator.isActiveAndEnabled || !animator.isInitialized) return 0f;
            float phase = animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0).normalizedTime
                : animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            if (float.IsNaN(phase) || float.IsInfinity(phase)) return 0f;
            return CurrentPose == PoseState.BoostStop || CurrentPose == PoseState.Jump
                ? Mathf.Clamp01(phase) : Mathf.Repeat(phase, 1f);
        }
    }
    float StopDuration => boostStopClip != null ? boostStopClip.length : 0f;

    void Awake() { part = GetComponent<Part>(); if (animator != null) animator.applyRootMotion = false; }
    void OnEnable()
    {
        if (part == null) part = GetComponent<Part>();
        Bind(part.EquippedBy != null ? part.EquippedBy.GetComponent<MechMovementController>() : null);
    }
    void OnDisable() => Bind(null);

    public void Bind(MechMovementController movement, float phase = 0f, PoseState previousPose = PoseState.Neutral)
    {
        if (owner == movement && animator != null && animator.enabled == (movement != null)) return;
        owner = movement;
        if (animator == null) return;
        animator.applyRootMotion = false;
        if (!gameObject.activeInHierarchy) { animator.enabled = false; return; }
        animator.enabled = true;
        animator.Rebind();
        hasActionStates = animator.HasState(0, Animator.StringToHash("Base Layer.Boost"));
        blend = owner != null && ShouldWalk(owner.AnimationData) ? 1f : 0f;
        wasBoosting = owner != null && IsBoost(owner.AnimationData);
        stopAge = 0f;
        CurrentPose = ResolvePose();
        if (previousPose == PoseState.BoostStop && owner != null && owner.isActiveAndEnabled
            && owner.AnimationData.IsGrounded && !wasBoosting && phase < 1f)
        {
            CurrentPose = PoseState.BoostStop;
            stopAge = Mathf.Clamp01(phase) * StopDuration;
        }
        // Do not carry a ground gait's phase into a different action.
        float start = CurrentPose == previousPose ? phase : 0f;
        WriteParameters(0f);
        PlayPose(CurrentPose, start, true);
        animator.Update(0f);
        if (owner == null) animator.enabled = false;
    }

    void Update() => ApplyParameters(Time.deltaTime);

    public void ApplyParameters(float deltaTime)
    {
        if (animator == null || !animator.isActiveAndEnabled) return;
        deltaTime = Mathf.Max(0f, deltaTime);
        bool boosting = owner != null && owner.isActiveAndEnabled && IsBoost(owner.AnimationData);
        if (CurrentPose == PoseState.BoostStop) stopAge += deltaTime;
        PoseState next = ResolvePose();
        if (owner != null && owner.isActiveAndEnabled && owner.AnimationData.IsGrounded && !boosting)
        {
            if (wasBoosting && StopDuration > 0f) { stopAge = 0f; next = PoseState.BoostStop; }
            else if (CurrentPose == PoseState.BoostStop && stopAge < StopDuration
                && !(owner.MoveDirection.sqrMagnitude > 0.01f && stopAge >= movingStopDuration)) next = PoseState.BoostStop;
        }
        wasBoosting = boosting;
        if (next != CurrentPose)
        {
            CurrentPose = next;
            PlayPose(next, 0f, false);
        }
        WriteParameters(deltaTime);
    }

    PoseState ResolvePose()
    {
        if (owner == null || !owner.isActiveAndEnabled) return PoseState.Neutral;
        var data = owner.AnimationData;
        if (IsBoost(data)) return PoseState.Boost;
        if (!data.IsGrounded) return PoseState.Jump;
        return data.Speed > 0.1f ? PoseState.Locomotion : PoseState.Neutral;
    }

    void PlayPose(PoseState pose, float phase, bool immediate)
    {
        // Legacy single-state controllers still work while modules are upgraded.
        string state = hasActionStates ? pose.ToString() : "Locomotion";
        for (int layer = 0; layer < animator.layerCount; layer++)
        {
            int hash = Animator.StringToHash(animator.GetLayerName(layer) + "." + state);
            if (immediate || transitionDuration <= 0f) animator.Play(hash, layer, phase);
            else animator.CrossFadeInFixedTime(hash, transitionDuration, layer, 0f);
        }
    }

    void WriteParameters(float deltaTime)
    {
        var data = owner != null ? owner.AnimationData : default;
        float target = owner != null && owner.isActiveAndEnabled && ShouldWalk(data) ? 1f : 0f;
        if (deltaTime > 0f) blend = Mathf.Lerp(blend, target, 1f - Mathf.Exp(-blendResponse * deltaTime));
        animator.SetFloat(MoveBlend, blend);
        animator.SetFloat(PlaybackSpeed, target > 0f ? Mathf.Clamp(data.Speed / referenceSpeed, 0.35f, 2f) : 1f);
    }

    static bool ShouldWalk(LocomotionAnimationData data) => data.IsGrounded && data.Speed > 0.1f
        && !IsBoost(data);

    static bool IsBoost(LocomotionAnimationData data) => data.State == MechMovementController.MovementState.QuickBoost
        || data.State == MechMovementController.MovementState.AssaultBoost;
}
