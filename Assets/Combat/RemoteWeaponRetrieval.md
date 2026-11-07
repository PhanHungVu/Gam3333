# Experimental combat-loot system

Branch inspected: `meow-meow-test`, base commit `0134cd4`, 2026-11-07. Updated 2026-11-08. This document records the original code audit, the user's revised requirements and the implementation checkpoints. Keep each stage separate and wait for feedback after its result. Stages 1–9 are complete, including sword/mace return damage, the cannon arrival blast, the 24-axe arrival barrage, the small-crossbow arrival speed buff and the large-crossbow piercing return bolt. Fresh drops now use physical pickup bounds rather than imported pivots; the barrage launches above the player's feet. Retrieval now reaches **20m**. All arm/shoulder weapons lift, hover, return quickly and shake the existing camera on arrival; body parts retain their earlier travel. The starting cannon remains on E/right. Next is stage 10, combined combat/horde testing and balance.

**Active temporary damage-test setting:** the three player prefabs currently enable **Bypass Cooldown For Testing** under **Retrieval Bonus Cooldowns**. Every configured sword/mace/cannon/throwing-axe/large-crossbow pull can deal its bonus damage without the 30s wait; small-crossbow pulls can also refresh their speed buff without waiting. This is the user's temporary testing request after stage 6 and remains enabled through stage 9. The normal cooldown rules documented below resume when this checkbox is disabled. Normal firing/reload cooldowns, per-attack hit protection and exclusive pickup/bonus reservations remain intact.

## Accepted control and cooldown rules

- F becomes remote part retrieval. MMB takes over the existing hard/soft camera-lock toggle.
- 1 and 2 retain nearby left/right part swapping; V currently cycles nearby parts.
- Remote retrieval prefers an empty compatible socket. When both are occupied, use the existing left-first slot priority, previewing the selected replacement before F. Single-slot parts use their actual socket. All equipment types can be retrieved; only configured weapons receive a retrieval bonus.
- Each player has a shared 30-second retrieval-bonus cooldown per weapon family, across every copy of that weapon. All swords share one timer; all maces share another; cannon, axe, small crossbow and large crossbow each have their own timer. Timers belong to the player, not the dropped item, HUD or PartData, so swapping/equipping a different copy cannot reset them.
- This cooldown controls the bonus damage/buff. Ordinary retrieval and swapping remain available during it. Nearby 1/2 swaps retain their ordinary behavior; the new combat bonuses belong to remote retrieval.
- Reserve a pending bonus before travel to prevent simultaneous same-family retrievals from both granting it. Start its 30 seconds when the bonus actually triggers: departure for melee/bolt, arrival for cannon/axe/buff. Cancelling before the bonus triggers releases its reservation. Cancelling after a path effect starts keeps its cooldown. This is local ownership/state designed for a future authority layer, not a networking implementation.

## Phase 0: actual implementation audit

### Starting equipment and mission spawning

`Scripts/Part System/EquipManager.cs` already reads serialized `startingLoadout` entries (`LoadoutEntry` in `PartSlots.cs`) and parts authored under sockets. It initializes torso sockets first and supports prefab references in the loadout without changing general equipment code.

The normal `FullPlayer_mech_specialist` and regular `FullPlayer_mech_golem` previously had a left-arm sword, body/head/legs, and empty shoulders/right arm. Heavy is a separate all-weapons demo and already carries a cannon. The correct active cannon is `Prefabs/Mech/Specialist/shoulder_cannon_specialist.prefab`, using `PartCatalogue/PartData_Shoulder_Cannon.asset`, ID `backweaponA3`. Its existing authored configuration has 30 total rounds, one-round magazine, a three-second reload, 100 direct damage and 50 splash damage within radius 3. These values are reused, not changed for the experiment.

`Scripts/Level/Mission/MissionPlayerSpawner.cs` uses `MissionLoadout.PlayerPrefab` when supplied; otherwise it keeps the scene player or uses the SubLevel's default. `Prefabs/Level/Common/SubLevel.prefab` defaults to Specialist. The quest board's `PreMission/PrototypeBoardCatalogue.asset` maps the regular torso to Specialist and the demo torso to Heavy. `SceneMissionStartHook` hands over that prefab. No new mission-start hook or JhMapScene-specific path is needed.

### Cooldowns, HUD and removed ready effect

`Part.cs` owns ammo, magazine rounds, `ReadyAt`, reload completion, cooldown duration/progress, `WeaponCooldownCycle` and `TryConsumeWeaponReadyCue`. These are physical-item runtime states, preserved through swaps. Both melee and ranged attacks use them. `WeaponsHudPod.cs` still shows ammo, dimming and the bottom-to-top cooldown sweep. `MechWeaponsHud.cs` rebuilds pods on equipment changes.

The reusable `Prefabs/UI/Combat HUD/Ability Ready Burst.prefab`, `AbilityReadyVfx.cs` and `AbilityReadyBurstGraphic.cs` survive. Ability pods still use them; weapon pods lost the connection. Git revision `ee37c6a` contains the old weapon-pod cue logic. Restore that logic adapted to the current nested prefab, rather than its old runtime effect builder. Suppress initial-ready/HUD-rebuild flashes and handle held fire immediately beginning another cycle. `WeaponReadyVfxChecks.cs` now explicitly asserts that weapons have no burst, so stage 2 must replace those retired expectations with real readiness-transition checks.

### Inputs and aiming

`InputSystem_Actions.inputactions` and `MechPlayerInput.cs` currently route WASD/arrows to movement, Shift to dash, Space to legs/jump, Tab to head, Ctrl to torso, R to reload, held LMB/RMB to arms, Q/E to shoulders, F to camera lock, 1/2 to swap and V to cycle. Q/E currently fire on presses; preserve that behavior in the input change. The old F-plus-attack-button pickup scheme is already gone.

`MechTargetLockController.ProcessLockButton` toggles the existing hard lock on press. Soft mode keeps automatic weapon aim and a freely movable camera. The disabled-targeting/free-aim state is distinct and should not replace soft mode. Gameplay currently has no MMB binding; the UI action map has a separate middle-click pointer action. Keep gameplay/UI input maps separate.

`Scripts/New Folder with Selection/Misc/ZooToggle.cs` also defaults to F for showcase floor switches. Stage 3 must resolve that test-scene interaction conflict and update its prompt instead of allowing one F press to both retrieve and toggle a switch. The arena's F5-F12/PageUp/PageDown/Home/End controls can remain.

### Pickup, highlighting and equipment

`PickupDetector.cs` collects nearby trigger colliders, deduplicates their Parts, cycles selection and supplies the existing selected-part effect. `EquipManager.TryEquip` validates type/socket and torso compatibility, drops replaced equipment, applies attachment/muzzle poses and raises `EquipmentChanged`. Its implicit-slot overload prefers an empty compatible slot, then the type's left/default slot.

The new `PickupHighlight` prefab supplies the white star/faint outline to loose items and hides when equipped. `PartSwapWorldHudPresenter`, `PartSwapWorldView` and `PartSwapDisplay` already present the selected part, its category, compatible sockets and comparisons. Extend their existing presentation with the F destination/range status; avoid a second combat HUD.

Missing: ranged screen-space pickup selection, reservation/ownership, travel, cancellation and per-family effect cooldowns. Preserve the local 1/2 range restriction when adding remote selection, so the old keys cannot bypass travel. The visible combat cursor follows selected enemies (`LockOnCursor`); pickup acquisition should use the camera-centre aim region independently of enemy auto-aim, with a dedicated pickup highlight and destination preview. It must not change the enemy lock target.

### Damage, projectiles and buffs

`CombatHealth`, `CombatTeams` and `CombatBlast` already provide damage, owner/team exclusion, wall blocking and duplicate-collider protection. Reuse `CombatBlast.Resolve` for the cannon burst. Melee return-path damage needs swept checks with a hit history across the whole retrieval; the current per-call AoE deduplication alone is insufficient.

