using UnityEngine;

// Optional participation only. The Behavior Graph still decides when to move or attack.
[DisallowMultipleComponent, RequireComponent(typeof(EnemyGraphController))]
public sealed class EnemyCoordinationMember : MonoBehaviour
{
    [SerializeField] EnemyHordeCoordinator coordinator;
    [SerializeField] string positioningStatus = "Not assigned";
    [SerializeField] string attackPermissionStatus = "Not requesting";
    EnemyGraphController controller;
    CombatHealth health;
    internal Transform AssignedTarget;
    internal int Slot = -1;
    internal bool HasPressureGoal;
    internal float PressurePlanAt;
    internal float ApproachBias;
    internal float LeaseUntil, InnerEligibleAt, WaitingSince, ProgressAt, LastDistance;
    internal Vector3 PreviousDestination;
    internal Vector3 PlannedTargetPosition;
    public EnemyGraphController Controller => controller;
    public EnemyAgent Agent => controller != null ? controller.Agent : null;
    public EnemyHordeCoordinator Coordinator => coordinator;
    public bool HasPosition => (Slot >= 0 || HasPressureGoal) && coordinator != null && coordinator.isActiveAndEnabled;
    public bool IsAttackPosition => HasPosition && (HasPressureGoal || coordinator.IsInnerSlot(Slot));
    public int PositionIndex => HasPosition ? Slot : -1;
    public Vector3 DesiredPosition { get; internal set; }
    public Vector3 SteeringPosition { get; internal set; }
    public string PositioningStatus => positioningStatus;
    public string AttackPermissionStatus => attackPermissionStatus;
    public bool IsBasicMelee => controller != null && controller.Role >= EnemyBehaviorRole.Sword && controller.Role <= EnemyBehaviorRole.ThrowingAxe;
    public bool AttackCoordinationEnabled => isActiveAndEnabled && IsBasicMelee && coordinator != null
        && coordinator.isActiveAndEnabled && coordinator.UseAttackPermissions;
    public bool HasAttackPermission => AttackCoordinationEnabled && coordinator.HasAttackPermission(this);
    public bool HasCommittedAttack => AttackCoordinationEnabled && coordinator.HasCommittedAttack(this);
    public bool AtAttackPosition => !HasPosition || IsAttackPosition && Steering.Flat(DesiredPosition - Agent.Position).sqrMagnitude <= 0.81f;
    internal bool Eligible => isActiveAndEnabled && controller != null && controller.IsReady
        && IsBasicMelee && controller.TargetDetected
        && controller.Graph != null && controller.Graph.isActiveAndEnabled && controller.Graph.Graph != null
        && controller.Graph.Graph.IsRunning && Agent.Target != null;

    void Awake() { controller = GetComponent<EnemyGraphController>(); health = GetComponent<CombatHealth>(); }
    void OnEnable()
    {
        ApproachBias = Mathf.Sin(transform.position.x * 0.61f + transform.position.z * 0.97f) * 0.3f;
        WaitingSince = Time.time;
        if (health != null) health.Died += Release;
        if (coordinator != null) coordinator.Register(this);
    }
    void OnDisable()
    {
        if (health != null) health.Died -= Release;
        if (coordinator != null) coordinator.Unregister(this);
        Clear("Disabled");
    }
    public void Bind(EnemyHordeCoordinator owner)
    {
        if (coordinator != null) coordinator.Unregister(this);
        coordinator = owner; WaitingSince = Time.time; InnerEligibleAt = 0;
        Clear("Not assigned");
        if (isActiveAndEnabled && coordinator != null) coordinator.Register(this);
    }
    public bool TryGetPosition(out Vector3 position)
    {
        position = default;
        if (coordinator != null) return coordinator.TryGetPosition(this, out position);
        Clear("Ordinary approach"); return false;
    }
    public bool TryGetApproachRecovery(out Vector3 position)
    {
        position = default;
        return coordinator != null && coordinator.TryGetApproachRecovery(this, out position);
    }
    public bool RequestAttackPermission(bool ready) => coordinator != null && coordinator.RequestAttackPermission(this, ready);
    public void CommitAttack() { if (coordinator != null) coordinator.CommitAttack(this); }
    public void ReleaseAttack() { if (coordinator != null) coordinator.ReleaseAttack(this); }
    public void CancelQueuedAttack() { if (coordinator != null) coordinator.CancelQueuedAttack(this); }
    public bool TryGetWaitingPosition(out Vector3 position)
    { position = default; return coordinator != null && coordinator.TryGetWaitingPosition(this, out position); }
    void Release()
    {
        if (coordinator != null) coordinator.Unregister(this);
        Clear("Unavailable");
    }
    internal void Clear(string reason)
    {
        Slot = -1; HasPressureGoal = false; AssignedTarget = null; positioningStatus = reason;
        if (Agent != null) Agent.EndPositioning();
    }
    internal void Describe(string reason) => positioningStatus = reason;
    internal void DescribeAttack(string reason) => attackPermissionStatus = reason;
}
