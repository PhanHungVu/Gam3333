using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(50)]
public class MechBoostPresentation : MonoBehaviour
{
    [SerializeField] MechMovementController movement;
    [SerializeField] Transform visualRoot;
    [SerializeField] Material boostMaterial;
    [SerializeField] AudioClip ignitionClip;
    [SerializeField, Range(0f, 1f)] float ignitionVolume = 0.22f;
    [SerializeField, Range(0f, 15f)] float maximumLean = 9f;
    [SerializeField, Min(0.01f)] float leanResponse = 0.12f;
    [SerializeField] bool showThrusters = true;
    [SerializeField] bool playAudio = true;

    Quaternion restRotation;
    Vector3 previousVelocity;
    ParticleSystem thrusters;
    TrailRenderer[] trails;
    AudioSource audioSource;

    void Awake()
    {
        if (movement == null)
            movement = GetComponent<MechMovementController>();
        if (visualRoot == null)
            visualRoot = transform.Find("VisualMech");
        if (visualRoot == null)
        {
            Debug.LogError("Assign a VisualMech child for boost presentation.", this);
            enabled = false;
            return;
        }
        restRotation = visualRoot.localRotation;
        if (boostMaterial != null)
            CreateThrusters();
    }

    void OnEnable()
    {
        if (movement == null)
            return;
        movement.QuickBoostStarted += OnQuickBoost;
        movement.AssaultBoostStarted += PlayIgnition;
    }

    void OnDisable()
    {
        if (movement != null)
        {
            movement.QuickBoostStarted -= OnQuickBoost;
            movement.AssaultBoostStarted -= PlayIgnition;
        }
        if (audioSource != null)
            audioSource.Stop();
        if (thrusters != null)
            thrusters.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (trails != null)
            foreach (TrailRenderer trail in trails) { trail.emitting = false; trail.Clear(); }
        if (visualRoot != null)
            visualRoot.localRotation = restRotation;
        previousVelocity = Vector3.zero;
    }

    void LateUpdate() => UpdatePresentation(Time.deltaTime);

    void UpdatePresentation(float deltaTime)
    {
        if (visualRoot == null || movement == null || deltaTime <= 0f || CombatHitStop.IsFrozen(transform))
            return;
        bool active = movement.isActiveAndEnabled;
        Vector3 velocity = active ? movement.Velocity : Vector3.zero;
        Vector3 localVelocity = transform.InverseTransformDirection(velocity);
        Vector3 acceleration = transform.InverseTransformDirection((velocity - previousVelocity) / deltaTime);
        bool boosting = active && (movement.IsQuickBoosting || movement.IsAssaultBoosting);
        float pitch = Mathf.Clamp(localVelocity.z * 0.2f + acceleration.z * 0.04f, -maximumLean, maximumLean);
        float bank = Mathf.Clamp(-localVelocity.x * 0.4f - acceleration.x * 0.025f, -maximumLean, maximumLean);
        Quaternion desired = restRotation * Quaternion.Euler(pitch, 0f, bank);
        visualRoot.localRotation = Quaternion.Slerp(visualRoot.localRotation, desired,
            1f - Mathf.Exp(-deltaTime / leanResponse));
        previousVelocity = velocity;
        if (thrusters != null)
        {
            var emission = thrusters.emission;
            emission.enabled = showThrusters && boosting;
            emission.rateOverTime = movement.IsQuickBoosting ? 90f : 45f;
            if (boosting && showThrusters && !thrusters.isPlaying)
                thrusters.Play();
        }
        if (trails != null)
            foreach (TrailRenderer trail in trails)
                trail.emitting = showThrusters && boosting;
    }

    void OnQuickBoost(Vector3 direction) => PlayIgnition();

    void PlayIgnition()
    {
        // An empty clip slot is intentionally silent.
        if (!playAudio || ignitionClip == null)
            return;
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
        }
        audioSource.PlayOneShot(ignitionClip, ignitionVolume);
    }

    void CreateThrusters()
    {
        var emitter = new GameObject("Boost Exhaust");
        emitter.transform.SetParent(visualRoot, false);
        emitter.transform.localPosition = new Vector3(0f, -0.6f, -0.6f);
        emitter.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        thrusters = emitter.AddComponent<ParticleSystem>();
        thrusters.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = thrusters.main;
        main.playOnAwake = false;
        main.loop = true;
        main.startLifetime = 0.16f;
        main.startSpeed = 9f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.28f);
        main.startColor = new Color(0.3f, 0.85f, 1f, 0.9f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 80;
        var emission = thrusters.emission;
        emission.enabled = false;
        var shape = thrusters.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 12f;
        shape.radius = 0.3f;
        var colors = new Gradient();
        colors.SetKeys(new[] { new GradientColorKey(Color.white, 0f),
            new GradientColorKey(new Color(0.05f, 0.45f, 1f), 1f) },
            new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
        var colorLife = thrusters.colorOverLifetime;
        colorLife.enabled = true;
        colorLife.color = colors;
        thrusters.GetComponent<ParticleSystemRenderer>().sharedMaterial = boostMaterial;
        trails = new TrailRenderer[2];
        for (int i = 0; i < trails.Length; i++)
        {
            var trailObject = new GameObject("Boost Trail " + (i + 1));
            trailObject.transform.SetParent(visualRoot, false);
            trailObject.transform.localPosition = new Vector3(i == 0 ? -0.7f : 0.7f, -0.4f, -0.5f);
            var trail = trailObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = boostMaterial;
            trail.time = 0.18f;
            trail.widthMultiplier = 0.12f;
            trail.minVertexDistance = 0.05f;
            trail.colorGradient = colors;
            trail.emitting = false;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trails[i] = trail;
        }
    }

    void OnDestroy()
    {
        if (thrusters != null)
            Destroy(thrusters.gameObject);
        if (trails != null)
            foreach (TrailRenderer trail in trails)
                if (trail != null) Destroy(trail.gameObject);
        if (audioSource != null)
            Destroy(audioSource);
    }
}