`MechProjectile` already performs swept collision and terrain checks and can be launched without `RangedAttack.Fire`. That permits temporary axe projectiles without consuming weapon ammo. It currently stops at its first impact, so the large-crossbow bolt needs a separate piercing extension/helper with a persistent per-enemy hit history. Keep that behavior independent from normal crossbow shots.

Weapon effects must be keyed by PartData/configuration, not display names or subtype alone: cannon and large crossbow are both Splash; axe and small crossbow are both Projectile. Current IDs are sword `weaponA1`, mace `weaponB1`, axe `weaponA2`, small crossbow `weaponB2`, cannon `backweaponA3`, large crossbow `backweaponA4`.

The project has `TimedEffect` and source-keyed `DamageModifiers` in `Scripts/Abilities/AbilityRules.cs`, plus a working damage-amp ability. Movement reads equipped-part multipliers through `MechStats`; there is no temporary move-speed/attack-speed modifier yet. Add a small per-player timed modifier for normal movement and accepted attack cadence, with refresh rather than stacking. Integrate cadence in `MechCombat`, `RangedAttack` and `MeleeAttack`; `MechWeaponAnimator` already fits its attack/reload playback to the physical part's accepted cooldown. Do not edit shared stats, dash tuning or unrelated animation clocks.

### Testing and recommended boundaries

Reuse `EnemyBehaviorPlaytest.unity` and its existing controlled encounter shortcuts for mixed-horde checks; Zoo remains useful for art/pickup inspection. KOTHTest, EscortTest and EliminationTest validate real mission spawning. No AI/wave redesign is needed.

Extend `PickupDetector` for selection/presentation, add a separate `RemotePartRetrieval` controller for validation/reservation/travel, expose a small read-only attachment-pose/compatibility API from `EquipManager` so travel reaches the actual weapon-corrected socket pose, and keep final swapping in `TryEquip`. Put reservation state on the physical item with a retrieving-owner token, rather than a single global selected item. Restore the loose physics state on cancellation and revalidate the destination/owner before equipping.

Add a configurable retrieval-effect catalogue/extension point with separate melee-path, cannon-burst, axe-barrage, speed-buff and piercing-bolt handlers. Put the shared family cooldown gate on the player. Damage, projectiles and temporary modifiers remain separate from item movement and equipment.

Expected existing files to change in later stages: `Part.cs`, `PickupDetector.cs`, `EquipManager.cs`, `MechPlayerInput.cs`, `InputSystem_Actions.inputactions`, `WeaponsHudPod.cs`, weapon HUD pod prefab, world swap view/presenter, `MechStats.cs`, `MechCombat.cs`, `RangedAttack.cs`, `MeleeAttack.cs`, corresponding focused checks and relevant player prefab connections. New controller/effect/configuration files should live alongside those systems. No enemy AI, normal weapon assets or dash changes are planned.

## Staged delivery

1. Starting cannon: normal Specialist/regular golem use a serialized existing-cannon entry, retaining the sword/body parts. Initially ShoulderL/Q; moved to ShoulderR/E in stage 3 at the user's request. Heavy already has its cannon. Validate spawn, ammo/reload and actual direct/splash damage.
2. Restore ready burst to all weapon HUD slots using the existing reusable effect and actual readiness cycles.
3. MMB lock toggle and F pickup request; keep 1/2 nearby swaps, existing attacks and abilities. Resolve Zoo's F interaction and update pickup prompts.
4. Basic remote retrieval at configurable 20m/base 0.2s defaults (initially 10m, then 15m): independent screen-space target, clear destination, reservation, movement to the actual attachment pose, cancellation and successful ordinary swaps. Stage 6 adds weapon lift/hover and a separate 0.1s fast flight.
5. Sword/mace return-path damage and the 30-second per-family bonus gate. Their timers are separate despite their shared initial attack behavior.
6. Cannon arrival blast (initial radius 5m), through the same family cooldown gate.
7. Axe arrival barrage (initial eight temporary axes), through that gate.
8. Small-crossbow arrival buff (3s, 1.5x normal movement, 1.25x attack cadence), refresh-only and family-gated.
9. Large-crossbow separate piercing return bolt, family-gated.
10. Combined test encounters: moving/dashing/firing during retrieval, empty/full sockets, competing items, cancelled/destroyed targets, body parts, several same-family copies, independent family cooldowns, and horde performance/readability.

Wait for feedback after each completed stage. Stage 3 introduced MMB lock and automatic pickup; stage 4 makes F physically retrieve distant drops. Stage 5 enables sword/mace path bonuses. Stage 6 adds weapon anticipation/arrival shake and the cannon blast. Stage 7 adds the axe barrage. Each family has an independent shared 30-second cooldown when the current temporary bypass is off. Crossbow bonuses remain for the following stages.

## Stage 1 completed: starting cannon

Modified files:

- `Prefabs/Mech/Specialist/FullPlayer_mech_specialist.prefab`: serialized entry referencing the existing grey cannon Part prefab, initially ShoulderL/Q and now ShoulderR/E.
- `Prefabs/Mech/Base/FullPlayer_mech_golem.prefab`: the same serialized entry for existing regular-player references.
- `Tests/Editor/SpecialistPlayerIntegrationChecks.cs`: check both starters' cannon/ammunition/reload, a real direct-and-splash hit, and cannon presence in Zoo/KOTH/Escort/Elimination; update the earlier sword-only wording.
- `Combat/SpecialistPlayerIntegration.md`: document this experimental starting loadout.
- `Combat/RemoteWeaponRetrieval.md` (and its metadata): this audit, accepted control/cooldown rules and stage results.

The Heavy demo already carries the cannon and was preserved. This stage changes no general equipment/combat code, weapon stats, enemy loadouts, loot tables, controls or dash settings. The cannon naturally contributes its existing -4% movement modifier through equipped-part stats.

`SpecialistPlayerIntegrationChecks.RunBatch` passed **250 checks**, exit 0, in the isolated Unity 6000.5.8f1 project. Both normal player prefabs start with full authored ammo, fire through `MechCombat`, consume a round, enter the authored reload and damage both a direct target and a separate splash target. All four scene startup checks find the cannon; the existing integration checks also cover HUD binding, weapon attachments/animations, walking and dash presentation. Log: `.quest-review/Results/RemoteRetrievalStage1/retrieval-starting-cannon-validation.log`.

To test the current loadout manually, stop/restart Play Mode, open KOTHTest, EscortTest, EliminationTest or ZooScene, and use E to fire the right-shoulder cannon. The left-arm sword remains on LMB; the right arm/left shoulder remain empty on the normal starter. Check ammo/reload on the current HUD and fire into grouped enemies to test normal AoE. Launching a regular torso from the quest board uses the same Specialist prefab; Heavy remains its separate all-weapons demo.

The stage 1 checkpoint verified starting equipment only. Later completed checkpoints are recorded below. Gameplay feel and balance need the later retrieval/horde playtest.

## Stage 2 completed: weapon cooldown-ready bursts

The four weapon HUD cells now use a linked instance of the existing white upward burst prefab. Its initial flash, dense rising particles and subtle icon pulse last about 0.55 seconds; the current dimming/sweep remains. Actual item-owned completion tokens trigger the cue once for melee/ranged attack and reload cycles, including immediate held-fire restarts. Initialization, ready-item refresh/rebinding, duplicate views, empty ammunition and unseen completions while the HUD is closed do not fabricate/replay bursts. Swaps and HUD rebuilds preserve genuine pending cooldown completions and clean up active external columns.

Modified files for this checkpoint:

- `Scripts/UI/WeaponsHudPod.cs`: observe existing physical weapon cue cycles and control the linked effect; no runtime effect builder.
- `Prefabs/UI/Combat HUD/WeaponsHUDPodPrefab.prefab`: one nested instance of the existing **Ability Ready Burst** prefab, bound to the row's icon.
- `Scripts/Presentation/Editor/AbilityReadyPrefabSetup.cs`: apply the reusable connection to both pod prefabs without changing effect tuning.
- `Scripts/UI/AbilityReadyVfx.cs`: clarify its shared weapon/ability presentation role in a comment.
- `Tests/Editor/AbilityReadyPrefabChecks.cs`: verify the weapon prefab connection and shared HUD reference alongside abilities.
- `Tests/Editor/WeaponReadyVfxChecks.cs`: replace the retired no-weapon-burst expectations with real combat/HUD lifecycle and capture checks.
- `Combat/AbilityReadyVfx.md` and this file: usage, verification and staged status.

