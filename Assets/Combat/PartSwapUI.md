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

The upper card contains **NEW**, the incoming part name and its category on the left, with up to **four basic numeric stats on the right**. Lower cards show **[1] Left / [2] Right** (or **[1] Head**, etc.), the current occupant's short name and up to **four changed stats**. Each difference sits directly beside its stat label: **Damage +10**, **Movement -4**. There are no bars, arrows, repeated base-value blocks or percentage indicators.

Differences are **incoming minus the part in that particular socket**: new damage 50 versus old damage 40 shows green **Damage +10**; the reverse shows red **Damage -10**. **Fire rate / Attack rate** reads the new **FireRate** stat directly in attacks per second, so a faster weapon produces a positive rate difference. Attack timing uses `PartData.ShotInterval` (1 / FireRate), independently of magazine reload seconds. Other values retain the authored stat units: Range is in golem lengths; Movement is the legs' base metres per second or any other part's speed modifier in percentage points. Numbers use at most two decimal places, with scientific notation for very small nonzero values so they do not read as zero. Damage, rate, movement, health, range, ammo, magazine and blast size are prioritized; technical projectile fields and inactive ability/accuracy stats are omitted. A stat is compared only when both parts actually supply it: missing melee ammunition is not interpreted as zero capacity. Identical/no-comparison values show a dash; an empty socket shows **Empty**. These are readouts of existing authored stats; gameplay tuning is unchanged.

The only footer is **[V] Next** when multiple distinct parts are nearby. Binding labels and stat text are larger. Existing rise/fade, target changes, socket acknowledgement and lifecycle cleanup remain. `EquipmentChanged` still owns successful-swap feedback; failed attempts do not invent a confirmation event.

## Presentation and setup

**Tools → Mech → UI → Set Up World Part Swap UI** connects the existing view/player and applies layout version **2** once. The incoming stats use the existing right-hand text object in the upper card; the name/type occupy the left half. Comparison text is slightly smaller to accommodate actual numbers. The serialized layout version prevents repeat setup from overwriting later artist font/styling adjustments. Existing labels/card references and reusable view objects are retained.

Edit the view prefab for final styling. Presenter settings expose screen width, centered-target fallback side, player clearance, ground clearance, rise, timing and Reduced Motion. The panel chooses its side from the selected ground part's position relative to the player **in the gameplay camera view**: a visible right-side pickup uses the right, a left-side pickup uses the left. Cycling to a different part chooses its side immediately during the existing fade transition. A small center buffer prevents flicker while the camera/player moves; clearly crossing the center updates the side. `preferRightSide` is now only the fallback for a newly selected part aligned exactly with the player. The existing space check can use the opposite side if the chosen side cannot fit a readable panel. Framing moves/scales only the UI and keeps it clear of the player and ground. The existing gameplay camera is retained.

The world-space panel renders over scene geometry, including parts between it and the camera. Its backing cards use `Materials/UI/Part Swap Overlay.mat` and `PartSwapOverlay.shader`; text uses the referenced TMP Distance Field Overlay shader. Both draw in the overlay queue without depth testing/writes. The view copies each distinct source font material once when created, preserving its atlas/style and leaving shared font assets and other HUDs untouched; it releases those copies when destroyed. Setup wires these rendering references independently of the layout version, so existing styled prefabs receive the fix without a layout reset. Positioning, selection range, fades, camera behavior and input remain their existing systems.

The scene remains free to move, boost, aim and attack while the panel is visible. Disable/destroy/selection changes clear stale information and release subscriptions; one view is reused per player. The older Popup1/Popup2 hints are suppressed only while the connected world view is enabled and available.

## Files and verification

Runtime: `Scripts/UI/PartSwapDisplay.cs`, `PartSwapWorldView.cs`, `PartSwapWorldHudPresenter.cs`; input routing/labels in `Scripts/Input/MechPlayerInput.cs`; optional debug shortcuts in `Scripts/Movement/MechLocomotionDebug.cs`; updated weapon-HUD instruction in `Scripts/UI/MechWeaponHud.cs`. `PickupDetector` retains the same detection/selection logic. `EquipManager` only receives an updated comment. Setup: `Scripts/Editor/PartSwapWorldUiSetup.cs`.

