# Shoulder weapons 1-9: playable greyboxes

All nine use `PartData`, physical `Part` pickups, the existing shoulder sockets, `MechCombat`, normal Q/E actions, the shared HUD and the existing ammo/reload/cooldown clocks. Cannon and Large Crossbow retain their existing models and prefabs. Weapons 3-9 have separate definitions and primitive pickup prefabs. No new enemy wave entries/drop probabilities, movement systems, authored animations, sounds or polished effects are introduced.

## Authored stats and status

Numbers below are base values; ordinary loot keeps the existing +/-10% stat distribution. Ammo includes loaded rounds. N/A ammo on Special/Melee parts is unlimited/no ammunition use, not an empty ranged weapon.

| ID | Weapon | Type | Direct / splash | Ammo / magazine | Activations/s | Reload | Range (golems / metres) | AoE | Move |
| --- | --- | --- | --- | --- | ---: | --- | --- | --- | --- |
| 1 | Cannon | Splash | 200 / 120 | 30 / 1 | 1 | 5s | 10 / 40m | 4m radius | -4% |
| 2 | Large Crossbow | Projectile | 180 / 0 | 30 / 1 | 1 | 2.5s | 20 / 80m | None; piercing | -5% |
| 3 | Shield | Special | 0 | N/A | 0.5 | N/A | N/A | 0.5s block | -4% |
| 4 | Giant Rock on a Rope | Melee | 210 | N/A | 1/3 | N/A | 7 / 28m | Unfilled 1m band | -6% |
| 5 | Fishing Line | Projectile | 10 | 50 / 10 | 0.7 | 2s | 7 / 28m | None | -2% |
| 6 | Mortar | Splash | 100 / 100 | 25 / 1 | 1 | 5s | 20 / 80m | 5m radius | -3% |
| 7 | Katyusha | Splash, full-magazine salvo | 20 / 20 per rocket | 100 / 10 | 0.2 salvos | 5s | 15 / 60m | 2m radius per rocket | -7% |
| 8 | Tentacles | Melee | 50 | N/A | 1.5 | N/A | 4 / 16m | Directed box | -3% |
| 9 | Tesla Coil | Projectile (instant chain) | 4 per target | 70 / 35 | 5 | 3s | 7 / 28m initial target | 4m jumps, max 5 targets | Neutral |

One golem length remains 4m by default. Cannon retains its previously buffed 200 direct / 120 splash damage, 4m radius and 5s reload. A direct Cannon hit deals **320**, nearby enemies **120**, giving **64 sustained single-target DPS** under the existing reload-overlap timing. The current balance pass does not edit Cannon, Large Crossbow, Shield, Fishing Line, Mortar or Tentacles data/prefabs or their intended attack behavior.

Large Crossbow's definition and piercing implementation are preserved: 180 once per opposing health root, unlimited distinct enemies within normal range by default, scenery/friendly blocking, no splash. Its sustained single-target DPS remains 72.

## Balance changes and sustained DPS

| Weapon | Before | Current | Ideal sustained DPS |
| --- | --- | --- | --- |
| Cannon | Preserved | 200 direct + 120 splash; 5s reload | 64 single target |
| Large Crossbow | Preserved | 180 direct, piercing; 2.5s reload | 72 single target |
| Shield | Preserved | 0.5s block, 2s activation interval | N/A |
| Giant Rock | 70 damage, 0.1/s | 210 damage, 1/3/s | **70 per target occupying the ring** |
| Fishing Line | Preserved | 10 damage, 10-round magazine, 0.7/s, 2s reload | 6.73 single target |
| Mortar | Preserved | 100 direct + 100 splash, 5s reload | 40 single target |
| Katyusha | Ten sequential shots at 3/s | Ten full-damage rockets simultaneously; 0.2 salvos/s, 5s reload | **80 nominal**, if every rocket contributes direct + splash |
| Tentacles | Preserved | 50 damage, 1.5/s | 75 single target |
| Tesla Coil | 0.7/s, six targets, 8m bounce | 5/s, five targets, 4m bounce; damage stays 4 | **14.29 single target / 71.43 across five targets** |

