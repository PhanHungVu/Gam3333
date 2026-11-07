using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Events;
using System;

// The one place that reads the player's devices. Each frame it turns input into actions for the
// other systems: movement (move, jump, boosts), the camera (look), targeting (lock-on, switching),
// the part system (F picks a compatible slot, 1/2 swap left/right, V cycles the highlighted part) and combat (attack
// with the socket's part). It also owns the cursor rule: Esc frees the mouse, a click captures it,
// and gameplay input only flows while it is captured and the game is not paused.
[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)]
public sealed class MechPlayerInput : MonoBehaviour
{
    [SerializeField] InputActionAsset inputActions;
    [SerializeField] bool lockCursorOnPlay = true;
    [Header("Part ability requests — gameplay supplies head/torso effects")]
    [SerializeField] UnityEvent<Part> headAbilityRequested = new UnityEvent<Part>();
    [SerializeField] UnityEvent<Part> torsoAbilityRequested = new UnityEvent<Part>();
    [SerializeField] UnityEvent<Part> legAbilityRequested = new UnityEvent<Part>();
    [Tooltip("Built-in jump / hover lift on Space, used while the equipped legs have no ability of their own (legs with an ability, e.g. Jump, use that instead).")]
    [SerializeField] bool useBuiltInLegMovement = true;
    public event Action<PartCategory, Part> AbilityRequested;
    // Batch Unity cannot capture a native cursor; tests set this to run the real input path.
    public bool AssumeCursorCaptured { get; set; }

    InputActionAsset runtimeInputActions;
    InputAction move, jump, quickBoost, look, lockOn, reload, headAbility, torsoAbility;
    InputAction leftArm, rightArm, leftShoulder, rightShoulder, swapLeft, swapRight, cyclePart, pickupAction;
    MechMovementController movement;
    MechCombat combat;
    EquipManager parts;
    PickupDetector pickup;
    RemotePartRetrieval retrieval;
    MechTargetLockController targeting;
    CameraControlThirdPerson cameraControl;
    bool attacksArmed;
    MechLocomotionDebug locomotionDebug;

    // Presentation queries only. The existing Update/Attack path still owns all input.
    public string SwapBindingLabel(PartCategory socket)
    {
        var button=socket==PartCategory.ArmR || socket==PartCategory.ShoulderR ? swapRight : swapLeft;
        return button!=null && button.enabled ? DisplayBinding(button,"<Keyboard>") : null;
    }
    public string CycleBindingLabel => cyclePart!=null && cyclePart.enabled ? DisplayBinding(cyclePart,"<Keyboard>") : null;
    public string PickupBindingLabel => pickupAction!=null && pickupAction.enabled ? DisplayBinding(pickupAction,"<Keyboard>") : null;
    static string DisplayBinding(InputAction action,string preferredDevice)
    {
        string fallback=null; bool keyboardMouseFallback=false;
        for(int i=0;i<action.bindings.Count;i++)
        {
            var binding=action.bindings[i]; string path=binding.effectivePath;
            if(binding.isComposite || binding.isPartOfComposite || string.IsNullOrEmpty(path)) continue;
            string label=action.GetBindingDisplayString(i); if(string.IsNullOrEmpty(label)) continue;
            if(path.StartsWith(preferredDevice,System.StringComparison.OrdinalIgnoreCase)) return label;
            bool keyboardMouse=path.StartsWith("<Keyboard>",System.StringComparison.OrdinalIgnoreCase)||path.StartsWith("<Mouse>",System.StringComparison.OrdinalIgnoreCase);
            if(fallback==null || (keyboardMouse&&!keyboardMouseFallback)) { fallback=label; keyboardMouseFallback=keyboardMouse; }
        }
        return fallback;
    }

    void Awake()
    {
        movement = GetComponent<MechMovementController>();
        locomotionDebug = GetComponent<MechLocomotionDebug>();
        combat = GetComponent<MechCombat>();
        parts = GetComponent<EquipManager>();
        retrieval = GetComponent<RemotePartRetrieval>();
        pickup = GetComponentInChildren<PickupDetector>(true);
        targeting = GetComponent<MechTargetLockController>();
    }

