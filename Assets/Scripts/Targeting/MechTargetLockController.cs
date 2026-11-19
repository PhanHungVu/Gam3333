using System;
using UnityEngine;

// Run after the camera rig and Cinemachine have moved the gameplay camera.
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public class MechTargetLockController : MonoBehaviour
{
    public enum LockMode { NoLockOn, FreeCameraLockOn, CameraLockOn }

    [SerializeField] float lockRange = 60f;
    [SerializeField] LayerMask targetLayers = ~0;
    [SerializeField, Range(0f, 0.49f)] float screenPadding = 0.04f;
    [Tooltip("Aim-point tolerance as a fraction of the camera's smaller screen dimension.")]
    [SerializeField, Range(0f, 0.25f)] float targetSwitchRadius = 0.025f;
    [Tooltip("Automatic weapon-aim radius around screen center, as a fraction of the smaller screen dimension. Free-camera aiming never steers the camera.")]
    [SerializeField, Range(0.01f, 0.5f)] float autoAimRadius = .18f;
    [SerializeField] Camera gameplayCamera;

    [Header("Hard Lock Target Switching")]
    [Tooltip("Mouse travel required within the gesture window to switch targets.")]
    [SerializeField, Min(1f)] float switchFlickDistance = 80f;
    [SerializeField, Min(0.01f)] float switchGestureWindow = 0.2f;
    [Tooltip("Slower movement is ignored and lets the next gesture arm.")]
    [SerializeField, Min(0f)] float switchMinimumSpeed = 120f;
    [SerializeField, Min(0.01f)] float switchReleaseTime = 0.1f;
    [SerializeField, Min(0f)] float switchCooldown = 0.25f;
    [SerializeField, Range(1f, 89f)] float switchDirectionHalfAngle = 60f;

    MechTargetable currentTarget;
    Collider[] candidates = new Collider[32];
    bool hardLockEnabled;
    bool targetingEnabled;
    bool rememberedHardLock;
    bool enabledOnce;
    LockMode disabledMode;
    bool switchLatched;
    Vector2 switchTravel;
    float gestureTime;
    float quietTime;
    float cooldownRemaining;

    public MechTargetable CurrentTarget => currentTarget;
    public Camera GameplayCamera => gameplayCamera;
    public bool IsFreeAim => !targetingEnabled;
    public bool IsFreeCamera => !IsHardLockMode;
    public bool IsHardLockMode => isActiveAndEnabled && targetingEnabled && hardLockEnabled;
    public bool IsHardLocked => isActiveAndEnabled && targetingEnabled && hardLockEnabled && IsTargetInRange(currentTarget);
    public LockMode Mode => !targetingEnabled ? LockMode.NoLockOn
        : hardLockEnabled ? LockMode.CameraLockOn : LockMode.FreeCameraLockOn;
    public event Action<MechTargetable, MechTargetable> TargetChanged;
    public event Action<bool> HardLockChanged;

    public void EnterFreeAim()
    {
        if (targetingEnabled) rememberedHardLock = hardLockEnabled;
        targetingEnabled = false;
        SetHardLock(false);
        SetTarget(null);
    }

    public void ResumeTargeting()
    {
        if (!isActiveAndEnabled || targetingEnabled) return;
        targetingEnabled = true;
        if (gameplayCamera == null) gameplayCamera = Camera.main;
        SelectTarget(gameplayCamera);
        // Retain a remembered hard mode even if there is no enemy yet. Acquisition
        // is allowed only after this explicit request, never while in free aim.
        SetHardLock(rememberedHardLock);
    }

    // One press advances one mode. Holding/releasing never advances the cycle.
    // No lock disables weapon auto-aim as well as camera tracking.
    public void ProcessLockButton(bool pressed, bool held, bool released, float deltaTime)
    {
        if (!isActiveAndEnabled || deltaTime <= 0f) return;
        if (pressed) CycleLockMode();
    }

    public void CycleLockMode() => SetLockMode((LockMode)(((int)Mode + 1) % 3));

    public void SetLockMode(LockMode mode)
    {
        if ((int)mode < 0 || (int)mode > (int)LockMode.CameraLockOn) throw new ArgumentOutOfRangeException(nameof(mode));
        if (mode == LockMode.NoLockOn) { EnterFreeAim(); return; }
        if (!isActiveAndEnabled) return;
        targetingEnabled = true;
        SetHardLock(mode == LockMode.CameraLockOn);
        if (gameplayCamera == null) gameplayCamera = Camera.main;
        // Explicitly entering free-camera mode ignores the remembered hard mode.
        // Entering camera lock retains the current visible auto-aim selection.
        if (!hardLockEnabled || gameplayCamera == null || !IsValidTarget(currentTarget, gameplayCamera))
            SelectTarget(gameplayCamera);
    }

    public void CancelLockButtonInput()
    {
        switchTravel = Vector2.zero; gestureTime = quietTime = 0;
        switchLatched = false;
    }

    public void ToggleHardLock()
    {
        if (!isActiveAndEnabled) return;
        if (IsFreeAim)
            ResumeTargeting();
        if (hardLockEnabled)
        {
            SetHardLock(false);
            return;
        }
        if (gameplayCamera == null) gameplayCamera = Camera.main;
        // Toggling follows the visible selection; it must not silently choose another enemy.
        SetHardLock(true);
        if (gameplayCamera == null || !IsValidTarget(currentTarget, gameplayCamera)) SelectTarget(gameplayCamera);
    }

    // Called once per frame by the camera input owner, before movement and camera updates.
    public void ProcessSwitchInput(Vector2 mouseDelta, float deltaTime)
    {
        if (!IsHardLocked || deltaTime <= 0f) return;
        cooldownRemaining = Mathf.Max(0f, cooldownRemaining - deltaTime);
        gestureTime += deltaTime;
        if (mouseDelta.magnitude / deltaTime < switchMinimumSpeed || mouseDelta == Vector2.zero)
        {
            quietTime += deltaTime;
            if (quietTime >= switchReleaseTime)
            {
                switchLatched = false;
                switchTravel = Vector2.zero;
                gestureTime = 0f;
            }
            return;
        }
        quietTime = 0f;
        if (switchLatched || cooldownRemaining > 0f) return;
        if (gestureTime > switchGestureWindow || Vector2.Dot(switchTravel, mouseDelta) < 0f)
        {
            switchTravel = Vector2.zero;
            gestureTime = deltaTime;
        }
        switchTravel += mouseDelta;
        if (switchTravel.magnitude < switchFlickDistance) return;

        SwitchInDirection(switchTravel.normalized);
        // Consume even a gesture toward empty space, so holding it cannot switch later.
        switchLatched = true;
        cooldownRemaining = switchCooldown;
        switchTravel = Vector2.zero;
        gestureTime = 0f;
    }

    void SwitchInDirection(Vector2 direction)
    {
        if (gameplayCamera == null || !IsHardLocked) return;
        Vector3 origin = gameplayCamera.WorldToScreenPoint(currentTarget.AimPosition);
        if (origin.z < gameplayCamera.nearClipPlane) return;
        float minimumDot = Mathf.Cos(switchDirectionHalfAngle * Mathf.Deg2Rad);
        MechTargetable next = null;
        float nearestDistance = float.MaxValue;
        int count = QueryCandidates();
        for (int i = 0; i < count; i++)
        {
            var candidate = candidates[i].GetComponentInParent<MechTargetable>();
            if (candidate == currentTarget || !IsValidTarget(candidate, gameplayCamera)) continue;
            Vector2 offset = (Vector2)gameplayCamera.WorldToScreenPoint(candidate.AimPosition) - (Vector2)origin;
            if (offset.sqrMagnitude < 1f || Vector2.Dot(offset.normalized, direction) < minimumDot) continue;
            if (offset.sqrMagnitude < nearestDistance)
            {
                nearestDistance = offset.sqrMagnitude;
                next = candidate;
            }
        }
        Array.Clear(candidates, 0, count);
        if (next != null) SetTarget(next);
    }

    void SetHardLock(bool active)
    {
        if (hardLockEnabled == active) return;
        hardLockEnabled = active;
        if (targetingEnabled) rememberedHardLock = active;
        switchTravel = Vector2.zero;
        gestureTime = quietTime = cooldownRemaining = 0f;
        switchLatched = false;
        HardLockChanged?.Invoke(active);
    }

    public void BindCamera(Camera camera) => gameplayCamera = camera;

    void Awake()
    {
        if (gameplayCamera == null)
            gameplayCamera = Camera.main;
    }

    void OnDisable()
    {
        disabledMode = Mode;
        CancelLockButtonInput();
        EnterFreeAim();
    }
    void OnEnable()
    {
        if (enabledOnce) SetLockMode(disabledMode);
        else { enabledOnce = true; ResumeTargeting(); }
    }

    // Hard lock re-targets at the start of the frame, before the camera frames it.
    void Update()
    {
        if (!IsFreeAim && hardLockEnabled) RefreshHardLockTarget();
    }

    void LateUpdate()
    {
        if (IsFreeAim) return;
        if (gameplayCamera == null)
            gameplayCamera = Camera.main;

        if (hardLockEnabled)
        {
            RefreshHardLockTarget();
            return;
        }
        SelectTarget(gameplayCamera);
    }

    // The camera can refresh before framing, so death never produces a soft-camera frame.
    // Mode and target lifetime are separate: only player input changes soft/hard mode.
    public void RefreshHardLockTarget()
    {
        if (!IsHardLockMode) return;
        if (gameplayCamera == null) gameplayCamera = Camera.main;
        bool found = false;
        int count = QueryCandidates();
        for (int i = 0; i < count; i++)
            found |= candidates[i].GetComponentInParent<MechTargetable>() == currentTarget;
        Array.Clear(candidates, 0, count);
        if (gameplayCamera != null && IsTargetInRange(currentTarget) && found) return;
        SetTarget(null);
        SelectTarget(gameplayCamera);
    }

    void SelectTarget(Camera viewCamera)
    {
        if (IsFreeAim || viewCamera == null)
        {
            SetTarget(null);
            return;
        }

        MechTargetable nearestTarget = null;
        MechTargetable centeredTarget = null;
        MechTargetable rayTarget = null;
        float nearestScore = float.MaxValue;
        float centeredScore = float.MaxValue;
        float rayDistance = float.MaxValue;
        bool keepCurrentTarget = false;
        Vector2 screenCenter = viewCamera.pixelRect.center;
        float switchRadius = targetSwitchRadius * Mathf.Min(viewCamera.pixelWidth, viewCamera.pixelHeight);
        float acquisitionRadius = autoAimRadius * Mathf.Min(viewCamera.pixelWidth, viewCamera.pixelHeight);
        Ray centerRay = viewCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        int candidateCount = QueryCandidates();

        for (int i = 0; i < candidateCount; i++)
        {
            Collider candidate = candidates[i];
            MechTargetable targetable = candidate.GetComponentInParent<MechTargetable>();
            if (!IsValidTarget(targetable, viewCamera))
                continue;

            Vector2 screenPoint = viewCamera.WorldToScreenPoint(targetable.AimPosition);
            float score = (screenPoint - screenCenter).sqrMagnitude;
            bool crossed = candidate.Raycast(centerRay, out RaycastHit hit, Mathf.Infinity);
            if (!hardLockEnabled && !crossed && score > acquisitionRadius * acquisitionRadius) continue;
            keepCurrentTarget |= targetable == currentTarget;
            // Soft acquisition follows hidden aim, including when no collider is
            // directly under it. Hard-lock recovery keeps its world-distance rule.
            float distance = hardLockEnabled
                ? (targetable.AimPosition - transform.position).sqrMagnitude : score;
            if (distance < nearestScore)
            {
                nearestScore = distance;
                nearestTarget = targetable;
            }

            if (score <= switchRadius * switchRadius && score < centeredScore)
            {
                centeredScore = score;
                centeredTarget = targetable;
            }

            // Crossing any part of an enemy's collider counts, not just its aim point.
            if (crossed && hit.distance < rayDistance)
            {
                rayDistance = hit.distance;
                rayTarget = targetable;
            }
        }

        // Hidden screen-center aim is evaluated every frame. No mouse speed,
        // travel, or new input is required; an enemy crossing the aim can be selected.
        // Empty space keeps a valid selection instead of cycling through enemies.
        MechTargetable aimedTarget = rayTarget != null ? rayTarget : centeredTarget;
        SetTarget(!hardLockEnabled && aimedTarget != null ? aimedTarget
            : keepCurrentTarget ? currentTarget : nearestTarget);
        Array.Clear(candidates, 0, candidateCount);
    }

    int QueryCandidates()
    {
        // Reuse storage once warmed up, but grow rather than silently drop enemies
        // when a horde exceeds the initial capacity.
        int count;
        while ((count = Physics.OverlapSphereNonAlloc(transform.position, lockRange,
            candidates, targetLayers, QueryTriggerInteraction.Ignore)) == candidates.Length)
        {
            Array.Resize(ref candidates, candidates.Length * 2);
        }
        return count;
    }

    void SetTarget(MechTargetable nextTarget)
    {
        // Reference equality also clears a destroyed Unity object and notifies listeners.
        if (ReferenceEquals(currentTarget, nextTarget))
            return;
        MechTargetable previousTarget = currentTarget;
        currentTarget = nextTarget;
        TargetChanged?.Invoke(previousTarget, nextTarget);
    }

    void OnValidate()
    {
        lockRange = Mathf.Max(0.01f, lockRange);
        autoAimRadius = Mathf.Clamp(autoAimRadius, .01f, .5f);
        screenPadding = Mathf.Clamp(screenPadding, 0f, 0.49f);
        targetSwitchRadius = Mathf.Clamp(targetSwitchRadius, 0f, 0.25f);
        switchFlickDistance = Mathf.Max(1f, switchFlickDistance);
        switchGestureWindow = Mathf.Max(0.01f, switchGestureWindow);
        switchMinimumSpeed = Mathf.Max(0f, switchMinimumSpeed);
        switchReleaseTime = Mathf.Max(0.01f, switchReleaseTime);
        switchCooldown = Mathf.Max(0f, switchCooldown);
    }

    bool IsTargetInRange(MechTargetable targetable)
    {
        if (targetable == null || !targetable.isActiveAndEnabled
            || targetable.transform.IsChildOf(transform))
            return false;

        return (targetable.AimPosition - transform.position).sqrMagnitude <= lockRange * lockRange;
    }

    bool IsValidTarget(MechTargetable targetable, Camera viewCamera)
    {
        if (!IsTargetInRange(targetable)) return false;
        Vector3 viewportPosition = viewCamera.WorldToViewportPoint(targetable.AimPosition);
        // Padding affects acquisition only. Keep a selected enemy until it actually exits.
        float padding = targetable == currentTarget ? 0f : screenPadding;
        return viewportPosition.z >= viewCamera.nearClipPlane
            && viewportPosition.z <= viewCamera.farClipPlane
            && viewportPosition.x >= padding && viewportPosition.x <= 1f - padding
            && viewportPosition.y >= padding && viewportPosition.y <= 1f - padding;
    }
}