**72 combined ability/weapon checks and 982 gameplay regression checks passed**, both exit 0, in isolated Unity 6000.5.8f1. D3D11 captures at 1080p and 720p were reviewed. Results: `.quest-review/Results/RemoteRetrievalStage2`. Protected-file hashes confirm that normal combat code, item cooldown state, part stats, inputs, player prefabs, the shared standalone burst, the ability pod and the root CombatHUD asset retain their prior content.

To test, restart Play Mode in a mission/Zoo. Fire LMB for the starter sword or E for the starter cannon. Watch its existing cooldown sweep; when the weapon becomes ready, its icon emits the white upward column once. Hold an arm-weapon mouse button to check repeated cooldown completions. Swap parts while another weapon cools, or fire a magazine's final round, to check ownership and depleted-ammo behavior. Expand a weapon pod under the runtime `MechWeaponsHud` to find its linked **Ability Ready Burst** instance. Tune the shared Project prefab to change the visual on abilities and weapons together.

This HUD checkpoint did not change controls or retrieval effects. Animation-only placeholders with no accepted gameplay weapon cooldown have no readiness transition to display. Stage 3's subsequent controls are recorded below.

## Stage 3 completed: E starting cannon, MMB lock and F pickup

Both normal player prefabs now start with the grey cannon in **ShoulderR (E)**, with ShoulderL (Q) empty. The sword remains on LMB and Heavy retains its demo loadout. Only the loadout socket changed; weapon models, attachment corrections, ammo and damage values retain their existing configuration.

`Player/LockOn` now uses **MMB** instead of F; the existing gamepad right-stick binding remains. A press toggles the existing hard/soft camera behavior, and holding MMB does not repeat the toggle. Soft mode still permits free camera movement with automatic weapon aim. The separate UI pointer map is preserved.

The new `Player/Pickup` action uses **F**. At this checkpoint it immediately equips the selected **nearby** loose part, preferring an empty compatible socket and otherwise using a stable left/default replacement. It supports head, legs and compatible torso parts as well as weapons. Existing **1/2** explicit swaps and **V** nearby cycling remain. The existing comparison panel adds a compact `[F] Pick up: Left/Right` (or single-slot name) prompt reflecting the same compatibility decision as the transaction.

`MechPlayerInput.RequestPickup()` is the exposed entry point for stage 4. It rejects paused/finished gameplay, hitstop and stale/out-of-range selection, checking the real pickup sensor overlap before equipping. `EquipManager.CanEquip(part, slot)` and `PreferredSlotFor(part)` provide shared read-only validation, including torso socket retention. The usual equip transaction still owns attachment, outgoing-item drops, occupant transfer, selection cleanup and equipment events.

Zoo's showcase switches, sound cubes and menu pad now use **G** to avoid a simultaneous F interaction. The menu pad displays its configured binding. Mission objectives and encounter shortcuts keep their existing bindings.

Modified files for this checkpoint:

- `InputSystem_Actions.inputactions`, `Scripts/Input/MechPlayerInput.cs`: input rebinding and automatic nearby pickup.
- Both normal player prefabs: right-shoulder starting cannon.
- `Scripts/Part System/EquipManager.cs`, `PickupDetector.cs`: shared compatible-slot choice and real sensor-overlap validation.
- `Scripts/UI/PartSwapDisplay.cs`, `PartSwapWorldView.cs`: compatible comparisons and destination prompt in the existing view.
- Zoo interaction scripts, `Prefabs/Toggle.prefab`, `Prefabs/Misc/SoundCube.prefab`, `Scenes/ZooScene.unity`: G interaction without the pickup conflict.
- `Scripts/Targeting/LockOnCursor.cs`, `MechTargetLockController.cs`: comments/tooltips match MMB; aiming behavior is preserved.
- `Tests/Editor/RemotePickupControlChecks.cs`: runtime input/equipment checks; existing integration/control/UI checks updated for the current bindings and loadout. The layout capture test briefly unpauses while sending V because gameplay correctly ignores cycling during pause.
- This document and `Combat/SpecialistPlayerIntegration.md`: current loadout, controls and remaining stages.

**37 dedicated control checks, 250 Specialist integration checks, 982 gameplay regression checks and 95 pickup UI checks passed**, exit 0, in isolated Unity 6000.5.8f1. The dedicated checks exercise real E firing/ammo, MMB lock, held F without repeated swaps, empty/full sockets, 1/2 overrides, body parts, incompatible torso rejection, pause/cursor gating and an out-of-range tracked item. The scene checks cover Zoo/KOTH/Escort/Elimination with the E cannon and shared HUD. D3D11 pickup-panel captures at 1280x720 and 1024x768 were reviewed, including the F destination footer and cycling between both sides. Logs/captures: `.quest-review/Results/RemoteRetrievalStage3`.

Manual check: restart Play Mode in a mission or Zoo, press E to fire the starting cannon, press MMB twice to compare hard and soft lock, then approach a dropped arm weapon. The F prompt should name Right while that arm is empty; press F to fill it. With both arms occupied, F previews Left, while 1/2 still choose either side explicitly. Use G for Zoo showcase pads.

At this checkpoint F still required nearby overlap; stage 4 replaces that immediate F transaction with the physical retrieval below. Nearby 1/2 transactions retain their immediate behavior.

## Stage 4 completed: basic physical remote retrieval

F now pulls the actual dropped part to its compatible equipment socket, then performs the existing equip/drop transaction on arrival. It works with weapons, head, legs and compatible torso parts. Enemy lock-on, attacks, player movement, dash, abilities, normal ammo and item-owned cooldowns retain their existing behavior. This stage adds no retrieval damage, buffs, sounds or extra trail/particle effects.

### Targeting and controls

- Aim the **centre of the gameplay camera view** approximately toward a loose drop. Pickup selection is independent of the enemy-following combat cursor and never changes the enemy target or hard/soft mode.
- The current configurable range is **20 world units from the player**. Non-weapons retain 0.2-second travel; weapons lift/hover before a 0.10-second return (stage 6 below). The preview band extends to 25m and shows **Out of range (20m)**. Original ranges were 10m in stage 4 and 15m in stage 5.
- A forgiving screen-space region picks one compatible drop by distance from view centre, then world distance and a stable runtime entity tie-break. Compound colliders are deduplicated. Only loose, active, unreserved parts in the player's scene are eligible. The query uses the existing MechPart layer; growing non-alloc buffers handle dense piles without silently truncating the initial buffer.
- The existing coloured selection marker and comparison panel are reused. The footer previews **[F] Retrieve: Left/Right**, or the actual single-slot name. Distant 1/2 cards are grey and marked **nearby only**. Current enemy/actor bodies do not block pickup queries; solid world geometry does.
- **1/2** still perform immediate nearby swaps, revalidating the real sensor overlap at input time. They cannot equip a distant item selected for F. **V** cycles nearby candidates and temporarily gives that local choice priority until the aimed remote candidate changes. If nothing is aimed at, the ordinary nearby selection remains available for F or 1/2.
- Empty compatible sockets win; occupied sockets use the same stable left/default fallback shown in the panel. F accepts one item in flight at a time. Holding F does not repeatedly swap items.

### Ownership, travel and cancellation

`RemotePartRetrieval` owns targeting and travel. It reserves the **physical Part** before movement, suspends that item's own colliders/rigidbody, and interpolates its world position, rotation and scale to the current socket pose with a small upward arc. The player can move, dash and fire during the pull. `EquipManager.TryGetAttachmentPose` uses the same per-weapon position/rotation corrections and scale calculation as final attachment, including the existing player crossbow corrections.

