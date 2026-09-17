using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[DefaultExecutionOrder(300)]
public class MechCombatController : MonoBehaviour
{
    [SerializeField] InputActionAsset inputActions;
    [SerializeField] Camera gameplayCamera;
    [SerializeField] Material combatMaterial;
    [SerializeField] LayerMask hitLayers = ~0;
    [Header("Projectile Weapon")]
    [SerializeField] Vector3 muzzleOffset = new Vector3(0.65f, 0.5f, 0.65f);
    [SerializeField, Min(0.01f)] float fireInterval = 0.2f;
    [SerializeField, Min(0f)] float projectileDamage = 15f;
    [SerializeField, Min(0.01f)] float projectileSpeed = 100f;
    [SerializeField, Min(0.01f)] float projectileRadius = 0.12f;
    [SerializeField, Min(0.01f)] float projectileRange = 100f;
    [Header("Melee Weapon")]
    [SerializeField] Vector3 meleeCenter = new Vector3(0f, 0f, 2.25f);
    [SerializeField] Vector3 meleeSize = new Vector3(3f, 2.5f, 3.5f);
    [SerializeField, Min(0f)] float meleeDamage = 60f;
    [SerializeField, Min(0.01f)] float meleeCooldown = 0.65f;
    [SerializeField] bool showMeleeBox = true;
    [Header("Melee Reach Assist")]
    [SerializeField, Min(0f)] float maximumMeleeApproach = 1f;
    [SerializeField, Range(1f, 60f)] float maximumMeleeAssistAngle = 30f;
    [SerializeField, Min(0.01f)] float meleeApproachDuration = 0.12f;
    [SerializeField, Min(0f)] float meleeCameraHold = 0.45f;
    MechTargetLockController targeting;
    InputAction fireAction, meleeAction;
    bool inputArmed;
    float fireTimer, meleeTimer, flashTimer;
    Collider[] meleeCandidates = new Collider[32];
    readonly HashSet<CombatHealth> damaged = new HashSet<CombatHealth>();
    LineRenderer meleeOutline;
    LineRenderer groundOutline;
    CharacterController movementCapsule;
    MechMovementController movement;
    bool meleePending;
    float approachTime, approachDistance, meleeExtension, meleeCameraTimer;
    Quaternion strikeRotation;
    MechTargetable assistedTarget;
    public bool MeleeCameraActive => isActiveAndEnabled && meleeCameraTimer > 0f;
    public bool IsMeleeApproaching => meleePending;
    public float MeleeExtension => meleeExtension;
    static readonly int[] BoxCorners = { 0, 1, 3, 2, 0, 4, 5, 1, 5, 7, 3, 7, 6, 2, 6, 4 };
    static readonly int[] GroundCorners = { 0, 1, 5, 4, 0 };
    public event Action<MechProjectile> ShotFired;
    public event Action<int> MeleePerformed;

    void Awake()
    {
        targeting = GetComponent<MechTargetLockController>();
        movementCapsule = GetComponent<CharacterController>();
        movement = GetComponent<MechMovementController>();
        if (gameplayCamera == null) gameplayCamera = Camera.main;
        if (combatMaterial != null) CreateMeleeOutline();
    }

    void OnEnable()
    {
        fireAction = inputActions != null ? inputActions.FindAction("Player/Attack", false) : null;
        meleeAction = inputActions != null ? inputActions.FindAction("Player/Melee", false) : null;
        fireAction?.Enable();
        meleeAction?.Enable();
    }

    void OnDisable()
    {
        fireAction?.Disable();
        meleeAction?.Disable();
        inputArmed = false;
        meleePending = false;
        meleeExtension = meleeCameraTimer = flashTimer = 0f;
        if (meleeOutline != null) meleeOutline.enabled = false;
        if (groundOutline != null) groundOutline.enabled = false;
    }