The existing reload begins with the magazine's final attack and overlaps its shot interval. Let `t = 1 / FireRate`, `R = Reload`, `M = magazine rounds`, and `D = damage per shot to the intended target(s)`:

- Ordinary magazine: `DPS = M * D / ((M - 1) * t + max(t, R))`.
- No-ammo melee: `DPS = Damage / t`.
- Full-magazine salvo: `DPS = M * DamagePerRocket / max(t, R)`.

Examples: rock `210 / 3 = 70`; Katyusha `10 * (20 + 20) / 5 = 80`; Tesla single target `35 * 4 / ((34 / 5) + 3) = 14.2857`, or five targets `35 * (4 * 5) / 9.8 = 71.4286`. Fishing Line remains `100 / ((9 / 0.7) + 2) = 6.7308`. These use authored values and ideal valid hits, excluding initial projectile travel/startup, finite-ammo exhaustion and frame quantization. Normal +/-10% loot rolls change the actual physical item's values. Group splash damage is deliberately not capped.

## Default interpretations for new mechanics

### Shield

Activating Q/E begins a **0.5s all-direction damage block**, with a **2s activation interval**. The cube moves to a raised position in front of the player, then returns to its shoulder pose. `Block All Directions` is configurable; with it off, the default frontal half-angle is 90 degrees and unknown-source damage is not blocked. No parry, reflect, energy use, new movement restriction or added invulnerability tail is implemented.

`CombatHealth` has a small optional `ICombatDamageBlocker` admission hook. The Shield registers only for its active window. Blocked attacks return false before HP loss/damage/death events; normal health with no blockers follows its previous path. Dropping, swapping, disabling or ending the mission clears protection immediately. Each Shield's own physical part keeps its existing cooldown across swaps.

### Giant Rock on a Rope

The authored Range is interpreted as **outer damage radius**: 7 golem lengths = 28m. The authored 1m AoE is the **band's full radial thickness**, so the default safe center extends to 27m and the damage band covers **27-28m**. Geometry and sweep behavior are retained. Only damage changes from 70 to **210**, and the interval from 10s to **3s**. `Ring Range Scale`, `Ring Thickness Scale`, `Ring Half Height` and `Ring Sweep Seconds` remain configurable.

A sphere and LineRenderer rotate through one **0.8s clockwise sweep** around the player. Damage follows each swept sector; it is not a filled disk. Actual collider contact with the band is checked, so a large body extending from the center into the band can still be hit. Each health root takes **210 at most once per activation**, including compound colliders/overlapping frames. Ring height defaults to +/-1.5m around the waist-level origin. Orange inner/outer boundary lines show the actual band while active. Shared melee wall/team/push/death/drop rules are reused. The player can move during the sweep; its center follows the player.

### Fishing Line

A normal swept sphere shot with **120m/s speed** and **0.025m collision radius** provides a narrow precision attack. It has no Hook pull or other retrieval/movement mechanic. Head Accuracy, ordinary projectile collision and existing magazines/reload remain in use.

### Mortar

The existing `MechProjectile` gains an opt-in ballistic mode used by Mortar only. Its default horizontal speed is **40m/s** (the PartData Projectile Speed), gravity **25m/s squared**, and minimum flight time **0.55s**. The trajectory solves velocity toward the existing intended aim point. High/long steps are subdivided into swept collision segments; ceilings, walls and actors can intercept the shell.

Range is interpreted as **horizontal target reach**, capped at 80m, rather than the longer curved path length. If the shell reaches its planned area without first striking a collider, it detonates there as a 100-damage area burst. A struck body takes 100 direct +100 splash; other valid bodies within 5m take 100. Normal straight projectiles keep their prior range-expiry behavior. The collision radius is **0.2m**. No homing or new explosion damage system is added.

### Katyusha

