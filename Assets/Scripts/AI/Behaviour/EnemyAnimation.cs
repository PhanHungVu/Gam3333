using UnityEngine;

// Uses the shared authored biped rig without installing player input/movement scripts.
[RequireComponent(typeof(EnemyAgent))]
[DefaultExecutionOrder(80)]
public sealed class EnemyAnimation : MonoBehaviour
{
    [SerializeField] Animator animator;
    [SerializeField] MechAnimationPoseSource poses;
    [SerializeField] MechPartPoseReceiver body, head;
    [SerializeField, Min(0.01f)] float referenceSpeed = 6f;
    EnemyAgent agent;
    public Animator Animator => animator;
    void Awake() { agent = GetComponent<EnemyAgent>(); }
    void OnEnable()
    {
        if (animator == null) return;
        animator.applyRootMotion = false; animator.enabled = true;
        for (int layer = 0; layer < animator.layerCount; layer++) animator.Play("Locomotion", layer, 0f);
    }
    void OnDisable() { if (animator != null) animator.enabled = false; }
    void Update()
    {
        if (animator == null) return;
        float speed = agent.Velocity.magnitude;
        animator.SetFloat("MoveBlend", speed > 0.1f ? 1f : 0f);
        animator.SetFloat("PlaybackSpeed", speed > 0.1f ? Mathf.Clamp(speed / referenceSpeed, 0.35f, 2f) : 1f);
    }
    void LateUpdate()
    {
        if (poses == null || !poses.TryRead(out var bodyPose, out var headPose)) return;
        if (body != null) body.Apply(bodyPose);
        if (head != null) head.Apply(headPose);
    }
}
