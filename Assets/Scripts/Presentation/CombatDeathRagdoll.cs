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
    public CombatRagdoll LastRagdoll { get; private set; }
    void Awake() => health = GetComponent<CombatHealth>();
    void OnEnable() { if (health == null) health = GetComponent<CombatHealth>(); LastRagdoll = null; health.Died += OnDeath; }
    void OnDisable() { if (health != null) health.Died -= OnDeath; }
    void OnDeath()
    {
        // Resolve loot before corpse creation/promotion, regardless of Died subscription order.
        var drop = GetComponent<EnemyPartDrop>();
        if (drop != null && drop.isActiveAndEnabled) drop.ResolveDeathDrop();
        var carriedDrop = drop != null && drop.isActiveAndEnabled ? drop.DroppedCarriedPart : null;
        if (TryGetComponent<EnemyMeleeReaction>(out var reaction) && reaction.ReleaseForDeath() is CombatRagdoll existing)
        {
            LastRagdoll = existing.BecomeCorpse(maximumActive, restSeconds, maximumLifetime, restSpeed, restAngularSpeed) ? existing : null;
            if (LastRagdoll != null) LastRagdoll.HideDroppedPart(carriedDrop);
            return;
        }
        Vector3 velocity = TryGetComponent<MechMovementController>(out var player) ? player.Velocity
            : TryGetComponent<EnemyAgent>(out var enemy) ? enemy.Velocity : Vector3.zero;
        LastRagdoll = CombatRagdoll.Spawn(transform, Vector3.ClampMagnitude(velocity,15) - transform.forward*deathKick + Vector3.up*.5f,
            maximumActive, maximumBodies, restSeconds, maximumLifetime, restSpeed, restAngularSpeed);
        if (LastRagdoll != null) LastRagdoll.HideDroppedPart(carriedDrop);
    }
}
