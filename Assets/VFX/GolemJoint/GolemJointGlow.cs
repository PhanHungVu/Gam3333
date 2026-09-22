using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public sealed class GolemJointGlow : MonoBehaviour
{
    [SerializeField, Min(1f)] float boostBrightness = 6f;
    [SerializeField, Min(1f)] float dodgeBrightness = 1.8f;
    [SerializeField, Min(1f)] float boostSize = 2.75f;
    [SerializeField, Min(1f)] float dodgeSize = 2.25f;
    [SerializeField, Min(0.001f)] float brightenTime = 0.035f;
    [SerializeField, Min(0.001f)] float fadeTime = 0.16f;

    static readonly int GlowProperty = Shader.PropertyToID("_JointGlow");
    ParticleSystemRenderer[] renderers;
    MaterialPropertyBlock properties;
    MechMovementController owner;
    float brightness = 1f;
    float size = 1f;
    Vector3 originalScale;
    bool initialized;

    void Awake()
    {
        renderers = GetComponentsInChildren<ParticleSystemRenderer>(true);
        properties = new MaterialPropertyBlock();
        originalScale = transform.localScale;
        initialized = true;
    }

    void LateUpdate()
    {
        // Resolve the current owner even when an ancestor part is detached or swapped.
        var movement = GetComponentInParent<MechMovementController>();
        if (owner != movement)
        {
            owner = movement;
            brightness = 1f;
            size = 1f;
        }

        float target = 1f;
        float targetSize = 1f;
        if (owner != null && owner.isActiveAndEnabled)
        {
            // A dodge takes priority over sustained boost, matching the movement controller.
            if (owner.IsQuickBoosting)
            {
                target = dodgeBrightness;
                targetSize = dodgeSize;
            }
            else if (owner.IsAssaultBoosting || owner.State == MechMovementController.MovementState.GroundBoost)
            {
                target = boostBrightness;
                targetSize = boostSize;
            }
        }

        float response = target > brightness ? brightenTime : fadeTime;
        brightness = Mathf.Lerp(brightness, target,
            1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.001f, response)));
        float sizeResponse = targetSize > size ? brightenTime : fadeTime;
        size = Mathf.Lerp(size, targetSize,
            1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.001f, sizeResponse)));
        // Scale only this VFX root, relative to the artist's size for this joint.
        // Local simulation and Hierarchy scaling also expand particles already alive.
        transform.localScale = originalScale * size;
        ApplyBrightness();
    }

    void OnDisable()
    {
        owner = null;
        brightness = 1f;
        size = 1f;
        if (initialized) transform.localScale = originalScale;
        ApplyBrightness();
    }

    void ApplyBrightness()
    {
        if (renderers == null || properties == null) return;
        foreach (var renderer in renderers)
        {
            if (renderer == null || renderer.sharedMaterial == null
                || !renderer.sharedMaterial.HasProperty(GlowProperty)) continue;
            // Affect existing particles immediately without editing a shared material.
            renderer.GetPropertyBlock(properties);
            properties.SetFloat(GlowProperty, brightness);
            renderer.SetPropertyBlock(properties);
        }
    }
}