`PartSwapSummaryChecks.RunBatch` verifies exact numeric differences, consistent rate units, missing/non-finite values, locale-independent decimals and the four-line cap. `PartSwapWorldUiChecks.RunBatch` uses real prefab/input actions for number-key swaps, binding refresh, numeric pixel rendering and cleanup. `RunSceneBatch` uses JhMapScene's existing camera and actual arm pickup trigger. `MechFoundationChecks.RunBatch` covers the existing gameplay/equipment/input lifecycle. Run these fixture-building checks only in a disposable temporary project.

The rendering-over-models revision passed **95 UI checks** and **20 existing-scene checks** in Unity 6000.5.8f1. Rendered comparisons place a solid mesh between the camera and the whole panel and check both font and backing-card visibility. Card-only sampling with opaque fixture backings isolates depth visibility from expected scenery/alpha blending; ordinary captures retain the authored translucency. Shared font assets, the presenter/camera placement code, player prefab, input actions, gameplay scene and empty mission-start hook retained their hashes.

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

## Numeric comparison revision — 2026-10-30

The incoming part's basic values now occupy the upper card's right half, above the existing left/right comparison cards. Exact signed differences replace the qualitative +/++ indicators. The name and comparison fonts are slightly smaller; no bars or connecting lines are added. This is layout version 2 of the same view prefab, preserving its GUID and serialized references. The incoming and comparison sections each remain limited to four relevant stat rows.

Unity compilation and **92 focused checks** passed: **17** numeric formatter checks, **60** rendered UI/input/lifecycle checks and **15** existing gameplay-scene checks. Rendered captures were reviewed at 1280×720, 1024×768 and 1600×900, including actual scene framing. Verification covers the explicit 50-versus-40 damage example, negative changes, rate conversion, missing/non-finite stats, decimal formatting, head and both weapon sockets, real 1/2 input, equipment acknowledgement, cycling/binding refresh and cleanup. Protected gameplay scripts, part data, input actions, the shared player prefab, scenes, source art and the empty mission-start hook retain their pre-revision hashes. The current Input System-only project setting was also used by the isolated checks. No standalone player build was run.

## Selected-part placement revision — 2026-10-30

Panel placement now follows the selected part's projected side relative to the golem. Actual V cycling between a left and right drop moves the same reusable view to the corresponding side. Camera orbit and a selected part moving across the player update placement; a 2% screen-width center buffer prevents rapid switching near the center. A new selection chooses its visible side immediately, with `preferRightSide` retained only for centered targets. Existing screen-edge/readability constraints remain active.

The rendered UI regression passed **90 checks**, including real V input in both directions, left/right framing, camera orbit, center stability, numeric comparisons, 1/2 swaps and lifecycle cleanup. Captures cover both sides at 1280×720 and the right-side view at 1024×768. The cleanup assertion now waits for the complete hidden-and-cleared state: alpha can cross the visibility threshold one frame before the equip acknowledgement clears its stored data. Production acknowledgement timing is unchanged.

The existing gameplay-scene run also passed **15 checks** with its real pickup trigger and camera, for **105 checks** in this placement revision. Its 1600×900 capture was reviewed. Gameplay/camera code, input actions, part data, the shared player/view prefabs, scenes and the empty mission-start hook retain their pre-placement hashes. No asset setup step is needed for this runtime placement fix.

## Non-blocking dropped parts — 2026-11-06

Loose parts now pass through players and enemies while retaining gravity, floor contact and pickup-trigger detection. Every loose collider child uses MechPart, including children authored on a custom layer. Equipping restores its original layer. Character body and limb colliders exclude MechPart through per-collider overrides; the global layer collision matrix is unchanged. Original part exclusions are restored on drop.

Movement and grounding queries, enemy positioning checks, spinner charge checks and rope movement ignore MechPart. A wall behind loot still blocks movement. This applies to both player variants, existing enemies, newly spawned enemies, enemy drops and parts displaced by equipment swaps; no scene/prefab upgrade step is required.

`DroppedPartCollisionChecks.RunBatch` passed **17 checks** in isolated Unity 6000.5.8f1: both real player variants traverse solid loot, still stop at walls, detect/select pickups, equip and drop again, and retain floor contact. The enemy checks verify movement, grounding, a wall behind loot and falling parts passing through its head/arm/body colliders. Results are under `.quest-review/Results/DroppedPartCollisions`.

The existing equipment/camera regression also passed **29 checks**, covering head, arm, torso and leg swaps, restored authored layers, physical drops and unchanged camera framing. Both runs completed with exit 0, without C# diagnostics or runtime exceptions; verification was headless.
