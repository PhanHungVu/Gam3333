# Compact world-space part swap UI

The shared `Prefabs/Mech/Base/FullPlayer_mech_base.prefab` owns one `PartSwapWorldHudPresenter`, using `Prefabs/UI/PartSwapWorldUI.prefab`. The panel follows the existing selected dropped part. It has no buttons, raycaster, colliders or device reads. Gameplay detection and equipment validation remain in `PickupDetector` and `EquipManager`.

## Controls

| Selected part | 1 | 2 |
| --- | --- | --- |
| Arm | Left arm | Right arm |
| Shoulder | Left shoulder | Right shoulder |
| Head / legs / torso | Its single matching socket | No action |

`V` still cycles distinct nearby parts. No F modifier or attack-button combination is needed. Mouse buttons and Q/E retain normal attacks. With no selected pickup, 1/2 do nothing. Existing compatibility and torso-transfer validation can still reject an equip; the UI does not override those rules.

The shared Input System asset defines **Player/SwapLeft** (`<Keyboard>/1`) and **Player/SwapRight** (`<Keyboard>/2`). `MechPlayerInput` routes these actions through `PartSlots.SlotsFor` and the existing `TrySwapSelectedInto` method. The same actions supply the displayed key labels, including binding overrides. Single-slot parts use SwapLeft. The optional leg-debug harness now uses **F1–F4**, freeing 1/2 for real pickups. The obsolete global HOLD F instruction is removed from the weapon HUD.

## Compact information

The upper card contains only **NEW**, the incoming part name and its category. The full stat blocks are hidden. Lower cards show **[1] Left / [2] Right** (or **[1] Head**, etc.), the current occupant's short name and up to **four changed stats**. There are no raw values, percentages, current→new number strings or unchanged stat rows.

Examples: **Damage --**, **Fire rate ++**, **Movement -**. Green plus signs mean improvement; red minus signs mean loss. One sign denotes a change below 25% of the old value; two denote 25% or more. Lower cooldown/reload improves Fire rate (Attack rate for melee), so its direction is inverted correctly. Damage, rate, movement, health, range, magazine, ammo and blast size are prioritized; technical projectile fields and boolean flags are omitted. A stat is compared only when both parts actually supply it: missing melee ammunition is not interpreted as zero capacity. Identical/no-comparison values show a dash; an empty socket shows **Empty**. These are qualitative indicators over existing authored stats, not a new gameplay calculation.

The only footer is **[V] Next** when multiple distinct parts are nearby. Binding labels and stat text are larger. Existing rise/fade, target changes, socket acknowledgement and lifecycle cleanup remain. `EquipmentChanged` still owns successful-swap feedback; failed attempts do not invent a confirmation event.

## Presentation and setup

**Tools → Mech → UI → Set Up World Part Swap UI** connects the existing view/player and applies the compact layout once. The serialized layout version prevents repeat setup from overwriting later artist font/styling adjustments. Existing labels/card references and reusable view objects are retained.

Edit the view prefab for final styling. Presenter settings expose screen width, preferred side, player clearance, ground clearance, rise, timing and Reduced Motion. The default left placement avoids the existing upper-right ammo HUD; framing moves/scales only the UI and keeps it clear of the player and ground. The existing gameplay camera is retained.

The scene remains free to move, boost, aim and attack while the panel is visible. Disable/destroy/selection changes clear stale information and release subscriptions; one view is reused per player. The older Popup1/Popup2 hints are suppressed only while the connected world view is enabled and available.

## Files and verification

Runtime: `Scripts/UI/PartSwapDisplay.cs`, `PartSwapWorldView.cs`, `PartSwapWorldHudPresenter.cs`; input routing/labels in `Scripts/Input/MechPlayerInput.cs`; optional debug shortcuts in `Scripts/Movement/MechLocomotionDebug.cs`; updated weapon-HUD instruction in `Scripts/UI/MechWeaponHud.cs`. `PickupDetector` retains the same detection/selection logic. `EquipManager` only receives an updated comment. Setup: `Scripts/Editor/PartSwapWorldUiSetup.cs`.

