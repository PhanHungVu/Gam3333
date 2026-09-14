# Mech controller foundation

The current controls are the baseline. Mouse input controls camera rotation independently of enemy selection. The visible cursor follows the selected enemy. Center aim selects a replacement; moving the camera alone does not release a valid target. With no target the cursor returns to the gameplay camera viewport center.

WASD movement is always camera-relative, including while an enemy is selected. Movement has no dependency on the targeting controller. Selecting, switching, moving, or losing an enemy cannot steer locomotion or Quick Boost. Body facing remains camera-relative during normal movement and follows the captured dodge direction during Quick Boost. Idle facing does not automatically turn toward enemies.

## Responsibilities

- `MechMovementController`: reads movement input once per frame, calculates direction, advances the motor, resolves CharacterController movement, and publishes state/telemetry. Separate methods handle planar motion, vertical motion, and facing.
- `CameraControlThirdPerson`: owns the independent player anchor and Cinemachine rig, mouse look, normal framing, and Quick Boost follow damping. It has no combat-target reference or enemy-driven profiles.
- `MechTargetLockController`: owns selection and publishes target changes. Collider storage grows to accommodate crowds and is reused after that. It does not create or modify UI.
- `LockOnCursor`: projects the selected aim point through the gameplay camera into its actual UI parent. `A33` explicitly assigns its cursor, canvas, camera, and targeting controller. Dynamically spawned HUDs can call `Bind`.
- `MechTargetable`: supplies an enemy aim position. `AutoMoveTarget` remains an active test-scene helper.

Update order is movement in Update, camera rig in LateUpdate (-100), Cinemachine at its default order, selection (100), then cursor projection (200).

## Extension points

Movement states currently describe implemented actions only: `Grounded` (idle/walking), `GroundBoost` (sprinting with movement input), `Airborne`, and `QuickBoost`.

| Signal | Meaning / intended consumer |
| --- | --- |
| `StateChanged(previous, next)` | Fires only on transitions; animation and sustained effects. |
| `QuickBoostStarted(direction)` | Captured world-space dodge direction; ignition audio, trails, and a future FOV pulse. |
| `QuickBoostEnded()` | Completion or controller-disable cancellation; shut down transient effects. |
| `Landed(impactSpeed)` | Positive downward speed before contact; landing animation, dust, audio, and optional impulse. |
| `TargetChanged(previous, next)` | Selection changed or cleared; weapons and HUD. A destroyed target compares equal to null under Unity equality. |

`Velocity` is actual world displacement per second after collision resolution. `MoveDirection` and `FacingDirection` are separate intentions. `QuickBoostProgress` is normalized while boosting and returns zero when inactive. `QuickBoostCooldownRemaining` includes active boost time. Consumers should subscribe on enable and unsubscribe on disable, and read the current state on subscription.

Per-frame movement events are published after motion and state have been updated. A boost with zero recovery can end and restart in the same frame; listeners may receive ended then started while the final state is still QuickBoost.

## Preserved tuning and next work

The serialized `normalProfile` name and its members are retained to preserve existing scenes. It is the single active camera settings block; obsolete lock profiles, duplicate inactive fields, and redundant profile blending have been removed. Movement speeds, acceleration, boost curve, duration/recovery, multipliers, and free-look sensitivity are retained.

Build the next features incrementally:

1. Configure camera obstacle avoidance and target line of sight with explicit environment layers, plus an expanded test arena.
2. Drive Quick Boost presentation from its event and progress. Keep camera rotation manual.
3. Extend vertical motion with ascent, air dodges, and fast descent, including ceiling/ground contact handling.
4. Add energy checks at action admission and drain during sustained movement.
5. Add Assault Boost as its own movement state with explicit transitions and steering rules.
6. Introduce a visual child/torso rig for banking, recoil, and weapon aiming independently of the motor.

Do not introduce camera hard lock, enemy-driven camera framing, target-relative locomotion, or threat-based automatic target switching as part of these extensions.

## Regression checks

`Tests/Editor/MechFoundationChecks.cs` contains Unity play-mode checks for targeting, UI projection, crowd-buffer growth, movement tuning, boost lifecycle, landing signals, and camera rig lifecycle.

Run in an isolated Unity 6000.5.8f1 project containing the Scripts and Tests folders and the same Cinemachine/Input System/uGUI package versions:

```
Unity.exe -batchmode -nographics -projectPath <isolated-project> -executeMethod MechFoundationChecks.RunBatch -logFile <test-log>
```

Do not add `-quit`: the runner enters Play mode asynchronously and exits with code 0 or 1 when finished. The batch entry point intentionally rejects interactive use. Tests create their own objects and do not load or save the working scene.