    void LateUpdate()
    {
        TickCooldowns(Time.deltaTime);
        TickMelee(Time.deltaTime);
        UpdateMeleeOutline();
        if (Cursor.lockState != CursorLockMode.Locked || Time.timeScale == 0f)
        {
            inputArmed = false;
            return;
        }
        bool fire = fireAction != null ? fireAction.IsPressed() : Mouse.current != null && Mouse.current.leftButton.isPressed;
        bool meleeHeld = meleeAction != null ? meleeAction.IsPressed() : Mouse.current != null && Mouse.current.rightButton.isPressed;
        bool melee = meleeAction != null ? meleeAction.WasPressedThisFrame() : Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
        // A click to recapture the mouse should not also fire a weapon.
        if (!inputArmed) { inputArmed = !fire && !meleeHeld; return; }
        if (fire || melee) Physics.SyncTransforms();
        if (melee) TryMelee();
        if (fire) TryFire();
    }

    void TickCooldowns(float deltaTime)
    {
        fireTimer = Mathf.Max(0f, fireTimer - deltaTime);
        meleeTimer = Mathf.Max(0f, meleeTimer - deltaTime);
        flashTimer = Mathf.Max(0f, flashTimer - deltaTime);
        meleeCameraTimer = Mathf.Max(0f, meleeCameraTimer - deltaTime);
        if (!meleePending && flashTimer <= 0f) meleeExtension = 0f;
    }

    public MechProjectile TryFire()
    {
        if (!isActiveAndEnabled || fireTimer > 0f) return null;
        if (gameplayCamera == null) gameplayCamera = Camera.main;
        if (gameplayCamera == null) return null;
        Vector3 muzzle = transform.TransformPoint(muzzleOffset);
        Vector3 aimPoint = GetAimPoint();
        Vector3 direction = (aimPoint - muzzle).normalized;
        if (direction.sqrMagnitude < 0.001f) direction = gameplayCamera.transform.forward;
        fireTimer = fireInterval;
        var shot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        shot.name = "Player Projectile";
        var collider = shot.GetComponent<Collider>();
        collider.enabled = false;
        Destroy(collider);
        // No projectile collider or rigidbody: swept sphere queries resolve its motion.
        shot.transform.position = transform.position + Vector3.up * muzzleOffset.y;
        shot.transform.localScale = Vector3.one * (projectileRadius * 2f);
        var renderer = shot.GetComponent<Renderer>();
        if (combatMaterial != null) renderer.sharedMaterial = combatMaterial;
        var tint = new MaterialPropertyBlock();
        tint.SetColor("_Color", new Color(1f, 0.85f, 0.15f));
        renderer.SetPropertyBlock(tint);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var projectile = shot.AddComponent<MechProjectile>();
        projectile.Launch(transform, direction, projectileSpeed, projectileDamage, projectileRadius,
            projectileRange, hitLayers, muzzle);
        ShotFired?.Invoke(projectile);
        return projectile;
    }

    Vector3 GetAimPoint()
    {
        var enemy = targeting != null && targeting.isActiveAndEnabled && !targeting.IsFreeAim ? targeting.CurrentTarget : null;
        if (enemy != null && enemy.isActiveAndEnabled) return enemy.AimPosition;
        Ray ray = gameplayCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 aimPoint = ray.GetPoint(projectileRange);
        float nearest = projectileRange;
        foreach (var hit in Physics.RaycastAll(ray, projectileRange, hitLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(transform) || hit.distance >= nearest) continue;
            nearest = hit.distance;
            aimPoint = hit.point;
        }
        return aimPoint;
    }

    public bool TryMelee()
    {
        if (!isActiveAndEnabled || meleeTimer > 0f || meleePending) return false;
        meleeTimer = meleeCooldown;
        meleeCameraTimer = meleeCameraHold;
        strikeRotation = transform.rotation;
        meleeExtension = 0f;
        approachDistance = FindMeleeApproach();
        if (approachDistance > 0.01f)
        {
            meleePending = true;
            approachTime = 0f;
            UpdateMeleeOutline();
        }
        else ResolveMelee();
        return true;
    }

