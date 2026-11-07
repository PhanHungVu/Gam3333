# Ability and weapon cooldown ready burst prefab

The upward white burst is `Prefabs/UI/Combat HUD/Ability Ready Burst.prefab`. It is a linked child of both `AbilitiesHUDPodPrefab.prefab` and `WeaponsHUDPodPrefab.prefab`, which the shared CombatHUD creates for the equipped slots. The old AbilityReadyVfx component on the pod root and both runtime object/component builders have been removed.

Open **Ability Ready Burst** in Prefab Mode to edit the effect. Its root has the **Ability Ready Vfx** controller and tuning; **Column** contains the Canvas-native **Ability Ready Burst Graphic** renderer. The visual remains the same dense rising white column, initial flash and small icon pulse. It uses a UI mesh rather than a ParticleSystem, with no new textures, shaders, Canvas or audio.

## Verify in Play Mode

1. Enter Play Mode with the player and shared HUD.
2. Expand `CombatHUD > AbilitiesHUD` and select an ability pod. Its **Ability Ready Burst** child is the prefab instance.
3. Use the **Abilities Hud Pod > Activate** context menu, or successfully use a connected ability. At cooldown completion, its burst plays once.
4. For weapons, expand `CombatHUD > WeaponsHUD` and inspect an equipped weapon pod's **Ability Ready Burst** child. Fire the weapon and wait for its actual cooldown/reload. The column and icon pulse play once at completion.
5. To tune it, edit the Project prefab rather than a temporary Play Mode instance.

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

Weapon pods retain their existing grey-out and rising cooldown line, and now play the same linked burst on completion. `Part.WeaponCooldownCycle` / `TryConsumeWeaponReadyCue` supply item-owned completion tokens, including the case where held fire starts another shot before the HUD updates. A newly bound already-ready item starts observing its next cycle, suppressing startup/rebind flashes. A view bound during a real cooldown can show that completion. Depleted ammunition does not produce a false ready cue. No normal weapon timers or gameplay code are changed.

**Tools > Mech > UI > Connect Ability Ready Burst Prefab** reapplies the connection if needed. It preserves an existing burst prefab's tuning and does not add duplicate instances. The saved HUD is already connected; this menu is not required to use it.

## Verification — 2026-11-06

**30 checks passed** for the earlier ability-only integration in isolated Unity 6000.5.8f1 (exit 0), with no C# diagnostics or runtime exceptions. Coverage included the saved prefab connection and repeated setup, real cooldown expiry, independent icons, no initialization/rebind replay, duplicate views, renderer reuse, missing-prefab behavior, Canvas scaling, click-through, icon restoration and cleanup during HUD destruction. This was automated headless verification. Historical results are saved in `.quest-review/Results/AbilityReadyBurstPrefab`.

## Experimental retrieval branch: weapon restoration

Stage 2 passed **72 combined ability/weapon prefab and readiness checks**, exit 0, using D3D11. Graphics captures at 1920x1080 and 1280x720 were reviewed. Tests use normal combat and the real weapon HUD, covering all four slots, melee/ranged cooldowns, automatic/manual reloads, held-fire completion latching, duplicate views, refreshes, swaps, rebuilding during cooldown/active bursts, empty ammunition, hidden HUD reopening, scale restoration and column cleanup. **982 existing gameplay regression checks also passed**, exit 0. Logs and captures are in `.quest-review/Results/RemoteRetrievalStage2`.

The standalone burst prefab, ability pod, root CombatHUD, inputs, part stats and normal attack/reload timing were preserved. The only weapon prefab change is one linked burst instance and its icon/controller reference. See [RemoteWeaponRetrieval.md](RemoteWeaponRetrieval.md) for the next stage and complete file list.
