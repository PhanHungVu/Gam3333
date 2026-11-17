# Weapon balance, Large Crossbow piercing and melee debugging

This pass edits existing weapons in place. Equipment slots, meshes, models, projectile speed/size, movement, enemy AI/spawning and retrieval mechanics are retained. Balance numbers below are authored base stats; ordinary rolled loot retains the existing +/-10% variation. Bench/Zoo exact-stat pickups still use the authored values.

## Balance changes

| Weapon | Before | After | Sustained single-target DPS |
| --- | --- | --- | ---: |
| Large Crossbow | 100 direct + 10 splash; reload 4s | 180 direct, no splash/radius; reload 2.5s; piercing | 72 |
| Minigun | Damage 10 | Damage 9 | 65.22 |
| Spear | 130 direct / 130 secondary | 90 / 90 | 90 |
| Shotgun | 60 total, 0.4 attacks/s, reduced intrinsic cone | 150 total (6 x 25), 0.8/s, independent 8-degree half-angle | 93.75 |
| Supersword | 150 / 150, 0.1 attacks/s | 250 / 250, 0.35/s | 87.5 |
| Scythe | 0.4 attacks/s | 0.65/s; damage 100 and 10% confirmed-damage lifesteal retained | 65 |
| Metal Blades | Direct 40, 0.7 attacks/s | Direct 60, 1/s; secondary damage 30 retained | 67.5 |

These DPS values assume every hit lands on one target and no buffs. Existing melee direct and secondary damage do not stack on the same target per strike. Ranged splash still stacks with direct damage, so Metal Blades deals 90 to its direct victim and 30 to nearby victims using its existing single-splash shrapnel fallback.

With damage per activation D, fire rate F, magazine M and reload L, the current reload-overlap timing gives:

`sustained DPS = D * M / ((M - 1) / F + max(1 / F, L))`

Melee uses `D * F` (Punchy retains three timed hits per activation). All unspecified stats are retained. Sword, Mace, Throwing Axe, Small Crossbow, Cannon, Hammer, Hook, Punchy Arm and the unfinished Blaster definitions are protected from balance edits.

## Large Crossbow normal shots

`PartData_Shoulder_LargeCrossbow` remains a Shoulder part, now with Projectile subtype, `Piercing Projectile` enabled and `Maximum Pierced Targets = 0` (unlimited distinct targets within its existing 80m range). A positive value caps the total distinct targets hit, e.g. 2 stops on the second target. This is non-stat configuration, so loot rolls do not change the cap.

`RangedAttack` passes this opt-in setting to `MechProjectile`. Existing non-piercing projectiles retain their first-impact behavior. Piercing bolts process swept sphere-cast contacts from near to far, including multiple enemies within one high-speed step. A per-projectile health-root set prevents repeat damage from compound colliders and later overlapping frames. The existing non-alloc buffers grow only if needed for crowded casts.

Solid scenery and friendly bodies still stop bolts; friendly health is not damaged. Living opposing-team targets receive direct damage once and the bolt continues, including when that hit kills/deactivates the target. Neutral damageable props take their normal direct hit and stop the bolt, retaining obstacle behavior. Each contact publishes the existing direct-impact feedback, without splash or explosions. Range, terrain collisions, the muzzle obstruction check and one final finish event remain in effect. The existing bolt model, 160m/s speed and 0.07m radius are unchanged. Remote retrieval is not involved.

## Shotgun cone

The existing Shotgun prefab exposes `Greybox Arm Weapon > Pellet Spread Angle = 8` degrees **half-angle** (16-degree total cone). This is independently configurable. Head Accuracy shifts the shared blast center once using the normal head error and distance factors. Each of the six pellets then gets its own direction inside the fixed physical cone around that center.

Neither the 0.25 factor nor distance tightening shrinks the intrinsic pellet cone. All pellets share one ammo consumption, attack event and cooldown admission; each carries one-sixth of the current rolled/authored Damage. Other weapons keep their previous accuracy rules, including Minigun half-radius spread. The cursor retains a conservative reference envelope; see [AccuracySpread.md](AccuracySpread.md).

## Game-view melee hitboxes

During Play Mode, select the active player and expand its existing **Melee Box Preview** component:

1. Enable **Show Melee Hitboxes**.
2. Leave **Show Splash Hitboxes** enabled to display secondary areas.
3. Leave **Show Hitboxes Only During Attacks** enabled for actual strike snapshots; disable it for continuous previews of the equipped melee arms.

Red translucent boxes show direct queries; orange translucent spheres show secondary queries. Each instantaneous damage query stays visible for 0.15s (configurable). Three reusable snapshot slots support all Punchy Arm strikes. The snapshot records the actual center, world size, rotation and secondary sphere passed to damage detection. If the player moves after a hit, its brief snapshot stays at the actual hit position. New strikes and idle previews update to the new pose.

Supported: Sword, Mace, Hammer, Spear, Supersword, Scythe, Punchy Arm, and any other part using the existing `MeleeAttack` path. Sword/Mace's current shared box and reach assistance are visualized without correcting their Range/hitbox mismatch. Melee damage queries and secondary exclusion rules are unchanged.

