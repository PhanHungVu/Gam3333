# Per-item loot stats

New loot receives an independent **-10% to +10% roll for every numeric entry in its PartData stats list**. For example, a weapon with base Damage 100 rolls between 90 and 110; its fire rate, range, reload time and other stats each use a separate roll. Head, torso and leg loot use the same path for health, movement and ability stats.

`Part.data` remains the shared item definition and identity. `Part.Stats` returns a private runtime PartData copy for rolled loot. Item IDs, names, types, icons, ability kinds, projectile/VFX references and model configuration stay authored. The source ScriptableObject asset and other copies of the item are never modified.

## Lifetime and integration

- `EnemyPartDrop.ResolveDeathDrop` rolls the new physical item immediately after spawning it, before a same-frame pickup can equip it. The gold carrier's promised item identity, configured drop chance and drop list are preserved.
- A fresh scene/world pickup rolls in `Part.Start`, before normal player interaction.
- Equipping, unequipping, disabling/re-enabling and dropping the same item again retain its exact rolled values and seed. These actions never replenish its ammo, clear cooldowns or consume new stat-roll randomness.
- Starting equipment, immediate scripted equipment grants and enemy-carried weapons keep their authored stats. A discarded starting item retains those original values. Enemy attacks therefore do not receive unrequested balance variation, and repeatedly discarding starting gear cannot reroll it.
- Destroying the physical item releases its private runtime stat object. There is no new inventory/save system in this change.

Gameplay and UI already read `Part.Stats`: weapon damage/fire rate/reload/projectiles, melee swing timing, aggregate health/movement, ability cooldown/duration/power, ammo HUD and the existing numeric swap comparisons therefore use the same rolled values. The animation-only weapon fallback was also updated to read `Stats`. No HUD layout or controls changed.

## Numeric rules

All finite nonzero stat values roll independently, including signed movement modifiers. A -20 movement penalty varies between -22 and -18; it never becomes a bonus. Zero/off values remain zero, so disabled mechanics are not enabled by a roll.

Ammo and magazine capacities are rounded to whole numbers **inside** the allowed interval. Base Ammo 30 can produce 27-33. A one-round magazine stays at one because neither zero nor two is within ten percent. If an authored count has no integer inside its interval, its original value is preserved. Non-count stats retain floating-point precision; the existing UI formats displayed numbers as before.

## API and future spawners

`Part.RollLootStats()` rolls an uninitialized world item once, using a newly generated seed. `Part.RollLootStats(int seed)` does the same with a deterministic seed. They return false for an already prepared/equipped item and cannot be used as a reroll or ammo-reset API. Custom loot spawners that equip immediately should call this before attachment. Ordinary scene pickups and the current enemy dropper are already connected.

`HasRolledStats` and `StatRollSeed` expose the item's roll state. The seed uses a local System.Random stream, so generating each individual stat does not advance global combat RNG. A future save implementation can retain the base definition identity and seed, then reconstruct a new instance with the seeded method before equipping it. `PartStatRoll.Create` is the shared stat-generation helper; callers outside Part must destroy the returned runtime copy when finished.

If gameplay explicitly replaces `Part.data` with a different definition, `Stats` returns that new definition instead of exposing a stale roll from the old item. This is definition replacement, not a way to reroll an unchanged item.

## Verification

Isolated Unity 6000.5.8f1 validation: **1,318 checks passed**, all exit 0: 67 focused stat-roll checks, 1,010 existing foundation checks, 206 pickup/gold-loot graphics checks and 35 melee/death/drop checks. No C# compiler warnings/errors or unexpected runtime errors were reported.

`PartStatRollChecks.RunBatch` samples 1,000 seeded rolls, checks all catalogue definitions and tests actual world pickups, enemy deaths, item-instance isolation/cleanup, repeated swaps, ammo/cooldown preservation, real ranged/melee attacks, melee animation timing, numeric comparisons, health, movement and abilities. The gold marker still promises the correct weapon identity and corpses never inherit its live marker.

Logs under `.quest-review/ReadabilityProject`: `part-stat-roll-checks.log`, `part-stat-roll-foundation.log`, `part-stat-roll-loot-glow.log` and `part-stat-roll-melee-drops.log`.

Manual check: restart Play Mode, defeat several loot-carrying enemies and compare two copies of the same weapon in the existing part-swap UI. Their numeric stats should differ within ten percent of the base definition. Equip and drop one of them repeatedly; its values and remaining ammunition should stay the same. Inspecting the shared PartData asset still shows the authored base values.
