using UnityEngine;

// Presentation only: the existing attack owns shot release, damage, aim and cooldown.
[DisallowMultipleComponent, RequireComponent(typeof(EnemyAgent), typeof(EnemyAttack), typeof(CombatHealth))]
[DefaultExecutionOrder(180)]
public sealed class EnemyFodderAnimation : MonoBehaviour
{
    public enum Pose { Locomotion, ChargeStart, ChargeLoop, Fire, HitFront, HitBack, HitLeft, HitRight }
    [SerializeField] Animator animator;
    [SerializeField, Min(.01f)] float chargeStartSeconds = .3f;
    [SerializeField, Min(.01f)] float fireSeconds = 1.6f;
    [SerializeField, Min(.01f)] float hitSeconds = .33f;
    EnemyAgent agent;
    EnemyAttack attack;
    CombatHealth health;
    int observedReleases;
    float poseAge;
    bool observedWindup;
    public Animator Animator => animator;
    public Pose CurrentPose { get; private set; }
    static readonly string[] States = { "Locomotion", "Fodder Charge Start", "Fodder Charge Loop", "Fodder Fire",
        "Fodder Hit Front", "Fodder Hit Back", "Fodder Hit Left", "Fodder Hit Right" };
    static readonly int MoveX = UnityEngine.Animator.StringToHash("MoveX"), MoveZ = UnityEngine.Animator.StringToHash("MoveZ");
    public static string StateName(Pose pose) => States[(int)pose];
    void Awake() { agent = GetComponent<EnemyAgent>(); attack = GetComponent<EnemyAttack>(); health = GetComponent<CombatHealth>(); }
    void OnEnable()
    {
        observedReleases = attack.Releases; observedWindup = false; poseAge = 0; CurrentPose = Pose.Locomotion;
        health.DamageReceived += OnDamage;
        if (animator != null) { animator.applyRootMotion = false; animator.speed = 1; }
    }
    void OnDisable()
    {
        health.DamageReceived -= OnDamage;
        if (animator != null) animator.speed = 1;
        observedWindup = false; poseAge = 0; CurrentPose = Pose.Locomotion;
    }
    void Play(Pose pose)
    {
        if (animator == null || !animator.isActiveAndEnabled) return;
        int state = UnityEngine.Animator.StringToHash("Base Layer." + StateName(pose));
        if (!animator.HasState(0, state)) return;
        CurrentPose = pose; poseAge = 0;
        animator.CrossFadeInFixedTime(state, .06f, 0, 0);
    }
    void OnDamage(CombatDamageInfo info)
    {
        if (health.IsDead || animator == null || !isActiveAndEnabled) return;
        Vector3 local = info.SourcePosition.HasValue ? transform.InverseTransformDirection(info.SourcePosition.Value - transform.position) : Vector3.forward;
        var pose = Mathf.Abs(local.x) > Mathf.Abs(local.z) ? (local.x < 0 ? Pose.HitLeft : Pose.HitRight) : (local.z < 0 ? Pose.HitBack : Pose.HitFront);
        Play(pose);
    }
    void LateUpdate()
    {
        if (animator == null || !animator.isActiveAndEnabled || health.IsDead) return;
        bool frozen = CombatHitStop.IsFrozen(transform); animator.speed = frozen ? 0 : 1;
        if (frozen) return;
        poseAge += Time.deltaTime;
        var local = transform.InverseTransformDirection(agent.Velocity) / Mathf.Max(.1f, agent.MaxSpeed);
        animator.SetFloat(MoveX, Mathf.Clamp(local.x, -1, 1), .08f, Time.deltaTime);
        animator.SetFloat(MoveZ, Mathf.Clamp(local.z, -1, 1), .08f, Time.deltaTime);
        bool windup = attack.isActiveAndEnabled && attack.IsTelegraphing;
        if (attack.Releases != observedReleases)
        { observedReleases = attack.Releases; Play(Pose.Fire); }
        else if (CurrentPose >= Pose.HitFront && poseAge < hitSeconds) { }
        else if (windup)
        {
            bool charging = CurrentPose == Pose.ChargeStart || CurrentPose == Pose.ChargeLoop;
            if (!observedWindup || !charging) Play(attack.TelegraphProgress < .45f ? Pose.ChargeStart : Pose.ChargeLoop);
            else if (CurrentPose == Pose.ChargeStart && poseAge >= chargeStartSeconds) Play(Pose.ChargeLoop);
        }
        else if (CurrentPose == Pose.ChargeStart || CurrentPose == Pose.ChargeLoop
            || CurrentPose == Pose.Fire && poseAge >= fireSeconds || CurrentPose >= Pose.HitFront && poseAge >= hitSeconds)
            Play(Pose.Locomotion);
        observedWindup = windup;
    }
}
