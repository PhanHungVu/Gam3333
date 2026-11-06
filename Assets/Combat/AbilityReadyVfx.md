# Ability cooldown ready burst prefab

The upward white burst is now `Prefabs/UI/Combat HUD/Ability Ready Burst.prefab`. It is a linked child of `AbilitiesHUDPodPrefab.prefab`, which the shared CombatHUD creates for each ability slot. The old AbilityReadyVfx component on the pod root and both runtime object/component builders have been removed.

Open **Ability Ready Burst** in Prefab Mode to edit the effect. Its root has the **Ability Ready Vfx** controller and tuning; **Column** contains the Canvas-native **Ability Ready Burst Graphic** renderer. The visual remains the same dense rising white column, initial flash and small icon pulse. It uses a UI mesh rather than a ParticleSystem, with no new textures, shaders, Canvas or audio.

## Verify in Play Mode

1. Enter Play Mode with the player and shared HUD.
2. Expand `CombatHUD > AbilitiesHUD` and select an ability pod. Its **Ability Ready Burst** child is the prefab instance.
3. Use the **Abilities Hud Pod > Activate** context menu, or successfully use a connected ability. At cooldown completion, its burst plays once.
4. To tune it, edit the Project prefab rather than a temporary Play Mode instance.

The effect starts hidden. Each pod owns one authored column, reused for every cue. While playing, the column temporarily moves to the existing root Canvas so pod masks cannot clip it. After completion it returns to its owning prefab. Disabling the pod immediately hides it and restores the icon scale; HUD destruction removes it. It follows the icon through screen-space/world-space Canvas scaling and never blocks UI clicks. A pod without a connected burst no longer generates a replacement from code.

## Tuning on the burst prefab

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

Cooldown state and cue ownership still come from `PartAbilityCooldown`. Only an observed cooling-to-ready transition emits the cue: initialization, rebinding and duplicate HUD views do not add flashes. Gameplay durations, ability behavior and input are unchanged. Particles use a private visual RNG and unscaled animation time.

Weapon pods retain their existing grey-out and rising cooldown line. In the current branch the upward burst is ability-only.

**Tools > Mech > UI > Connect Ability Ready Burst Prefab** reapplies the connection if needed. It preserves an existing burst prefab's tuning and does not add duplicate instances. The saved HUD is already connected; this menu is not required to use it.

## Verification — 2026-11-06

**30 checks passed** in isolated Unity 6000.5.8f1 (exit 0), with no C# diagnostics or runtime exceptions. Coverage includes the saved prefab connection and repeated setup, real cooldown expiry, independent icons, no initialization/rebind replay, duplicate views, renderer reuse, missing-prefab behavior, Canvas scaling, click-through, icon restoration and cleanup during HUD destruction. Weapon pods remain burst-free. This was automated headless verification; no manual visual playthrough or player build was performed. Results are saved in `.quest-review/Results/AbilityReadyBurstPrefab`.