    // Reach assistance translates only the attack box. It never steers or teleports
    // the movement root, and never changes target or direction after admission.
    float FindMeleeApproach()
    {
        assistedTarget = targeting != null && !targeting.IsFreeAim ? targeting.CurrentTarget : null;
        if (assistedTarget == null || !assistedTarget.isActiveAndEnabled || maximumMeleeApproach <= 0f
            || movement != null && (movement.IsQuickBoosting || !movement.IsGrounded)) return 0f;
        Vector3 direction = Vector3.ProjectOnPlane(assistedTarget.AimPosition - transform.position, Vector3.up);
        float angle = Vector3.Angle(transform.forward, direction);
        if (angle >= maximumMeleeAssistAngle) return 0f;
        float budget = maximumMeleeApproach * (1f - angle / maximumMeleeAssistAngle);
        Vector3 halfSize = Vector3.Scale(meleeSize, AbsScale()) * 0.5f;
        Vector3 center = MeleeBoxCenter(0f);
        Vector3 front = center + strikeRotation * Vector3.forward * halfSize.z;
        float closestGap = float.MaxValue;
        foreach (var candidate in assistedTarget.GetComponentsInChildren<Collider>())
        {
            if (!candidate.enabled || candidate.isTrigger || !candidate.gameObject.activeInHierarchy
                || (hitLayers.value & (1 << candidate.gameObject.layer)) == 0) continue;
            var health = candidate.GetComponentInParent<CombatHealth>();
            if (health == null || health.IsDead || !health.isActiveAndEnabled || BlockedByWorld(candidate)) continue;
            Vector3 local = Quaternion.Inverse(strikeRotation) * (candidate.ClosestPoint(front) - center);
            if (Mathf.Abs(local.x) > halfSize.x || Mathf.Abs(local.y) > halfSize.y) continue;
            closestGap = Mathf.Min(closestGap, local.z - halfSize.z);
        }
        if (closestGap <= 0f || closestGap + 0.03f > budget) return 0f;
        float extension = closestGap + 0.03f;
        return LimitMeleeApproach(extension) >= extension - 0.001f ? extension : 0f;
    }

