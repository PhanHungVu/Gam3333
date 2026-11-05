# Weapon, camera and health feedback — tasks 1–3

The later hit-confirm shake and stronger incoming damage feedback are documented in [Hit feedback](HitFeedback.md). That pass also adds bounded actor-local hitstop; the numbers below describe the original presentation pass.

This document records the original tasks 1–3 pass and the subsequent merge audit. Milestones 4–7 have since been implemented for the current local game; see [Remaining milestones](RemainingMilestones.md) for their behavior, verification and exposed gameplay connections. The deferred statuses below describe the earlier audit.

This pass extends existing presentation. Ammunition, fire rate, reload timing, damage, enemy coordination, player movement and mission loading retain their gameplay rules. Task 4's weapon/ability HUD, Spinner knockback, result screens and ragdolls are deferred. Existing authored animation clips and audio assignments are preserved.

## Weapon presentation

`WeaponVisualSettings` lives on each physical weapon, including the weapons carried inside the player/enemy prefabs. Its configuration travels with swapped and dropped parts. Runtime decisions use those settings and the attack subtype, rather than weapon-name comparisons.

- Crossbow arms, shoulder crossbows and cannons have successful-shot flashes and short smoke puffs at their muzzle markers. Throwing axes omit those firing puffs.
- Bolts, axes and cannon projectiles leave configurable trails. `CombatProjectileTrail` is owned by the feedback pool, independently of the projectile. Both impact and range expiry stop emission and let the existing tail finish; reused trails clear their old vertices. The trail pool is capped, and saturation skips a new trail without truncating another live shot.
- Sword/mace trails follow a separate **VFX Swing Tip** on the actual animated mesh. Setup derives the tip from its geometry, supporting rigid meshes on bones and skinned rigs; existing firing markers are unchanged. The renderer stays at world scale so imported bone scaling cannot collapse its width. Trails emit during **0.12–0.80 of the current Fire pose**, stop during idle/reload, and clear on drop/disable. `MechWeaponAnimator` still owns the original clips and playback speed.
- Projectile dust uses the swept hit's surface position and normal. Overlap-at-launch impacts use a closest-point contact and an estimated normal. The projectile-center position used by splash damage is unchanged.
- Cannon impacts retain their existing flash, expanding ring and embers, with an additional smoke plume. All accepted melee victims emit one impact puff, even when they have several colliders.

`CombatVisualEvents` carries shared accepted-action notifications from `RangedAttack`, `MechProjectile` and melee `CombatBlast.ResolveBox` calls. Player and enemy weapons share the presentation path. Existing `MechCombat` events remain available. Impact deduplication, team filtering, wall checks and damage continue to belong to gameplay code; VFX never apply damage. Source owner is captured in projectile impact data so swapping/dropping the weapon during flight does not transfer its camera/kill feedback to another actor.

`CombatFeedback` reuses its existing bounded effect pool. The new soft-puff shader/material render generic smoke and dust without imported artwork. Smoke, dust and trails have no interaction colliders or physics bodies. Optional audio fields remain unchanged; this pass does not synthesize or assign sounds.

### Tuning

On a weapon's **Weapon Visual Settings**, adjust flash/smoke toggles, muzzle size/time, trail width/lifetime/color, impact size and firing-shake strength/duration. For melee, the trail anchor and normalized swing window are exposed. The supplied defaults distinguish light bolts, axes, large crossbows and cannons without changing their weapon stats.

On the player's **Combat Feedback**, presentation, projectile trails, impacts, firing impulses and damage impulses have separate controls. Existing explosion/death/kill-confirmation controls remain available. Maximum effects defaults to **32**, maximum projectile trails to **64**. Missing optional visual materials skip their effects safely.

The existing local-player `CombatFeedback.Current` service remains the presentation owner. This pass does not introduce multiplayer player/camera routing; the later multiplayer integration must replace the existing singleton/local-player assumptions.

## Camera feedback

Firing impulses occur only after successful shots from this feedback owner's weapons. Light weapons use short impulses; the supplied cannon uses **0.075 strength for 0.26 seconds**. Enemy firing plays world VFX without applying local firing shake.