    void OnEnable()
    {
        runtimeInputActions = MechInputActions.Create(inputActions);
        if (runtimeInputActions == null) { Debug.LogWarning("MechPlayerInput has no input actions assigned.", this); return; }
        move = Enable("Player/Move"); jump = Enable("Player/Jump");
        quickBoost = Enable("Player/QuickBoost");
        reload = Enable("Player/Reload"); headAbility = Enable("Player/HeadAbility"); torsoAbility = Enable("Player/TorsoAbility");
        look = Enable("Player/Look"); lockOn = Enable("Player/LockOn");
        leftArm = Enable("Player/Melee"); rightArm = Enable("Player/Attack");
        leftShoulder = Enable("Player/Cannon"); rightShoulder = Enable("Player/Crossbow");
        swapLeft = Enable("Player/SwapLeft"); swapRight = Enable("Player/SwapRight"); cyclePart = Enable("Player/CyclePart");
        pickupAction = Enable("Player/Pickup");
    }

    InputAction Enable(string name)
    {
        var action = runtimeInputActions.FindAction(name, false);
        action?.Enable();
        return action;
    }

    void Start() { if (lockCursorOnPlay) SetCursorLocked(true); }

    void OnDisable()
    {
        retrieval?.Cancel();
        MechInputActions.Release(ref runtimeInputActions);
        move = jump = quickBoost = look = lockOn = reload = headAbility = torsoAbility = null;
        leftArm = rightArm = leftShoulder = rightShoulder = swapLeft = swapRight = cyclePart = pickupAction = null;
        targeting?.CancelLockButtonInput();
        attacksArmed = false;
        SetCursorLocked(false);
    }

