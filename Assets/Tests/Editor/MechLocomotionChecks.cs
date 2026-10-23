using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class MechLocomotionChecks
{
    public static void Run(Action<bool, string> check)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/FullPlayer_mech_base.prefab");
        CheckBoostStopping(check, prefab);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.transform.position = new Vector3(30000, -0.5f, 0);
        floor.transform.localScale = new Vector3(500, 1, 500);
        var view = new GameObject("Locomotion Check View");
        var player = Object.Instantiate(prefab, new Vector3(30000, 1.6f, 0), Quaternion.identity);
        var motor = player.GetComponent<MechMovementController>();
        var equipment = player.GetComponent<EquipManager>();
        var debug = player.GetComponent<MechLocomotionDebug>();
        var energy = player.GetComponent<MechEnergyController>();
        var combat = player.GetComponent<MechCombat>();
        var controller = player.GetComponent<CharacterController>();
        GameObject wall = null;
        Part dropped = null;
        try
        {
            check(motor.CurrentModule is BipedLocomotionModule, "Starting equipped legs select biped without changing the root motor");
            foreach (string name in new[] { "Biped", "Hover", "Tank" })
            {
                var module = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Locomotion/" + name + "Module.prefab");
                check(module != null && module.GetComponent<Part>().partType == PartType.Legs
                    && module.GetComponent<LocomotionModule>() != null
                    && module.GetComponentsInChildren<MechMovementController>(true).Length == 0
                    && module.GetComponentsInChildren<CharacterController>(true).Length == 0,
                    name + " is an ordinary equippable Legs prefab with no second motor/capsule");
            }
            Physics.SyncTransforms();
            Step(motor, view, 90);
            Step(motor, view, 60, Vector2.up);
            check(motor.IsGrounded && Near(motor.AnimationData.Speed, 6f), "Biped retains the existing 6 m/s ground speed");
            var torso = equipment.GetEquipped(PartCategory.Torso);
            Vector3 torsoPosition = torso.transform.localPosition;
            Vector3 position = player.transform.position;
            Quaternion rotation = player.transform.rotation;
            Vector3 velocity = motor.Velocity;
            float charge = energy.Current;
            int ammo = combat.AmmoRemaining(PartCategory.ShoulderL);
            var inputField = typeof(MechMovementController).GetField("runtimeInputActions", BindingFlags.Instance | BindingFlags.NonPublic);
            object inputInstance = inputField.GetValue(motor);
            check(debug.TrySwap(2), "Development swap selects hover through EquipManager");
            check(player.transform.position == position && player.transform.rotation == rotation && motor.Velocity == velocity,
                "Moving lower-body swap preserves root pose and resolved velocity");
            check(equipment.GetEquipped(PartCategory.Torso) == torso && torso.transform.localPosition == torsoPosition
                && combat.AmmoRemaining(PartCategory.ShoulderL) == ammo && energy.Current == charge,
                "Lower-body swap preserves torso, ammunition and energy");
            check(ReferenceEquals(inputField.GetValue(motor), inputInstance) && controller == player.GetComponent<CharacterController>()
                && player.GetComponentsInChildren<MechMovementController>(true).Length == 1 && player.GetComponent<Rigidbody>() == null,
                "Swap keeps the same input instance and one root CharacterController motor");
            check(motor.AnimationData.LocomotionType == LocomotionType.Hover, "Animation module identity updates immediately on equip");

            Step(motor, view, 150, Vector2.up);
            check(!motor.IsGrounded && Near(motor.AnimationData.Speed, 12f) && player.transform.position.y > 2f,
                "Hover glides at 12 m/s above actual ground contact");
            float oldSpeed = motor.AnimationData.Speed;
            Step(motor, view);
            check(Near(motor.AnimationData.Speed, oldSpeed - 0.12f), "Hover retains momentum with lower braking acceleration");
            Step(motor, view, 150);
            float hoverHeight = player.transform.position.y;
            Step(motor, view, 120, held: true);
            check(player.transform.position.y > hoverHeight + 1.7f, "Holding the generic jump command increases hover clearance");
            Step(motor, view, 150);
            check(Mathf.Abs(player.transform.position.y - hoverHeight) < 0.08f, "Releasing lift settles hover at a stable clearance");
            Step(motor, view, 60, Vector2.up, sprint: true);
            check(!motor.AnimationData.IsBoosting && !motor.AnimationData.IsGrounded && motor.AnimationData.Speed < 17f,
                "Removed sprint does not activate hover boost");
            Step(motor, view, 1, Vector2.right, boost: true);
            check(motor.IsQuickBoosting && energy.Current < charge, "Hover accepts airborne quick boost and spends existing energy");
            float cooldown = motor.QuickBoostCooldownRemaining;
            velocity = motor.Velocity;
            charge = energy.Current;
            check(debug.TrySwap(3) && motor.IsQuickBoosting && motor.QuickBoostCooldownRemaining == cooldown
                && motor.Velocity == velocity && energy.Current == charge, "Swap during boost preserves its captured motion, timer and energy");
            Step(motor, view, 160);
            check(motor.IsGrounded && motor.LocomotionType == LocomotionType.Tank, "Tank resumes gravity and lands after a hover swap");
            float tankHeight = player.transform.position.y;
            Step(motor, view, 1, jump: true, held: true);
            check(motor.IsGrounded && player.transform.position.y <= tankHeight + 0.01f, "Tank ignores jump without moving or lifting its root");
            Step(motor, view, 90, Vector2.up);
            check(Near(motor.AnimationData.Speed, 4.5f), "Tank uses its slower 4.5 m/s travel speed");
            Step(motor, view, 60);
            Vector3 forward = player.transform.forward;
            Step(motor, view, 1, Vector2.left, boost: true);
            check(motor.IsQuickBoosting && Vector3.Dot(motor.Velocity.normalized, forward) > 0.95f,
                "Tank converts a sideways boost command into a forward burst");
            Step(motor, view, 120);

            check(debug.TrySwap(0), "Biped can be restored after repeated module changes");
            Step(motor, view, 90, Vector2.right);
            var data = motor.AnimationData;
            check(data.SideSpeed > 5.8f && Mathf.Abs(data.ForwardSpeed) < 0.1f && data.IsGrounded,
                "Animation data distinguishes root-local strafe from forward travel");
            Step(motor, view, 90, Vector2.down);
            check(motor.AnimationData.ForwardSpeed < -5.8f, "Animation forward speed is signed for backward travel");
            view.transform.rotation = Quaternion.Euler(0, 90, 0);
            Step(motor, view, 1, Vector2.up);
            check(motor.AnimationData.IsTurning && motor.AnimationData.TurnRate > 1f
                && Vector3.Dot(motor.AnimationData.AimDirection, Vector3.right) > 0.99f,
                "Animation reports actual signed yaw and world camera aim");
            Step(motor, view, 120);
            check(motor.AnimationData.Speed < 0.01f && !motor.AnimationData.IsTurning
                && motor.AnimationData.VerticalSpeed == 0f, "Idle data settles without ground-stick vertical noise");
            Step(motor, view, 1, jump: true);
            check(!motor.AnimationData.IsGrounded && motor.AnimationData.VerticalSpeed > 0f, "Biped jump publishes upward motion and loses ground contact");
            Step(motor, view, 20);
            check(motor.AnimationData.VerticalSpeed < 0f, "Falling publishes negative vertical motion");
            Step(motor, view, 100);

            wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = player.transform.position + Vector3.right * 4f;
            wall.transform.localScale = new Vector3(1, 8, 12);
            Physics.SyncTransforms();
            Step(motor, view, 150, Vector2.up);
            check(motor.MoveDirection.sqrMagnitude > 0.9f && motor.AnimationData.Speed < 0.05f,
                "Wall-blocked movement publishes actual zero speed despite full input");
            Object.DestroyImmediate(wall); wall = null;
            var snapshot = motor.AnimationData;
            Step(motor, view, 1, Vector2.up, dt: 0f);
            check(motor.AnimationData.Speed == snapshot.Speed && motor.AnimationData.TurnRate == snapshot.TurnRate,
                "Paused/zero-time ticks preserve a finite animation snapshot");

            dropped = equipment.Unequip(PartCategory.Legs);
            check(motor.CurrentModule == null && motor.AnimationData.LocomotionType == LocomotionType.Biped,
                "Removing legs selects the documented legacy biped fallback");
            check(equipment.TryEquip(dropped) && motor.CurrentModule == dropped.GetComponent<LocomotionModule>(),
                "Ordinary pickup path restores locomotion without using debug shortcuts");
            dropped = null;
            var moduleComponent = motor.CurrentModule;
            moduleComponent.enabled = false;
            Step(motor, view);
            check(motor.CurrentModule == null, "Disabled module safely falls back without a stale strategy");
            moduleComponent.enabled = true;
            Step(motor, view);
            check(motor.CurrentModule == moduleComponent, "Re-enabled module resumes without rebinding input");
            motor.enabled = false;
            check(motor.AnimationData.Speed == 0f && motor.AnimationData.TurnRate == 0f && !motor.AnimationData.IsBoosting,
                "Disabling movement clears transient animation values");
            motor.enabled = true;
            Step(motor, view, 90, Vector2.up);
            check(Near(motor.AnimationData.Speed, 6f), "Movement remains usable after re-enable");

            check(debug.TrySwap(2), "Hover reuses its stored module");
            Step(motor, view, 100);
            floor.SetActive(false);
            Physics.SyncTransforms();
            Step(motor, view, 20);
            check(motor.AnimationData.VerticalSpeed < -1f, "Hover falls under gravity when no surface is in probe range");
        }
        finally
        {
            if (wall != null) Object.DestroyImmediate(wall);
            if (dropped != null) Object.DestroyImmediate(dropped.gameObject);
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(floor);
            Object.DestroyImmediate(view);
        }
    }

    static void CheckBoostStopping(Action<bool, string> check, GameObject prefab)
    {
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.transform.position = new Vector3(90000f, -0.5f, 0f);
        floor.transform.localScale = new Vector3(500f, 1f, 500f);
        var view = new GameObject("Boost braking view");
        var player = Object.Instantiate(prefab, new Vector3(90000f, 1.6f, 0f), Quaternion.identity);
        var motor = player.GetComponent<MechMovementController>();
        motor.ReadPlayerInput = false;
        var energy = player.GetComponent<MechEnergyController>();
        try
        {
            Physics.SyncTransforms();
            Step(motor, view, 90);
            void Boost()
            {
                energy.TickRecovery(10f, true);
                for (int i = 0; i < 50; i++)
                    motor.Tick(new MechLocomotionInput { move = Vector2.up, assaultBoost = true }, view.transform, 0.02f);
                check(motor.IsAssaultBoosting && Near(motor.AnimationData.Speed, 28f),
                    "Stronger boost stopping preserves the full 28 m/s active boost");
            }
            Boost();
            Vector3 releasedAt = player.transform.position;
            Step(motor, view, 22);
            check(!motor.IsAssaultBoosting && motor.AnimationData.Speed < 0.02f
                && Vector3.Distance(player.transform.position, releasedAt) < 6.3f,
                "Released boost stops within 0.44 seconds and about six metres of travel");
            Step(motor, view, 60, Vector2.up);
            Step(motor, view);
            check(Near(motor.AnimationData.Speed, 5.36f),
                "Ordinary 32 m/s² walking braking resumes after boost momentum settles");
            Boost();
            Step(motor, view, 18, Vector2.up);
            check(!motor.IsAssaultBoosting && Near(motor.AnimationData.Speed, 6f),
                "Holding forward after boost returns to walking speed within 0.36 seconds");
            Step(motor, view, 90);
            energy.TickRecovery(10f, true);
            Step(motor, view, 1, Vector2.up, boost: true);
            check(motor.IsQuickBoosting && Near(motor.AnimationData.Speed, 27.3f),
                "Faster dash stopping preserves its authored ignition curve and speed");
            for (int i = 0; motor.IsQuickBoosting && i < 30; i++) Step(motor, view);
            releasedAt = player.transform.position;
            Step(motor, view, 6);
            check(motor.AnimationData.Speed < 0.02f && Vector3.Distance(player.transform.position, releasedAt) < 0.8f,
                "Dash residual momentum settles within 0.12 seconds after its active duration");
            check(player.GetComponent<MechLocomotionDebug>().TrySwap(1), "Heavy legs equip for boost stopping check");
            Step(motor, view, 90);
            Boost();
            Step(motor, view, 20, Vector2.up);
            check(Near(motor.AnimationData.Speed, 3f),
                "Post-boost braking returns heavy legs to their own 3 m/s travel speed");
        }
        finally
        {
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(view);
            Object.DestroyImmediate(floor);
        }
    }

    static bool Near(float value, float expected) => Mathf.Abs(value - expected) < 0.08f;
    static void Step(MechMovementController motor, GameObject view, int count = 1, Vector2 move = default,
        bool jump = false, bool held = false, bool boost = false, bool sprint = false, float dt = 0.02f)
    {
        for (int i = 0; i < count; i++)
            motor.Tick(new MechLocomotionInput { move = move, jump = jump && i == 0,
                jumpHeld = held, quickBoost = boost && i == 0, sprint = sprint }, view.transform, dt);
    }
}
