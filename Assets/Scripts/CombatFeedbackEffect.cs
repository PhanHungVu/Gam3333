using UnityEngine;
using UnityEngine.Rendering;

// A reusable visual only: no colliders, rigidbodies, health, or damage queries.
public sealed class CombatFeedbackEffect : MonoBehaviour
{
    public enum Kind { Muzzle, Explosion, Death, Wave, Orb }
    const int RingSegments = 40;
    LineRenderer flash, ring;
    MeshRenderer core;
    Mesh coreMesh;
    MaterialPropertyBlock coreTint;
    ParticleSystem sparks;
    ParticleSystemRenderer sparkRenderer;
    float age, duration, radius;
    Color color;
    bool showFlash, showRing;
    Kind kind;
    public bool IsPlaying { get; private set; }
    public float Age => age;
    public int Generation { get; private set; }
    public bool IsLeased => IsPlaying && (kind == Kind.Wave || kind == Kind.Orb);

    public void Initialize(Material material)
    {
        coreTint = new MaterialPropertyBlock();
        var coreObject = new GameObject("Flash Core", typeof(MeshFilter), typeof(MeshRenderer));
        coreObject.transform.SetParent(transform, false);
        // A tiny reusable octahedron, built once per pool entry without a collider.
        coreMesh = new Mesh { name = "Pooled Flash Octahedron" };
        coreMesh.vertices = new[] { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
        coreMesh.triangles = new[] { 0, 4, 3, 0, 3, 5, 0, 5, 2, 0, 2, 4, 1, 3, 4, 1, 5, 3, 1, 2, 5, 1, 4, 2 };
        coreMesh.RecalculateBounds();
        coreMesh.RecalculateNormals();
        coreObject.GetComponent<MeshFilter>().sharedMesh = coreMesh;
        core = coreObject.GetComponent<MeshRenderer>();
        core.sharedMaterial = material;
        core.shadowCastingMode = ShadowCastingMode.Off;
        core.receiveShadows = false;
        flash = MakeLine("Flash", material);
        flash.positionCount = 12;
        Vector3[] star = { Vector3.left, Vector3.right, Vector3.zero, Vector3.up,
            Vector3.down, Vector3.zero, Vector3.forward, Vector3.back, Vector3.zero,
            new Vector3(-0.7f, 0.7f, 0), new Vector3(0.7f, -0.7f, 0), Vector3.zero };
        flash.SetPositions(star);
        ring = MakeLine("Shockwave", material);
        ring.loop = true;
        ring.positionCount = RingSegments;
        for (int i = 0; i < RingSegments; i++)
        {
            float a = i * Mathf.PI * 2f / RingSegments;
            ring.SetPosition(i, new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)));
        }
        var particles = new GameObject("Embers");
        particles.transform.SetParent(transform, false);
        sparks = particles.AddComponent<ParticleSystem>();
        sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = sparks.main;
        main.playOnAwake = false;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 20;
        main.gravityModifier = 0.25f;
        var emission = sparks.emission;
        emission.enabled = false;
        var shape = sparks.shape;
        shape.enabled = false;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(1, 0.3f, 0.05f), 1) },
            new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
        var colors = sparks.colorOverLifetime;
        colors.enabled = true;
        colors.color = gradient;
        var size = sparks.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0));
        sparkRenderer = particles.GetComponent<ParticleSystemRenderer>();
        sparkRenderer.sharedMaterial = material;
        sparkRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        sparkRenderer.lengthScale = 2;
        sparkRenderer.velocityScale = 0.08f;
        sparkRenderer.shadowCastingMode = ShadowCastingMode.Off;
        sparkRenderer.receiveShadows = false;
        Stop();
    }

    LineRenderer MakeLine(string label, Material material)
    {
        var obj = new GameObject(label);
        obj.transform.SetParent(transform, false);
        var line = obj.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.useWorldSpace = false;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.numCapVertices = 2;
        return line;
    }

    public void Play(Kind effectKind, Vector3 position, float size, float lifetime, Color tint,
        bool brightFlash, bool shockwave, bool secondary, Material material)
    {
        if (material == null) return;
        Stop();
        kind = effectKind;
        Generation++;
        duration = Mathf.Max(0.04f, lifetime);
        radius = Mathf.Max(0.05f, size);
        color = tint;
        showFlash = brightFlash;
        showRing = shockwave;
        age = 0;
        transform.SetPositionAndRotation(position, Quaternion.identity);
        core.sharedMaterial = flash.sharedMaterial = ring.sharedMaterial = sparkRenderer.sharedMaterial = material;
        IsPlaying = true;
        gameObject.SetActive(true);
        if (secondary)
        {
            sparks.Play();
            int count = kind == Kind.Muzzle ? 4 : kind == Kind.Death ? 10 : 16;
            for (int i = 0; i < count; i++)
            {
                float angle = i * 2.399963f;
                Vector3 velocity = new Vector3(Mathf.Cos(angle), 0.35f + (i % 4) * 0.22f, Mathf.Sin(angle));
                var particle = new ParticleSystem.EmitParams
                {
                    position = position,
                    velocity = velocity * (kind == Kind.Muzzle ? 2f : radius * 1.8f),
                    startSize = kind == Kind.Muzzle ? 0.08f : 0.22f,
                    startLifetime = duration * (0.65f + 0.35f * (i % 3) / 2f),
                    startColor = color
                };
                sparks.Emit(particle, 1);
            }
        }
        Tick(0);
    }

    public void Tick(float deltaTime)
    {
        if (!IsPlaying) return;
        age += Mathf.Max(0, deltaTime);
        if (age >= duration) { Stop(); return; }
        if (IsLeased) return; // Ability updates geometry on the same tick as its damage/motion.
        float t = age / duration;
        float flashTime = Mathf.Min(duration, kind == Kind.Muzzle ? 0.1f : 0.14f);
        float flashProgress = Mathf.Clamp01(age / flashTime);
        flash.enabled = showFlash && flashProgress < 1;
        flash.transform.localScale = Vector3.one * radius * (kind == Kind.Muzzle
            ? Mathf.Lerp(0.55f, 0.8f, flashProgress) : Mathf.Lerp(0.12f, 0.4f, flashProgress));
        flash.widthMultiplier = (kind == Kind.Muzzle ? 0.18f : 0.32f) * (1 - flashProgress);
        Color bright = Color.Lerp(new Color(1, 1, 0.85f), color, flashProgress);
        bright.a = 1 - flashProgress;
        flash.startColor = flash.endColor = bright;
        core.enabled = flash.enabled;
        core.transform.localScale = Vector3.one * radius * (kind == Kind.Muzzle ? 0.55f : kind == Kind.Death ? 0.65f : 0.2f)
            * Mathf.Lerp(0.8f, 1.3f, flashProgress);
        Color coreColor = Color.Lerp(new Color(1, 0.8f, 0.2f), color, flashProgress);
        coreColor.a = 1 - flashProgress;
        coreTint.SetColor("_Color", coreColor);
        core.SetPropertyBlock(coreTint);
        ring.enabled = showRing;
        ring.transform.localScale = Vector3.one * Mathf.Lerp(radius * 0.12f, radius, 1 - (1 - t) * (1 - t));
        ring.widthMultiplier = Mathf.Lerp(0.3f, 0.04f, t);
        Color fading = color;
        fading.a = (1 - t) * 0.85f;
        ring.startColor = ring.endColor = fading;
    }

    public void Stop()
    {
        IsPlaying = false;
        if (sparks != null) sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        gameObject.SetActive(false);
    }

    public void SetWave(float frontRadius, float width)
    {
        flash.enabled = core.enabled = false;
        ring.enabled = true;
        ring.transform.localScale = Vector3.one * Mathf.Max(0.01f, frontRadius);
        ring.widthMultiplier = Mathf.Clamp(width, 0.03f, 2);
        Color tint = color; tint.a = 0.65f;
        ring.startColor = ring.endColor = tint;
    }

    public void SetOrb(Vector3 position, float size)
    {
        transform.position = position;
        flash.enabled = ring.enabled = false;
        core.enabled = true;
        core.transform.localScale = Vector3.one * size;
        coreTint.SetColor("_Color", color);
        core.SetPropertyBlock(coreTint);
    }

    public void HideLease() { flash.enabled = ring.enabled = core.enabled = false; }

    void OnDestroy() { if (coreMesh != null) Destroy(coreMesh); }
}

// Generation prevents an old ability from moving or stopping an effect that has been reused.
public readonly struct CombatEffectLease
{
    readonly CombatFeedbackEffect effect;
    readonly int generation;
    public CombatEffectLease(CombatFeedbackEffect effect)
    { this.effect = effect; generation = effect != null ? effect.Generation : 0; }
    public bool IsValid => effect != null && effect.IsPlaying && effect.Generation == generation;
    public void Wave(float radius, float width) { if (IsValid) effect.SetWave(radius, width); }
    public void Orb(Vector3 position, float size) { if (IsValid) effect.SetOrb(position, size); }
    public void Release() { if (IsValid) effect.Stop(); }
    public void Hide() { if (IsValid) effect.HideLease(); }
}
