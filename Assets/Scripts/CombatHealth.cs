using System;
using UnityEngine;

[DisallowMultipleComponent]
public class CombatHealth : MonoBehaviour
{
    [SerializeField, Min(1f)] float maximumHealth = 100f;
    [SerializeField] Material feedbackMaterial;
    [SerializeField] float healthBarHeight = 1.4f;
    public float CurrentHealth { get; private set; }
    public float MaximumHealth => maximumHealth;
    public bool IsDead => CurrentHealth <= 0f;
    public event Action<float> Damaged;
    public event Action Died;
    LineRenderer bar;
    Camera view;

    void Awake()
    {
        CurrentHealth = maximumHealth;
        if (feedbackMaterial == null) return;
        var display = new GameObject("Enemy Health");
        display.transform.SetParent(transform, false);
        bar = display.AddComponent<LineRenderer>();
        bar.sharedMaterial = feedbackMaterial;
        bar.positionCount = 2;
        bar.startWidth = bar.endWidth = 0.1f;
        bar.startColor = bar.endColor = new Color(0.3f, 1f, 0.35f);
        bar.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        bar.receiveShadows = false;
    }

    public bool ApplyDamage(float amount)
    {
        if (!isActiveAndEnabled || IsDead || amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return false;
        float applied = Mathf.Min(CurrentHealth, amount);
        CurrentHealth -= applied;
        Damaged?.Invoke(applied);
        if (IsDead)
        {
            Died?.Invoke();
            // Immediate removal also clears colliders, movement, and lock eligibility.
            gameObject.SetActive(false);
        }
        return true;
    }

    void LateUpdate()
    {
        if (bar == null) return;
        if (view == null) view = Camera.main;
        Vector3 right = view != null ? view.transform.right : Vector3.right;
        Vector3 start = transform.position + Vector3.up * healthBarHeight - right;
        bar.SetPosition(0, start);
        bar.SetPosition(1, start + right * (2f * CurrentHealth / maximumHealth));
    }

    void OnValidate() => maximumHealth = Mathf.Max(1f, maximumHealth);
}
