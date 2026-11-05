# Combat presentation slice

The latest stronger damage/hit shake and actor-local impact holds are documented in [Hit feedback](HitFeedback.md), including the editable settings and recovery behavior.

Current weapon VFX, firing/damage camera impulses and health feedback are documented in [Game feel — tasks 1–3](GameFeel.md). HUD integration, Spinner/orbit knockback, mission results and physics deaths are documented in [Milestones 4–7](RemainingMilestones.md), including the exposed gameplay hooks and remaining networking/ability dependencies.

Current player bindings, automatic running, reload and ability-input connections are documented in [Player controls](ControlScheme.md). The HUD is being revamped separately; this control revision does not edit its files.

`Prefab/Mech.prefab` has `CombatFeedback`, using the existing `Prototype Combat.mat` and the mesh-only `VisualMech/Body` for recoil. `Prefab/Enemy.prefab` and the unpacked enemies in `Scenes/A33.unity` have `EnemyCombatFeedback`. No extra scene setup is required.

## Events and gameplay boundary

- Existing `MechCombatController.ShotFired` drives the cannon muzzle flash, visual recoil, and optional firing audio. Other weapons keep their current presentation.
- `MechProjectile.ImpactResolved` reports weapon, position, radius, distinct successful hits, and kills **after** the existing immediate direct/AoE damage pass. The controller forwards it through `ProjectileImpact`. Subscription occurs before launch so an obstruction at the muzzle cannot lose the impact event.
- Damage still uses the same `CombatHealth.ApplyDamage`, range multiplier, overlap, owner exclusion, compound-collider deduplication, and world line-of-sight checks. No feedback component performs damage queries or applies damage.
- Enemy feedback subscribes to `CombatHealth.Damaged` and `Died`. Mesh flashes use cached property blocks, restoring the previous block afterwards. Death effects are transferred to the player's scene-root effect pool before the enemy deactivates. Death timing, collision, and targetability are unchanged.
- Each cannon impact produces at most one reused HUD confirmation. A three-kill blast says `3 DESTROYED`, with a stronger text pulse and bounded camera impulse. Separate blasts refresh the same readout; individual damage events never create HUD notifications.

## Inspector controls

On the mech's **Combat Feedback** component:

- **Presentation Enabled** mutes every new effect. Disabling the component also clears active pooled visuals, resets recoil, hides confirmation, stops audio, and mutes its impulse listener.
- Independent switches: muzzle flash, visual recoil, explosion flash, shockwave ring, secondary embers, enemy hit flash, enemy death effects, kill confirmation, camera impulse, firing audio, impact audio, and confirmation audio.
- Tune muzzle size/duration, recoil distance/angle/duration, explosion color/duration, death color/duration, confirmation duration, and impulse strength/duration/range. These values do not change weapon stats or the blast damage radius.
- **Recoil Visual** must be a mesh-only descendant, not the movement root, a collider hierarchy, or the boost-controlled `VisualMech` root. The existing body is already assigned. Boost lean remains on its separate parent.
- **Effect Material** is already assigned. If missing, procedural effects are skipped safely; gameplay, recoil, hit tinting, and HUD remain functional.
- **Cannon Fire Clip**, **Cannon Impact Clip**, and **Multi Kill Clip** are intentionally empty and silent. Assign your own clips later. Boost audio is untouched. Combat audio has at most four reusable voices.

On each enemy's **Enemy Combat Feedback**, independently disable hit flash/death effect or adjust hit color and duration. The optional receiver defaults to the active local player's `CombatFeedback`; this slice targets the existing single-player scene.

## Camera and performance

The optional impulse uses a dedicated Cinemachine listener on channel mask 64, after the Noise stage. It adds a short translation correction through Cinemachine, never writing camera transforms, FOV, follow/look-at targets, lock selection, or melee framing. Overlapping impulses use the largest signal instead of accumulating. No new noise profile or package is required. With no camera controller, this effect is skipped.

Effects contain a tiny reusable flash mesh, two line renderers, and one small particle system (maximum 20 particles). They have no physics components. The pool prewarms 8 effects, grows to at most 32 by default, and recycles its oldest visual if saturated. Completed effects deactivate and clear particles for reuse. There are no effect primitives or material instances created per hit. Pool/HUD objects are cleaned up with their owner. The existing projectile allocation model is unchanged.

The bright flash is short, the ring expands to the existing blast radius, and sparse embers fade out; no large translucent smoke spheres obscure surviving enemies. Ring orientation is horizontal at impact height, without additional ground physics queries.

## Validation

`Tests/Editor/CombatFeedbackChecks.cs` runs through the existing isolated `MechFoundationChecks.RunBatch` entry point. It checks actual damage/death events, compound colliders, a triple-kill confirmation, inactive enemy roots with surviving presentation, pooled cleanup/cap, visual recoil isolation, optional-asset handling, an immediate muzzle-obstruction impact, and identical gameplay with presentation muted. Existing camera, melee, other weapons, movement, targeting, input lifecycle, and boost regressions run alongside it.

The automated scene/render check uses a temporary copy of `A33`; it does not save enemy test positions back into the working scene. Final comfort tuning of impulse strength, recoil visibility while moving, and audio levels with real clips should be judged during normal interactive play.

See [Parts and Abilities](Parts-and-Abilities.md) for the integrated FullPlayer prefab, swappable weapons, restored Spiral Barrage, and the three optional ability modules.