The outgoing equipment stays attached and usable until arrival. Only then does the normal `TryEquip` transaction drop it, install the incoming part and transfer occupied sockets when replacing a torso. A competing player or ordinary equip call cannot take a reserved flying part. This is local reservation/state management, not network authority.

The controller revalidates the owner, target, destination compatibility and intended outgoing occupant throughout travel. Terrain blocks both initial selection/request and the swept travel path. If an obstruction appears, the target is disabled/destroyed, the owner dies/disables/unloads, or the intended socket occupant changes, the pull cancels and releases ownership. A surviving item retains its current position, restores its original world scale and per-collider/rigidbody settings, and remains an ordinary loose drop. Pause/hitstop suspend travel; unpausing resumes the same item. There is no early outgoing-item drop or duplicate equipment creation.

`Started`, `Arrived` and `Cancelled` lifecycle events expose the later retrieval-effect integration points. `Cancelled` can supply an unavailable/destroyed Part; listeners must tolerate that. Combat bonuses remain separate from this controller and are not implemented at this checkpoint.

### Files and tuning

- New `Scripts/Part System/RemotePartRetrieval.cs` (and metadata): screen-space acquisition, validation, physics suspension, moving socket destination and cancellation.
- `Scripts/Part System/Part.cs`: per-item retrieval reservation and immediate cancellation when disabled.
- `Scripts/Part System/EquipManager.cs`: reservation-aware compatibility and shared read-only attachment-pose calculation.
- `Scripts/Part System/PickupDetector.cs`: remote/local selection arbitration and the existing selection marker.
- `Scripts/Input/MechPlayerInput.cs`: F routes to retrieval; 1/2 require real nearby overlap; input disable cancels pending travel.
- `Scripts/UI/PartSwapWorldView.cs`, `PartSwapWorldHudPresenter.cs`: F destination/range/travel status and nearby-only explicit-swap cards in the existing panel.
- The status row uses a small matching backing strip so F/range text remains legible over bright scenery; it adds no input targets or separate HUD.
- Normal golem, Specialist and Heavy demo player prefabs: one authored **Remote Part Retrieval** component on the player root. Heavy's body and all-weapons demo loadout remain unchanged.
- `Tests/Editor/RemotePartRetrievalChecks.cs` (and metadata): remote acquisition/travel, ownership, ordinary drops, active combat/movement, attachment correction, body parts, UI and failure paths. Existing control/UI fixtures now distinguish nearby controls from remote acquisition.

To tune, select the player prefab root and edit **Remote Part Retrieval**: Pickup Range, Aim Radius (fraction of viewport height), Range Preview Multiplier, Travel Duration, Arc Height and Obstruction Radius. The values are per prefab; no shared PartData, normal weapon timing or HUD art assets are changed.

Validation: **61 remote retrieval checks, 37 control checks, 95 pickup UI checks, 250 Specialist integration checks and 982 gameplay regression checks passed** in isolated Unity 6000.5.8f1 (all exit 0). They cover real distant acquisition/F input, blocked 1/2 at distance, reservations across owners, visible travel, moving/dashing/firing E, real crossbow attachment corrections, ordinary outgoing drops, body/torso transfers, range, terrain, pause, destruction, player death and cancellation physics. Remote available/out-of-range captures and existing pickup-panel captures were reviewed. Logs and remote captures: `.quest-review/Results/RemoteRetrievalStage4`. Broader UI layout checks deliberately control their local selection; remote acquisition has its own suite. Their editor setup only modifies the disposable fixture; generated UI assets were not copied into the live project. Combat feel in a live horde remains a player playtest, followed by the later bonus-effect stage.

Manual check: restart Play Mode in Zoo or a mission, place/leave a part about 18–19m in front, and aim the view centre toward it. Confirm its panel names the F destination while 1/2 remain grey; press F while moving or firing E. The item should visibly pull into that socket and drop the old part only on arrival. Walk closer and use V/1/2 to check immediate nearby swapping. Aim at a drop about 21m away for the out-of-range message, and put a wall between it and the player to verify blocking. For slow inspection, temporarily increase **Weapon Flight Duration** for weapons (current default 0.10s), or **Travel Duration** for body parts (0.20s).

The next checkpoint at that time was stage 5, implemented below.

## Stage 5 completed: sword/mace return damage and shared family cooldowns

Normal golem, Specialist and Heavy demo prefabs now use **15m retrieval range**. Travel stays at 0.2s and range-preview text updates from the actual setting. Nearby 1/2 swaps, MMB lock, held arm attacks and the E starting cannon retain their existing behavior.

Retrieving a configured **sword or mace with F** starts a damaging return path. The attack covers the initial overlap and every accepted physical travel segment, using a swept capsule to catch targets between frames. Each target health component can be damaged **once across the whole retrieval**, including targets with multiple hitboxes. Existing team exclusion, owner exclusion, corpse-query filtering, walls, damage events and melee impact feedback apply. The path stops when travel ends/cancels. It follows the same movement that the actual part makes, including changes to the destination while the player moves.

Defaults in `Combat/DefaultRetrievalEffects.asset` are **1x the incoming weapon's normal damage** and **0.75m hit radius** (1.5m total width). With current data this is 100 for sword and 70 for mace before existing player damage multipliers. These values are independent from normal weapon hit boxes and do not change shared PartData, ammo, attack cooldowns or reload state. Normal 1/2 swaps do not create this attack.

### Cooldown and exploit prevention

`RetrievalBonusCooldowns` is one component on the **player root**, retained through weapon and torso changes. Every sword shares that player's Sword timer; every mace shares a separate Mace timer. A different physical copy, a renamed/runtime stat-roll copy of the same canonical part ID, equipment refresh, or component disable/enable cannot clear a committed timer. Other players have their own timers. Pause stops the gameplay cooldown clock.

The default duration is **30 seconds**. A valid path attack reserves and commits its family bonus **at departure**, when the starting overlap becomes active. It consumes the bonus even if it misses. Rejected pickup requests consume nothing. Cancelling after the path starts keeps the cooldown and releases the loose item normally. During the cooldown F still retrieves/equips the weapon, and 1/2 still swap nearby parts, with no bonus damage.

The gate also exposes token-based reservation/commit/release for arrival-triggered effects. Cancelling a pending reservation before its effect triggers releases it without starting a timer; a committed timer cannot be refunded by releasing its old token. Cannon, ThrowingAxe, SmallCrossbow and LargeCrossbow already have independent family keys. Stage 5 configured sword/mace; stage 6 below adds cannon.

### Components and tuning

- `Scripts/Part System/RetrievalEffectCatalogue.cs`: configurable canonical PartData/part-ID to family/effect entries. Display names and broad projectile/melee subtypes never select bonuses.
- `Combat/DefaultRetrievalEffects.asset`: the two current sword/mace entries, damage multipliers and hit radii. Add later effect entries as their milestones are implemented.
- `Scripts/Part System/RetrievalCombatEffects.cs`: subscribes to accepted retrieval lifecycle/travel; snapshots incoming damage and existing damage multipliers; owns whole-path hit history. It does not control equipment movement or normal firing.
- `Scripts/Part System/RetrievalBonusCooldowns.cs`: player-owned family timers and pending token ownership; ordinary pickup never checks this gate.
- `Scripts/Editor/RetrievalBonusCooldownsEditor.cs`: live family timers in the Inspector during Play Mode. No new HUD or audio is introduced.
- `Scripts/Damage/CombatBlast.cs`: shared swept-capsule resolver using the same accepted-health-loss and combat-feedback path as ordinary attacks.
- `Scripts/Part System/RemotePartRetrieval.cs`: 15m default and a `Travelled(part, from, to)` callback only after the terrain sweep accepts movement. The callback guard handles a listener finishing a mission or cancelling/disabling the owner mid-hit.
- All three active player prefabs: one catalogue-backed effects listener and one 30s cooldown pool, alongside the existing retrieval controller. Heavy's demo equipment stays unchanged.
- `Tests/Editor/RetrievalCombatEffectsChecks.cs`: real-path damage, copy/family/player isolation, damage multipliers, pending and committed cancellation, pause/expiry, overlap deduplication and wall/team rules. `RemotePartRetrievalChecks` now verifies 14m selection and the 15m/16m range boundary.