Accepted incoming player damage generates a **0.08-strength, 0.20-second** impulse by default. All camera impulses reuse `CameraControlThirdPerson.GetCombatImpulseListener`, its existing channel **64**, additive noise stage and largest-signal listener mode. They do not edit the camera's base transform, follow/LookAt targets, lens, lock mode or locomotion. Existing player cannon-impact shake remains available. Strength and duration are configurable, and the feedback master toggle clears its visuals and mutes the listener.

## Health feedback

`CombatHealth.Changed` publishes final HP/capacity values after accepted damage and `SetMaximum`/refill. Existing `Damaged`, `Died` and availability events remain intact. No healing ability or new health mechanic is added.

`MechHealthHud` uses the change notification instead of depending on equipment-event ordering. The existing white damage segment holds until **one second without further damage**, then drains to current HP over **0.65 seconds**. Both times are configurable. Another hit preserves the still-visible lost segment and restarts the delay. Refill, capacity changes and initial binding synchronize both bars without manufacturing a damage chunk. Death hides the health display through its existing lifecycle. The existing sprites and layout are retained, including their green remaining-HP artwork.

`PlayerDamageVignette` on the player owns a separate, non-interactive edge overlay beneath the combat HUD. It does not rebuild or rearrange that HUD. The center remains transparent. Its alpha is linear in health percentage:

| Health | Edge alpha |
| --- | --- |
| 50% or above | 0% |
| 25% | About 40.8% |
| 1% or below | 80% |

The onset, full-opacity health level, maximum alpha, tint and optional replacement edge sprite are configurable. Set maximum alpha to zero or disable the component to omit the vignette. Health events update it immediately; refill/healing clears it as health recovers. Death/disable removes the overlay. An owned procedural mask is created only when needed and destroyed with the player.

Only the health HUD script/prefab is changed in task 3. `CombatHUD`, weapon/ability HUD scripts and pods, the energy HUD and the shared UI action map are untouched. The health meter continues to appear only in scenes that already use its HUD prefab; the player vignette works independently in other combat scenes.

## Asset setup and review

**Tools > Mech > Combat > Apply Weapon Camera and Health Polish** updates only weapon presentation components in the owned part/player/enemy prefabs, the player's smoke material/vignette connection and the existing health prefab's damage-bar timing. It does not rebuild the scene, edit PartData balance, replace models, change clips/audio, or modify weapon/ability HUD layouts.

The focused disposable-project entry point is `CombatPolishChecks.RunBatch`. It checks accepted/rejected shots, real projectile travel and surface contacts, independent fading tails, shared enemy VFX, compound-collider melee deduplication, additive camera impulses, health timing/capacity/refill/death, exact vignette alpha and real sword/mace animation/trail motion. It also runs the existing projectile-model, mission-rules, combat-feedback and enemy-combat regressions.

Verified on **2026-11-04** in isolated Unity **6000.5.8f1 / Direct3D 11**: **149 checks passed**, with no C# compilation warnings/errors or unexpected runtime errors. Rendered smoke/impact, low-health, sword-trail and mace-trail captures were reviewed. The final trail checks explicitly verify world-scale width and an anchor within the posed weapon geometry. Results are retained in `.quest-review/Results/CombatPolishTasks1to3`. No full player build or manual working-editor playthrough was run; real-input intensity/timing review remains.

Recommended manual review: `EnemyBehaviorPlaytest` for firing, impacts, camera lock/free-camera and melee timing; `JhCombatUIRevampScene` for the health artwork, white damage segment and vignette with the teammate's HUD. Real-input shake strength and trail-window polish remain play-feel tuning rather than a new gameplay rule.

## HUD merge review — 2026-11-05

Reviewed `55489fd..c45af0c`: part-icon artwork, weapon HUD icon/background changes and the controls dialogue. The tasks 1–3 working changes remain present. This review changes no gameplay, HUD layout, prefab or scene assets.

