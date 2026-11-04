# Player controls

The gameplay controls are defined in the **Player** map of `InputSystem_Actions.inputactions` and read by `MechPlayerInput`. Every player owns an input-asset instance; the shared UI action map is unchanged.

| Input | Action |
| --- | --- |
| WASD | Camera-relative movement |
| Q / E | Fire the left / right shoulder part |
| Hold LMB / RMB | Repeatedly attack with the left / right arm when its cooldown and ammunition allow |
| F | Toggle camera lock immediately on press |
| R | Reload equipped ranged weapons with partially empty magazines and reserve ammunition |
| Shift | Dodge using the existing Quick Boost movement and cooldown |
| Tab | Request the equipped head's ability |
| Space | Request the equipped legs' ability; the existing jump / held hover lift remains the default |
| Ctrl | Request the equipped torso's ability |
| 1 / 2 | Swap the selected nearby part into its single/left socket or right socket |
| V | Cycle nearby part selection, as before |
| Escape / LMB | Release / recapture the gameplay cursor, as before |

There are currently at most two sockets for any one part type. Head, legs and torso use **1**; arms and shoulders use **1 / 2**. Slot compatibility and torso-transfer validation remain in `EquipManager`. The old Enter attack alias is removed. The serialized `Jump` action name is retained for Space/leg movement compatibility; `AssaultBoost` remains in the asset for compatibility but has no player binding and is not enabled by the input controller.

## Free camera and automatic aim

Players start with **free camera and automatic weapon aim**. Moving the mouse rotates the camera horizontally and vertically using the existing normal camera limits. Selecting an auto-aim target does not change Cinemachine's LookAt, orbit pitch, or follow pivot.

Automatic aim looks for a visible-screen target inside **60 world units** and a circle around screen center with radius **0.18 times the smaller screen dimension**. Tune **Lock Range** and **Auto Aim Radius** on `MechTargetLockController`. Crossing the center ray or inner switching tolerance can select another enemy; valid selections remain stable until they leave the acquisition area or become unavailable. Actual projectile collision still blocks shots at terrain.

F immediately toggles the camera's hard-lock mode. Holding F no longer switches to the old aim-without-assistance mode, and releasing F does not toggle a second time. Switching back to free camera keeps automatic weapon aim enabled. Hard-lock switching gestures, target-loss recovery and camera obstacle filtering remain available. `EnterFreeAim()` remains an explicit API for future manual aiming; F does not call it.

**MMB first-person/manual crosshair aiming is deferred until after M2.** MMB no longer activates gameplay lock-on. No reticle or HUD artwork is added or removed here.

## Reloading

Empty magazines already reload automatically when ammunition remains. R calls `MechCombat.ReloadWeapons()`, which reloads each eligible equipped ranged part independently. Full magazines, weapons without magazines, depleted reserves and parts already reloading ignore the request. Reloading preserves total ammunition and waits for both the remaining shot cooldown and reload duration. Its state belongs to the physical `Part`, so dropping or swapping it does not refill its ammo.

Gameplay can call `MechCombat.TryReload(PartCategory)` for one socket, or `Part.TryReload()` for a prepared physical weapon. `RemainingAmmo` includes loaded rounds; reloading does not create extra rounds.

## Automatic running and retired energy costs

`MechMovementController` starts running after **five seconds of uninterrupted movement** without a successful attack or incoming damage. It uses the existing per-leg sprint speed and part speed modifiers. Stopping, losing actual movement, a dodge/boost or leaving supported ground travel resets the timer. Attacking and taking damage cancel running immediately and cap ordinary movement back to walking speed. Pause freezes the timer. Tune **Auto Run Delay** and the existing sprint-speed settings on the motor/modules.

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

These functions dispatch the same Inspector/C# events and leave the actual effects unassigned. Requests are ignored while the input component is disabled or gameplay time is paused, or when the relevant part is missing. They do not equip parts, implement effects or start ability cooldowns.

Requests fire once per press and only when the corresponding part is equipped. An unhandled request does nothing. Space continues to drive the locomotion module's existing jump / held hover lift. Turn off **Use Built In Leg Movement** when a future legs ability supplies its own movement to avoid triggering both implementations. No new ability cooldown is consumed by an unhandled callback.

## Verification

In isolated Unity 6000.5.8f1 / Direct3D 11, **42 focused model/control checks** passed, including public ability-function dispatch and paused/disabled request guards. **24 existing pre-mission release checks** also passed during the preceding control revision. `QuestAndControlRevisionChecks.RunBatch` uses actual virtual-device Input System events for the keys and mouse, real equipped parts, magazine/cooldown timing, CharacterController collision, damage events and Cinemachine targets. It verifies blocked versus clear-path running, attack/damage/stop cancellation, empty-energy dodge, context swaps, free-camera aim and manual camera lock. Board coverage includes 64 seeded outward-clearance plans and rendered BigSlab/side-slab inspection. Results are retained in `.quest-review/Results/BigSlabAndControls`, with the public-function follow-up in `.quest-review/Results/AbilityHooks`. HUD code/assets and the shared UI action map remain unchanged. No player build was run.