To verify manually, restart Play Mode with sword/mace drops and enemies between the drops and the player. Pull a sword with F and watch enemies along the return take one hit. Retrieve a different sword immediately: it should still equip but cause no extra damage. A mace can still trigger its separate bonus. Select the runtime player root and expand **Retrieval Bonus Cooldowns** to see Sword/Mace seconds counting down; changing parts should keep those timers. After 30 gameplay seconds the next sword/mace retrieval can damage again. Use the catalogue asset to tune damage multiplier/hit radius, and the player root's cooldown component to tune duration. Gameplay balance and readability should be judged in the existing horde playtest.

Validation: **47 retrieval combat checks, 62 remote retrieval checks, 37 pickup control checks, 250 Specialist/player-scene integration checks and 982 gameplay regression checks passed**, all exit 0 in isolated Unity 6000.5.8f1. Tests cover initial overlap plus actual curved travel, whole-path compound-collider deduplication, large between-frame sweeps, walls/teams/owner exclusion, ordinary item cooldown retention, damage-amp integration without shared-stat changes, copy/ID/family/player isolation, rejected pickup, pre-trigger token release, post-trigger cancellation, disable/enable persistence and pause/natural expiry. The 14m pickup/16m rejection and rendered **Out of range (15m)** status were verified. The ordinary player checks cover Zoo, KOTH, Escort and Elimination. Logs/captures: `.quest-review/Results/RemoteRetrievalStage5`. Isolated checks establish functionality; horde balance remains a live playtest.

The next checkpoint at that time was stage 6, implemented below.

## Stage 6 completed: weapon suspense, fast return, arrival shake and cannon blast

All **arm and shoulder weapon** retrievals now have three transform-driven phases, including weapons whose combat bonus is cooling down or has not been implemented yet:

| Phase | Current default | Behavior |
| --- | --- | --- |
| Lift | 0.12s, +0.8m | Rise vertically at the drop location while preserving original rotation and world scale. |
| Hover | 0.18s | Remain suspended at that location; no premature flight or retrieval damage. |
| Fast return | 0.10s | Accelerate toward the current corrected equipment socket, rotating/scaling into its normal equipped pose. |
| Arrival | 0.12 strength, 0.16s shake | Equip/drop transaction completes, then use the existing bounded Cinemachine impulse channel. |

Weapon anticipation plus return takes about **0.40s**, excluding pause/hitstop. The player can continue moving/firing; the suspended drop stays at its original world location until launch and the fast return tracks the moving socket. Terrain checks also cover the lift, so a ceiling can safely cancel it. Head/torso/legs keep their earlier 0.20s travel and do not receive weapon arrival shake. Immediate nearby 1/2 swaps are unchanged.

`RemotePartRetrieval.Phase` and `HasDeparted` expose live state. `Started` now means a validated/reserved pickup; the new `Departed` event means the actual return has launched. `Travelled` emits only accepted fast-flight segments, so the visual lift and hover do not create invisible path damage. Sword/mace bonuses reserve during anticipation, commit at fast departure, and retain the cooldown after a launched attack is cancelled. Cancelling before departure releases their pending bonus without spending it.

`RetrievalFeedback` observes successful arrival for **every weapon family** and requests one brief shake from the existing `CombatFeedback` impulse API. A cancelled pull emits no arrival shake. A weapon whose bonus is on cooldown still receives the same physical animation/shake. Existing presentation/camera-impulse settings remain authoritative; this does not add a camera controller, body motion, HUD or audio.

### Cannon arrival bonus

The catalogue's Cannon entry now uses **ArrivalBlast**, **5m radius** and **1x the incoming cannon's Damage**. Current data gives **100 damage** before existing player damage multipliers. Each valid target in the sphere is damaged once through `CombatBlast.Resolve`, with normal team/owner exclusion, collider deduplication and wall blocking. The blast is centred at the player's current position (raised 0.5m for its origin), after the cannon has successfully equipped. It is independent of the normal cannon shot/splash and consumes no ammunition or normal firing/reload cooldown.

The Cannon family bonus is **reserved throughout lift, hover and flight**, and commits its **30-second shared cooldown only at successful arrival**. Cancelled, blocked or destroyed pulls, owner death, or disabling the effects listener before arrival release the pending reservation without damage or cooldown. A second cannon during cooldown still animates, equips, drops the outgoing part and shakes the camera, but cannot create another explosion. Sword and Mace retain their independent timers.

An accepted cannon bonus raises `CombatVisualEvents.RetrievalBlast` after damage resolves. `CombatFeedback` reuses its pooled explosion flash, shockwave, embers and smoke, plus existing kill confirmation. The retrieval event adds no firing effect, projectile or audio call; the shared arrival listener supplies the arrival shake.

### Files, configuration and verification

- `Scripts/Part System/RemotePartRetrieval.cs`: weapon lift/hover/fast-flight fields, phase state and departure callback; terrain/cancellation and normal equipment handoff retained.
- `Scripts/Part System/RetrievalCombatEffects.cs`: pending family token through anticipation; melee launch commit; cannon arrival commit/damage; lifecycle cleanup and read-only blast counters.
- `Scripts/Part System/RetrievalEffectCatalogue.cs`, `Combat/DefaultRetrievalEffects.asset`: third entry maps the canonical cannon data to ArrivalBlast (5m/1x), without treating the large crossbow's shared Splash subtype as cannon.
- New `Scripts/Presentation/RetrievalFeedback.cs` (and metadata): all-weapon arrival impulse.
- `Scripts/Presentation/CombatVisualEvents.cs`, `CombatFeedback.cs`: dedicated post-damage retrieval-blast notification and existing pooled visuals.
- Normal golem, Specialist and Heavy demo player roots: authored weapon timing and one arrival feedback component. Loadouts and existing socket corrections are preserved.
- New `Tests/Editor/RetrievalAnticipationAndCannonChecks.cs` (and metadata): all six real weapon models, hover pose/scale, short flight, arrival impulse, cannon radius/teams/walls/deduplication, cooldown/cancellation and pooled presentation. Prior retrieval/control/melee fixtures explicitly isolate their earlier timing checks; the new suite exercises authored timing.

Tune weapon height/timings on the player root's **Remote Part Retrieval** component, and arrival shake on **Retrieval Feedback**. Tune the Cannon entry's radius/damage multiplier in `Combat/DefaultRetrievalEffects.asset`; the existing **Retrieval Bonus Cooldowns** component still owns each family's 30s duration and live Inspector readout. No normal weapon PartData or HUD assets change.

Manual test: restart Play Mode in Zoo or a mission and remotely retrieve each weapon. Watch it rise, pause, then snap into the correct slot with a brief shake. Retrieve a cannon with enemies around the player: the explosion should occur only after equip, around the player rather than the old drop location. Immediately retrieve another cannon to verify ordinary pickup/shake without a second blast. Cancel or obstruct the first pull during hover/flight and inspect the Cannon gate: it should remain ready. Sword/mace gates should remain reserved during hover and begin counting only at launch.

Validation: **72 anticipation/cannon checks, 47 melee retrieval checks, 62 ordinary remote retrieval checks, 37 pickup control checks, 250 Specialist/player integration checks and 982 gameplay regressions passed**, all exit 0 in isolated Unity 6000.5.8f1. The new suite instantiates all six actual weapon prefabs, checks unchanged hover scale and the short flight, verifies one arrival impulse per successful weapon pickup and presentation-master suppression, and tests cannon radius/team/wall rules, compound hitboxes, ammo/cooldown preservation, same-family copies, hover/flight cancellation, pause, effect disable, blocked lift and owner death. Melee regressions verify cooldown commitment at actual departure. Focused hover/blast graphics captures were reviewed; unrelated test-player comparison panels were hidden only for those model/VFX captures. Results: `.quest-review/Results/RemoteRetrievalStage6`. Live horde feel/balance remains a player playtest.

The next checkpoint at that time was stage 7, implemented below with eight axes and the same arrival reservation rules.

