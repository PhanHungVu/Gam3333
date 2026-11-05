# Combat milestones 4–7

This implements the remaining work supported by the current local game. The existing combat HUD layout, weapon art, authored animation clips, audio assignments and combat balance are retained. The pre-mission `MissionStartHook.StartMission(QuestDefinition, PartData)` is still intentionally empty.

## 4 — HUD integration

The player rig supplies the existing `CombatHUD` under its shared cursor Canvas only when the scene does not already contain a weapons HUD. The teammate's authored HUD remains in `JhCombatUIRevampScene`; no second copy is created there. The ability pod resolves explicit `PartData.icon`, then the configured slot ability artwork, with matching part artwork as a fallback. The confirmed artwork mapping is **Heal → head, Damage Amp → torso, Jump → legs**, including Fast/Heavy Legs variants. This changes presentation only; it does not implement those ability effects.

`WeaponsHudPod` keeps **loaded rounds / total rounds remaining** by default. Its **Ammo Denominator** Inspector field also supports magazine capacity or total ammo capacity. This pass deliberately preserves the existing reload rule: counts refill at reload start while firing is locked until reload ends. No magazine sizes or ammo counts are changed.

Weapons now share the ability icon's **bottom-to-top cooldown sweep**. Fire a weapon (hold LMB/RMB for arms, Q/E for shoulders) or reload a partially empty magazine with R: the icon/ammo dim, a warm horizontal line rises, and normal brightness returns when the weapon can fire again. This includes melee attack cooldowns, ordinary shot intervals, automatic reloads and manual reloads. `Part.CooldownRemaining`, `CooldownDuration` and `CooldownProgress` read the actual `ReadyAt` deadline. Rejected fire/reload requests do not restart the timer; part swaps and HUD rebuilds retain progress. An ammunition-exhausted weapon stays dim after its final cooldown without a moving line. Existing magazine timing, capacities, fire rates and HUD layout are unchanged.

