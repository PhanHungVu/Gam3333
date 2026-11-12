# Loose part highlight

Reusable prefab: `Assets/VFX/Pickups/Resources/PickupHighlight.prefab`.

Loose parts automatically show a white four-point star glint and a mesh silhouette. The outline is now **3.5 pixels** (previously 1.5), and the glint's world size is **0.95** (previously 0.55), with a **28-pixel** minimum target size (previously 18) and brightness 2.2. The existing coloured selected-pickup effect and swap UI still work. Equipping hides the marker immediately; dropping reuses it. Enemy-carried parts and renderer-only corpses do not receive this white pickup effect.

## Gold loot carriers

`Assets/VFX/Pickups/Resources/EnemyDropHighlight.prefab` is a gold, sparkle-only **variant** of the same reusable prefab. It inherits the larger glint, follows the living enemy's mesh bounds and adds no outline copies, colliders, lights or sounds. This is the Elden Ring-inspired cue: a gold-marked enemy has an actual part drop planned. It does not grant bonus loot, health, damage or currency.

`EnemyPartDrop` rolls its existing chance and chooses the part **once per spawned life**, then remembers both. `WillDropPart` and `PlannedDrop` expose the result. The death callback spawns that exact planned item without a second roll; repeated death callbacks still produce at most one drop. Existing probabilities and loot lists are preserved. Normal frames, hits, camera visibility and other combat randomness cannot change a carrier's promise.

The marker is automatic on existing enemy prefabs through their `EnemyPartDrop`. The Inspector has **Show Drop Marker** and an optional **Drop Marker Prefab** override. Disable/death hides it immediately, before corpse meshes are copied. Pooled enemies reuse one marker and receive a new plan per life. Editing the chance/list in Play Mode deliberately refreshes the plan so existing development/test controls still work. Empty lists, null selections and zero chance never advertise a drop.

## Reuse and tuning

Drag the prefab under an object with visible meshes and enter Play Mode. Its `PickupHighlight` component defaults to outlining its parent, including that parent's mesh children. Alternatively assign **Target**, or call `PickupHighlight.Attach(item.transform)` / `effect.Bind(item.transform)` from code. A standalone instance without a parent can also serve as a star marker. Enable/disable the prefab object to show/hide it. After replacing a target's mesh hierarchy at runtime, call `RebuildOutline()`.

The prefab Inspector exposes **Draw Outline**, outline colour/opacity and width in pixels; star colour, size, brightness, pulse period and height; and visibility distance. The two star meshes billboard in the shader, preserving their shape on rotating or scaled objects. Their additive halo works without requiring camera bloom. Outline renderers reuse the source meshes, including skinned bones/blend shapes, and a shared unlit material. They add no colliders, shadows or lights and leave source meshes/materials untouched. Both shaders respect scene depth. The enemy variant overrides only the gold tint and Draw Outline, so common white/glint-size tuning remains on the base prefab.

`Part.SetEquippedPhysics` contains only the connection to create/show/hide this presentation. Resources loads the prefab once on demand so existing loose part prefabs, death drops and swapped-out parts share the same effect without per-prefab migrations. Keep the prefab in its Resources folder for automatic part loading. The effect itself works on objects without `Part`.

**Tools > Mech > Presentation > Create Pickup Highlight Prefab** recreates missing assets and preserves existing authored prefab/material tuning. `PickupHighlightChecks.RunBatch` verifies real equip/drop cycles, reuse on a non-Part prop, no added colliders, no extra active weapon meshes after equip, renderer output in URP, distance hiding, and the death/drop/corpse distinction in an isolated graphics project.

## Merge and verification

Current loot-carrier update: **206 pickup/graphics checks, 35 melee/death/drop checks and 1,010 core gameplay checks passed** (1,251 total), all exit 0. Seeded fixtures verify that the configured chance and item selection are retained, reading the marker does not reroll loot, and death honors the promised item even after combat changes RNG. Real Fodder checks cover gold vs ordinary enemies, pooling, no enemy outline copies and no gold marker in corpse snapshots. URP captures were visually reviewed; the thicker-outline comparison detected 1,450 brighter edge pixels. Current logs: `.quest-review/ReadabilityProject/loot-carrier-highlight-checks.log`, `loot-carrier-melee-drop-checks.log` and `loot-carrier-foundation.log`; captures: `PickupHighlightCaptures` in that project.

Verified against merge `1e3e235` on 2026-11-07. The teammate's weapon-owned firing VFX and Specialist cannon particle prefab are preserved. Tests also play that firing VFX while equipped and rebuild the pickup outline after dropping, confirming that particle renderers stay out of the mesh outline. Three older `CombatFeedbackChecks` expectations were updated for the merged removal of automatic procedural muzzle flash/smoke; runtime combat code was preserved.

**90 pickup/graphics checks and 982 gameplay regression checks passed**, both exit 0. URP captures confirm the star and actual silhouette rendering; the outline-only comparison detected 626 brighter edge pixels. Logs and reviewed captures are under `.quest-review/Results/PickupHighlight`.