## Temporary retrieval damage cooldown bypass

Enabled on normal golem, Specialist and Heavy demo prefab roots. **Retrieval Bonus Cooldowns → Bypass Cooldown For Testing** makes effective family cooldown remaining report zero, allowing each new valid pull to grant its configured bonus. It does not change the retained **Cooldown Duration = 30**, normal PartData, ammo, firing/reload clocks, travel phases, arrival effects or the single hit per target in each return path.

Actual trigger times are still recorded internally. Unchecking the bypass immediately restores the cooldown remaining since each family's latest triggered bonus; toggling it does not erase the timers. Pending token ownership is still exclusive, and cancellation rules still apply. The Inspector shows an explicit test-mode notice while the bypass is enabled. To restore normal play permanently, disable the checkbox on all three player prefabs; editing just the runtime player changes that Play session only. Future retrieval families using the shared gate also follow this setting until testing mode is turned off.

The dedicated combat checks now exercise repeated Sword, Mace and Cannon damage with this prefab setting, exclusive reservations while bypassed, and switching back to normal suppression. Existing cooldown/cancellation suites explicitly turn the bypass off in their private fixtures so the permanent 30-second behavior remains covered.

Verification: **65 retrieval combat checks and 72 anticipation/cannon checks passed**, exit 0, in the isolated project. Repeated swords/maces hit their targets on each pull; repeated cannons create fresh arrival blasts; disabling the bypass restores the saved family timers and suppresses repeat damage again. Logs: `.quest-review/Results/RetrievalDamageBypass`.

## Stage 7 completed: 20m range and throwing-axe arrival barrage

All three active player prefabs now use **20m** retrieval range, with the existing 1.25x preview band reaching 25m. The lift, hover, 0.1s fast return, arrival shake, nearby 1/2 controls and **enabled temporary cooldown bypass** are retained.

When a configured throwing axe successfully equips after an F retrieval, it launches **24 temporary attack projectiles**, evenly spaced **15 degrees** apart across the horizontal circle around the player's current position. Their orientation starts from the player's forward direction; enemy lock-on does not redirect all axes at one target. The retrieved part remains equipped, and the outgoing equipment follows the ordinary drop rules. The spawned attacks contain no Part/equipment component and never become extra pickups. The initial eight-axe version was expanded in the post-stage-7 fix below.

The barrage uses the existing throwing-axe projectile prefab and `MechProjectile` swept collision/damage/lifetime system. Default damage is **1x the incoming axe's Damage** (currently **50 per axe**, before existing player damage multipliers). Speed and range come from its weapon stats: currently 100 units/s and 8 golem lengths (32m at the default 4m/golem). The retrieval-only collision radius uses the larger of the weapon stat and the configured **0.4m** minimum; ordinary axe shots retain their **0.18m** stat radius. At those defaults an unobstructed axe travels for about 0.32s, then cleans itself up; collisions end it earlier. Each projectile hits once, including against compound hitboxes. Different axes are independent projectiles and may hit the same sufficiently large/close target.

The catalogue entry configures projectile count (24, bounded 1–32), spawn height (**1.2m above CharacterController feet**), radial muzzle offset (0.75m), collision radius minimum (0.4m), damage multiplier (1x), speed multiplier (1x) and range multiplier (1x). Each axe starts at that height above the player centre and uses the normal centre-to-muzzle obstruction sweep before moving to its radial offset. Walls cannot be bypassed by spawning the projectile beyond them. Existing team/owner rules apply; friendly bodies block without taking friendly damage. Ground parts and ragdolls retain their existing query exclusions.

No normal firing call is made: ammo, magazine rounds, ordinary firing/reload cooldowns, weapon attack events and ready-burst state are preserved. `CombatVisualEvents.RetrievalShotReleased` connects the projectiles to the existing lingering trail system without replaying weapon muzzle effects, recoil, firing audio or firing camera impulses for every axe. Projectile impacts retain ordinary damage/impact feedback, and the pickup retains one arrival shake.

### Family cooldown and cancellation

The ThrowingAxe family token stays pending throughout lift, hover and fast travel. It commits only after successful equipment arrival, when the barrage launches. Cancelling the pull, terrain obstruction, disabling the effects listener, rejected range requests and owner death create no barrage and release the untriggered token. Re-enabling after an abandoned arrival cannot replay it.

With normal rules enabled, all throwing-axe copies share one **30-second player-owned timer**, independent of Sword, Mace and Cannon. Ordinary retrieval still works while that timer suppresses the barrage. The user's **temporary bypass remains enabled** on the three player prefabs, so repeated F axe pickups currently produce repeated volleys for damage testing. Turning bypass off restores the stored axe timer like the other families.

### Files and manual test

- `Scripts/Part System/RemotePartRetrieval.cs` and the normal golem/Specialist/Heavy roots: 20m default, preserving all existing timing and bypass settings.
- `Scripts/Part System/RetrievalEffectCatalogue.cs`, `Combat/DefaultRetrievalEffects.asset`: appended ArrivalBarrage kind and fourth canonical entry (`weaponA2`); configurable volley fields. The small crossbow shares Projectile subtype but remains unconfigured for this effect.
- New `Scripts/Combat/RetrievalAxeBarrage.cs` (and metadata): radial temporary projectile creation through existing APIs; no ammo consumption or equipment cloning.
- `Scripts/Part System/RetrievalCombatEffects.cs`: arrival dispatch, shared token commit, read-only barrage counts and a projectile-created notification; earlier melee/cannon behavior retained.
- `Scripts/Presentation/CombatVisualEvents.cs`, `CombatFeedback.cs`: retrieval-projectile trail notification and cleanup subscriptions; normal shot behavior is unchanged.
- New `Tests/Editor/RetrievalAxeBarrageChecks.cs` (and metadata): actual model acquisition at 18m, eight directions/damage, temporary bypass and normal gate, walls/friendlies, finite lifetime, ordinary weapon-state retention, cancellation and a radial graphics capture. Existing range checks now cover 19m success and 21m rejection.

Restart Play Mode in Zoo or a mission, leave a throwing axe within 20m, stand among enemies and retrieve it with F. The volley should appear only after the held axe reaches its arm slot, and should travel out in 24 directions at small-enemy body height. Retrieve another axe immediately to test repeated damage with bypass on. For the normal gate test, disable **Retrieval Bonus Cooldowns → Bypass Cooldown For Testing** on the runtime player and inspect the ThrowingAxe timer. Tune volley count/height/offset/radius/multipliers on the catalogue's axe entry. No new HUD, scene, enemy-wave, normal weapon-stat or audio asset changes are required.

Validation: **42 axe-barrage checks, 65 retrieval combat/bypass checks, 62 range/retrieval checks, 72 anticipation/cannon checks, 37 input checks, 250 player/scene integration checks and 982 gameplay regression checks passed**, all exit 0 in isolated Unity 6000.5.8f1. The real dropped axe was acquired by the camera-centre query at 18m; range fixtures verify 19m acceptance and 21m rejection. The volley hit eight targets once each, preserved item ammo/magazine/cooldown state, cleaned up impacted/empty-direction projectiles, blocked walls/friendly bodies, retained the temporary bypass and restored the normal family timer when switched off. Cancellation/destruction prerequisites and other configured effects remain covered. A focused radial-model/trail capture was reviewed. Logs/captures: `.quest-review/Results/RemoteRetrievalStage7`. Gameplay balance/readability remains a live horde playtest.

## Post-stage-7 fix: grounded pickups and axe misses

Two separate runtime problems were reproduced and fixed before proceeding to the next milestone.

**Fresh small crossbow rejected F:** an actual upside-down crossbow settles with its imported transform pivot at **Y -0.332**, while its visible mesh/solid collider centre is correctly above the floor at **Y +0.272**. The old pickup rays and travel sweeps aimed at that buried pivot and hit the floor. Equipping it with 1/2, then dropping it, resets its orientation: the pivot moves to **Y +0.875**, although the collider centre still settles at **Y +0.272**, explaining why the workaround restored F.

