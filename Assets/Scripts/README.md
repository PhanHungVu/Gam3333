# Mech controller foundation

The game starts in free aim: the existing cursor stays at screen center and no enemy is acquired automatically, including when aiming at one. Press middle mouse to resume targeting in the last used soft/hard mode (soft on the first use). While targeting is active, hold middle mouse for 1.2 seconds to return to free aim. Releasing that hold does not turn targeting back on. Mode memory lasts for the current controller instance; it is not saved between game sessions.

In soft lock, mouse input controls camera rotation independently of enemy selection. The visible cursor initially acquires the nearest visible enemy by world distance from the mech. It stays with that enemy until the player actively aims the hidden screen-center cursor at another enemy, or the selected enemy becomes invalid. Enemies getting closer or crossing a stationary center aim cannot steal the selection. A lost, disabled, destroyed, out-of-range, or offscreen target is replaced by the nearest remaining visible enemy even without mouse input. This automatic selection is enabled only after the player explicitly leaves free aim.

A short middle-mouse click while targeting switches between soft and hard lock on release, without reselecting. Delaying the tap until release prevents a long hold from briefly changing modes first. In hard lock, the camera looks down from above and behind the mech, aims at the enemy's aim point, and the whole mech turns to face it, including while idle or boosting. The hidden center aim stays invisible. A deliberate mouse flick switches to the nearest visible enemy in that screen direction. Small mouse movements do not switch or rotate the locked view. Losing an active hard-lock target selects the nearest eligible visible replacement while preserving hard mode. With no replacement, hard mode waits for another enemy. Restoring a remembered hard mode with no eligible enemy waits safely until one becomes available; the camera remains under mouse control while waiting.

The first press from free aim restores immediately and consumes that whole button gesture, so holding or releasing that same press cannot immediately exit or toggle again. Pause, cursor release, and camera disable cancel unfinished gestures. `Free Aim Hold Duration` on `MechTargetLockController` sets the hold time. `IsFreeAim` reports whether targeting is explicitly off; `IsHardLocked` reports actual camera/body tracking of a valid enemy. `EnterFreeAim` and `ResumeTargeting` provide the same mode transitions for other callers.

WASD movement is always camera-relative. Hard lock rotates that camera, so the movement axes follow the view, but there is no enemy-relative approach/orbit movement. An active Quick Boost retains its captured world direction even when targets switch. With hard lock off, body facing retains the previous camera-relative movement and dodge behavior; idle facing does not turn toward enemies.

## Responsibilities

- `MechMovementController`: reads movement input once per frame, calculates direction, advances the motor, resolves CharacterController movement, and publishes state/telemetry. Separate methods handle planar motion, vertical motion, and facing.
- `CameraControlThirdPerson`: owns the player anchor and Cinemachine rig, mouse look, lock input, framing, and Quick Boost follow damping. Both lock modes share orbit framing and centered composition. Hard lock normally looks at the selected aim point; committed close melee briefly biases the look-at point toward the space between player and enemy.
- `MechTargetLockController`: owns selection, optional hard-lock state, and directional flick recognition; publishes target and mode changes. Collider storage grows to accommodate crowds and is reused after that. It does not create or modify UI.
- `LockOnCursor`: projects the selected aim point through the gameplay camera into its actual UI parent. `A33` explicitly assigns its cursor, canvas, camera, and targeting controller. Dynamically spawned HUDs can call `Bind`.
- `MechTargetable`: supplies an enemy aim position. `AutoMoveTarget` remains an active test-scene helper.
- `MechEnergyController`: boost costs, delayed recovery, and the energy change signal. The motor advances its recovery exactly once per movement tick.
- `MechBoostPresentation`: leans only `VisualMech` and manages exhaust/trails. The ignition audio slot is intentionally empty, so dashing is silent. Assign a clip later to enable boost audio; there is no generated fallback sound.
- `MechEquipmentLocomotion` / `MechLegAnimator`: bind the equipped locomotion strategy and its part-owned animation rig. Fast/heavy legs use the real imported clips without root motion.
- `MechUpperBodyAnimation`: carries the module's authored body/head pose to the existing visible parts after Animator evaluation. `MechAnimationPoseSource` isolates bone references; `MechPartPoseReceiver` moves upper visuals/sockets without moving the leg socket or gameplay root.
- `MechEnergyHud`: displays remaining energy on the scene Canvas.

