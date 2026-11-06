# Player damage feedback — Phase 1

## Audit

| System | Existing behavior | This change |
| --- | --- | --- |
| CombatHealth | Confirms HP loss, emits amount-only Damaged and Changed events, disables dead actors | Adds optional source data without replacing existing callers/events |
| CombatBlast / MechProjectile | Shared team, obstruction, hit-history and damage rules | Passes attack-origin data after the same damage admission checks |
| CombatFeedback | Damage Cinemachine impulse and actor hitstop already exist | Unchanged |
| MechHealthHud | Current HP and delayed white damage bar; handles healing/capacity changes | Unchanged |
| PlayerDamageVignette | Persistent low-HP edges, starting at 50%, reaching 0.8 alpha at 1% HP | Unchanged |
| Shared CombatHUD | Player-bound health, weapons and abilities under Mech Cursor Canvas | Adds one nested Directional Damage HUD prefab |
| Directional indicator / temporary hit flash | Neither was implemented | Directional indicator only; temporary flash remains a later phase |

## How it works

`CombatHealth.ApplyDamage(float)` remains available. Its new overload accepts an optional world-space source position:

```csharp
health.ApplyDamage(amount, sourcePosition);
```

Both calls retain the existing validation, HP loss, Damaged/Changed events and death behavior. The new per-health `DamageReceived` event carries `CombatDamageInfo`: the actual applied loss and optional source position. Rejected damage emits no event. Healing and capacity changes emit no damage event. Invalid or absent source coordinates are non-directional.

Melee boxes supply their wall/attack origin. Direct projectiles supply a point along their incoming trajectory at impact, preserving the direction even if the shooter moves or disappears. Explosion and circular-area damage supply their area center. Damage rules, projectile motion and enemy decisions are unchanged.

`DirectionalDamageHud` subscribes only to its bound player's health. At impact it captures the horizontal bearing; the live gameplay camera projects that bearing to the screen each LateUpdate. Player body rotation and movement do not determine the direction. Front appears at the top, behind at the bottom, and left/right at their respective edges. Camera rotation updates cues already on screen.

Default presentation is a red, outward-pointing chevron with a feathered halo, a 0.12-second hold and 0.38-second smooth fade. Animation uses unscaled time so damage hitstop does not freeze the indicator. Eight authored widgets form a fixed pool; at most four are shown by default. Hits within 30 degrees refresh an existing cue. When full, a new direction replaces the oldest cue. No objects are instantiated per hit.

## Prefab and tuning

Open `Prefabs/UI/Combat HUD/Directional Damage HUD.prefab`. Its controller exposes maximum simultaneous cues (1–8), hold/fade duration, merge angle, edge padding and tint. The child graphics define the chevron dimensions. The prefab is nested in the existing `CombatHUD.prefab`, so both player variants and scenes using the shared canvas receive it automatically.

The widgets do not intercept clicks, move the camera or cover the screen center. An authored nested Canvas uses sorting order 50 to keep cues above dialogue and other HUD panels while sharing the parent Canvas/camera; it has no raycaster. They clear when the player/HUD becomes unavailable or dies. `Bind(MechPlayerRig)` supports explicitly binding a particular player's view; the default auto binding follows the project's existing local-player HUD convention. No network or global damage-event dependency is introduced.

**Tools > Mech > UI > Connect Directional Damage HUD** reapplies the saved prefab connection without duplicating it or overwriting existing indicator tuning. No setup step is needed for the already connected assets.

## Verify in Play Mode

1. Open `EnemyBehaviorPlaytest`, ZooScene or a game-mode test scene and enter Play Mode.
2. Expand the live `Mech Cursor Canvas > CombatHUD > Directional Damage HUD` and select its controller.
3. In the component context menu, use **Debug > Take 1 Damage From Front / Behind / Left / Right**. These Editor-only commands apply one real damage point through CombatHealth, exercising confirmed feedback rather than faking a visual.
4. Hard-lock an enemy in front, then choose the Behind command. Its cue should appear at the bottom, regardless of the golem's facing direction.
5. Rotate the camera while a cue is visible. Repeat hits from different sides, including during dodge/boost movement. The cue count stays bounded and fades in approximately half a second.
6. Test normal enemy melee/projectile/area hits. Damage with no meaningful horizontal source creates no directional cue; existing non-directional feedback continues to handle that hit.

This phase does not add the temporary red screen flash or retune the white HP bar, shake or low-HP vignette. Those roadmap phases remain separate for review.

## Verification — 2026-11-06

**75 checks passed** in isolated Unity 6000.5.8f1 / Direct3D 11 (exit 0), including the existing DamageChecks regression. Tests cover optional/unknown sources, applied-loss and death events, friendly-fire rejection, melee/area origins and a real projectile with a moving shooter. Both actual player variants receive one shared HUD; tests verify all four directions against a reversed player body, camera rotation, near-vertical pitch, independent/merged cues, 100 rapid hits, configurable caps, fade, unrelated actors, healing, death and HUD lifecycle cleanup.

The existing EnemyBehaviorPlaytest scene was loaded without modifying it. Its real movement controller exercised both dodge and sustained assault boost while damage indicators remained camera-relative. Rendered 1280×720 and 960×720 captures were reviewed, with all four red cues visible above dialogue panels and no red pixels in the central half of the screen. The review fixed a nested-Canvas sorting issue discovered at 4:3.

The completed log and reviewed images are retained in `.quest-review/Results/DirectionalDamagePhase1`. There were no C# diagnostics or runtime exceptions in the completed run. This was automated scene/runtime/render verification; no manual real-input playthrough or player build was performed.
