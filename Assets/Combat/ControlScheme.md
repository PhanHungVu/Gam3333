# Player controls

The gameplay controls are defined in the **Player** map of `InputSystem_Actions.inputactions` and read by `MechPlayerInput`. Every player owns an input-asset instance; the shared UI action map is unchanged.

| Input | Action |
| --- | --- |
| WASD | Camera-relative movement |
| Q / E | Activate the left / right Shoulder; press once for a Katyusha salvo, hold for rapid Tesla pulses |
| Hold LMB / RMB | Repeatedly attack with the left / right arm when its cooldown and ammunition allow |
| F | Cycle no lock-on -> free-camera lock-on -> camera-lock lock-on |
| R | Reload equipped ranged weapons with partially empty magazines and reserve ammunition |
| Shift | Dodge using the existing Quick Boost movement and cooldown |
| Tab | Request the equipped head's ability |
| Space | Request the equipped legs' ability; the existing jump / held hover lift remains the default |
| Ctrl | Request the equipped torso's ability |
| 1 / 2 | Swap the selected nearby part into its single/left socket or right socket |
| V | Cycle nearby part selection, as before |
| Escape | Open / close the mission pause menu |
| LMB while the gameplay cursor is free | Recapture the cursor outside the pause menu |

There are currently at most two sockets for any one part type. Head, legs and torso use **1**; arms and shoulders use **1 / 2**. Slot compatibility and torso-transfer validation remain in `EquipManager`. The old Enter attack alias is removed. The serialized `Jump` action name is retained for Space/leg movement compatibility; `AssaultBoost` remains in the asset for compatibility but has no player binding and is not enabled by the input controller.

## Free camera and automatic aim

Players start with **free camera and automatic weapon aim**. Moving the mouse rotates the camera horizontally and vertically using the existing normal camera limits. Selecting an auto-aim target does not change Cinemachine's LookAt, orbit pitch, or follow pivot.

Automatic aim looks for a visible-screen target inside **60 world units** and a circle around screen center with radius **0.18 times the smaller screen dimension**. Tune **Lock Range** and **Auto Aim Radius** on `MechTargetLockController`. Crossing the center ray or inner switching tolerance can select another enemy; valid selections remain stable until they leave the acquisition area or become unavailable. Actual projectile collision still blocks shots at terrain.

F advances exactly once per press through three modes:

| Mode | Weapon aim | Camera |
| --- | --- | --- |
| No lock-on | Screen-center aiming; no selected target or automatic enemy acquisition | Manual mouse orbit |
| Free-camera lock-on | Existing automatic enemy selection/aim | Manual mouse orbit; the selected enemy does not steer the camera |
| Camera-lock lock-on | Aim at the selected enemy | Camera smoothly follows that enemy; existing mouse-flick target switching remains available |

The starting mode remains **free-camera lock-on**. Its first F press enters camera lock, the next enters no lock, and the next returns to free-camera lock-on. Holding/releasing F never advances the mode. A hard-locked enemy dying, becoming disabled or leaving range can select a replacement without changing the chosen mode. With no available enemy, camera-lock mode remains armed for a later target. No-lock mode never reacquires by itself. Disabling/re-enabling the targeting controller preserves the selected mode.

`MechTargetLockController.Mode` exposes the current `LockMode` enum. `CycleLockMode()` uses the same three-state cycle as F, and `SetLockMode(...)` selects one explicitly. Existing `EnterFreeAim()`, `ResumeTargeting()` and the explicit soft/hard `ToggleHardLock()` helper remain compatible with scripted callers; normal F input uses the three-state cycle.

The shared `CameraControlThirdPerson` uses **Hard Lock Rotation Damping**, default **0.35 seconds**, for entering camera lock, flick switching and automatic replacement after death. Cinemachine interpolates from its rendered orientation instead of instantly pointing at the new target. Hard-lock screen bounds no longer bypass that damping, and entering lock no longer forces an immediate minimum-pitch jump. The existing **Hard Lock Orbit Response** still eases the follow orbit and the screen-position offset blends as well. Losing a close target eases any elevated pitch back toward manual limits, and camera framing can reacquire an enemy killed after targeting's Update before displaying a free-camera frame. These component changes apply to all gameplay scenes using the shared player camera, including cameras created by `MechPlayerRig`; no HUD or scene-specific camera controller is added.

**MMB first-person/manual crosshair aiming is deferred until after M2.** MMB no longer activates gameplay lock-on. No reticle or HUD artwork is added or removed here.

## Reloading

Empty magazines already reload automatically when ammunition remains. R calls `MechCombat.ReloadWeapons()`, which reloads each eligible equipped ranged part independently. Full magazines, weapons without magazines, depleted reserves and parts already reloading ignore the request. Reloading preserves total ammunition and waits for both the remaining shot cooldown and reload duration. Its state belongs to the physical `Part`, so dropping or swapping it does not refill its ammo.

Gameplay can call `MechCombat.TryReload(PartCategory)` for one socket, or `Part.TryReload()` for a prepared physical weapon. `RemainingAmmo` includes loaded rounds; reloading does not create extra rounds.

## Automatic running and retired energy costs