Weapons also use the white upward **ready burst** when an actual attack/reload cooldown completes, including under held fire. Their final empty-ammunition cooldown does not falsely signal readiness. Tuning and the completed 129-check integration verification are in [Ready VFX](AbilityReadyVfx.md#weapon-ready-bursts).

Ability cooldown state lives on the physical part in `PartAbilityCooldown`, rather than on a HUD pod. It survives removal, re-equipping and HUD rebuilds. It uses the gameplay clock, dims the icon and shows the remaining seconds, then restores the ready state. There is no invented fallback cooldown.

A thin horizontal line travels from the bottom of the icon to the top during cooldown. It uses `1 - Remaining / Duration` from the accepted gameplay cooldown, so authoritative updates and re-equipping resume at the correct height. The whole icon stays grey while the line moves. At zero remaining time the line/countdown disappear and the icon returns to its normal ready brightness. The line is non-interactive and stays inside the existing icon; it adds no Canvas or HUD layout changes. Its color and thickness are editable on `AbilitiesHudPod`.

Ability cooldown completion also emits a short white upward particle column, flash and subtle icon pulse. It triggers once per actual cooling-to-ready transition. See [Ability ready VFX](AbilityReadyVfx.md) for preview steps, Inspector tuning and integration details.

The ready effect passed **21 new checks** within a completed **108-check isolated integration run**, including compatibility with older HUD pods and no replay on ready initialization, rebinds or tiny authoritative corrections. Reviewed 1920×1080 and 1280×720 captures and validation details are linked above.

**Gameplay connection:** after an ability actually succeeds, call:

```csharp
PartAbilityCooldown.For(acceptedPart).ConfirmActivation(actualCooldownSeconds);
```

For an equipped slot using its authored stat, `MechAbilitiesHud.ReportAcceptedActivation(slot)` is also available (`Activate(slot)` remains a compatibility alias). `PartAbilityCooldown.Synchronize(remainingSeconds, durationSeconds)` accepts authoritative updates for a future ability/network owner. Call this only after acceptance; Tab/Ctrl/Space requests alone do not start a countdown. Head/torso effects remain unimplemented, as previously requested. Space retains the existing leg movement.

**Preview the ability indicator before gameplay connects those hooks:** enter unpaused Play Mode, expand the live **CombatHUD → AbilitiesHUD** in the Hierarchy, select a spawned ability pod, then open the **Abilities Hud Pod** component's context menu and choose **Activate**. The icon dims, its countdown appears, and the line rises for that part's authored cooldown (head 10s, torso 30s, legs 6s). Select the live scene instance, not the Project prefab. This previews accepted cooldown state only; it does not perform Heal/Damage Amp/Jump gameplay. Ready abilities intentionally have no line.

## 5 — Spinner knockback

`MechMovementController.ApplyKnockback(direction, speed, duration)` adds a bounded, decaying horizontal velocity through the same `CharacterController.Move` call as ordinary movement. It respects walls, preserves gravity and player steering, ends automatic running and clears on disable/death. It does not add a Rigidbody or replace dash/leg locomotion.

The small `EnemySpinnerAttack` and charging rope-orbit variant now both apply **32 m/s decaying over 0.65s** on accepted hits. On level, unobstructed ground with no player input, this moves the player approximately **10.4 metres** (speed × duration ÷ 2), beyond the orbit circle's 6m radius and the small Spinner's 9m charge reach. The earlier defaults displaced only 2.25m and 1.25m respectively. Walls and active player steering affect the final distance. Both settings are configurable on their attack component; the controller's existing 40m/s and 1s bounds remain. The existing once-per-charge/orbit victim history, team rules and wall checks gate damage and knockback together. The throw-and-pull variant's behavior is retained.

## 6 — Mission results

`MissionParticipant` on the player creates/registers with a scene-local `MissionOutcomeController`. The existing extraction `MissionTracker` and prototype collection objective report success to it. The old freeze behavior is retained only as a fallback when no results controller exists. No global time pause is used by the new result flow.

- Success shows **MISSION COMPLETE**, renderer-only copies of the actual registered participants' equipped models, Retry and Main Menu.
- Team elimination shows **MISSION FAILED** only after every registered participant is dead. Disabling an actor does not silently remove it from the roster.
- Results are terminal and idempotent. They release the cursor, stop normal combat input/AI/spawning in that scene and remove active projectiles.
- Retry reloads the current scene. Main Menu defaults to `Assets/Scenes/MainMenuScene.unity`. Editor play tests can use scene paths; player builds need those scenes in the build profile.

The new editable presentation is **Prefabs/UI/Mission Results.prefab**, separate from the teammate's combat HUD. `MissionResultsView` owns its own Canvas and supports the new Input System. Success copies only transforms and mesh renderers; it never instantiates additional players, gameplay scripts, audio or colliders.

**Objective connection:** a genuinely terminal objective failure calls:

```csharp
MissionOutcomeController.Find(gameObject.scene)?.ReportFailure("Objective destroyed.");
```

KOTH's existing failed hold still resets and can be retried; it is not a terminal failure. Gameplay can explicitly call `ReportSuccess()` for other completed mission types. `Finished`, `succeeded` and `failed` events provide integration points. These do not launch a mission or connect the pre-mission hook.

**Future team/session integration:** `BeginParticipantRegistration()`, `RegisterParticipant(id, health, locallyControlled)` and `CompleteParticipantRegistration()` allow an actual session owner to supply the complete roster before elimination is evaluated. The current project still has no network synchronization or remote-player ownership implementation. The default is the existing one-local-player setup. Registered actor models must remain available for result presentation; the current health lifecycle already retains/deactivates them.

## 7 — Physics deaths

`CombatDeathRagdoll` listens to the unchanged health death event on the player and current enemy prefabs. Death/drop/kill logic and immediate actor deactivation still occur normally. A separate renderer-only copy uses connected Rigidbody proxies and limited joints to drive the existing generic leg bones and modular body pieces. Uniform world-sized physics proxies avoid the tiny imported animation-bone scales. No authored clips or models are replaced.

Defaults per death component:

| Setting | Default |
| --- | --- |
| Concurrent corpses | 12 globally |
| Physics bodies per corpse | At most 14 |
| Continuous rest before removal | 5 seconds |
| Linear/angular rest thresholds | 0.15 m/s / 0.30 rad/s |
| Maximum lifetime | 30 unscaled seconds |

Existing movement momentum plus a small backward death impulse starts the simulation. Movement resets the continuous-rest timer. The maximum lifetime removes bodies that never settle, including while scaled time is paused. If the cap is full or no supported mesh exists, ordinary death still completes without allocating another physics corpse.

Corpse proxies use built-in **Ignore Raycast** layer 2. Combat, AI ground/clearance probes and follow-camera avoidance exclude them. Corpse colliders ignore character contacts (including later spawns), their own connected pieces and other corpses; they still collide with the environment. This does not change the project's collision matrix or layers. Keep solid combat scenery off Ignore Raycast, in line with that layer's purpose.

The automatic rig is a functional generic/modular golem ragdoll. Future artwork can replace its collider/joint configuration; individual joint limits and high-count performance still benefit from playtesting on target hardware. No articulated human rig or networking support is assumed.

## Setup and validation

**Tools > Mech > Combat > Apply HUD Results and Ragdoll Integration** installs the part icons, player result/HUD connections and death components into the owned prefabs. It creates the result prefab only when absent. It does not rewrite scenes, PartData balance, authored clips/audio or the quest-launch hook.

`CombatRemainingChecks.RunBatch` runs the existing combat-polish regressions plus focused real-prefab HUD, knockback, team/result, drop and ragdoll checks in a disposable project. `RunFocusedBatch` omits the old regressions during iteration. Validation artifacts and final counts are recorded after the full run in `.quest-review/Results/CombatMilestones4to7`.

Verified **2026-11-05**, isolated Unity **6000.5.8f1 / Direct3D 11**: **204 checks passed** (149 original combat regressions plus 55 new checks), with no C# compilation warnings/errors or unexpected runtime errors. Rendered ability HUD, success/failure and player corpse captures were reviewed. The checks include actual player/six enemy-family ragdolls, a guaranteed normal part drop, Spinner/orbit knockback, real extraction completion, roster sealing, terminal results and Retry/Main Menu callbacks loading isolated review scenes. No full player build or manual real-input playthrough was run. Networking, authored ability effects and final target-hardware tuning remain gameplay/art integration work.

## Subsequent HUD merge

Reviewed `c45af0c..d4a4b8c`: new Heal/Damage Amp/Jump artwork, a hard-lock cursor sprite and sprite-switching presentation, the new `QuestTooltip` view, and dialogue/scene layout changes. No combat, movement, AI, cooldown-state, mission-results or ragdoll code was replaced by these commits. The removed controls text and collapsed locomotion debug panel remain preserved.

The change to `JhCombatUIRevampScene` removes its temporary collection objective, objective item, return point and old tooltip, replaces the tooltip with `QuestTooltip`, and adds an elite Cannon test enemy. The old collect/return success route is therefore no longer available in that UI review scene. Other gameplay maps retain their existing mission-tracker integration. `QuestTooltip.SetQuest(type, objective)` and `SetObjective(text)` are exposed, but its `Refresh()` remains empty: gameplay still needs to supply the active mission/objective. The pre-mission launch hook remains empty as requested.

The merged project passed **208 isolated Unity checks** (the previous 204 plus four existing scene checks). Ability captures at 0%, 50%, 95% and ready were reviewed, using the real accepted cooldown state, grey-out and imported artwork. The original ability pod positions/sizes are preserved. No C# warnings/errors or unexpected runtime errors occurred. Artifacts are in `.quest-review/Results/AbilityCooldownSweepAndMerge`; no manual full-map playthrough or player build was performed.

## Weapon sweep, stronger knockback and lingering trails — 2026-11-05

Verified in isolated Unity **6000.5.8f1 / Direct3D 11**: **238 checks passed** (151 combat regressions plus 87 integration/hit-feedback checks), with no C# compilation warnings/errors or unexpected runtime exceptions. Real accepted weapon fire, manual magazine reloads, rejected repeated inputs, part swaps, ready brightness and empty-ammunition presentation were checked. Both Spinner variants moved the idle player over 10m on unobstructed ground; the small Spinner measured 10.4m. Solid walls still block motion, and the orbit attack cannot repeat damage or restart knockback against the same victim while it remains inside the circle.

Rendered weapon/ability sweeps and post-impact projectile trails were reviewed. Trail geometry persists and fades after projectile despawn, then returns to the bounded pool; reuse restores the original flight lifetime and opacity. Only the new sweep child/reference is added to the weapon pod; existing authored HUD transforms/artwork and prefab GUIDs were checked before export. Existing weapon profiles receive only the new trail-linger value. Artifacts are retained in `.quest-review/Results/WeaponCooldownKnockbackTrails`. No manual real-input playthrough or player build was performed.