**Press Q/E once** to launch the entire loaded magazine in the **same simulation frame**. The authored magazine has ten rockets. Normal straight sphere missiles retain **65m/s speed**, **0.12m radius**, and **20 direct + 20 splash damage per rocket**, with a 2m explosion radius. The fire rate is **0.2 salvo activations/s** and reload remains **5s**. Holding the key past reload does not trigger another salvo; release and press again. No homing is added.

`Salvo Fan Angle` on the Katyusha prefab defaults to **8 degrees total** (+/-4 degrees). Ten evenly spaced directions form a consistent horizontal fan around the existing aimed area. **Salvo Launch Spacing**, default **0.3m**, puts the ten origins into a small five-column/two-row rack, making the individual sphere rockets visible immediately. Every muzzle path uses the normal swept validation, so an offset port cannot spawn through a wall. Head Accuracy supplies the common aim variation; the configurable fan supplies the separate intrinsic spread. Setting both fan angle and launch spacing to zero stacks the shots for diagnostic testing. Every rocket keeps full damage and its own existing swept collision/impact/splash history; a single enemy can legitimately take damage from several different rockets. No per-target cap is added.

`MechCombat` launches the normal `RangedAttack` projectiles synchronously. The first rocket atomically spends the loaded rounds through `Part.Consume(..., rounds)`, beginning one cooldown/reload cycle; subsequent rockets do not spend more ammo. `ShotFired` reports every rocket while `WeaponAttackStarted` reports one activation, and the shared ready-cue/HUD logic sees one reload. Ordinary shots still default to one round, and Arm multi-projectile damage splitting is preserved. Rolled magazine sizes produce correspondingly sized salvos; a partial final magazine fires only its actual rounds, without replenishing ammunition. There is no existing projectile pool to extend, so this uses the current ten ordinary projectile instances/lifecycle rather than adding another pooling framework.

### Tentacles

On activation, prefer the existing selected enemy if valid/in reach; otherwise choose the closest visible opponent within 16m, in any direction. A **1.25m wide, 3m high** box extends from the waist origin toward the chosen body (maximum length = Range), dealing **50 once** to valid bodies it intersects using the existing melee query, wall, team and push rules. Repeated activations can select different enemies, including behind/above the player. A thin primitive extends/retracts procedurally over **0.25s**; no authored animation, automatic attack or extra combo hits are added. Without a target the action is not admitted.

### Tesla Coil

**Hold Q/E** for lightning pulses at **5/s** when ready. Initial acquisition uses the current selected opponent when valid, else the closest visible opponent within 28m. Each subsequent jump selects the nearest unvisited valid opponent within **4m** of the previous impact, with **5 unique total targets per pulse** by default. The four-metre limit measures the actual hitbox-center beam endpoints, so a large collider's nearer edge cannot silently extend a jump. Each target receives the authored **4 damage**; root history prevents duplicate hits from compound colliders, then resets for the next pulse. World geometry blocks target acquisition/jumps and friendly/neutral bodies are not lightning targets. Range applies to the first acquisition; jumps can extend the chain beyond it. A target dying during the chain is handled normally.

Temporary LineRenderers show successful damaging links for **0.15s** and are reused. Unused links are hidden when a later chain is shorter. Fully blocked damage does not draw a damage arc. A longer configured visual lifetime does not throttle the next gameplay pulse. `Chain Distance` and `Maximum Chain Targets` (1-32) remain configurable. Each pulse uses one round, regardless of target count; no valid initial target means no ammo/cooldown use. After 35 pulses the existing 3s reload starts; holding the key resumes firing after reload. There is **no movement penalty** and the base damage is intentionally kept at 4.

## Assets and integration

- Existing Cannon: `PartCatalogue/PartData_Shoulder_Cannon.asset`, current Specialist cannon prefab.
- Existing Large Crossbow: `PartCatalogue/PartData_Shoulder_LargeCrossbow.asset`, existing `shoulder_crossbow` prefab.
- Seven new definitions: `PartCatalogue/GreyboxShoulders/PartData_Shoulder_{Shield,GiantRock,FishingLine,Mortar,Katyusha,Tentacles,TeslaCoil}.asset`.
- Seven separate pickup prefabs: `Prefabs/Mech/GreyboxShoulders/shoulder_{name}.prefab`.
- Nine-entry catalogue: `PartCatalogue/GreyboxShoulders/ShoulderWeapons1-9.asset`.
- Primitive materials and flat placeholder HUD icons: `Combat/GreyboxShoulders`.

