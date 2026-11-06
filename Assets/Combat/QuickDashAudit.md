# Quick dash audit and proposed implementation

Historical Phase 0 audit: 2026-11-06, commit `bb14ca7` (Damage feedback). The user subsequently approved all four implementation phases together. See [QuickDashPolish.md](QuickDashPolish.md) for the implemented tuning, verification and testing instructions. The measurements below describe the original implementation.

## Existing movement

Both `FullPlayer_mech_golem.prefab` and `FullPlayer_mech_heavy.prefab` use `MechMovementController`. No saved scene overrides its quick-dash fields.

| Field or behavior | Current value |
| --- | --- |
| Serialized peak | 24 units/s |
| Distance/speed multiplier | 1.75; effective peak is 42 units/s |
| Serialized duration | 0.22s |
| Duration multiplier | 1.35; effective duration is 0.297s |
| Initial relative speed | 0.65; initial speed is 27.3 units/s |
| Curve keys | (0, .65), (.12, 1), (.55, .75), (1, .1) |
| End-of-curve speed | 4.2 units/s before normal recovery |
| Recovery after active dash | 0.35s |
| Nominal interval between starts | 0.647s, rounded up by movement ticks |
| Post-boost deceleration | 64 units/s²; shared with retained assault-boost braking |
| Retained assault-boost speed | 28 units/s |

The dash already bypasses walking acceleration, starts in the accepted input tick, and captures its movement direction. It cannot sharply steer mid-dash. Without movement input it uses camera-forward. Hard lock changes facing without changing the captured dash direction. CharacterController.Move handles the actual displacement and collision.

Biped/Fast/Heavy legs require grounding. Hover allows airborne quick boost. Tank requires grounding and substitutes the tank's forward direction. MechEquipmentLocomotion already applies these strategies; they should be retained.

Shift is the current quick-boost action. Hitstop already buffers a dash press. A press rejected during ordinary cooldown is not independently buffered. Serialized energy fields remain, but the motor no longer spends/checks energy for the dash. Energy was retired in the current control pass and should not be reintroduced.

The brief's Ctrl/assault-boost wording is outdated for this branch: Ctrl invokes the torso ability. Assault boost remains implemented in the movement API but is not requested by MechPlayerInput. Preserve the current controls and the retained assault-boost behavior.

## Measured baseline

An isolated Unity 6000.5.8f1 run stepped the real CharacterController and unchanged player prefabs against a flat floor at three tick rates. Both variants produced the same active-dash results. Side input during the burst did not alter its direction.

| Tick rate | Active movement time | Peak speed | Active-dash distance | Distance in first nominal half |
| --- | --- | --- | --- | --- |
| 30 FPS | 0.300s | 41.82 | 8.727 units | 61.6% |
| 60 FPS | 0.300s | 41.82 | 8.522 units | 63.6% |
| 120 FPS | 0.300s | 41.93 | 8.423 units | 64.6% |

The motor samples one curve value per movement step and applies it to the full step. It does not integrate the active fraction of a step crossing the dash boundary. This produces approximately 3.6% distance variation between these 30/120 FPS runs. Cooldown admission is also tick-quantized: measured first-sample-to-ready time was 0.700/0.667/0.658s; nominal timing remains 0.647s.

Walking speeds with the current starting loadouts were approximately 9.80 units/s for golem and 8.71 for heavy. The existing dash can therefore dip below ordinary walking speed toward the end. Its recovery should blend toward requested walking velocity rather than introduce a brief stop.

These measurements characterize the current implementation; they are not results for the proposed tuning.

## Existing camera and presentation

`CameraControlThirdPerson`, configured by `Prefabs/Main Camera.prefab`, already has a quick-boost FOV curve with a configured +7° kick. It starts at zero, peaks at 20% dash progress and returns to zero. A shared exponential response with a fixed 0.08s time constant smooths the requested offset; +7° is a configured maximum, not a claim that the rendered FOV reaches it during every dash.

Quick-boost follow damping is already zero, versus normal damping (.045, .09, .13). More immediate following is not needed by default. Avoid adding a second lag controller. Melee framing temporarily holds movement feedback updates, which Phase 2 should check when verifying FOV recovery.

