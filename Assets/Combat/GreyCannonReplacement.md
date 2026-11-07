# Active cannon model

All current player/enemy cannon copies, scene pickups and cannon drop references use the grey Specialist model and its authored animations. The active standalone pickup is `Prefabs/Mech/Specialist/shoulder_cannon_specialist.prefab`.

The old coloured cannon remains at `Prefabs/Mech/Base/Parts/shoulder_cannon.prefab`. Its model, textures and saved animation clips are preserved for reference and legacy import validation. The old prototype assets under `Parts/Old` are also retained. These are excluded from the replacement pass; gameplay scenes and active character prefabs do not spawn the archived cannon.

The new cannon already shares the original `PartData_Shoulder_Cannon` and cannon sound. Damage, ammunition, reload, range, splash and enemy weapon multipliers are unchanged. The replacement keeps the existing Part and MechWeaponAnimator components on embedded character weapons, preserving their attack references. New visuals, animator bindings and muzzle references replace the old child rig. Legacy bodies receive a socket attachment offset to account for the old rig's baked mount translation; the Specialist keeps its existing raised/outward shoulder poses. Permanent enemy weapon colliders remain disabled.

The Heavy demo player, cannon elite, both rope elite variants and boss are covered. Existing scene instances inherit their updated prefabs. Explicit cannon pickups/overrides in ZooScene, JhMapScene and JhCombatUIRevampScene are updated as well. Mission wave lists, AI behavior, player movement and HUD layouts are preserved.

`EnemyPartDrop` references the new unequipped pickup. Its Inspector **Fill From Carried Weapons** action also resolves carried cannons to that pickup, including when the weapon is embedded in an enemy prefab.

**Tools > Mech > Replace Active Cannons With Specialist Model** reapplies the migration in Edit Mode. It changes only assets that still contain old cannon copies/references. Reapplying it leaves migrated assets unchanged. The Specialist model integration tool no longer edits the archived cannon's muzzle.

`GreyCannonReplacementChecks.RunBatch` runs in a disposable Unity project. It checks archive/art/stat preservation, enemy attack settings and references, migration idempotence, every active prefab and scene, clip bindings, both player shoulder slots, actual elite projectile release and an actual death drop. Graphical captures cover the Specialist player, Heavy demo and cannon elite. The legacy weapon-import tests explicitly equip the archived cannon fixture when comparing its saved clips with the original glTF source; active loadouts are validated separately with the new cannon.

Review artifacts are stored under `.quest-review/Results/GreyCannonReplacement`. No standalone build or human gameplay feel review is included.

Validation on 2026-11-07 in Unity 6000.5.8f1: **702 migration/runtime checks** passed with D3D11 graphics, followed by **982 foundation checks**, both exit 0. Player/elite captures were visually inspected. The final runs reported no C# diagnostics or audited runtime exceptions. The migration changed five character prefabs and three scenes; a second application made no changes. Archive/art/stat/HUD/control/wave preservation checks passed.
