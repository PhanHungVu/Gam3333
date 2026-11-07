# Loose part highlight

Reusable prefab: `Assets/VFX/Pickups/Resources/PickupHighlight.prefab`.

Loose parts automatically show a white four-point star glint and a faint mesh silhouette. The existing coloured selected-pickup effect and swap UI still work. Equipping hides the marker immediately; dropping reuses it. Enemy-carried parts and renderer-only corpses do not receive it.

## Reuse and tuning

Drag the prefab under an object with visible meshes and enter Play Mode. Its `PickupHighlight` component defaults to outlining its parent, including that parent's mesh children. Alternatively assign **Target**, or call `PickupHighlight.Attach(item.transform)` / `effect.Bind(item.transform)` from code. A standalone instance without a parent can also serve as a star marker. Enable/disable the prefab object to show/hide it. After replacing a target's mesh hierarchy at runtime, call `RebuildOutline()`.

The prefab Inspector exposes outline colour/opacity and width in pixels; star size, brightness, pulse period and height; and visibility distance. The two star meshes billboard in the shader, preserving their shape on rotating or scaled objects. Their additive halo works without requiring camera bloom. Outline renderers reuse the source meshes, including skinned bones/blend shapes, and a shared unlit material. They add no colliders, shadows or lights and leave source meshes/materials untouched. Both shaders respect scene depth.

`Part.SetEquippedPhysics` contains only the connection to create/show/hide this presentation. Resources loads the prefab once on demand so existing loose part prefabs, death drops and swapped-out parts share the same effect without per-prefab migrations. Keep the prefab in its Resources folder for automatic part loading. The effect itself works on objects without `Part`.

**Tools > Mech > Presentation > Create Pickup Highlight Prefab** recreates missing assets and preserves existing authored prefab/material tuning. `PickupHighlightChecks.RunBatch` verifies real equip/drop cycles, reuse on a non-Part prop, no added colliders, no extra active weapon meshes after equip, renderer output in URP, distance hiding, and the death/drop/corpse distinction in an isolated graphics project.

## Merge and verification

Verified against merge `1e3e235` on 2026-11-07. The teammate's weapon-owned firing VFX and Specialist cannon particle prefab are preserved. Tests also play that firing VFX while equipped and rebuild the pickup outline after dropping, confirming that particle renderers stay out of the mesh outline. Three older `CombatFeedbackChecks` expectations were updated for the merged removal of automatic procedural muzzle flash/smoke; runtime combat code was preserved.

**90 pickup/graphics checks and 982 gameplay regression checks passed**, both exit 0. URP captures confirm the star and actual silhouette rendering; the outline-only comparison detected 626 brighter edge pixels. Logs and reviewed captures are under `.quest-review/Results/PickupHighlight`.
