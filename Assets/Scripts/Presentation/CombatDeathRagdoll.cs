using UnityEngine;

[DisallowMultipleComponent, RequireComponent(typeof(CombatHealth))]
public sealed class CombatDeathRagdoll : MonoBehaviour
{
    [SerializeField, Range(1,24)] int maximumActive = 12;
    [SerializeField, Range(1,20)] int maximumBodies = 14;
    [SerializeField, Min(0)] float restSeconds = 5;
    [SerializeField, Min(1)] float maximumLifetime = 30;
    [SerializeField, Min(.01f)] float restSpeed = .15f, restAngularSpeed = .3f;
    [SerializeField, Range(0,5)] float deathKick = 1.5f;
    CombatHealth health;
    bool meleeLaunch;
    Vector3 meleeVelocity;
    public CombatRagdoll LastRagdoll { get; private set; }
    internal void BeginMeleeLaunch(Vector3 velocity) { meleeLaunch = true; meleeVelocity = velocity; }
    internal void EndMeleeLaunch() { meleeLaunch = false; meleeVelocity = Vector3.zero; }
    void Awake() => health = GetComponent<CombatHealth>();
    void OnEnable() { if (health == null) health = GetComponent<CombatHealth>(); EndMeleeLaunch(); LastRagdoll = null; health.Died += OnDeath; }
    void OnDisable() { if (health != null) health.Died -= OnDeath; }
    void OnDeath()
    {
        var drop = GetComponent<EnemyPartDrop>();
        if (drop != null && drop.isActiveAndEnabled) drop.ResolveDeathDrop();
        Vector3 velocity = TryGetComponent<MechMovementController>(out var player) ? player.Velocity
            : TryGetComponent<EnemyAgent>(out var enemy) ? enemy.Velocity : Vector3.zero;
        LastRagdoll = CombatRagdoll.Spawn(transform, meleeLaunch ? meleeVelocity : Vector3.ClampMagnitude(velocity,15) - transform.forward*deathKick + Vector3.up*.5f,
            maximumActive, maximumBodies, restSeconds, maximumLifetime, restSpeed, restAngularSpeed, lockPose: meleeLaunch);
        if (LastRagdoll != null && drop != null && drop.isActiveAndEnabled) LastRagdoll.HideDroppedPart(drop.DroppedCarriedPart);
    }
}