`Part.PickupPoint` now uses the centre of active owned solid collider bounds, with owned mesh bounds and then the pivot as fallbacks. Remote range, camera aim and visibility checks use that physical point with a small upward sweep-radius allowance. Retrieval snapshots a local physical probe before disabling colliders and sweeps that probe through lift/flight while still moving the root to its existing corrected socket pose. Pickup markers and the comparison panel use the same physical anchor. Solid-world obstruction checks remain active; this does not globally ignore floors or walls, change physics layers, or edit model/socket assets. The fresh inverted crossbow now completes the actual `MechPlayerInput.RequestPickup()` path without a prior 1/2 swap.

**Axe barrage passed over real enemies:** the Specialist root is at its waist (**Y 1.660** after settling), so the previous root + 1.2m launch height was **Y 2.860**. Actual small-enemy head hitboxes sit around **Y 1.352**. The barrage now launches **1.2m above CharacterController.bounds.min.y**, approximately **Y 1.3** in this ground fixture. This height correction addresses the main miss; increasing the volley from **8 to 24 axes** (45 to 15 degrees) and its retrieval-only collision radius from **0.18 to 0.4m** also closes much of the old spacing between firing lines. Ordinary weapon stats and normal shots are unchanged.

The initial synthetic barrage test aligned targets with its eight rays and placed tall targets relative to the player's root, so it did not expose the real enemy-height problem. The expanded regression now lets actual enemy prefabs ground-snap normally and puts them between the old rays. Before the fix, all four retained 500 HP. After the fix, Sword, Crossbow and Mace each fall to **450 HP**, and Fodder falls to **300 HP** because several independent axes intersect its broader body. Different axes can hit the same target; collider deduplication within one projectile is preserved.

Updated files: `Part.cs`, `RemotePartRetrieval.cs`, `PickupDetector.cs`, `PartSwapWorldHudPresenter.cs`, `RetrievalAxeBarrage.cs`, `RetrievalEffectCatalogue.cs`, and the axe entry in `Combat/DefaultRetrievalEffects.asset`. The tests add `RetrievalGroundAndEnemyAudit.cs` and update barrage, range, combat, cannon and physical-anchor UI fixtures. Paused UI-fixture teleports explicitly sync physics transforms before checking native collider bounds; no per-frame global physics sync was added to presentation.

Verification in isolated Unity 6000.5.8f1: **17 real grounded-drop/enemy checks, 43 barrage checks, 62 remote retrieval checks, 95 pickup UI checks, 65 retrieval combat/bypass checks, 72 anticipation/cannon checks and 982 gameplay regression checks passed (1,336 total)**. The settled upside-down crossbow, actual F entry, actual enemy hitboxes, feet-relative release height, obstruction/cancellation rules, other retrieval effects and comparison-panel placement are covered. Focused dense-volley and left/right pickup-panel captures were inspected. Logs/captures: `.quest-review/Results/RetrievalGroundFix`. Live horde balance remains a playtest.

The **20m range**, lift/hover/fast flight, arrival shake and **enabled temporary bonus-cooldown bypass** remain as requested. No new milestone, enemy AI, normal weapon data, audio or HUD layout work was added in this fix.

## Stage 8 completed: small-crossbow arrival speed buff

Retrieving the real small crossbow (`weaponB2`, `PartData_Arm_SmallCrossbow`) with **F** now grants **3 seconds of 1.5x normal movement speed and 1.25x weapon cadence**, after it successfully reaches and equips in its chosen socket. Lift, hover and departure do not apply the buff. Nearby 1/2 swapping remains an ordinary equipment change and grants no retrieval bonus. The buff has no damage requirement: even a zero-Damage rolled copy with the canonical ID can receive it.

The bonus belongs to the player through `RetrievalSpeedBuff`, separate from the dropped item, travel and shared family gate. It uses the existing `TimedEffect` timer and scaled gameplay time, so pausing pauses it. Another eligible pickup **refreshes to three seconds** and replaces the same multipliers; it never multiplies them or adds durations. Replacing the held crossbow does not transfer the bonus to another player or reset its lifetime. Natural expiry, death, player disable, disabling the buff receiver or disabling retrieval effects clear the multipliers. Cancelling a later pull does not erase an already-running bonus.

### Movement, attacks and presentation

- Normal walking and automatic-run targets multiply their equipped-part/module speed by 1.5. Actual collision-aware movement uses the existing motor. Dash peak speed, committed burst, recovery, assault-boost speed, jump height, gravity, acceleration and knockback settings are not rewritten. The existing dash exit blend still returns to the current normal movement target.
- Newly accepted melee swings and ranged firing intervals divide by 1.25. Newly started ranged automatic and manual reloads also divide by 1.25. This matters because the current weapons have one-round magazines: increasing only Fire Rate can leave the reload as a bottleneck. A 1.25x rate means 80% of the usual duration, not a 25% duration reduction.
- Existing in-progress weapon clocks are left intact when the buff starts, refreshes or ends. Ammo, damage, projectile speed/range and shared PartData stay unchanged. Enemy callers of the shared ranged API retain the default cadence of 1.
- `MechWeaponAnimator` already fits its firing/reload animations to the accepted physical part cooldown; it follows the shorter interval. Animation-only arm attacks also read the current player cadence. Weapon HUD cooldown sweeps and ready cues still read the same physical part clocks; no new HUD layout, animation asset, shader, sound or VFX is authored in this stage. The existing arrival shake remains.

### Shared cooldown, configuration and files

The **SmallCrossbow** family reserves its bonus through anticipation/flight and commits its **30-second player-owned timer at successful arrival**. All small-crossbow copies share that timer, independent of Sword, Mace, Cannon and ThrowingAxe. During its normal cooldown, F still retrieves and equips another crossbow but cannot start/refresh its buff. Rejection, obstruction, destroyed drop, cancelled flight or an unavailable effect receiver refund the pending bonus without starting the timer.

The user's **temporary bypass remains enabled on normal golem, Specialist and Heavy demo**. Therefore repeated F crossbow pickups currently refresh the buff for testing; disabling **Retrieval Bonus Cooldowns → Bypass Cooldown For Testing** restores the saved family timers, including SmallCrossbow. This temporary bypass does not alter normal firing/reload waits.

Tune `buffDuration`, `movementMultiplier` and `attackCadenceMultiplier` on the SmallCrossbow entry of `Combat/DefaultRetrievalEffects.asset`. Watch **Retrieval Speed Buff** on the runtime player for Remaining/Movement Multiplier/Attack Cadence Multiplier; public read-only properties also expose active state and activation count. Read the family timer under **Retrieval Bonus Cooldowns**.

New: `Scripts/Part System/RetrievalSpeedBuff.cs` and metadata; `Tests/Editor/RetrievalSpeedBuffChecks.cs` and metadata. Updated: `RetrievalEffectCatalogue`, default catalogue, `RetrievalCombatEffects`, the three player prefab roots, `MechMovementController`, `MechCombat`, `MeleeAttack`, `RangedAttack`, `Part.TryReload`, `MechWeaponAnimator`, and the existing combat catalogue-count assertion. New enum entries are appended to preserve serialized effect IDs.

Manual test: restart Play Mode, F-retrieve a small crossbow within 20m, and compare walking and held arm fire for the following three seconds. The existing arrival shake marks the trigger. With testing bypass enabled, retrieve another crossbow before expiry and inspect the timer refreshing to three seconds with multipliers staying 1.5/1.25. Disable the bypass and repeat: equipment still changes, but the bonus does not refresh until the SmallCrossbow family timer is ready. Walking should recover on expiry and the next newly started weapon action should use normal timing. No new pickup input or HUD controls are required.