The existing HUD is suitable for the next integration milestone. Reuse `CombatHUD`, `WeaponsHUDPodPrefab`, `AbilitiesHUDPodPrefab` and the shared health meter rather than replacing the teammate's layout. `JhCombatUIRevampScene` currently contains the new combat HUD and controls dialogue; the `CombatHUD` prefab is not yet placed in other scenes.

### What already works

- All six weapon prefabs resolve their imported icons through `WeaponsHudPod.IconFor`; an explicit `PartData.icon` takes priority over the name lookup.
- Weapon rows bind to the actual local player's equipment, rebuild on swaps, read live ammunition and dim during reload. Multi-round magazines work even though the shipped ranged parts currently use one-round magazines.
- The merged scene retains one weapons HUD, one ability HUD and one health HUD, with no missing MonoBehaviour scripts. Its dialogue override uses `Controls_dialogue_1.json`. The dialogue loads its five lines, reveals text and hides independently without consuming gameplay input.
- Tasks 1–3's weapon effects, camera feedback, delayed white HP segment and vignette pass their regressions alongside the merged HUD.

### Remaining work and dependencies

| Milestone | Readiness | Revised scope |
| --- | --- | --- |
| 4A — Ammunition HUD | Ready for focused integration | Preserve the authored rows and icons. Confirm the intended denominator and reload-count timing before changing their meaning. Verify scene coverage and swaps; no HUD redesign is needed. |
| 4B — Ability HUD | Presentation exists; gameplay connection pending | Connect the available head/torso/legs artwork and expose authoritative ability-state updates. Keep head/torso request functions available for gameplay to implement. Connect countdowns only to accepted activations and real remaining cooldowns. |
| 5 — Spinner knockback | Ready to implement independently | Add a configurable external-motion path to the existing CharacterController movement; trigger it on an accepted Spinner hit and prevent repeated application from one attack. Do not replace movement with Rigidbody control. |
| 6 — Mission results | Existing completion event can be reused; failure/team tracking incomplete | Add a shared terminal result state and input handling, then success/failure presentation. Preserve KOTH retry behavior. All-player elimination and all-player model presentation require a participant roster/network integration that is still absent. |
| 7 — Death ragdolls | Rig and performance setup still required | Inspect the existing rigs, add appropriate physics bodies/joints where supported, preserve death/drop events and cap concurrent ragdolls. Add resting-time despawn plus a timeout fallback. |

`WeaponsHudPod.AmmoText` currently means **loaded magazine / total shots remaining**, where the total includes the loaded rounds. `Part.Consume` and `Part.TryReload` replenish the loaded count at **reload start**, while firing remains blocked until the timer ends. The merge did not change either rule. A capacity denominator or refill-on-completion would be a separate, explicit adjustment.

`AbilitiesHudPod` reads only `PartData.icon`; it does not use the weapon pod's new icon-list lookup. All three equipped ability icons are therefore currently unassigned, despite matching imported art being available. Their authored cooldown stats are present (head 10s, torso 30s, legs 6s), but the pod owns an independent presentation countdown. `MechAbilitiesHud.Activate` has no gameplay caller, and Tab/Ctrl still raise the intentionally unimplemented ability requests. Space retains built-in leg movement. A request must not automatically be treated as a successful cooldown-bearing activation. A swap also rebuilds the pods, so authoritative remaining cooldown must live outside the HUD if it is meant to survive a swap/rebind.

The mission-launch hook remains intentionally empty; no `JhMapScene` connection is part of these milestones.

### Verification

Isolated Unity **6000.5.8f1 / Direct3D 11** completed **178 checks**: all 149 original combat-polish checks plus 29 merged scene/HUD checks. Compilation produced no C# warnings/errors, and the run had no unexpected runtime errors. The merged HUD capture was visually reviewed. New checks cover all weapon icons, actual player binding, accepted firing, reload dimming/recovery, a four-round test magazine, a part swap, health feedback and dialogue. Scene structure was inspected in Edit Mode; the complete gameplay scene was not manually played through. Test scaffolding stayed in the ignored disposable project. Artifacts are in `.quest-review/Results/HudMergeReadiness`.
