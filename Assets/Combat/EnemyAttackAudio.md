# Enemy attack sound timing

Enemy weapon animations begin silently during their warning phase. `EnemyAttack` now releases the equipped weapon's existing attack clip at the same moment it creates a projectile or executes the melee damage box, using the existing `releaseFrame` setting. A valid melee swing can still make its sound when the player dodges the damage box; the sound indicates an active attack, rather than confirming a hit.

Cancelled windups, lost/dead targets, and melee strikes prevented by the existing release-time range or wall checks do not play an attack sound. Independent weapons use their own release clocks. A new windup stops the preceding sound on that weapon instead of replaying it early. Player weapon sound timing is unchanged.

Spinner and rope attacks use the shared special-attack audio hook when their charge or throw starts. Their optional `Attack Sound` field replaces `Windup Sound` with `FormerlySerializedAs` migration, preserving any assigned clip. The current special-attack prefabs leave this optional clip empty. Existing impact audio hooks remain attached to actual impacts/contact; no new audio clips or prefab overrides were introduced.

## Verification

`EnemyAttackAudioChecks.RunBatch` runs in a disposable Unity review project. Its 85 checks cover actual roster/Fodder/legacy ranged prefabs, real projectile launches and melee damage, silent warnings, cancellation/death/target loss, release-time walls/range, repeated and independent weapons, special-attack audio migration/timing, and unchanged player audio. Headless Unity playback checks passed. The editor's audio output was muted during testing.

The existing `MechFoundationChecks.RunBatch` suite also passed all 1,012 checks, including its updated enemy sound expectations and existing player weapon audio checks.
