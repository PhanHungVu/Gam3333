using System;
using UnityEngine;

[DisallowMultipleComponent]
public class MechEnergyController : MonoBehaviour
{
    [SerializeField, Min(1f)] float capacity = 100f;
    [SerializeField, Min(0f)] float regenerationPerSecond = 25f;
    [SerializeField, Min(0f)] float recoveryDelay = 0.6f;
    float recoveryTimer;

    public float Current { get; private set; }
    public float Capacity => capacity;
    public float Normalized => Current / Mathf.Max(1f, capacity);
    public event Action<float> Changed;

    void Awake() => Current = capacity;

    public bool CanSpend(float amount) => Current + 0.0001f >= Mathf.Max(0f, amount);

    public bool TrySpend(float amount)
    {
        amount = Mathf.Max(0f, amount);
        if (!CanSpend(amount))
            return false;
        if (amount == 0f)
            return true;
        recoveryTimer = recoveryDelay;
        SetCurrent(Current - amount);
        return true;
    }

    // The motor advances recovery once per movement tick so pause and boost state agree.
    public void TickRecovery(float deltaTime, bool recovering)
    {
        if (deltaTime <= 0f)
            return;
        if (!recovering)
        {
            recoveryTimer = recoveryDelay;
            return;
        }
        float recoveryTime = Mathf.Max(0f, deltaTime - recoveryTimer);
        recoveryTimer = Mathf.Max(0f, recoveryTimer - deltaTime);
        SetCurrent(Current + regenerationPerSecond * recoveryTime);
    }

    void SetCurrent(float amount)
    {
        float next = Mathf.Clamp(amount, 0f, capacity);
        if (Mathf.Approximately(next, Current))
            return;
        Current = next;
        Changed?.Invoke(Current);
    }

    void OnValidate()
    {
        capacity = Mathf.Max(1f, capacity);
        regenerationPerSecond = Mathf.Max(0f, regenerationPerSecond);
        recoveryDelay = Mathf.Max(0f, recoveryDelay);
    }
}
