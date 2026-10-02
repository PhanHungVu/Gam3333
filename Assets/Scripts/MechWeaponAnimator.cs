using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Owns only the weapon's visual rig. Gameplay admits attacks and owns cooldown/ammo.
[RequireComponent(typeof(Part))]
[DisallowMultipleComponent]
[DefaultExecutionOrder(90)]
public sealed class MechWeaponAnimator : MonoBehaviour
{
    public enum PoseState { Neutral, Fire, Reload }
    [SerializeField] Animator animator;
    [SerializeField] MechWeaponAnimationSet animations;
    [Tooltip("Optional authored attachment bone, e.g. the axe's unanimated Barrel bone.")]
    [SerializeField] Transform placement;
    [Tooltip("The displayed axe/bolt follows the rig while loaded, and hides until reloaded after a shot.")]
    [SerializeField] Renderer[] loadedProjectileRenderers = new Renderer[0];
    Part part;
    MechCombatController owner;
    MechEquipmentCombat equipment;
    EnemyAttack enemyOwner;
    PlayableGraph graph;
    AnimationMixerPlayable mixer;
    readonly AnimationClipPlayable[] clips = new AnimationClipPlayable[3];
    readonly float[] weights = new float[3];
    readonly float[] fromWeights = new float[3];
    float age, neutralTime, fireDuration, reloadDuration, blendAge, previewReadyAt;
    public Animator Animator => animator;
    public MechWeaponAnimationSet Animations => animations;
    public Transform Placement => placement;
    public PoseState CurrentPose { get; private set; }
    public float PreviewCooldownRemaining => Mathf.Max(0f, previewReadyAt - Time.time);
    public bool CanAnimate => isActiveAndEnabled && part != null && part.isEquipped
        && (part.EquippedBy != null && part.EquippedBy.isActiveAndEnabled
            || enemyOwner != null && enemyOwner.isActiveAndEnabled && enemyOwner.OwnsWeapon(this)) && graph.IsValid();
    public bool NeutralPlaying => CanAnimate && CurrentPose == PoseState.Neutral
        && (equipment != null && equipment.isActiveAndEnabled && equipment.IdleWeapon == this
            || enemyOwner != null && enemyOwner.NeutralReady && enemyOwner.Weapon == this);
    // The same clock can drive authored hit/release windows in the next combat step.
    public float NormalizedTime => CurrentPose == PoseState.Fire ? Mathf.Clamp01(age / Mathf.Max(0.01f, fireDuration))
        : CurrentPose == PoseState.Reload ? Mathf.Clamp01((age - fireDuration) / Mathf.Max(0.01f, reloadDuration))
        : animations != null && animations.neutral != null ? Mathf.Repeat(neutralTime / Mathf.Max(0.01f, animations.neutral.length), 1f) : 0f;

    void Awake() => part = GetComponent<Part>();
    void OnEnable()
    {
        RefreshEquipment();
    }

    public void RefreshEquipment()
    {
        if (part == null) part = GetComponent<Part>();
        BindOwner();
        bool hasOwner = part.EquippedBy != null && part.EquippedBy.isActiveAndEnabled
            || enemyOwner != null && enemyOwner.isActiveAndEnabled && enemyOwner.OwnsWeapon(this);
        if (!isActiveAndEnabled || !part.isEquipped || !hasOwner)
        {
            StopGraph();
            return;
        }
        if (graph.IsValid()) return;
        CreateGraph();
    }

    void CreateGraph()
    {
        if (animator == null || animations == null || animations.neutral == null || animations.fire == null) return;
        animator.applyRootMotion = false;
        animator.enabled = true;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        graph = PlayableGraph.Create(name + " Weapon Poses");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        mixer = AnimationMixerPlayable.Create(graph, 3);
        var motions = new[] { animations.neutral, animations.fire, animations.reload };
        for (int i = 0; i < motions.Length; i++)
        {
            weights[i] = fromWeights[i] = i == 0 ? 1f : 0f;
            if (motions[i] == null) continue;
            clips[i] = AnimationClipPlayable.Create(graph, motions[i]);
            clips[i].SetApplyFootIK(false);
            clips[i].SetApplyPlayableIK(false);
            clips[i].SetSpeed(0);
            graph.Connect(clips[i], 0, mixer, i);
            mixer.SetInputWeight(i, weights[i]);
        }
        AnimationPlayableOutput.Create(graph, "Weapon", animator).SetSourcePlayable(mixer);
        graph.Play();
        CurrentPose = PoseState.Neutral;
        RefreshLoadedProjectile();
        age = neutralTime = 0f;
        graph.Evaluate(0f);
    }

    void OnDisable()
    {
        if (owner != null) owner.WeaponAttackStarted -= OnAttack;
        owner = null;
        equipment = null;
        StopGraph();
    }

