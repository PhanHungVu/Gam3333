# Mech controller foundation

The current controls are the baseline. Mouse input controls camera rotation independently of enemy selection. The visible cursor follows the selected enemy. Center aim selects a replacement; moving the camera alone does not release a valid target. With no target the cursor returns to the gameplay camera viewport center.

WASD movement is always camera-relative, including while an enemy is selected. Movement has no dependency on the targeting controller. Selecting, switching, moving, or losing an enemy cannot steer locomotion or Quick Boost. Body facing remains camera-relative during normal movement and follows the captured dodge direction during Quick Boost. Idle facing does not automatically turn toward enemies.

## Responsibilities

- `MechMovementController`: reads movement input once per frame, calculates direction, advances the motor, resolves CharacterController movement, and publishes state/telemetry. Separate methods handle planar motion, vertical motion, and facing.
- `CameraControlThirdPerson`: owns the independent player anchor and Cinemachine rig, mouse look, normal framing, and Quick Boost follow damping. It has no combat-target reference or enemy-driven profiles.
- `MechTargetLockController`: owns selection and publishes target changes. Collider storage grows to accommodate crowds and is reused after that. It does not create or modify UI.
- `LockOnCursor`: projects the selected aim point through the gameplay camera into its actual UI parent. `A33` explicitly assigns its cursor, canvas, camera, and targeting controller. Dynamically spawned HUDs can call `Bind`.
- `MechTargetable`: supplies an enemy aim position. `AutoMoveTarget` remains an active test-scene helper.
- `MechEnergyController`: boost costs, delayed recovery, and the energy change signal. The motor advances its recovery exactly once per movement tick.
- `MechBoostPresentation`: leans only `VisualMech` and manages exhaust/trails. The ignition audio slot is intentionally empty, so dashing is silent. Assign a clip later to enable boost audio; there is no generated fallback sound.
- `MechEnergyHud`: displays remaining energy on the scene Canvas.

Update order is movement in Update, camera rig in LateUpdate (-100), Cinemachine at its default order, selection (100), then cursor projection (200).

## Extension points

Movement states describe implemented actions only: `Grounded` (idle/walking), `GroundBoost` (sprinting with movement input), `Airborne` (existing jump/fall), `QuickBoost`, and ground-only `AssaultBoost`. There is no flight, sustained ascent, air dodge, or new combat system.

| Signal | Meaning / intended consumer |
| --- | --- |
| `StateChanged(previous, next)` | Fires only on transitions; animation and sustained effects. |
| `QuickBoostStarted(direction)` | Captured world-space dodge direction; ignition audio, trails, and a future FOV pulse. |
| `QuickBoostEnded()` | Completion or controller-disable cancellation; shut down transient effects. |
| `Landed(impactSpeed)` | Positive downward speed before contact; landing animation, dust, audio, and optional impulse. |
| `TargetChanged(previous, next)` | Selection changed or cleared; weapons and HUD. A destroyed target compares equal to null under Unity equality. |
| `AssaultBoostStarted()` / `AssaultBoostEnded()` | Continuous rush presentation; release, dodge, loss of ground, exhaustion, and a blocking wall can end it. |

`Velocity` is actual world displacement per second after collision resolution. `MoveDirection` and `FacingDirection` are separate intentions. `QuickBoostProgress` is normalized while boosting and returns zero when inactive. `QuickBoostCooldownRemaining` includes active boost time. Consumers should subscribe on enable and unsubscribe on disable, and read the current state on subscription.

Per-frame movement events are published after motion and state have been updated. A boost with zero recovery can end and restart in the same frame; listeners may receive ended then started while the final state is still QuickBoost.

## Preserved tuning and next work

The serialized `normalProfile` name and its members are retained to preserve existing scenes. It is the single active camera settings block; obsolete lock profiles, duplicate inactive fields, and redundant profile blending have been removed. Movement speeds, acceleration, boost curve, duration/recovery, multipliers, and free-look sensitivity are retained.

## Ground movement feature pass

`A33` includes a slalom west of spawn, a walled camera corridor east of spawn, a gentle ramp southwest, and a marked 100m Assault Boost runway farther east. Course objects live under `Ground Movement Course` and can be edited normally.

| Control | Action |
| --- | --- |
| WASD | Camera-relative movement |
| Left Shift | Ground boost (existing sprint) |
| Left Alt | Quick Boost, 20 energy |
| Hold Left Ctrl | Ground Assault Boost; release to cancel |
| Escape / left click | Release / recapture mouse |

Energy capacity is 100, recovery is 25/second after a 0.6-second delay, and Assault Boost drains 18/second. Normal movement remains available with no energy. A motor without an assigned energy component retains unlimited-energy behavior for standalone prototypes.

Assault Boost accelerates toward 28 units/second, turns at up to 90 degrees/second, and requires 10 energy to start. Quick Boost cancels the rush and uses the normal camera-relative dodge direction. Exhaustion, collision cancellation, and dodge cancellation require releasing Ctrl before starting another rush; holding the key cannot produce automatic restart loops. No enemy data participates in movement.

Camera obstacle handling uses the Default layer and ignores objects tagged Player. New environment colliders should be on an included obstacle layer. The player uses the Player tag. Camera radius and return damping are adjustable. Quick Boost adds a maximum 7-degree FOV pulse; Assault Boost adds up to 10 degrees and 12% follow distance. The Movement Feedback toggle disables these lens/framing effects while retaining free mouse look and collision handling.

The player root is unscaled and keeps the equivalent upright capsule dimensions. The visible mesh is under `VisualMech/Body` with its original dimensions. Presentation never changes the root transform or collider. The old duplicate BoxCollider has been removed.

The next work is tuning this ground prototype in play. Combat and aerial movement are deferred; target line-of-sight changes are also deferred with combat.

Do not introduce camera hard lock, enemy-driven camera framing, target-relative locomotion, or threat-based automatic target switching as part of these extensions.

## Regression checks

`Tests/Editor/MechFoundationChecks.cs` contains Unity play-mode checks for targeting, UI projection, crowd-buffer growth, movement tuning, boost lifecycle, landing signals, and camera rig lifecycle.

Run in an isolated Unity 6000.5.8f1 project containing the Scripts and Tests folders and the same Cinemachine/Input System/uGUI package versions:

```
Unity.exe -batchmode -nographics -projectPath <isolated-project> -executeMethod MechFoundationChecks.RunBatch -logFile <test-log>
```

Do not add `-quit`: the runner enters Play mode asynchronously and exits with code 0 or 1 when finished. The batch entry point intentionally rejects interactive use. Tests create their own objects and do not load or save the working scene.