The preview defaults **off** on both Heavy and Specialist prefabs. Debug meshes have no active colliders, cast no shadows, and never perform physics queries or apply damage. Objects/materials are created once when the preview is first enabled and reused. Snapshot events and rendering code are compiled only for the Editor or Development builds; normal release builds contain no debug rendering implementation. No gameplay key or external package is added.

## Changed files

- `PartCatalogue/PartData_Shoulder_LargeCrossbow.asset`.
- `PartCatalogue/GreyboxArms/PartData_Arm_{Minigun,Spear,Shotgun,Supersword,Scythe,MetalBlades}.asset`.
- `Prefabs/Mech/GreyboxArms/arm_Shotgun.prefab`: cone configuration only.
- `Scripts/Part System/PartData.cs`: opt-in piercing configuration.
- `Scripts/Combat/RangedAttack.cs`, `MechProjectile.cs`: normal-shot piercing.
- `Scripts/Combat/MechCombat.cs`, `AccuracySpread.cs`, `GreyboxArmWeapon.cs`: shared-center physical Shotgun cone.
- `Scripts/Combat/MeleeAttack.cs`, `Scripts/Presentation/MeleeBoxPreview.cs`: development query snapshots and reusable Game-view shapes.
- `Scripts/Combat/Editor/GreyboxArmWeaponSetup.cs`: updated balance table so the asset-authoring command does not restore old numbers.
- Tests/docs updated for the new behavior; `WeaponBalanceChecks.cs` covers piercing, cone, DPS and all seven debug hitboxes.

## Testing in the existing arena

Open `Scenes/EnemyBehaviorPlaytest`. The existing Greybox Arm Weapon Bench selects prototypes with [ / ], equips left with comma and right with period, and spawns a normal ground pickup with backslash. Existing Zoo pickups also read the updated shared data. Aim/fire with the normal weapon controls. To test Large Crossbow, use the existing `shoulder_crossbow` part/pickup in a shoulder slot; line up several enemies and place a wall after the first few.

For visualization, enable the Inspector options above and try all seven melee weapons while moving/turning. Punchy should show three separate query events. Toggle visualization off and verify the same damage and movement behavior. For Shotgun, compare near/far targets and change head Accuracy: blast-center precision should change while the intrinsic eight-degree cone remains.

Validation in isolated **Unity 6000.5.8f1**, using the existing combat-testing scene and source-matching assets:

| Suite | Passed checks | Coverage |
| --- | ---: | --- |
| `WeaponBalanceChecks.RunBatch` | 79 | Normal fired Large Crossbow, unlimited/configured piercing, close/compound colliders, killing/deactivating compound roots mid-cast, walls, neutral props, friendly blocking, range expiry, no splash, fixed Shotgun cone/shared center, expected DPS, all seven melee query/debug shapes, toggles, unchanged damage and verified nonblank red/orange capture |
| `GreyboxArmWeaponChecks.RunBatch` | 152 | Exact changed/unchanged stats, ordinary ammo/reload/damage, weapon slots, movement penalties, lifesteal, three punches, existing Metal Blades splash and Hook behavior |
| `AccuracySpreadChecks.RunBatch` | 68 | Existing ordinary/Minigun spread, all four sockets, head Accuracy, cursor scaling, contact-distance tightening and collisions |
| `MechFoundationChecks.RunBatch` | 1,012 | Existing combat, equipment, movement, camera, input, HUD and mission checks |
| `GreyboxArmWeaponArenaChecks.RunBatch` | 45 | Existing combat scene, real bench keys, physical pickups, both arm sockets, HUD binding and held fire |
| `MeleePushChecks.RunBatch` | 35 | Existing melee push/death/drop behavior |

The focused suite ran with Direct3D 11 and saved `.quest-review/ReadabilityProject/weapon-balance-melee-hitboxes.png`; red direct and orange secondary meshes were visually reviewed. The capture waits for the first gameplay frames and checks that both debug colors occupy substantial pixels, preventing a silently blank batch render. The earlier model regression expected a spent splash bolt against a 100HP dummy; it now uses an opposing-team 1,000HP dummy to verify the new 180 direct damage and continued flight. Initial killed-compound testing caught an inactive-parent lookup bug; the final piercing code resolves inactive health parents so residual child contacts cannot stop a bolt which already hit that enemy.

All 25 relevant implementation, asset and test files matched the passing review fixture by hash. Separate before/after hashes confirm the nine protected weapon definitions (including Blaster) and player movement script were not changed in this pass. No source scene edits, standalone player build or interactive human playtest were performed; gameplay balance still needs hands-on evaluation.

Final logs are under `.quest-review/ReadabilityProject`: `weapon-balance-render-verified.log`, `weapon-balance-greybox-final.log`, `weapon-balance-accuracy-final.log`, `weapon-balance-release-foundation.log`, `weapon-balance-arena-final.log` and `weapon-balance-melee-push-final.log`.