`GreyboxShoulderWeapon` contains only the per-weapon special behavior/configuration. `MechCombat` dispatches its special admissions but still owns ordinary projectile firing, damage modifiers and accepted-action events. Shared `CombatBlast` adds ring sectors and non-alloc target selection. Mortar uses the existing projectile lifecycle, collision and splash resolution; no parallel projectile controller is introduced.

`PartSubtype.Special` is appended without changing old serialized enum values. Shoulder melee/specials keep the Shoulder part type. `MechRulesConfig` prevents Arm<->Shoulder crossover even when a mixed-body testing flag is enabled. New Shoulder definitions are strict-type pickups. Ammo, cooldown UI, ready cues, R reload, movement penalties and +/-10% rolls continue to read the same physical `Part`/`PartData` state. New test pickups are exact-stat grants; ordinary prefab drops still roll normally.

This checkout has **nearby 1/2 pickup/swap**, but no remote F-retrieval implementation. No replacement retrieval framework/bonus attacks were invented. These are ordinary side-free Shoulder `Part` prefabs, ready for a retrieval system's normal equipment path, but F-specific behavior has not been tested on a branch containing that feature.

## How to test

Open **EnemyBehaviorPlaytest**. Select the existing **Greybox Arm Weapons** bench and enable **Shoulder Mode**. Its Shoulder Catalogue references all nine weapons.

- `[ / ]`: select weapon.
- `, / .`: equip left/right Shoulder.
- Backslash: spawn a separate exact-stat physical pickup.
- **Q / E**: activate the left/right Shoulder; press once per Katyusha salvo and hold for rapid Tesla pulses.
- Existing F5-F12 controlled enemy encounter keys are retained.
- Disable Shoulder Mode to use the original Arm bench again.

The bench Inspector also provides Left / Right / Drop buttons for each entry. **ZooScene** has seven new labelled physical pickups under `parts / Shoulder Weapons 3-9 - Ground Pickups`, beside the existing weapon display; Cannon/Crossbow retain their existing pickups. Nearby selection/cycling and 1/2 equip still use the normal player equipment system.

In Shoulder Mode the existing bench now provides **1 Tanky Target**, **5 Grouped**, **15 Spread** and **Clear Shoulder Targets** buttons. These optional development-only primitive opponents have 10,000 HP and no AI, waves or loot. Stand in an open section of the test ground and clear active AI using the existing encounter controls first. The five-target layout uses 1.8m spacing; the fifteen-target grid uses 5m spacing. With Giant Rock selected, group layouts instead place all but one target on the 27.5m ring path and one labelled safe-center control at 8m. The controls clear only their own previous targets; other enemies are retained.

Select an opponent in the Inspector to inspect its `CombatHealth` before/after attacks. The tanky/group/spread comparisons are deliberately stationary and repeatable; use the existing F5-F12 live enemy encounters afterward to assess moving enemies, escape routes and combat pressure.

Editor authoring tools are under **Tools > Mech > Greybox Shoulder Weapons**. They update only the seven new definitions/prefabs and their catalogue, connect the existing bench, or idempotently place new Zoo pickups. Source scene changes add the catalogue reference and new pickup objects/parent link; unrelated scene objects are retained. No normal mission spawning or loot probabilities are edited.

## Files edited by this balance pass