    float LimitMeleeApproach(float distance)
    {
        Vector3 halfSize = Vector3.Scale(meleeSize, AbsScale()) * 0.5f;
        foreach (var hit in Physics.BoxCastAll(MeleeBoxCenter(0f), halfSize, strikeRotation * Vector3.forward,
            strikeRotation, distance, hitLayers, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(transform) && hit.collider.GetComponentInParent<CombatHealth>() == null)
                distance = Mathf.Min(distance, Mathf.Max(0f, hit.distance - 0.03f));
        return distance;
    }

    void TickMelee(float deltaTime)
    {
        if (!meleePending || deltaTime <= 0f) return;
        if (assistedTarget == null || !assistedTarget.isActiveAndEnabled
            || movement != null && (movement.IsQuickBoosting || !movement.IsGrounded))
        {
            meleePending = false;
            meleeExtension = 0f;
            MeleePerformed?.Invoke(0);
            return;
        }
        approachTime += deltaTime;
        Physics.SyncTransforms();
        meleeExtension = LimitMeleeApproach(approachDistance * Mathf.Clamp01(approachTime / meleeApproachDuration));
        if (approachTime >= meleeApproachDuration) ResolveMelee();
    }

    Vector3 MeleeBoxCenter(float extension) => transform.position
        + strikeRotation * (Vector3.Scale(meleeCenter, AbsScale()) + Vector3.forward * extension);

    void ResolveMelee()
    {
        flashTimer = 0.15f;
        damaged.Clear();
        Vector3 center = MeleeBoxCenter(meleeExtension);
        Vector3 halfSize = Vector3.Scale(meleeSize, AbsScale()) * 0.5f;
        int count;
        while ((count = Physics.OverlapBoxNonAlloc(center, halfSize, meleeCandidates,
            strikeRotation, hitLayers, QueryTriggerInteraction.Ignore)) == meleeCandidates.Length)
            Array.Resize(ref meleeCandidates, meleeCandidates.Length * 2);
        int hitCount = 0;
        for (int i = 0; i < count; i++)
        {
            var candidate = meleeCandidates[i];
            if (candidate.transform.IsChildOf(transform)) continue;
            var health = candidate.GetComponentInParent<CombatHealth>();
            if (health == null || damaged.Contains(health) || BlockedByWorld(candidate)) continue;
            damaged.Add(health);
            if (health.ApplyDamage(meleeDamage)) hitCount++;
        }
        Array.Clear(meleeCandidates, 0, count);
        meleePending = false;
        MeleePerformed?.Invoke(hitCount);
        UpdateMeleeOutline();
    }

    bool BlockedByWorld(Collider candidate)
    {
        Vector3 origin = transform.position + Vector3.up * muzzleOffset.y;
        Vector3 displacement = candidate.ClosestPoint(origin) - origin;
        if (displacement.sqrMagnitude < 0.0001f) return false;
        foreach (var hit in Physics.RaycastAll(origin, displacement.normalized, displacement.magnitude,
            hitLayers, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(transform) && hit.collider.GetComponentInParent<CombatHealth>() == null)
                return true;
        return false;
    }

    Vector3 AbsScale()
    {
        var scale = transform.lossyScale;
        return new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
    }

    void CreateMeleeOutline()
    {
        var outline = new GameObject("Melee Hitbox Preview");
        outline.transform.SetParent(transform, false);
        meleeOutline = outline.AddComponent<LineRenderer>();
        meleeOutline.useWorldSpace = true;
        meleeOutline.sharedMaterial = combatMaterial;
        meleeOutline.startWidth = meleeOutline.endWidth = 0.05f;
        meleeOutline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meleeOutline.receiveShadows = false;
        var ground = new GameObject("Melee Ground Reach");
        ground.transform.SetParent(transform, false);
        groundOutline = ground.AddComponent<LineRenderer>();
        groundOutline.sharedMaterial = combatMaterial;
        groundOutline.useWorldSpace = true;
        groundOutline.positionCount = 5;
        groundOutline.startWidth = groundOutline.endWidth = 0.055f;
        groundOutline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        groundOutline.receiveShadows = false;
        UpdateMeleeOutline();
    }

    void UpdateMeleeOutline()
    {
        if (meleeOutline == null) return;
        if (!meleePending && flashTimer <= 0f) strikeRotation = transform.rotation;
        meleeOutline.enabled = showMeleeBox;
        groundOutline.enabled = showMeleeBox;
        // Walk every edge of the box, retracing only the edges needed to connect them.
        meleeOutline.positionCount = BoxCorners.Length;
        for (int i = 0; i < BoxCorners.Length; i++)
        {
            int c = BoxCorners[i];
            Vector3 corner = new Vector3((c & 1) == 0 ? -0.5f : 0.5f,
                (c & 2) == 0 ? -0.5f : 0.5f, (c & 4) == 0 ? -0.5f : 0.5f);
            meleeOutline.SetPosition(i, MeleeBoxCenter(meleeExtension) + strikeRotation * Vector3.Scale(corner, Vector3.Scale(meleeSize, AbsScale())));
        }
        meleeOutline.startColor = meleeOutline.endColor = flashTimer > 0f
            ? new Color(1f, 0.9f, 0.25f) : meleePending ? new Color(1f, 0.55f, 0.15f)
            : new Color(0.2f, 0.8f, 1f, 0.7f);
        groundOutline.startColor = groundOutline.endColor = meleeOutline.startColor;
        float groundY = movementCapsule != null ? movementCapsule.bounds.min.y + 0.04f : transform.position.y - 1.46f;
        for (int i = 0; i < GroundCorners.Length; i++)
        {
            int c = GroundCorners[i];
            Vector3 point = MeleeBoxCenter(meleeExtension) + strikeRotation * Vector3.Scale(
                new Vector3((c & 1) == 0 ? -0.5f : 0.5f, 0f, (c & 4) == 0 ? -0.5f : 0.5f), Vector3.Scale(meleeSize, AbsScale()));
            point.y = groundY;
            groundOutline.SetPosition(i, point);
        }
    }

    void OnValidate()
    {
        meleeSize = Vector3.Max(Vector3.one * 0.01f, meleeSize);
        fireInterval = Mathf.Max(0.01f, fireInterval);
        meleeCooldown = Mathf.Max(0.01f, meleeCooldown);
        maximumMeleeApproach = Mathf.Max(0f, maximumMeleeApproach);
        meleeApproachDuration = Mathf.Max(0.01f, meleeApproachDuration);
    }
}