Update order is camera input/toggle/flick in Update (-100), movement in Update, camera rig in LateUpdate (-100), Cinemachine at its default order, selection/lock validation (100), then cursor projection (200). Hard lock suppresses automatic center-crossing switches and retains offscreen targets while the camera catches up. Disabled/destroyed targets, range loss, and loss of eligible colliders replace the selection without changing the selected camera mode.

Camera collision uses Cinemachine Third Person Follow in free aim and both lock modes. It moves in immediately when a solid obstacle blocks the rig, then returns smoothly after clearance. Its radius covers the near-plane corners, including wider screens and boost FOV. The reusable solid map prefabs under `Prefabs/Map Elements` have static mesh colliders matching their visual surfaces; platforms in JhMapScene inherit these without scene overrides. New structures need solid colliders on the camera's Obstacle Layers (Default by default). Triggers and the Player tag are ignored. `Tools > Mech > Map > Add Missing Structure Colliders` restores missing colliders on these prefabs without modifying the imported models; grass remains decorative.

The camera reports actual free-look input through `ReportAimInput` before soft selection. That intent is consumed each selection frame; follow motion and enemy movement do not count as player aim. Both lock modes default to a 20-degree downward orbit pitch with a 1.5-unit raised pivot. Close or low enemies steepen the angle to keep the sightline above the mech. `Hard Lock Pitch`, `Hard Lock Height`, and `Hard Lock Orbit Response` on the camera tune this framing; these existing serialized settings now apply to soft lock too. Soft lock keeps manual yaw and hard lock tracks the enemy. Soft lock looks at an independent focus ahead of the mech (Soft Lock Aim Distance, default 20 m), so the actual screen-center ray can cross enemies instead of pointing through the raised player pivot. Small vertical aim adjustments move this focus while the orbit stays elevated; selecting or moving an enemy does not steer the focus.

While targeting is enabled, soft-lock mouse pitch has a shared 20-degree baseline plus/minus 3 degrees at 25% vertical sensitivity. Close-range body clearance can raise the camera further in either mode. Set Lock On Vertical Range to zero for a fixed manual pitch. Hard mode ignores vertical mouse input; free aim retains its -18 to 30 degree range. The shared settings retain their original Hard Lock field names to preserve prefab tuning.

During a committed melee swing against a selected enemy within 6 m ahead and near the player's height, both modes blend in 7 degrees of extra downward pitch and reduce the lateral shoulder offset by 75%. Hard lock briefly frames a point weighted 65% toward the enemy and 35% toward the raised player focus. Soft lock retains manual yaw. The effect holds for 0.45 seconds and blends out; it introduces no zoom, and movement FOV/distance feedback is held steady during the blend. The visible cursor and projectile aim continue to use the actual enemy aim point. These values are adjustable under Melee Framing.

## Extension points

Movement states describe implemented actions only: `Grounded` (idle/walking), `GroundBoost` (reserved legacy value; sprinting is removed), `Airborne` (jump/fall or hover), `QuickBoost`, and `AssaultBoost`. Equipped locomotion modules decide jump and air-boost support; standalone/biped movement retains ground-only boosts. Weapons run independently of the movement state machine. See [modular locomotion and animated legs](../GroundMovement/Locomotion.md) for fast/heavy animation swapping, hover/tank behavior, debug controls and the animation-data contract.

