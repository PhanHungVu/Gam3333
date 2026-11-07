# Quick dash movement and presentation

Implemented on 2026-11-06 following approval to complete all four phases. `FullPlayer_mech_golem`, `FullPlayer_mech_heavy` and the newly merged `FullPlayer_mech_specialist` are configured. Existing scene instances inherit their prefab changes. See [SpecialistPlayerIntegration.md](SpecialistPlayerIntegration.md) for the new body's integration.

## 1. Movement

`MechMovementController` starts at 95% of a 50 units/s peak and runs for 0.25 seconds. Its editable speed curve uses (progress, speed) keys `(0,.95),(.1,1),(.3,.95),(.5,.75),(.75,.4),(1,0)`. Legacy speed/duration multipliers are retained for serialization compatibility and set to 1 on both player prefabs.

The motor integrates the curve across the active portion of each movement step. It commits to the launch direction for the first 80%, then blends the final 20% into requested walking movement. Releasing movement ends stationary; holding a direction restores walking without a pause or lingering boost speed. CharacterController still resolves the actual displacement, including external knockback.

Recovery is 0.40 seconds after the active dash, retaining approximately the previous 0.65-second start-to-start cadence. Countdown rounding snaps only sub-microsecond remnants to zero. Shift remains edge-triggered: release and press again for another dash.

Measurements using the actual capsules and player loadouts:

| Quantity | Result |
| --- | --- |
| Configured launch / peak speed | 47.5 / 50 units/s |
| Measured peak at 60 / 120 FPS | 49.84 / 49.93 units/s |
| Active duration | 0.25s |
| Active distance, released movement | 8.112 units |
| Active distance, held forward | 8.357 golem / 8.330 heavy |
| Distance in first half | 71.3% released; 69.2–69.4% held |
| Distance disagreement at 30/60/120 FPS | Below 0.00001 units on the flat fixture |
| Minimum nominal repeat interval | 0.65s, subject to input sampling |

The baseline 60 FPS distance was 8.522 units. The stronger beginning therefore keeps approximately the previous reach.

## 2. Camera

The existing `CameraControlThirdPerson` produces a +5 degree FOV pulse with 0.05s rise and 0.25s recovery. Recovery continues during melee framing. The existing quick-boost follow damping responds immediately, then blends back to normal damping over 0.12s. The current follow system supplies the acceleration response without another camera offset controller.

`QuickDashFeedback` requests start and stop impulses through `CombatFeedback` and its existing Cinemachine listener. Start strength/duration is 0.035/0.07s; grounded stop is weaker at 0.018/0.06s. Airborne endings and an actual landing on the same frame suppress the stop impulse. The listener continues to use the largest overlapping impulse rather than summing every weapon/damage/movement shake.

The retained assault-boost API still reaches 28 units/s with +10 FOV and 1.12x camera distance. Current input uses Ctrl for torso ability; this work does not rebind it to assault boost.

## 3. Dust

`Prefabs/Combat/Quick Dash Dust.prefab` copies the mission board's Small Impact Dust particle setup and shares its existing Impact Dust material. The original board prefab, material and texture are unchanged.

Each player prewarms twelve emitters. An accepted grounded dash starts with a backward puff at its cached push-off contact, then places additional independent puffs every 1.6 units of actual planar travel, up to six total. Ground is checked at each sampled position; particles remain in world space and fade naturally rather than forming a connected ribbon. Blocking a dash stops the dust advancing, and standing still cannot repeatedly emit a stack of puffs.

Each puff uses 48 particles, 1.35x scale and a 1.25x lifetime multiplier (approximately 0.375–0.75 seconds). These settings make the puffs visible along the dash rather than leaving the entire effect at its origin. Airborne hover dashes and rejected presses produce no new dust sequence. The fixed pool supports overlapping puffs and is removed with its owner. There is no continuous emission or TrailRenderer.

## 4. Controls and compatibility

Shift, weapon inputs, lock-on and current ability controls remain as configured. Biped/Fast/Heavy legs now allow dashing during a jump: Space + Shift together, jumping before dashing, and jumping during a grounded dash all work. The dash changes horizontal motion and preserves vertical jump/gravity, with the existing cooldown and no extra jump or airborne dust. Hover retains airborne dashes; Tank retains grounded forward-only dashes. Retired energy costs stay retired. Existing attacks and damage remain possible during a dash, with existing hitstop buffering preserved.

No HUD, enemy AI, weapon behavior, authored animation or sound assets were changed.

An editor-only prerequisite fix in `PartData` defers duplicate-ID validation until loading finishes and coalesces repeated requests. Its former `OnValidate` recursively loaded other parts while prefabs were deserializing and crashed the isolated Unity editor. Runtime part data and stats are unchanged.

## Inspector locations

- Player prefab → `MechMovementController` → Quick Boost: speed, duration, recovery, curve and exit blend.
- `Prefabs/Main Camera.prefab` → `CameraControlThirdPerson` → Movement Feedback: FOV pulse and follow recovery.
- Player prefab → `QuickDashFeedback`: dust prefab, count, scale, lifetime multiplier, spacing, maximum puffs per dash, pool size and impulse strengths/durations.
- `Prefabs/Combat/Quick Dash Dust.prefab`: particle appearance and backward spread.
- `Tools > Mech > Movement > Apply Quick Dash Polish` reapplies these starting settings to all three player prefabs and camera. It preserves an existing dust prefab's authored settings.

## Verification and playtest

The isolated Unity 6000.5.8f1 validation uses real capsules, player prefabs, equipment, particles and cameras. The initial foundation suite passed **980 checks**. After the spaced-puff revision, the graphical focused suite passed **221 checks** and the live A33 scene passed **8 checks**. Focused validation covers integrated travel at 30/60/120 FPS, direction commitment, recovery, cooldown, thin walls, uphill collision, dropped parts, leg rules, accepted/rejected dust, grounded/airborne stop impulses, landing deduplication, camera recovery, retained assault boost, damage, firing and pool cleanup. It also verifies six ground puffs spaced 1.6 units apart at every tested frame rate, coverage through the dash endpoint, no continuous emission or TrailRenderer, and suppression of extra puffs when a wall stops movement. The A33 run drives the real input actions with a simulated keyboard, verifies hard lock and the live FOV pulse, and verifies release/re-press behavior, pool reuse and several visible puffs during movement. Dust and scene captures were visually inspected.

Reproducible entry points in an isolated batch project:

- `QuickDashPolishChecks.RunBatch`: focused checks and graphical dust capture when graphics are enabled.
- `QuickDashPolishChecks.RunSceneBatch`: A33 live input/camera check and scene captures.
- `MechFoundationChecks.RunBatch`: existing regression suite.

Review artifacts are kept under `.quest-review/Results/QuickDashPolish`. Generated fixtures and test poses are confined to the disposable project; no source scene was saved.

For a manual feel pass, open A33 or your combat test scene, press Shift from idle and while holding each movement direction, then repeat while firing and hard-locked. Test a wall and a slope, release movement before the dash ends, and hold Shift long enough to verify it does not auto-repeat. Check airborne Hover and Tank's forward-only behavior via the existing locomotion debug controls. Tune distance and shake from those playtests; automated rendering cannot determine personal feel.

No standalone player build or human gameplay feel review is included in automated validation.