- Data: `PartCatalogue/GreyboxShoulders/PartData_Shoulder_GiantRock.asset`, `PartData_Shoulder_Katyusha.asset`, `PartData_Shoulder_TeslaCoil.asset`.
- Prefab configuration: `Prefabs/Mech/GreyboxShoulders/shoulder_Katyusha.prefab`, `shoulder_TeslaCoil.prefab`. Existing models/materials remain in use.
- Runtime: `Scripts/Combat/GreyboxShoulderWeapon.cs`, `Scripts/Combat/MechCombat.cs`, `Scripts/Combat/RangedAttack.cs`, `Scripts/Damage/CombatBlast.cs`, `Scripts/Part System/Part.cs`.
- Existing test bench/authoring: `Scripts/Combat/GreyboxArmWeaponBench.cs`, `Scripts/Combat/Editor/GreyboxArmWeaponBenchEditor.cs`, `Scripts/Combat/Editor/GreyboxShoulderWeaponSetup.cs`.
- Checks: `Tests/Editor/ShoulderWeaponChecks.cs`, `Tests/Editor/ShoulderWeaponArenaChecks.cs`.
- Documentation: this file and `Combat/ControlScheme.md`.

No scenes, Arm weapon definitions/prefabs or normal enemy/wave/drop settings are edited by this balance pass. The previous nine-entry catalogue and all independent Zoo pickups are reused.

## Verification

Verified in a disposable copy of this project using Unity 6000.5.8f1, without saving test overrides into the source scenes:

| Check | Result |
| --- | --- |
| `ShoulderWeaponChecks.RunBatch` | **127 passed**: all nine authored definitions, both sockets, Arm rejection, +/-10% rolls, unchanged Cannon/piercing/Shield/Fishing/Mortar/Tentacles behavior; same-frame full-damage salvos, fan/launch ports, one ammo/cooldown event, partial/rolled magazines; 210-damage ring geometry/history; Tesla cap/strict jump range/teams/walls/death/blocked damage, renderer reuse, long-visual rapid retrigger, damage/reload DPS formulas and real Fodder/Mace contacts. |
| `ShoulderWeaponArenaChecks.RunBatch` | **69 passed**: source Zoo pickups/floors, existing arena/bench/HUD, actual bracket/equip/drop keys, real nearby 1/2 swaps of all seven new parts, Q press-only simultaneous salvos, three target layouts, ring/center controls, Tesla 1/3/5-target pulses and held-E magazine/reload/resume/release. |
| Existing foundation suite | **1,012 passed**. |
| Existing Arm weapon suite | **152 passed**. |
| Prior Level Manager baseline | **27 passed before this pass**; its source is preserved. |

The final D3D11 run rendered and reviewed the separated rocket rack and 1/5/15-target layouts, as well as the existing ring/rope and Tesla presentation. The real held-E test fired 35 pulses over **7.00s from first to last pulse** at 60 simulated frames/s, then enforced its 3s reload before resuming. The ideal formula uses 6.8s for those 34 intervals; ordinary frame quantization accounts for the measured difference. Five beam renderers are reused rather than allocating new lines for each pulse.

Observed **one-salvo** Katyusha damage with the final 8-degree fan / 0.3m launch spacing:

| Stationary scenario | Total damage | Damage / nominal 5s cycle |
| --- | ---: | ---: |
| One 4m-wide tanky box | 400 | 80 |
| Five clustered boxes, 1.8m spacing | 900 | 180 total group DPS |
| Fifteen boxes spread at 5m spacing | 320 | 64 total encounter DPS |

These are repeatable greybox samples, not guarantees against moving enemy models. They demonstrate the intended AoE-overlap sensitivity; no individual enemy exceeded ten legitimate 40-damage rocket contacts. The ring comparison dealt 210 once to each of four band targets and zero to its center control. Tesla dealt 4 each in the 1/3/5-target comparisons and 140 each over its 35-pulse held-input test.

Final logs are under `.quest-review/ReadabilityProject`: `shoulder-balance-mechanics-rack.log`, `shoulder-balance-arena-rack.log`, `shoulder-balance-foundation-final.log` and `shoulder-balance-arms-final.log`. This is basic functional/graphics verification; subjective balance, moving-horde playtests and large-horde CPU/GPU profiling remain for later. No player build was run. No new weapons are added to production waves or enemy loot tables.

The only integration not verifiable here is remote F retrieval, which is absent from this checkout. Ordinary pickup, swapping, sockets, HUD and firing are verified above. Proper models, authored animation/audio and polished VFX remain outside this greybox task.
