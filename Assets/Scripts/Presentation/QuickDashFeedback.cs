using UnityEngine;
using UnityEngine.SceneManagement;

// Accepted-dash presentation only. Motion stays in the motor, impulses in CombatFeedback.
[DisallowMultipleComponent, RequireComponent(typeof(MechMovementController))]
public sealed class QuickDashFeedback : MonoBehaviour
{
    [SerializeField] ParticleSystem dustPrefab;
    [SerializeField, Range(1, 24)] int dustPoolSize = 12;
    [SerializeField, Range(1, 96)] int dustCount = 48;
    [SerializeField, Min(.01f)] float dustScale = 1.35f;
    [SerializeField, Range(.1f, 2f)] float dustLifetimeScale = 1.25f;
    [Tooltip("Distance travelled between separate ground puffs during an accepted dash.")]
    [SerializeField, Min(.25f)] float dustSpacing = 1.6f;
    [SerializeField, Range(1, 12)] int maxDustPuffsPerDash = 6;
    [SerializeField, Range(0, .1f)] float startImpulseStrength = .035f;
    [SerializeField, Range(.05f, .1f)] float startImpulseDuration = .07f;
    [SerializeField, Range(0, .1f)] float endImpulseStrength = .018f;
    [SerializeField, Range(.05f, .1f)] float endImpulseDuration = .06f;
    MechMovementController movement;
    CombatFeedback feedback;
    GameObject poolRoot;
    ParticleSystem[] dust;
    bool pendingStop, landedAtStop;
    bool dustActive;
    Vector3 lastDustSample, dashDirection;
    float distanceSincePuff;
    int puffsThisDash;
    public int DustBurstCount { get; private set; }
    public int StartImpulseCount { get; private set; }
    public int EndImpulseCount { get; private set; }
    public int PoolSize => dust != null ? dust.Length : 0;
    public Vector3 LastDustPosition { get; private set; }
    public Vector3 LastDustDirection { get; private set; }
    public ParticleSystem[] DustSystems => dust;
    void Awake()
    {
        movement = GetComponent<MechMovementController>(); feedback = GetComponent<CombatFeedback>();
        if (dustPrefab == null) return;
        poolRoot = new GameObject("Quick Dash Dust Pool"); SceneManager.MoveGameObjectToScene(poolRoot, gameObject.scene);
        dust = new ParticleSystem[Mathf.Clamp(dustPoolSize, 1, 24)];
        for (int i = 0; i < dust.Length; i++)
        {
            dust[i] = Instantiate(dustPrefab, poolRoot.transform, false);
            dust[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = dust[i].main; var lifetime = main.startLifetime;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.constantMin * dustLifetimeScale, lifetime.constantMax * dustLifetimeScale);
        }
    }
    void OnEnable()
    {
        movement.QuickBoostStarted += OnStart; movement.QuickBoostEnded += OnEnd; movement.Landed += OnLanding;
    }
    void OnDisable()
    {
        movement.QuickBoostStarted -= OnStart; movement.QuickBoostEnded -= OnEnd; movement.Landed -= OnLanding;
        pendingStop = landedAtStop = dustActive = false;
        if (dust != null) foreach (var system in dust) if (system != null) system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
    void OnStart(Vector3 direction)
    {
        pendingStop = landedAtStop = false;
        if (feedback != null && feedback.PlayMovementImpulse(Vector3.back, startImpulseStrength, startImpulseDuration)) StartImpulseCount++;
        dustActive = movement.QuickBoostHasGroundContact && dust != null;
        if (!dustActive) return;
        dashDirection = direction; lastDustSample = movement.QuickBoostStartPosition;
        distanceSincePuff = 0; puffsThisDash = 0;
        EmitDust(movement.QuickBoostGroundPoint, movement.QuickBoostGroundNormal);
        SampleDustPath(); // Include the accepted frame's already-resolved movement.
    }
    void SampleDustPath()
    {
        if (!dustActive || puffsThisDash >= maxDustPuffsPerDash) return;
        Vector3 current = transform.position;
        float distance = Vector3.ProjectOnPlane(current - lastDustSample, Vector3.up).magnitude;
        if (distance < .0001f) { lastDustSample = current; return; }
        float spacing = Mathf.Max(.25f, dustSpacing);
        for (float along = spacing - distanceSincePuff; along <= distance && puffsThisDash < maxDustPuffsPerDash; along += spacing)
        {
            Vector3 sample = Vector3.Lerp(lastDustSample, current, Mathf.Clamp01(along / distance));
            if (movement.TryGetGroundContact(sample, out var contact)) EmitDust(contact.point, contact.normal);
        }
        distanceSincePuff = (distanceSincePuff + distance) % spacing;
        lastDustSample = current;
    }
    void EmitDust(Vector3 point, Vector3 normal)
    {
        foreach (var system in dust)
        {
            if (system == null || system.IsAlive(true)) continue;
            Vector3 backward = Vector3.ProjectOnPlane(-dashDirection, normal).normalized;
            if (backward.sqrMagnitude < .001f) backward = Vector3.back;
            LastDustPosition = point + normal * .025f; LastDustDirection = backward;
            system.transform.SetPositionAndRotation(LastDustPosition, Quaternion.LookRotation(backward, normal));
            system.transform.localScale = Vector3.one * dustScale;
            system.Play(); system.Emit(dustCount); DustBurstCount++; puffsThisDash++; break;
        }
    }
    void OnEnd()
    {
        if (movement.isActiveAndEnabled) { SampleDustPath(); pendingStop = true; landedAtStop = false; }
        dustActive = false;
    }
    void OnLanding(float _) { if (pendingStop) landedAtStop = true; }
    void LateUpdate()
    {
        if (dustActive && movement.isActiveAndEnabled && movement.IsQuickBoosting) SampleDustPath();
        if (!pendingStop) return;
        pendingStop = false;
        if (!landedAtStop && movement.isActiveAndEnabled && movement.IsGrounded && movement.TryGetGroundContact(out _)
            && feedback != null && feedback.PlayMovementImpulse(Vector3.down, endImpulseStrength, endImpulseDuration)) EndImpulseCount++;
    }
    void OnDestroy() { if (poolRoot != null) Destroy(poolRoot); }
}
