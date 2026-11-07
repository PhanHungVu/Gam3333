using UnityEngine;

// One player-owned, refresh-only bonus. Never writes to shared PartData or an equipped item's clocks.
[DisallowMultipleComponent, DefaultExecutionOrder(-160)]
public sealed class RetrievalSpeedBuff : MonoBehaviour
{
    readonly TimedEffect timer = new TimedEffect();
    CombatHealth health;
    [Header("Live bonus (read-only)")]
    [SerializeField] float remaining;
    [SerializeField] float movementMultiplier = 1f;
    [SerializeField] float attackCadenceMultiplier = 1f;
    public bool Active => isActiveAndEnabled && timer.Active;
    public float Remaining => Active ? timer.Remaining : 0f;
    public float MovementMultiplier => Active ? movementMultiplier : 1f;
    public float AttackCadenceMultiplier => Active ? attackCadenceMultiplier : 1f;
    public int ActivationCount { get; private set; }

    void OnEnable()
    {
        health = GetComponent<CombatHealth>();
        if (health != null) health.Died += Clear;
    }
    void OnDisable()
    {
        if (health != null) health.Died -= Clear;
        Clear();
    }
    static bool Valid(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    public bool CanApply(float duration, float movement, float cadence) => isActiveAndEnabled
        && (health == null || !health.IsDead) && Valid(duration) && duration > 0
        && Valid(movement) && movement >= 1 && Valid(cadence) && cadence >= 1;
    public bool Apply(float duration, float movement, float cadence)
    {
        if (!CanApply(duration, movement, cadence)) return false;
        timer.Start(duration); remaining = duration;
        movementMultiplier = movement; attackCadenceMultiplier = cadence;
        ActivationCount++;
        return true;
    }
    void Update()
    {
        // Scaled gameplay time: pausing also pauses the buff, like the existing timed abilities.
        if ((health != null && health.IsDead) || timer.Tick(Time.deltaTime)) Clear();
        remaining = timer.Remaining;
    }
    public void Clear()
    {
        timer.Stop(); remaining = 0;
        movementMultiplier = attackCadenceMultiplier = 1f;
    }
}