Validation: **76 crossbow-buff checks, 982 gameplay regression checks, 65 existing retrieval-combat checks and 37 pickup-control checks passed (1,160 total)**, all exit 0 in isolated Unity 6000.5.8f1. This includes actual F pickup of the real crossbow, actual collision-aware walking at 1.5x speed, an unchanged committed dash burst/recovery, accelerated melee/crossbow/cannon clocks, variable-magazine firing and manual reload, animation timing, pause, normal expiry, refresh/no stacking, family cooldown suppression, zero-damage canonical copies, obstruction/cancellation, receiver/listener disable and owner death. Real synthetic RMB input fired three crossbow shots in 2.85s at roughly 1.33s intervals, consumed exactly three rounds, stopped on release and returned the same physical weapon to its normal timing after expiry. No production input or HUD layout changes were needed. Logs: `.quest-review/Results/RemoteRetrievalStage8`. Visual buff presentation and live horde balance remain player playtests; this stage adds gameplay multipliers and reuses existing arrival feedback.

## Stage 9 completed: large-crossbow piercing return bolt

F-retrieving the large crossbow (`backweaponA4`, `PartData_Shoulder_LargeCrossbow`) now fires **one independent piercing bolt at fast departure**, after the existing lift and hover. It starts over the weapon's **original ground location** and travels toward the player's position captured at departure. The physical crossbow still follows its ordinary return path and equips normally. Nearby 1/2 swapping grants no retrieval attack.

The initial tuning is **1x the incoming weapon's Damage (currently 100 per enemy)**, **0.5m collision radius / 1m hit width**, the weapon's **160m/s projectile speed**, and **2x the existing bolt visual's scale**. Player damage modifiers are captured with the pending bonus, like the earlier damage effects. The existing bolt model is reused on the temporary attack entity; neither the held crossbow model nor ordinary projectiles are rescaled.

### Route, damage and world rules

`RetrievalPiercingBolt.GroundOrigin` samples terrain below the original physical pickup point, excluding actors and loose parts, and adds the configured **1.2m combat height**. If no suitable ground is within 4m below it, the physical pickup point is the height fallback. The endpoint is **1.2m above the player's CharacterController bottom** at departure. This avoids the earlier waist-pivot launch-height bug and still follows slopes/airborne player positions. The bolt snapshots its direction and finite distance; it does not home or follow subsequent player movement. The route ends at the captured player point, capped by the weapon's configured range (80m at current normal stats, with retrieval itself still limited to 20m).

The separate projectile uses initial-overlap checks plus swept sphere casts for every movement step. Contacts are ordered by distance; the first solid-world/friendly-body contact clips the attack, including a long frame crossing several bodies and a wall. An enemy before a wall can be hit; an enemy behind it cannot. A per-bolt health history deduplicates all limbs/colliders across the complete trip. Each enemy takes damage once even if the bolt remains overlapping it across several updates. Query buffers grow when saturated, so dense compound hitboxes cannot silently hide a farther obstruction.

The shooter's own hierarchy is excluded. The launch-time combat team is retained; allies block the bolt without friendly damage, matching ordinary projectile body blocking. Loose dropped parts, ragdolls and trigger sensors use the existing query exclusions. The bolt deals direct piercing damage only: it does not repeat the large crossbow's normal Splash attack at every contact. Damage/kill feedback uses existing projectile hit events. Ordinary large-crossbow firing still ends at its first impact and retains its existing direct/splash damage, ammo, magazine and reload behavior.

### Cooldown and lifecycle

The **LargeCrossbow** family reserves through anticipation and commits its **30-second shared player timer when the bolt launches**, independent of Sword, Mace, Cannon, ThrowingAxe and SmallCrossbow. Cancelling before departure, a destroyed hovering drop, blocked lift, out-of-range rejection or disabling the listener before launch refunds the pending bonus and creates no bolt.

Once fired, the bolt is separate from the equipment transaction: cancelling the physical pull cannot undo damage or refund its committed cooldown. That already-fired projectile can finish its captured route even if the equipment pull is cancelled. Owner death, listener disable, terminal mission state, range expiry or external projectile disable stops/cleans up the live entity and detaches its trail. The effects listener tracks its own live bolts until they finish, including unusually slow configurations that allow more than one to remain alive.

Normal cooldown rules suppress subsequent bolts while allowing ordinary F pickup/equipment. The user's **temporary cooldown bypass remains enabled on all three player prefabs**, including LargeCrossbow, so repeated F pulls currently create repeated bolts for testing. Turning the bypass off restores the saved per-family timers.

### Presentation and configuration

The new `ICombatTrailSource` contract lets the independent bolt use `CombatProjectileTrail` and the existing bounded trail pool. Normal `MechProjectile` only implements that presentation contract; its normal impact/movement implementation is unchanged. A dedicated `CombatVisualEvents.RetrievalBoltReleased` notification starts the trail with at least 0.25m width at default hit radius, then preserves the existing configured linger/fade after the bolt stops. The retrieval does not raise normal weapon-fire events, spend rounds, change ready-burst state, replay firing recoil or add firing audio. The usual single arrival shake is retained. No new HUD, audio, scene or prefab assets are authored.

Tune the LargeCrossbow entry in `Combat/DefaultRetrievalEffects.asset`: damage multiplier, hit radius, spawn/combat height, speed multiplier, range multiplier and bolt visual scale. Public `BonusBoltCount` / `BoltCreated` on `RetrievalCombatEffects`, and per-bolt HitCount/KillCount/Direction/ImpactResolved/Finished expose runtime diagnostics/integration without coupling pickup movement to damage.

New files: `Scripts/Combat/RetrievalPiercingBolt.cs`, `Scripts/Combat/ICombatTrailSource.cs`, `Tests/Editor/RetrievalPiercingBoltChecks.cs`, and their metadata. Updated: `RetrievalCombatEffects`, `RetrievalEffectCatalogue`, default catalogue, `CombatVisualEvents`, `CombatFeedback`, `CombatProjectileTrail`, the presentation interface declaration on `MechProjectile`, and existing catalogue-count assertions in the combat/speed tests. The new effect enum is appended, preserving existing serialized IDs. The existing player catalogue references already connect all three player prefabs.

Manual test: restart Play Mode, leave a large crossbow within 20m and place several enemies between it and the player. Press F: after hover, a larger bolt should travel down that line, pierce each enemy once and leave its gold lingering trail, while the actual crossbow arrives with its existing shake. A wall should stop the bolt. With testing bypass enabled, repeat immediately; then disable the bypass and verify another crossbow still equips but does not create another bolt until the LargeCrossbow timer is ready. Compare an ordinary large-crossbow shot to confirm it still uses its original first-hit splash attack.

Validation: **48 piercing-bolt checks, 982 gameplay regression checks, 65 earlier retrieval-combat checks, 76 crossbow-buff checks, 43 axe-barrage checks and 151 combat-presentation checks passed (1,365 total)**, all exit 0 in isolated Unity 6000.5.8f1. The real F entry retrieved a naturally settled large crossbow through a line of four actual enemy prefabs; each went from 500 to 400 HP exactly once, and another bypassed pull took each to 300. Tests cover original ground/feet-relative launch height, normal weapon-state preservation, more than 16 compound contacts, long-step wall occlusion, friendly blocking/captured team, initial world overlap, configured range expiry, a fixed endpoint despite player movement, pre/post-launch cancellation, listener/owner cleanup and normal first-hit crossbow splash behavior. The normal and retrieval trail lifecycles, lingering geometry/fade, reuse and presentation-independent damage passed. Two focused graphics captures show the larger bolt crossing the enemy line and its remaining gold path. One stale presentation-test expectation was corrected: a cannon without authored firing VFX adds one impact smoke plume, not an extra firing plume; no firing smoke was reintroduced. Logs/captures: `.quest-review/Results/RemoteRetrievalStage9`. Live horde balance/readability and combined-family stress testing remain stage 10.

The follow-up [directional melee physics](MeleeImpactPhysics.md) adds knockdown/force to ordinary melee and both damaging sword/mace return paths, with stronger killing-blow launches. Return-path damage now follows the physical pickup anchor rather than an offset model pivot. The temporary family cooldown bypass and 20m range remain enabled/unchanged.

**Next: stage 10, combined combat and horde testing**, including mixed retrieval families, moving/dashing/firing during pulls, interacting cooldowns, dense loot and performance/readability. All six weapon-family bonuses are now implemented; stage 10 is the remaining checkpoint.
