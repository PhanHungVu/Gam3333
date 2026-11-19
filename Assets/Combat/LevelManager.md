# Level manager: observation and future progression controls

`LevelManager` is a scene-local source of level state. This first pass gathers information and stores neutral tuning controls. **It applies no new drop, spawning, enemy-damage or enemy-health scaling.** No progression formula, difficulty tiers or thresholds have been chosen yet.

## Where it runs

The existing `MissionOutcomeController` creates/ensures one **Level Manager** object in its scene during startup. The existing player `MissionParticipant` already creates that mission controller in gameplay/test scenes, so KOTH, Escort, Elimination and player-driven test maps receive the manager without scene/prefab edits. The quest board does not get a manager unless it deliberately creates a gameplay mission controller.

An authored `LevelManager` component is also supported: the startup path reuses it. `LevelManager.Find(scene)` retrieves the active manager; `LevelManager.Ensure(scene)` creates one if needed for a standalone custom test. Managers do not persist between scene loads. Additive scenes have separate managers.

During Play Mode, select **Level Manager** in the Hierarchy. The Inspector shows editable source overrides and clearly labelled future controls, followed by read-only **Observed** and **Enemies** sections. Sources are discovered in the same scene when left empty. Missing sources are retried twice a second, allowing generated levels and late player spawns to initialize normally.

## Available information

| Area | Exposed state |
| --- | --- |
| Clock | `elapsedGameSeconds`, paused flag, mission phase and terminal outcome |
| Local player | Missing/alive/dead, position, current/max HP, HP fraction, velocity, movement state, grounded, dash and automatic-running flags |
| Player systems | `Player`, `PlayerStats`, `PlayerEquipment`, `PlayerCombat`, `PlayerMovement` references for existing equipment/stat APIs |
| Objectives | Completed/total objective objects; completed/total steps; normalized step progress. Escort checkpoints count as steps, while the payload remains one objective |
| Horde | Active living enemy agents in this scene, total spawned/leashed counts, whether the director is enabled |
| Current wave | Wave asset, current remaining resource, runtime resource budget, spawn interval, concurrent-enemy limit, objective spawn-cost limit, refill state/timer and roaming-pause flag |
| Each enemy | Agent, health, normal/special attack and drop components; current/max HP; configured drop chance; special attack's existing damage value |
| Normal enemy weapons | Existing `enemy.attack.Weapons` plus `DamageOf(weapon)` / `CooldownOf(weapon)`, retaining each weapon's own tuning |

The player is resolved from the local mission participant, falling back to a gameplay player input component. A player-team escort payload is not a player candidate. Explicitly assign `Player` in the Inspector or call `BindPlayer(health)` for a custom player without those components. Cross-scene bindings are rejected. A dead player's cached health remains readable after its GameObject is deactivated.

The clock accumulates scaled gameplay delta while this manager is enabled. It pauses with gameplay pause/zero time scale and stops on the existing mission's success, failure or completion. Merely calling `Refresh()` does not advance time. There is no wall-clock/offline progression or timer-driven difficulty behavior.

Objective observation uses registered `IMissionObjective` components belonging to this scene; extraction is reported through mission phase rather than counted as another objective. Existing objective/mission logic still owns completion, extraction and results. Unscoped plain C# objectives would need an explicit scene association before they can be included in this per-scene observation.

Enemy observation follows `EnemyAgent.All`: disabled/dead agents and corpses are excluded. This is an active-horde count, not a historical kill counter. A normal enemy may have several weapons, so there is intentionally no invented shared damage number. Special attacks expose their existing component and damage property separately.

## Future tuning controls - not applied

All four `FutureTuning` values start at **1**:

- `dropRateMultiplier`
- `hordeSizeMultiplier`
- `enemyDamageMultiplier`
- `enemyHealthMultiplier`

These are stored values only. Changing them in the Inspector or code has **no gameplay effect in this pass**. `AppliesTuning` always returns `false`. Future work can decide how game time, player state and objective progress should produce these values, then define when/how consumers apply them. No current code consumes them to change enemies or loot.

In particular, this manager does not alter the existing 5% enemy drop configuration, consume spawn resource, start/stop waves, reroll promised drops, refill/heal enemies, multiply weapon stats or mutate shared `WaveData`/`PartData` assets. Existing gameplay behavior, including Escort's already-implemented checkpoint wave growth, remains owned by its existing systems.

## Code access

```csharp
var level = LevelManager.Find(gameObject.scene);
if (level == null) return;

var state = level.State;  // Value snapshot; reading it does not tick gameplay.
float seconds = state.elapsedGameSeconds;
float playerHp = state.playerHealthFraction;
int completed = state.stepsCompleted;
int activeEnemies = state.activeEnemies;

foreach (var enemy in level.Enemies)
{
    float dropChance = enemy.dropChancePercent;
    float maxHp = enemy.maximumHealth;
    if (enemy.attack == null) continue;
    foreach (var weapon in enemy.attack.Weapons)
        if (weapon != null)
        {
            float directDamage = enemy.attack.DamageOf(weapon);
            // Read only; the progression policy will be implemented later.
        }
}
```

`StateUpdated` publishes the value snapshot after normal `LateUpdate` observation or an explicit `Refresh()`. Subscribe/unsubscribe with the consumer's usual enable/disable lifecycle. `Enemies` is a reused collection; copy it if a consumer needs to retain historical snapshots across updates.

## Implementation and checks

- New `Scripts/Level/Mission/LevelManager.cs` and `Scripts/Editor/LevelManagerEditor.cs`.
- One startup call in `MissionOutcomeController.Awake`.
- Read-only `EnemyPartDrop.DropChancePercent` accessor; existing `EnemyAttack.Weapons`, `DamageOf` and `CooldownOf` APIs are reused.
- `SpawnDirector.InspectCurrentWave()` provides side-effect-free access to the existing wave/cycle values. The older `CurrentWave`/`ResourceLeft` getters can initialize roaming while resolving their state; the new observer deliberately avoids those getters.
- `Tests/Editor/LevelManagerChecks.cs` verifies startup, observations, clock/pause/end behavior, objective steps, per-scene isolation, normal enemy damage access and that future controls/reads do not change gameplay or RNG.

Verified in isolated Unity **6000.5.8f1**:

- `LevelManagerChecks.RunBatch`: **27 checks** covering automatic creation, neutral/unapplied controls, missing and late sources, actual player health/death, scaled clock/pause/terminal behavior, objective steps, enemy stats, runtime wave values, RNG preservation, dormant-roaming non-interference, additive-scene isolation and registration cleanup.
- `MechFoundationChecks.RunBatch`: **1,012 existing checks**, including combat, equipment, missions, objectives, escort, wave spawning, movement and HUD behavior.
- `MissionPauseMenuChecks.RunBatch`: **59 existing checks** with Direct3D 11, including pause/resume, frozen gameplay, real restart/retry/exit transitions and existing mission/spawning checks. Its screenshot step needs graphics enabled; an initial headless attempt crashed in Unity's capture path, and the graphics-enabled run passed.

All three final runs exited 0: **1,098 checks**. The six relevant implementation/test files matched the passing review fixture by hash. No weapon/level data assets, player movement/combat scripts, scenes or prefabs were edited. No standalone player build or manual gameplay balance evaluation was performed.

Logs: `.quest-review/ReadabilityProject/level-manager-verified.log`, `level-manager-foundation.log`, and `level-manager-pause-graphics.log`.
