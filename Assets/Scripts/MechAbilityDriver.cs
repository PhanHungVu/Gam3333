using UnityEngine;
using UnityEngine.InputSystem;

// Optional testing/equipment adapter. Any pattern can be assigned; no default bindings or inventory.
public sealed class MechAbilityDriver : MonoBehaviour
{
    [SerializeField] MechPartAbility ability;
    [SerializeField] Transform owner;
    [SerializeField] Transform origin;
    [SerializeField] Vector3 localOriginOffset;
    [SerializeField] Camera gameplayCamera;
    [SerializeField] LayerMask hitLayers = ~0;
    [SerializeField] bool originateAtAimPoint;
    [SerializeField, Min(0.1f)] float aimDistance = 20;
    [Header("Optional Test Input")]
    [SerializeField] bool enableTestKey;
    [SerializeField] Key testKey = Key.None;
    [SerializeField, Min(0)] float activationCooldown = 2;
    [Header("Optional Weapon Impact Adapter")]
    [SerializeField] bool activateOnWeaponImpact;
    [SerializeField] MechWeaponType sourceWeapon;
    MechCombatController combat;
    MechTargetLockController targeting;
    CombatFeedback feedback;
    readonly AbilityPhysics queries = new AbilityPhysics();
    float nextActivation;

    Part equippedPart;
    bool bound;
    void Awake()
    {
        if (ability == null) ability = GetComponent<MechPartAbility>();
        equippedPart = GetComponentInParent<Part>();
        if (!bound)
        {
            var parent = GetComponentInParent<MechCombatController>();
            BindOwner(equippedPart != null ? equippedPart.EquippedBy != null ? equippedPart.EquippedBy.transform : null
                : owner != null ? owner : parent != null ? parent.transform : transform.root);
        }
    }
    public void BindOwner(Transform nextOwner)
    {
        if (combat != null) combat.ProjectileImpact -= OnImpact;
        if (ability == null) ability = GetComponent<MechPartAbility>();
        ability?.Cancel();
        bound = true; owner = nextOwner;
        combat = owner != null ? owner.GetComponent<MechCombatController>() : null;
        targeting = owner != null ? owner.GetComponent<MechTargetLockController>() : null;
        feedback = owner != null ? owner.GetComponent<CombatFeedback>() : null;
        gameplayCamera = targeting != null && targeting.GameplayCamera != null ? targeting.GameplayCamera : Camera.main;
        if (isActiveAndEnabled && combat != null) combat.ProjectileImpact += OnImpact;
    }
    internal void SetAbility(MechPartAbility nextAbility) => ability = nextAbility;
    void OnEnable() { if (combat != null) { combat.ProjectileImpact -= OnImpact; combat.ProjectileImpact += OnImpact; } }
    void OnDisable() { if (combat != null) combat.ProjectileImpact -= OnImpact; ability?.Cancel(); }
    void Update()
    {
        if (enableTestKey && testKey != Key.None && Keyboard.current != null && Time.timeScale > 0
            && Keyboard.current[testKey].wasPressedThisFrame) Activate();
    }


    public float CooldownRemaining => Mathf.Max(0, nextActivation - Time.time);
    [ContextMenu("Activate Equipped Ability (Play Mode)")]
    public void Activate() => TryActivate();
    public bool TryActivate()
    {
        if (!Application.isPlaying || owner == null) return false;
        Vector3 position = origin != null ? origin.position : owner.TransformPoint(localOriginOffset);
        Vector3 direction = gameplayCamera != null ? gameplayCamera.transform.forward : owner.forward;
        MechTargetable target = targeting != null && !targeting.IsFreeAim ? targeting.CurrentTarget : null;
        Vector3 aim = target != null ? target.AimPosition : position + direction * aimDistance;
        if (target == null && gameplayCamera != null)
            queries.Sweep(gameplayCamera.transform.position, direction * aimDistance, 0.02f, owner, hitLayers, out aim, out _);
        direction = (aim - position).normalized;
        return ActivateAt(originateAtAimPoint ? aim : position, direction, target);
    }

    void OnImpact(MechProjectileImpact impact)
    {
        if (!activateOnWeaponImpact || impact.Weapon != sourceWeapon || impact.Secondary || impact.ConfirmationOnly) return;
        ActivateAt(impact.Position, impact.Direction, null);
    }

    bool ActivateAt(Vector3 position, Vector3 direction, MechTargetable target)
    {
        if (!isActiveAndEnabled || owner == null || !owner.gameObject.activeInHierarchy || ability == null || equippedPart != null && !equippedPart.isEquipped || Time.timeScale <= 0 || Time.time < nextActivation) return false;
        bool started = ability.TryActivate(new MechAbilityContext(owner, position, direction, target,
            combat != null ? combat.GetWeapon(sourceWeapon) : null, hitLayers, feedback));
        if (started) nextActivation = Time.time + Mathf.Max(0, activationCooldown);
        return started;
    }
}
