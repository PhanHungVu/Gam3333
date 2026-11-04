using UnityEngine;

// Execution contract for bespoke attacks. Graph nodes explicitly order windup,
// execution, movement and recovery. Damage stays in the shared CombatBlast rules.
[RequireComponent(typeof(EnemyAgent), typeof(CombatHealth), typeof(EnemyThreatMarker))]
public abstract class EnemyGraphAttack : MonoBehaviour
{
    public enum Stage { Idle, Windup, Attack, Pull, Recovery, Orbit, Grounded, OrbitLift }
    [SerializeField, Min(0.1f)] protected float windupDuration = 0.9f;
    [SerializeField, Min(0.1f)] protected float cooldown = 3.5f;
    [SerializeField, Min(0.1f)] protected float recoveryDuration = 0.8f;
    [SerializeField, Min(0)] protected float damage = 18;
    [Header("Optional authored audio (empty by default)")]
    [SerializeField] AudioClip windupSound, impactSound;
    protected EnemyAgent agent;
    protected EnemyThreatMarker marker;
    protected readonly CombatBlast blast = new CombatBlast();
    protected float age, readyAt;
    protected Vector3 committedTarget;
    AudioSource audioSource;
    public Stage CurrentStage { get; protected set; }
    public bool IsExecuting => CurrentStage == Stage.Windup || CurrentStage == Stage.Attack || CurrentStage == Stage.Pull
        || CurrentStage == Stage.Orbit || CurrentStage == Stage.Grounded || CurrentStage == Stage.OrbitLift;
    public float CooldownRemaining => Mathf.Max(0, readyAt - Time.time);
    public float Damage => damage;
    public bool RecoveryPoseComplete => CurrentStage == Stage.Recovery && age >= recoveryDuration;
    public virtual bool TargetInReach => agent.Target != null && agent.DistanceToTarget <= agent.AttackRange + 0.5f
        && Vector3.Angle(transform.forward, Steering.Flat(agent.TargetPosition - transform.position)) <= 60;
    protected virtual void Awake() { agent = GetComponent<EnemyAgent>(); marker = GetComponent<EnemyThreatMarker>(); }
    public virtual bool BeginWindup()
    {
        if (CooldownRemaining > 0 || !TargetInReach) return false;
        committedTarget = agent.TargetPosition; age = 0; CurrentStage = Stage.Windup; readyAt = Time.time + cooldown;
        Play(windupSound); return true;
    }
    public abstract bool TickWindup(float dt);
    public abstract void BeginAttack();
    public abstract bool TickAttack(float dt);
    public virtual void BeginPull() { }
    public virtual bool TickPull(float dt) => true;
    public virtual bool Recover(float dt)
    {
        if (CurrentStage != Stage.Recovery) { CurrentStage = Stage.Recovery; age = 0; marker.Hide(); }
        age += dt;
        if (age < recoveryDuration || CooldownRemaining > 0) return false;
        CurrentStage = Stage.Idle; return true;
    }
    protected void ImpactSound() => Play(impactSound);
    void Play(AudioClip clip)
    {
        if (clip == null) return;
        if (audioSource == null) { audioSource = gameObject.AddComponent<AudioSource>(); audioSource.playOnAwake = false; audioSource.spatialBlend = 1; }
        audioSource.PlayOneShot(clip);
    }
    public virtual void Cancel() { CurrentStage = Stage.Idle; age = 0; if (marker != null) marker.Hide(); if (audioSource != null) audioSource.Stop(); }
    protected virtual void OnDisable() => Cancel();
}
