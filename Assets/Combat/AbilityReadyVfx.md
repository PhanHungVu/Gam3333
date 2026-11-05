# Ability cooldown ready burst

Ability and weapon icons now emit a brief white flash and a narrow upward column of glowing dots when their **actual cooldown transitions from cooling to ready**. The icon returns to normal brightness, the existing sweep/countdown disappear, and the icon briefly pulses to 1.1× scale. Each icon owns its own visual.

## Verify in Play Mode

1. Enter unpaused Play Mode in a scene with the player/HUD.
2. In the Hierarchy, expand the live **CombatHUD → AbilitiesHUD** and select a spawned ability pod.
3. Open the **Abilities Hud Pod** component's context menu and choose **Activate**. Wait for its countdown to reach zero: the white column rises above that icon and fades away.

The existing authored head/torso/legs cooldowns are 10s/30s/6s. Select the live instance, not the Project prefab. Head/torso gameplay requests remain intentionally unimplemented, so pressing Tab/Ctrl alone does not confirm an ability or start its cooldown. The Inspector activation previews cooldown state; it does not perform Heal/Damage Amp gameplay.

## Weapon ready bursts

Fire or swing a weapon in Play Mode and let its cooldown finish. Arm/shoulder weapons and melee weapons use the same **60-particle, 0.55s white burst**, flash and 1.1× pulse as abilities. Automatic magazine reloads and accepted manual reloads also trigger the effect when their complete attack/reload wait ends. Existing grey-out and the rising cooldown line remain.

Weapon-specific tuning is on **Ability Ready Vfx** on `Prefabs/UI/WeaponsHUDPodPrefab.prefab`; it uses the shared renderer but can be tuned separately from the ability prefab. Older weapon pods receive the same runtime fallback.

`Part.WeaponCooldownCycle` and `TryConsumeWeaponReadyCue(firstObservedCycle)` track presentation against the existing `ReadyAt` deadline. A completed wait is recorded before an immediately repeated shot overwrites that deadline, so holding fire does not skip the ready cue. If that next shot already started, its new cooldown grey-out/sweep remain accurate while the completed wait emits its brief burst. No firing intervals, magazine counts, reload timings or attack admission rules change.

An exhausted ranged weapon does not emit a new ready burst when its final cooldown ends. Ready initialization/rebinding does not flash, duplicate rows consume one physical completion only, and a rebuilt row can observe a still-running cooldown finishing. Renderer allocation remains bounded to one cached visual per pod, including rapid held fire.

## Tuning

Edit **Ability Ready Vfx** on `Prefabs/UI/AbilitiesHUDPodPrefab.prefab`. Older scene/prefab copies receive the same component when their ability pods initialize.

| Setting | Default |
| --- | --- |
| Total duration | 0.55s |
| Initial flash | 0.08s |
| Particle count | 60 |
| Column height | 5.5 icon heights |
| Horizontal spread | 0.28 icon widths |
| Brightness | 1.35 |
| Particle lifetimes | 0.25–0.55s |
| Icon pulse | +0.10 scale (1.1× peak); 0 disables it |

The main effect is the upward streaming column, with a dense white core and softer outer glow. It uses Canvas-native geometry with soft particle edges, so it needs no imported texture, special camera bloom, ParticleSystem plugin, new Canvas or audio. It follows the icon through Canvas scaling and uses the same Canvas/render mode. The mesh sits at the end of the root Canvas's draw order and is non-interactive; masks on an individual pod cannot crop the column. Disabling/destroying the pod stops the visual and restores its original icon scale.

One renderer and a fixed particle buffer are reused per pod after its first burst. Particles do not instantiate individual GameObjects or materials, and their private visual random generator does not affect gameplay randomness. The visual's short animation uses unscaled time; cooldown readiness continues to use the existing gameplay clock.

## Gameplay connection and transition rules

Keep the existing integration: after an ability actually succeeds, call `PartAbilityCooldown.For(part).ConfirmActivation(actualCooldownSeconds)` or `MechAbilitiesHud.ReportAcceptedActivation(slot)`. Authoritative owners can continue to call `Synchronize(remainingSeconds, durationSeconds)`. No ability durations, input admission or effects were added/changed.

`PartAbilityCooldown` keeps a presentation-only cycle/consumption marker alongside its existing deadline. An observing HUD can consume each completed cycle once. Ready initialization/rebinding, a newly rebuilt already-ready icon and duplicate HUD views do not emit additional bursts. A rebuilt cooling icon continues observing the same part and emits once when it finishes. Tiny authoritative remaining-time corrections of 0.1s or less after reaching ready do not arm another visual cycle; an explicitly confirmed activation still arms one even with a short cooldown. The underlying cooldown values and availability are applied unchanged.

`AbilityReadyVfx.Play()` is the reusable cosmetic entry point; normal HUD use calls it only for the accepted cooling-to-ready transition. It does not start a cooldown or execute ability gameplay. Disabling the component omits the effect.

## Validation — 2026-11-05

Isolated Unity **6000.5.8f1 / Direct3D 11** completed **108 integration checks**, including **21 new ready-effect checks**. These cover real cooldown expiry, independent icons, no initialization/rebind replay, tiny authoritative corrections, duplicate HUD views, renderer reuse, older-pod fallback, cooldown-preserving rebuilds, icon restoration, click-through, private visual randomness and cleanup. Renders at **1920×1080 and 1280×720** were reviewed; the origin was also checked on a world-space Canvas. No C# warnings/errors or unexpected runtime exceptions occurred in the completed batch.

The broader run also passed all **151 combat regression checks**, then ran out of system memory while allocating a texture during a later generic HUD capture. The complete integration batch was therefore rerun separately and passed. This is not a claim that the combined batch completed. Artifacts, both logs and the reviewed renders are saved in `.quest-review/Results/AbilityCooldownReadyVfx`. No manual real-input playthrough or player build was performed.

The weapon extension subsequently passed **129 isolated integration checks**, including **21 new weapon-ready checks** and all 21 ability-ready checks. Real melee/arm/shoulder attacks, shot intervals, automatic/manual reloads, immediate held refiring, last-round exhaustion, duplicate views, swapping/rebuilding and visual cleanup were verified. A rendered weapon-ready capture was reviewed. Compilation produced no C# warnings/errors and the completed run had no unexpected runtime exceptions. Results are saved in `.quest-review/Results/WeaponCooldownReadyVfx`; no manual real-input session or player build was performed.
