# Shared gameplay HUD and repaired training scenes

`Prefabs/Mech Cursor Canvas.prefab` is the shared gameplay UI. This branch's prefab already contains the cursor, CombatHUD (health, weapons and abilities), DialogueBox and QuestTooltip. Its artwork and layout are preserved.

`Prefabs/Mech/Base/FullPlayer_mech_golem.prefab`, `FullPlayer_mech_heavy.prefab` and `Prefabs/Mech/Specialist/FullPlayer_mech_specialist.prefab` reference that canvas's current GUID and the CombatHUD fallback. Their stale canvas references have been corrected. `MechPlayerRig` creates the shared canvas when a scene has no cursor. If a scene has an authored combat HUD but no cursor, the rig keeps the authored HUD and removes the fallback canvas's duplicate combat HUD. KOTH/Escort and other map tests can therefore receive the HUD through any of these player prefabs.

Legacy canvases containing the stamina meter in A33, ZooScene, JhMapScene and Merge Test are replaced with linked instances of the shared canvas. This removes their old health meter and retired part-popup children as well. JhMapScene's prototype objective text is reconnected to the new quest readout. The authored JhCombatUIRevampScene remains unchanged.

JhMapScene and Merge Test no longer depend on the missing EnemySpawnerManager_Crossbow and EnemySpawnerManager_Mace prefabs. Their existing spawn locations are used by SpawnDirector/ZooEnemyWaves with a separate `LevelCatalogue/WaveData_Training.asset`. It enables the current nine small/elite enemy variants, with a two-second interval, eight-enemy cap, 50 resources and 15-second reset. Existing mission wave assets and Zoo's switches/roster are preserved.

The pre-mission heavy torso entry displays **Heavy Torso (Demo Only)** and describes the all-weapons demo loadout. It maps to `_heavy`; the regular torso now maps to `_specialist`. The legacy `_golem` prefab also receives the new body for older scene references. See [SpecialistPlayerIntegration.md](SpecialistPlayerIntegration.md) for the model/socket integration. No equipment or stat values are changed.

**Tools > Mech > Repair Gameplay HUD and Legacy Spawners** reapplies the migration. Stop Play Mode first. The menu handles unsaved scenes and returns to the previous saved scene setup.

Validation is implemented in `Tests/Editor/GameplaySceneMaintenanceChecks.cs`: scene loading, missing references, both player variants with/without authored UI, unique HUD instances, actual map startup and enemy spawning. Automated results are kept under `.quest-review/Results/UpdatedBranchHudRepair`.

On 2026-11-06, **50 checks passed** in isolated Unity 6000.5.8f1 (exit 0), with no C# diagnostics or runtime exceptions. Eight gameplay scenes opened without missing prefab/script instances or stamina HUDs. Both player variants passed all three UI startup cases. Actual KOTHTest, EscortTest, ZooScene and JhMapScene startup produced one complete HUD bound to the player; JhMapScene also spawned a current Behavior Graph enemy. This was automated headless verification, without a manual visual playthrough or player build.

## Mission pause menu

`Prefabs/UI/Mission Pause Menu.prefab` is a linked child of `Prefabs/Mech Cursor Canvas.prefab`. Its panel starts hidden. `MechPlayerRig` binds it to the local `MechPlayerInput` for both player prefabs, whether the shared canvas is authored in the scene or created at runtime. It cannot pause main-menu or pre-mission previews without a bound gameplay player.

**Escape** toggles pause. **Resume**, **Restart Mission** and **Exit Mission** have persistent Button click handlers visible in the prefab Inspector. Restart uses the existing mission retry path. Exit uses the existing mission return route, including a quest-board return when configured; otherwise it returns to MainMenuScene. Mouse input works immediately, and keyboard/gamepad navigation starts after movement/submit controls held on opening have been released. Resume clicks and the resume frame cannot also fire weapons, dodge or activate abilities.

The menu owns its `Time.timeScale = 0` and audio-listener pause and restores their previous values on resume, disable, scene departure or a terminal result. It unlocks the cursor while open and restores the prior cursor state when resuming gameplay. A missing EventSystem is supplied using Input System UI input. Objectives, mission transitions, spawning and enemy graph entry do not advance on zero-time frames. Simulation components remain enabled, preserving their ongoing movement/attack state for resume.

Edit the linked pause prefab to change its title, colors, font and button layout. The dimmer fills the HUD and its nested canvas draws over gameplay UI. Root layout is normalized when reused beneath a HUD canvas. **Tools > Mech > UI > Install Mission Pause Menu** reapplies the installation without duplicating the child or rebuilding the other HUD elements. For a custom HUD, include the prefab and call `menu.Bind(playerInput)`.

`MissionPauseMenuChecks.RunBatch` passed **59 checks** in isolated Unity 6000.5.8f1, covering persistent prefab links, both player configurations, real Escape and mouse input, held-key suppression, frozen player/enemy/objective activity, prior time/audio-state restoration, cleanup, actual restart/exit scene loads, mission-result handling and mission/spawning regressions. The rendered menu was visually reviewed. Log: `.quest-review/ReadabilityProject/mission-pause-menu-checks.log`; preview: `.quest-review/ReadabilityProject/mission-pause-menu.png`.