`PartSwapSummaryChecks.RunBatch` verifies the qualitative signs, cooldown direction, missing-value handling and four-line cap. `PartSwapWorldUiChecks.RunBatch` uses real prefab/input actions for number-key swaps, binding refresh, compact pixel rendering and cleanup. `RunSceneBatch` uses JhMapScene's existing camera and actual arm pickup trigger. `MechFoundationChecks.RunBatch` covers the existing gameplay/equipment/input lifecycle. Run these fixture-building checks only in a disposable temporary project.

The controlled UI fixture has a fixed camera while paused. Some source part prefabs, including the cannon, have only inactive collider children; their UI fixture gets a temporary root collider without modifying the gameplay asset. JhMapScene uses the arm prefab's original active colliders. The gameplay/art owner should still review the affected pickup colliders separately.

Manual review remains: readability and association with the selected part while moving/boosting/orbiting, crowded loot and camera extremes, and final art/font/timing choices. Sound, dialogue UI and pre-mission mission loading are unchanged.

## Compact revision validation — 2026-10-30

Unity **6000.5.8f1 / URP 17.5** compilation succeeded. **10** summary checks, **58** world-UI fixture checks, **14** JhMapScene checks and **932** gameplay/foundation checks passed. The weapon scene passed **41** assertions and the animated-leg scene passed **23** assertions. Real input checks cover 1/2 on both arms and shoulders, key 1 on a single-slot part, no swap without a target, no weapon firing or accidental leg-debug change, binding refresh, empty slots, success feedback and lifecycle recovery. Weapon firing/reload, F1/F2 debug module switching and key-1 ground-leg pickup while moving remain functional. Compact rendered captures were reviewed at 1280×720, 1024×768 and 1600×900.

The old leg fixture expected a fixed 3 m/s; it now checks the existing configured speed (5 m/s in this scene). Gait motion is sampled across its frame window so repeated endpoint poses do not create a false failure. No movement/animation tuning was changed. Existing debug scene fixtures now use F1–F4, and actual pickup tests use 1/2. The known Unity Editor Search startup exception remains under the existing narrow filter; no unexpected UI errors were reported. No standalone player build or human keyboard/mouse feel review was run for this revision.

## Equip camera correction — 2026-10-30

The head/first-person close-up was an unintended camera obstruction response. Replacing the head re-enabled the dropped `Golem1Head` collider beside the camera pivot. The live Cinemachine brain pulled from about **13.08 m to 0.34 m**, and the loose head could keep it there. No equipment event deliberately changes the camera target, distance or lens.

The brief socket acknowledgement, `EQUIPPED` pulse and fade have been restored after the initial animation request was clarified. Compact comparisons and 1/2 controls remain.

`ProjectSettings/TagManager.asset` now names previously unused user layer **8: MechPart**. `Part.SetEquippedPhysics` puts its own Default-layer collider objects on this layer while loose, including collider children, and restores their authored layers when attached. Explicit custom layers remain intact. `CameraControlThirdPerson` excludes MechPart from its configured obstruction mask; normal structure avoidance, near-plane clearance, damping, orbit and targeting remain in use. Collider enable states, gravity and Rigidbody behavior are retained. No physics collision matrix or part prefab is changed. Include the TagManager change when sharing this fix; custom queries restricted to the Default layer must include MechPart if they need to hit loose parts.

`MechEquipCameraSceneChecks.RunBatch` passed **29 checks** with the actual shared player and live camera: head, both arms, torso and leg replacement preserve approximately 13.08 m framing throughout each physical drop; reattachment and custom layers survive repeated swaps; a real wall still pulls the camera forward and clearing it restores the view. The starting torso has no own authored collider; the fixture preserves that asset rather than inventing one. These checks run headlessly in a disposable project. The first graphical test attempt ran out of memory during Editor startup; the headless regression completed successfully.

Final verification also passed **932** existing gameplay/foundation checks, **58** rendered UI checks (including the restored `EQUIPPED` acknowledgement/fade) and **14** real-scene UI checks, for **1,033 checks** in this revision. The graphical retry completed successfully. The existing custom-layer wall fixture now uses layer 9 instead of the newly reserved MechPart layer. Actual scene physics still discovers pickups on the new layer, and the view retains the existing gameplay camera and player target. Scene verification does not add any pre-mission gameplay handoff or scene loading. A human swap/camera comfort review remains to be done.