    void Update()
    {
        if (runtimeInputActions == null) return;
        if (MissionOutcomeController.BlocksGameplay(gameObject.scene))
        {
            attacksArmed = false; cameraControl?.SetLookInput(Vector2.zero); SetCursorLocked(false); return;
        }
        // Cursor rule: Esc frees the mouse, a click captures it again.
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) SetCursorLocked(false);
        else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame
            && (locomotionDebug == null || !locomotionDebug.PointerOverToggle)) SetCursorLocked(true);
        bool gameplay = (AssumeCursorCaptured || Cursor.lockState == CursorLockMode.Locked) && Time.timeScale > 0f;

        if (gameplay && cyclePart != null && pickup != null && Pressed(cyclePart)) pickup.CycleSelection();

        Vector2 lookDelta = gameplay ? Read(look) : Vector2.zero;
        if (cameraControl == null) cameraControl = FindCamera();
        if (cameraControl != null) cameraControl.SetLookInput(lookDelta);

        if (targeting != null)
        {
            if (!gameplay) targeting.CancelLockButtonInput();
            else
            {
                bool pressed = Pressed(lockOn), held = Held(lockOn), released = lockOn != null && lockOn.WasReleasedThisFrame();
                targeting.ProcessLockButton(pressed, held, released, Time.unscaledDeltaTime);
                if (!pressed && !held && !released) targeting.ProcessSwitchInput(lookDelta, Time.unscaledDeltaTime);
            }
        }

        if(gameplay && !CombatHitStop.BlocksNewActions(transform))
        {
            if(Pressed(swapLeft)) SwapSelected(false);
            else if(Pressed(swapRight)) SwapSelected(true);
            else if(Pressed(pickupAction)) RequestPickup();
            if (Pressed(reload) && combat != null) combat.ReloadWeapons();
            if (Pressed(headAbility)) RequestHeadAbility();
            if (Pressed(torsoAbility)) RequestTorsoAbility();
            if (Pressed(jump)) RequestLegAbility();
        }
        if (!gameplay) { attacksArmed = false; movement?.ClearHitStopInputBuffer(); }
        else if (!attacksArmed)
            // A click that captures the mouse must not also attack.
            attacksArmed = !Held(leftArm) && !Held(rightArm) && !Held(leftShoulder) && !Held(rightShoulder);
        else if (combat != null)
        {
            Attack(leftArm, PartCategory.ArmL, repeatWhileHeld: true);
            Attack(rightArm, PartCategory.ArmR, repeatWhileHeld: true);
            Attack(leftShoulder, PartCategory.ShoulderL);
            Attack(rightShoulder, PartCategory.ShoulderR);
        }
        // Attacks run first so a successful attack cancels running before this
        // frame's movement, including when the attack came from another caller.
        if (movement != null && movement.isActiveAndEnabled)
            movement.Tick(new MechLocomotionInput
            {
                move = gameplay ? Vector2.ClampMagnitude(Read(move), 1f) : Vector2.zero,
                jump = gameplay && UsesBuiltInLegMovement && Pressed(jump),
                jumpHeld = gameplay && UsesBuiltInLegMovement && Held(jump),
                quickBoost = gameplay && Pressed(quickBoost)
            }, Time.deltaTime);
    }

    // Public connection points. Effects remain unassigned until gameplay wires
    // the Inspector events or subscribes to AbilityRequested.
    public void RequestHeadAbility() => RequestAbility(PartCategory.Head, headAbilityRequested);
    public void RequestTorsoAbility() => RequestAbility(PartCategory.Torso, torsoAbilityRequested);
    public void RequestLegAbility() => RequestAbility(PartCategory.Legs, legAbilityRequested);

    // Space drives the built-in jump / hover lift unless the equipped legs bring their own ability.
    bool UsesBuiltInLegMovement
    {
        get
        {
            if (!useBuiltInLegMovement) return false;
            var legs = parts != null ? parts.GetEquipped(PartCategory.Legs) : null;
            return legs == null || legs.Stats == null || legs.Stats.ability == PartAbility.None;
        }
    }

    void RequestAbility(PartCategory socket, UnityEvent<Part> callback)
    {
        if (!isActiveAndEnabled || Time.timeScale <= 0f || MissionOutcomeController.BlocksGameplay(gameObject.scene)
            || CombatHitStop.BlocksNewActions(transform)) return;
        var part = parts != null ? parts.GetEquipped(socket) : null;
        if (part == null || part.Stats == null || !part.isActiveAndEnabled || !part.isEquipped) return;
        callback?.Invoke(part); AbilityRequested?.Invoke(socket, part);
    }

    // Remote travel owns F pickup. Nearby 1/2 swaps still use the ordinary immediate transaction.
    public bool RequestPickup()
    {
        if (!isActiveAndEnabled || Time.timeScale <= 0 || MissionOutcomeController.BlocksGameplay(gameObject.scene)
            || CombatHitStop.BlocksNewActions(transform)) return false;
        Physics.SyncTransforms();
        if (retrieval == null || !retrieval.isActiveAndEnabled || pickup == null || !pickup.isActiveAndEnabled) return false;
        retrieval.RefreshTarget(); pickup.RefreshSelection();
        return retrieval.TryStart(parts != null ? parts.GetSelectedPart() : null);
    }

    void SwapSelected(bool right)
    {
        var selected=parts!=null ? parts.GetSelectedPart() : null;
        if(selected==null || selected.Stats==null) return;
        Physics.SyncTransforms();
        if(pickup==null || !pickup.IsNearby(selected)) return;
        var slots=PartSlots.SlotsFor(selected.partType);
        int index=right ? 1 : 0;
        if(index<slots.Count) parts.TrySwapSelectedInto(slots[index]);
    }
    void Attack(InputAction button, PartCategory socket, bool repeatWhileHeld = false)
    {
        if (!(repeatWhileHeld ? Held(button) : Pressed(button))) return;
        Physics.SyncTransforms();
        combat.TryAttack(socket);
    }

    // The camera that follows this player (works with several players and cameras).
    CameraControlThirdPerson FindCamera()
    {
        foreach (var candidate in FindObjectsByType<CameraControlThirdPerson>())
            if (candidate.Target == transform) return candidate;
        return null;
    }

    static Vector2 Read(InputAction action) => action != null ? action.ReadValue<Vector2>() : Vector2.zero;
    static bool Pressed(InputAction action) => action != null && action.WasPressedThisFrame();
    static bool Held(InputAction action) => action != null && action.IsPressed();

    static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