| Signal | Meaning / intended consumer |
| --- | --- |
| `StateChanged(previous, next)` | Fires only on transitions; animation and sustained effects. |
| `QuickBoostStarted(direction)` | Captured world-space dodge direction; ignition audio, trails, and a future FOV pulse. |
| `QuickBoostEnded()` | Completion or controller-disable cancellation; shut down transient effects. |
| `Landed(impactSpeed)` | Positive downward speed before contact; landing animation, dust, audio, and optional impulse. |
| `TargetChanged(previous, next)` | Selection changed or cleared; weapons and HUD. A destroyed target compares equal to null under Unity equality. |
| `HardLockChanged(active)` | Player changed camera/body lock mode; target loss does not fire this event. `IsHardLockMode` includes waiting for a target; `IsHardLocked` requires a live target. |
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
| Left Shift | Quick Boost (dash), 20 energy |
| Left Alt | No movement action |
| Hold Left Ctrl | Ground Assault Boost; release to cancel |
| Middle mouse from free aim | Restore last used soft/hard targeting mode |
| Short middle-mouse click while targeting | Switch soft/hard mode on release |
| Hold middle mouse for 1.2 seconds while targeting | Enter free aim and remember the current mode |
| Mouse flick while locked | Switch to the nearest visible enemy in that direction |
| Escape / left click | Release / recapture mouse |

Energy capacity is 100, recovery is 25/second after a 0.6-second delay, and Assault Boost drains 18/second. Normal movement remains available with no energy. A motor without an assigned energy component retains unlimited-energy behavior for standalone prototypes.

Assault Boost accelerates toward 28 units/second, turns at up to 90 degrees/second, and requires 10 energy to start. Quick Boost cancels the rush and uses the normal camera-relative dodge direction. Exhaustion, collision cancellation, and dodge cancellation require releasing Ctrl before starting another rush; holding the key cannot produce automatic restart loops. Enemy data affects body facing only when hard lock is active.

Hard-lock switching settings are on `MechTargetLockController`: default 80 pixels within 0.2 seconds, minimum movement speed 120 pixels/second, 0.1 seconds of quiet input to rearm, and 0.25 seconds between switches. Candidates must lie within a 60-degree half-angle of the flick direction, in range and on screen; closest screen-space distance from the current enemy wins. One uninterrupted swipe triggers at most one switch. Lower `Switch Flick Distance` for easier switching. `Hard Lock Orbit Response` on the camera controls orbit catch-up (default 0.12 seconds). Existing right-stick-click LockOn binding remains available; flick recognition currently uses mouse delta.

Camera obstacle handling uses the Default layer and ignores objects tagged Player. New environment colliders should be on an included obstacle layer. The player uses the Player tag. Camera radius and return damping are adjustable. Quick Boost adds a maximum 7-degree FOV pulse; Assault Boost adds up to 10 degrees and 12% follow distance. The Movement Feedback toggle disables these lens/framing effects while retaining free mouse look and collision handling.

The player root is unscaled and keeps the equivalent upright capsule dimensions. The visible mesh is under `VisualMech/Body` with its original dimensions. Presentation never changes the root transform or collider. The old duplicate BoxCollider has been removed.

The current combat prototype is described below. Enemy attacks, player damage/invulnerability, optimal-range bonuses, waves, objectives and resource budgets remain deferred. Equipment is described in [parts and abilities](../Combat/Parts-and-Abilities.md); hover locomotion is an optional equipped strategy.

## Four-weapon prototype

The `Mech` prefab has `MechCombatController` and an ammo/reload HUD. All four weapons are equipped simultaneously and fire once per press. Holding an attack button does not repeatedly fire. The three ranged slots have independent one-round magazines and automatic reloads; there is no manual reload key. Ammo below is total available shots, including the initially loaded round. Empty weapons stay empty until a new player instance is created; pickups and resupply are not implemented.

| Control / weapon | Damage | Ammo | Cooldown/reload | Effective / maximum range |
| --- | --- | --- | --- | --- |
| RMB / sword | 100 | Unlimited | 0.7 s | 4 m melee reach |
| LMB / throwing axe | 50 | 30 | 0.5 s | 12 / 18 m |
| E / cannon | 100 | 20 | 3 s | 20 / 30 m |
| Q / large crossbow | 30 | 50 | 1 s | 40 / 60 m |