The retained assault boost uses separate +10° FOV and 1.12x distance settings; keep these. CombatFeedback already uses the camera's Cinemachine impulse listener (channel 64), so later dash impulses can share that system. There are no current quick-dash start/end or landing impulse subscriptions.

`MechBoostPresentation` already subscribes to accepted QuickBoostStarted events for its existing ignition/exhaust presentation. It controls visual lean, exhaust particles and existing trails, but has no ground-contact dust emission. It does not wait for an animation to permit movement.

## Existing dust

The board effect is **Small Impact Dust** inside `PreMission/StoneBoard/QuestStoneBoard.prefab`, referenced by QuestBoardImpactFeedback.smallDust. Runtime inspection reports a non-looping **world-space ParticleSystem**, 0.55–0.95s particle lifetime, unscaled time and `Impact Dust` material. It is compatible with world gameplay presentation.

`PreMission/StoneBoard/Dust Puff.asset` is its 32×32 texture, not a spawnable VFX prefab. The board particle object is on the quest-board presentation layer. Phase 3 should create a separate dash prefab from the particle settings, use an appropriate gameplay layer and scaled combat time, and preserve the board's prefab, materials and texture.

Dust should be pooled and emitted once per accepted grounded dash. Capture the push-off ground point before displacement: the existing QuickBoostStarted callback is published after the movement step, so reading only the callback-time feet would place dust ahead of the true launch point at high speed. Suppress ground dust for a genuinely airborne hover dash. No continuous emission or optional end dust is proposed initially.

## Proposed four implementation phases

1. **Dash movement:** Start around 95–100% speed; target a 50-unit/s peak and 0.25s active duration. Neutralize the legacy speed/duration multipliers on both player prefabs so those exposed numbers describe the actual dash. Use the brief's front-loaded curve, integrate its contribution across frame boundaries, retain the committed direction and preserve leg restrictions. Blend the final recovery toward requested normal movement. Target approximately 8–8.6 units of total active movement, close to the existing 60 FPS baseline, and retain an approximately 0.65s minimum start-to-start interval (about 0.40s recovery after the shorter dash). Keep existing energy retirement and assault boost unchanged.
2. **Camera response:** Retune the existing quick-dash FOV path around +5°, approximately 0.05s attack and 0.25s recovery. Preserve the assault profile and lock-on behavior. Keep or cautiously tune the existing fast follow response; do not add another lag rig. Reuse the existing Cinemachine impulse system for a short start impulse and weaker grounded end impulse, with real landing distinguished from dash completion.
3. **Ground dust:** Create a separate pooled dash-dust prefab from the compatible mission-board ParticleSystem settings. Bind it to accepted dash-start events and a cached launch ground point/direction. Preserve all original board assets. Tune only the start burst first.
4. **Responsiveness and compatibility:** Verify all directions, zero input, firing, hard/free aim, jumps, hover/tank/biped legs, cooldown/repeat presses, hitstop, damage, walls, slopes, corners, dropped parts and retained assault transitions. Compare peak, duration, early distance, final recovery and repeat interval at 30/60/120 FPS. Test camera and dust separately before the combined feel pass.

## Files expected to change after approval

Phase 1: `Scripts/Movement/MechMovementController.cs`, both `Prefabs/Mech/Base/FullPlayer_mech_golem.prefab` / `FullPlayer_mech_heavy.prefab`, focused movement tests, and existing tests with intentionally obsolete speed expectations (`MechLocomotionChecks` and `MechFoundationChecks`). Existing locomotion modules, input bindings, weapons, enemies and HUD need no changes.

Phase 2: `Scripts/Camera/CameraControlThirdPerson.cs`, `Prefabs/Main Camera.prefab`, and the smallest presentation hook needed to reuse `CombatFeedback` impulses. Phase 3: a separate dust prefab and bounded dash-presentation adapter, reusing existing event/pool patterns. Later file choices should be confirmed against the completed earlier phase rather than editing everything at once.

Audit artifacts are in `.quest-review/Results/QuickDashAudit`: six baseline measurements, the successful Unity log and the private audit script. Implementation and subsequent validation are recorded in [QuickDashPolish.md](QuickDashPolish.md).