`MechMovementController` starts running after **three seconds of uninterrupted movement** without a successful attack or incoming damage. Running is **twice the final walking speed**, including equipped legs and other parts' speed modifiers; legacy module boost speeds do not change that ratio. Stopping, losing actual movement, a dodge/boost or leaving supported ground travel resets the timer. Attacking and taking damage cancel running immediately and cap ordinary movement back to walking speed. Pause freezes the timer. Tune **Auto Run Delay** (3 seconds) and **Auto Run Speed Multiplier** (2) on the motor. The same motor rule applies to Specialist and Heavy player prefabs and to biped, hover and tank legs. `WalkSpeed` and `RunSpeed` expose the resulting speed targets to gameplay code.

`AutomaticRunChecks.RunBatch` passed **64 checks** in isolated Unity 6000.5.8f1 on the current `main` assets: both player prefabs, the three-second threshold, measured 2x movement speed, actual weapon attacks and damage, repeated combat interruptions, stopping, pause, wall obstruction, motor lifecycle and real biped/hover/tank equipment swaps. The log is `.quest-review/ReadabilityProject/automatic-run-3s-main.log`.

Dodge and the legacy direct assault-boost command no longer require or consume energy. Dodge still has its existing cooldown. Ctrl is reserved for the torso ability request, and no player input starts assault boost. `MechEnergyController`, its serialized references and legacy cost values remain for compatibility with the teammate's current HUD; no HUD files or HUD authoring tools are changed.

## Connecting abilities

The project has `AbilityCooldown` and `AbilityDuration` stats but no head or torso ability effects yet. This change wires their inputs without inventing a scan, shield, repair or other effect.

On **MechPlayerInput**, assign the **Head Ability Requested**, **Torso Ability Requested** and **Leg Ability Requested** Inspector events. Each passes the actual equipped `Part`, so a handler can read its `Stats`, identity and ownership. C# code can subscribe to:

```csharp
input.AbilityRequested += (socket, equippedPart) =>
{
    // Dispatch the effect owned by this part. The effect handles its own
    // cooldown/duration and calls motor.NotifyCombatAction() if it attacks.
};
```

Public functions on `MechPlayerInput` are also available for C# or UnityEvent callers:

```csharp
input.RequestHeadAbility();   // Same request as Tab.
input.RequestTorsoAbility();  // Same request as Ctrl.
input.RequestLegAbility();    // Requests the effect; Space handles built-in movement separately.
```

These functions dispatch the same Inspector/C# events and leave the actual effects unassigned. Requests are ignored while input is disabled, gameplay is paused, a terminal mission result is active, hitstop blocks new actions, or the relevant part is missing. They do not equip parts, implement effects or start ability cooldowns.

Requests fire once per press and only when the corresponding part is equipped. An unhandled request does nothing. Space continues to drive the locomotion module's existing jump / held hover lift. Turn off **Use Built In Leg Movement** when a future legs ability supplies its own movement to avoid triggering both implementations. No new ability cooldown is consumed by an unhandled callback.

## Verification

The three-mode follow-up passed **24 real-frame checks** in `MechLockModeChecks.RunBatch`, using the existing Heavy player prefab, actual F input events and its live Cinemachine camera/brain. Coverage includes the full mode cycle, held/released F, no-lock acquisition suppression, controller re-enabling, free-camera independence, camera-lock entry, directional flick switching, same-frame death replacement, the last enemy disappearing, a later arrival and pause. Unlike a forced camera reset, these checks use normal positive frame times. The camera took roughly **3-8 degrees in the first rendered frame** of tested **18-35 degree turns**, then converged to within **0.05 degrees** of the target. First/settled D3D11 captures were visually reviewed. The existing foundation suite also passed **1,012 checks**, including close-target camera stability, framing, movement and combat. Logs are `.quest-review/ReadabilityProject/camera-three-modes-final.log` and `camera-three-modes-foundation.log`. No player build was run.

The development-only locomotion panel starts hidden. Press **backquote (`)** or click the **Locomotion Debug** button in the top-left to toggle it. The mouse button is available when the cursor is free; Escape now opens the mission pause menu. F8 remains available for the enemy encounter test shortcut. The obsolete Movement Controls text has been removed from the shared cursor prefab and A33.

In isolated Unity 6000.5.8f1 / Direct3D 11, **42 focused model/control checks** passed, including public ability-function dispatch and paused/disabled request guards. **24 existing pre-mission release checks** also passed during the preceding control revision. `QuestAndControlRevisionChecks.RunBatch` uses actual virtual-device Input System events for the keys and mouse, real equipped parts, magazine/cooldown timing, CharacterController collision, damage events and Cinemachine targets. It verifies blocked versus clear-path running, attack/damage/stop cancellation, empty-energy dodge, context swaps, free-camera aim and manual camera lock. Board coverage includes 64 seeded outward-clearance plans and rendered BigSlab/side-slab inspection. Results are retained in `.quest-review/Results/BigSlabAndControls`, with the public-function follow-up in `.quest-review/Results/AbilityHooks`. HUD code/assets and the shared UI action map remain unchanged. No player build was run.