    void StopGraph()
    {
        if (graph.IsValid())
        {
            // Dropped/disabled parts must not retain a transient firing pose.
            for (int i = 0; i < 3; i++) mixer.SetInputWeight(i, i == 0 ? 1f : 0f);
            clips[0].SetTime(0);
            graph.Evaluate(0f);
            graph.Destroy();
        }
        if (animator != null) animator.enabled = false;
        age = neutralTime = 0f;
        CurrentPose = PoseState.Neutral;
        RefreshLoadedProjectile();
    }

    void Update() => RefreshEquipment();
    void LateUpdate() => Tick(Time.deltaTime);

    void BindOwner()
    {
        enemyOwner = isActiveAndEnabled && part != null && part.isEquipped ? GetComponentInParent<EnemyAttack>() : null;
        var next = isActiveAndEnabled && part != null && part.isEquipped && part.EquippedBy != null ? part.EquippedBy.GetComponent<MechCombatController>() : null;
        if (next == owner) return;
        if (owner != null) owner.WeaponAttackStarted -= OnAttack;
        owner = next;
        equipment = owner != null ? owner.GetComponent<MechEquipmentCombat>() : null;
        if (owner != null) owner.WeaponAttackStarted += OnAttack;
        if (graph.IsValid()) { age = 0f; ChangePose(PoseState.Neutral); }
    }

    void OnAttack(Part attacked)
    {
        if (attacked == part)
        {
            PlayAttack(Mathf.Max(0.01f, part.ReadyAt - Time.time));
            RefreshLoadedProjectile();
        }
    }

    void RefreshLoadedProjectile()
    {
        bool visible = owner == null || part == null || !part.isEquipped
            || part.RemainingAmmo > 0 && part.ReadyAt <= Time.time;
        foreach (var renderer in loadedProjectileRenderers) if (renderer != null) renderer.enabled = visible;
    }

    public bool PlayAttack(float cooldown, float requestedFireDuration = 0f)
    {
        RefreshEquipment();
        if (!CanAnimate) return false;
        equipment?.ResetWeaponIdle();
        cooldown = Mathf.Max(0.01f, cooldown);
        float authoredFire = Mathf.Max(0.01f, animations.fire.length);
        float authoredReload = animations.reload != null ? Mathf.Max(0.01f, animations.reload.length) : 0f;
        // Keep fire at its authored speed when possible. Fit shorter cooldowns,
        // then stretch the reload pose to the remaining actual gameplay interval.
        fireDuration = Mathf.Min(authoredFire, cooldown * authoredFire / (authoredFire + authoredReload));
        if (requestedFireDuration > 0f) fireDuration = Mathf.Min(requestedFireDuration, cooldown);
        reloadDuration = authoredReload > 0f ? cooldown - fireDuration : 0f;
        age = 0f;
        clips[1].SetTime(0f);
        ChangePose(PoseState.Fire);
        Tick(0f);
        return true;
    }

    public bool TryAnimationOnlyAttack()
    {
        if (animations == null || !animations.animationOnly || PreviewCooldownRemaining > 0f) return false;
        float cooldown = part != null && part.data != null ? Mathf.Max(0.01f, part.data.GetStat(StatType.CooldownReload)) : 1f;
        if (!PlayAttack(cooldown)) return false;
        previewReadyAt = Time.time + cooldown;
        return true;
    }

    public void Tick(float deltaTime)
    {
        if (!isActiveAndEnabled || !graph.IsValid()) return;
        if (!CanAnimate) { RefreshEquipment(); return; }
        deltaTime = Mathf.Max(0f, deltaTime);
        neutralTime = NeutralPlaying ? neutralTime + deltaTime : 0f;
        clips[0].SetTime(Mathf.Repeat(neutralTime, Mathf.Max(0.01f, animations.neutral.length)));
        if (CurrentPose != PoseState.Neutral)
        {
            age += deltaTime;
            if (age < fireDuration)
                clips[1].SetTime(age / fireDuration * animations.fire.length);
            else if (reloadDuration > 0f && age < fireDuration + reloadDuration)
            {
                clips[1].SetTime(animations.fire.length);
                ChangePose(PoseState.Reload);
                clips[2].SetTime((age - fireDuration) / reloadDuration * animations.reload.length);
            }
            else ChangePose(PoseState.Neutral);
        }
        blendAge += deltaTime;
        float blend = animations.blendDuration > 0f ? Mathf.Clamp01(blendAge / animations.blendDuration) : 1f;
        for (int i = 0; i < weights.Length; i++)
        {
            weights[i] = Mathf.Lerp(fromWeights[i], i == (int)CurrentPose ? 1f : 0f, blend);
            mixer.SetInputWeight(i, weights[i]);
        }
        graph.Evaluate(0f);
        RefreshLoadedProjectile();
    }

    void ChangePose(PoseState pose)
    {
        if (CurrentPose == pose) return;
        for (int i = 0; i < weights.Length; i++) fromWeights[i] = weights[i];
        CurrentPose = pose;
        blendAge = 0f;
    }
}