Range assumptions for this prototype: one golem length is 4 Unity metres, matching the existing sword reach. The supplied ranges are effective ranges; maximum range is 1.5 times effective range. Cannon AoE is interpreted as a 3-golem **diameter**, giving a 6 m explosion radius. These are editable defaults, not claims about Armored Core 6's internal formulas. Full projectile damage applies through effective range, then decreases linearly to zero at maximum range. Distance is projectile travel from the muzzle, not camera distance. Each ranged weapon exposes both ranges individually on the mech prefab.

All ranged weapons launch straight physical projectiles toward the selected enemy's current aim point in soft/hard lock. Free aim uses the hidden screen-center ray. There is no homing or prediction, so moving enemies can evade shots. The axe cannot melee; RMB always uses the separate sword. The cannon detonates on impact, deals its range-adjusted damage once per enemy inside the blast radius, and publishes one aggregate impact result for the pooled explosion and kill confirmation. See [combat presentation setup](../Combat/README.md). World obstacles block blast damage, and the shooter is excluded. The crossbow uses a smaller projectile for precision. Projectile speeds are editable prototype values: axe/cannon 100 m/s, preserving the recently tuned projectile speed, crossbow 160 m/s. Projectile visuals remain simple colored placeholders, with no added sounds or weapon animations.

Swept sphere collision catches thin walls and enemies between physics steps, ignores the shooter/triggers, prevents firing through walls intersecting the muzzle, and removes projectiles at maximum range. Range expiry does not detonate the cannon. Firing one weapon does not reset another weapon's reload or interrupt movement. Clicking to recapture the cursor requires releasing held attack buttons before weapons arm. Existing gamepad Attack/Melee bindings remain available; cannon/crossbow currently use E/Q.

Sword melee uses the existing oriented front box and reach assist. The cyan box and ground outline turn orange during approach and yellow on the damage check. Normal reach is 4 m, with at most 1 m extra over 0.12 seconds for a selected enemy within 30 degrees ahead. Assistance diminishes with angle, does not move or rotate the mech, and does not chase a fleeing target. Walls prevent extension/damage; losing the assisted target or beginning a dodge cancels a pending approach. Free aim and targets outside the assist limits use the ordinary immediate attack. Each enemy is damaged once per swing, including enemies with compound colliders. Box center/size remain (0, 0, 2.25)/(3, 2.5, 3.5).

`CombatHealth` starts at 100 on current test enemies and deactivates them on death. Consequently the sword and a full-damage cannon hit kill a standard test enemy in one hit. Health bars, `Damaged`/`Died`, `ShotFired`, and `MeleePerformed` remain available. No inventory, equipment screen, enemy attacks, player damage, waves, or resupply are introduced. Existing targeting modes, melee camera framing, and enemy-independent movement remain intact.

## Regression checks

`Tests/Editor/MechFoundationChecks.cs` contains Unity play-mode checks for targeting, UI projection, crowd-buffer growth, movement tuning, boost lifecycle, landing signals, camera rig lifecycle, hard-lock transitions, directional flicks, actual camera aim, and independent dodge direction while locked.

Run in an isolated Unity 6000.5.8f1 project containing the Scripts and Tests folders and the same Cinemachine/Input System/uGUI package versions:

```
Unity.exe -batchmode -nographics -projectPath <isolated-project> -executeMethod MechFoundationChecks.RunBatch -logFile <test-log>
```

Do not add `-quit`: the runner enters Play mode asynchronously and exits with code 0 or 1 when finished. The batch entry point intentionally rejects interactive use. Tests create their own objects and do not load or save the working scene.

## Input and package asset ownership

Movement, camera, and combat read the same input definitions through separate runtime copies (`MechInputActions`). Disabling a controller disposes its copy without disabling the shared project-wide/UI action asset. Re-enabling creates a fresh copy; input definition edits take effect on the next enable or Play session. Tests cover source layout changes, repeated component enable/disable, and gamepad connection/removal.

Scene enemies and the enemy prefab use `Combat/Enemy Metal Black.mat`, preserving the previous appearance without referencing the editable material in an immutable URP package. The package copy has been restored from the matching Unity installation. If Unity retains input assertions from a session that was already running when the bindings changed, stop Play mode and restart the editor once to discard that stale native state.
