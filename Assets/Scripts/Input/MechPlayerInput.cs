using UnityEngine;
using UnityEngine.InputSystem;

// The one place that reads the player's devices. Each frame it turns input into actions for the
// other systems: movement (move, jump, boosts), the camera (look), targeting (lock-on, switching),
// the part system (hold F + attack button swaps, V cycles the highlighted part) and combat (attack
// with the socket's part). It also owns the cursor rule: Esc frees the mouse, a click captures it,
// and gameplay input only flows while it is captured and the game is not paused.
[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)]
public sealed class MechPlayerInput : MonoBehaviour
{
    [SerializeField] InputActionAsset inputActions;
    [SerializeField] bool lockCursorOnPlay = true;
    // Batch Unity cannot capture a native cursor; tests set this to run the real input path.
    public bool AssumeCursorCaptured { get; set; }

    InputActionAsset runtimeInputActions;
    InputAction move, jump, quickBoost, assaultBoost, look, lockOn;
    InputAction leftArm, rightArm, leftShoulder, rightShoulder, swapPart, cyclePart;
    MechMovementController movement;
    MechCombat combat;
    EquipManager parts;
    PickupDetector pickup;
    MechTargetLockController targeting;
    CameraControlThirdPerson cameraControl;
    bool attacksArmed;

    void Awake()
    {
        movement = GetComponent<MechMovementController>();
        combat = GetComponent<MechCombat>();
        parts = GetComponent<EquipManager>();
        pickup = GetComponentInChildren<PickupDetector>(true);
        targeting = GetComponent<MechTargetLockController>();
    }

    void OnEnable()
    {
        runtimeInputActions = MechInputActions.Create(inputActions);
        if (runtimeInputActions == null) { Debug.LogWarning("MechPlayerInput has no input actions assigned.", this); return; }
        move = Enable("Player/Move"); jump = Enable("Player/Jump");
        quickBoost = Enable("Player/QuickBoost"); assaultBoost = Enable("Player/AssaultBoost");
        look = Enable("Player/Look"); lockOn = Enable("Player/LockOn");
        leftArm = Enable("Player/Melee"); rightArm = Enable("Player/Attack");
        leftShoulder = Enable("Player/Cannon"); rightShoulder = Enable("Player/Crossbow");
        swapPart = Enable("Player/SwapPart"); cyclePart = Enable("Player/CyclePart");
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
        MechInputActions.Release(ref runtimeInputActions);
        move = jump = quickBoost = assaultBoost = look = lockOn = null;
        leftArm = rightArm = leftShoulder = rightShoulder = swapPart = cyclePart = null;
        targeting?.CancelLockButtonInput();
        attacksArmed = false;
        SetCursorLocked(false);
    }

    void Update()
    {
        if (runtimeInputActions == null) return;
        // Cursor rule: Esc frees the mouse, a click captures it again.
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) SetCursorLocked(false);
        else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) SetCursorLocked(true);
        bool gameplay = (AssumeCursorCaptured || Cursor.lockState == CursorLockMode.Locked) && Time.timeScale > 0f;

        if (movement != null && movement.isActiveAndEnabled)
            movement.Tick(new MechLocomotionInput
            {
                move = Vector2.ClampMagnitude(Read(move), 1f),
                jump = Pressed(jump),
                jumpHeld = Held(jump),
                quickBoost = Pressed(quickBoost),
                assaultBoost = Held(assaultBoost)
            }, Time.deltaTime);

        if (cyclePart != null && pickup != null && Pressed(cyclePart)) pickup.CycleSelection();

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

        if (combat == null) return;
        if (!gameplay) { attacksArmed = false; return; }
        // A click that captures the mouse must not also attack.
        if (!attacksArmed) { attacksArmed = !Held(leftArm) && !Held(rightArm) && !Held(leftShoulder) && !Held(rightShoulder); return; }
        // Holding SwapPart with a floor part highlighted turns each attack button into
        // "swap the highlighted part into that button's socket" instead of attacking.
        bool swap = Held(swapPart) && parts != null && parts.GetSelectedPart() != null;
        Attack(leftArm, PartCategory.ArmL, swap);          // LMB: left arm
        Attack(rightArm, PartCategory.ArmR, swap);         // RMB: right arm
        Attack(leftShoulder, PartCategory.ShoulderL, swap); // Q: left shoulder
        Attack(rightShoulder, PartCategory.ShoulderR, swap); // E: right shoulder
    }

    void Attack(InputAction button, PartCategory socket, bool swap)
    {
        if (!Pressed(button)) return;
        if (swap) { parts.TrySwapSelectedInto(socket); return; }
        Physics.SyncTransforms();
        combat.TryAttack(socket);
    }

    // The camera that follows this player (works with several players and cameras).
    CameraControlThirdPerson FindCamera()
    {
        foreach (var candidate in FindObjectsByType<CameraControlThirdPerson>(FindObjectsSortMode.None))
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
