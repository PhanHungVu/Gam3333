using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class MechWeaponAnimationChecks
{
    public static void Run(Action<bool, string> check)
    {
        var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TestMech.PlayerPath),
            new Vector3(46000, 1.6f, 0), Quaternion.identity);
        var equipment = player.GetComponent<EquipManager>();
        var combat = player.GetComponent<MechCombat>();
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(TestMech.WeaponAnimationModel);
        var sets = AssetDatabase.LoadAssetAtPath<MechWeaponAnimationImportSettings>(MechWeaponAnimationImport.SettingsPath);
        var detached = new System.Collections.Generic.List<GameObject>();
        var reference = Object.Instantiate(source);
        try
        {
            TestMech.StopPlayerInput(player.GetComponent<MechMovementController>());
            // This suite compares the six archived glTF rig imports with their source clips.
            // Live loadouts use the Specialist cannon, covered by GreyCannonReplacementChecks.
            var equipmentForImports = player.GetComponent<EquipManager>();
            foreach (var slot in new[] { PartCategory.ShoulderL, PartCategory.ShoulderR })
            {
                var live = equipmentForImports.GetEquipped(slot);
                if (live == null || live.data.partId != "backweaponA3") continue;
                var archived = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(GreyCannonReplacementSetup.LegacyPath)).GetComponent<Part>();
                check(equipmentForImports.TryEquip(archived, slot), "Archived cannon remains usable for legacy clip-import validation");
                Object.DestroyImmediate(live.gameObject);
            }
            var bridge = player.GetComponent<MechWeaponIdle>();
            var drivers = player.GetComponentsInChildren<MechWeaponAnimator>();
            bridge.ResetWeaponIdle();
            bridge.TickWeaponIdle(2.99f, true);
            foreach (var driver in drivers) driver.Tick(0.2f);
            check(bridge.IdleWeapon == null && drivers.All(d => !d.NeutralPlaying && d.NormalizedTime == 0f),
                "Every weapon holds its rest pose before three stationary seconds");
            bridge.TickWeaponIdle(0.02f, true);
            foreach (var driver in drivers) driver.Tick(0.1f);
            var idle = bridge.IdleWeapon;
            check(idle != null && drivers.Count(d => d.NeutralPlaying) == 1
                && drivers.Where(d => d != idle).All(d => d.NormalizedTime == 0f),
                "After three seconds exactly one randomly selected equipped weapon plays Neutral");
            bridge.TickWeaponIdle(1f, true);
            check(bridge.IdleWeapon == idle, "Idle choice remains stable during the same stationary period");
            bridge.TickWeaponIdle(0.1f, false);
            foreach (var driver in drivers) driver.Tick(0.1f);
            check(bridge.IdleWeapon == null && drivers.All(d => !d.NeutralPlaying && d.NormalizedTime == 0f),
                "Moving immediately cancels Neutral and resets all weapons to rest");
            bridge.TickWeaponIdle(3.01f, true);
            check(sets.weapons.Length == 6, "All six weapon families have explicit clip/rig selections");
            foreach (var entry in sets.weapons)
            {
                check(entry.target.neutral != null && entry.target.fire != null,
                    entry.target.name + " has saved Neutral and Fire clips");
                foreach (var clip in new[] { entry.target.neutral, entry.target.fire, entry.target.reload }.Where(c => c != null))
                    check(AnimationUtility.GetCurveBindings(clip).All(b => b.path.StartsWith(entry.rigPath + "/", StringComparison.Ordinal)),
                        clip.name + " affects only its weapon rig");
            }
            foreach (var slot in new[] { PartCategory.ShoulderL, PartCategory.ShoulderR, PartCategory.ArmR })
            {
                var part = player.GetComponent<MechCombat>().GetPart(slot);
                var driver = part.GetComponent<MechWeaponAnimator>();
                check(driver != null && !driver.Animator.applyRootMotion, slot + " has an independent visual animator without root motion");
                var root = player.transform.position;
                var partPosition = part.transform.localPosition;
                var body = driver.Animator.transform.Find(sets.weapons.Single(e => e.part == part.data).rigPath + "/"
                    + sets.weapons.Single(e => e.part == part.data).bodyBone);
                var rest = body.position; var rotation = body.rotation;
                int ammo = combat.AmmoRemaining(slot);
                var projectile = combat.TryAttack(slot).Projectile;
                check(bridge.IdleWeapon == null, slot + " attack cancels the shared idle selection");
                check(projectile != null && combat.AmmoRemaining(slot) == ammo - 1 && driver.CurrentPose == MechWeaponAnimator.PoseState.Fire,
                    slot + " starts Fire only after gameplay admits a real shot");
                if (projectile != null) Object.DestroyImmediate(projectile.gameObject);
                driver.Tick(0.1f);
                check(Vector3.Distance(rest, body.position) > 0.001f || Quaternion.Angle(rotation, body.rotation) > 0.1f,
                    slot + " authored Fire curves visibly move its bone");
                check(player.transform.position == root && part.transform.localPosition == partPosition,
                    slot + " firing never moves the player or equipment root");
                check(combat.TryAttack(slot).Projectile == null && driver.CurrentPose == MechWeaponAnimator.PoseState.Fire,
                    slot + " rejected cooldown input does not restart presentation");
                if (driver.Animations.reload != null)
                {
                    driver.Tick(0.3f);
                    check(driver.CurrentPose == MechWeaponAnimator.PoseState.Reload, slot + " enters its authored Reload clip");
                }
                driver.Tick(4f);
                check(driver.CurrentPose == MechWeaponAnimator.PoseState.Neutral, slot + " returns to Neutral after the action");
            }
            var axe = equipment.GetEquipped(PartCategory.ArmR).GetComponent<MechWeaponAnimator>();
            var barrel = axe.Placement;
            var barrelPosition = barrel.localPosition; var barrelRotation = barrel.localRotation;
            var world = barrel.position;
            axe.PlayAttack(0.5f); axe.Tick(0.2f);
            check(Vector3.Distance(barrelPosition, barrel.localPosition) < 0.0001f
                && Quaternion.Angle(barrelRotation, barrel.localRotation) < 0.01f,
                "Axe placement bone retains its unanimated local pose");
            check(Vector3.Distance(world, barrel.position) > 0.001f && axe.GetComponentsInChildren<MeshFilter>()
                .Single(m => m.sharedMesh.name == "ThrowingAxeMesh").transform.IsChildOf(barrel),
                "Visible axe stays attached and follows the animated arm through its placement bone");
            var sword = equipment.GetEquipped(PartCategory.ArmL).GetComponent<MechWeaponAnimator>();
            check(combat.TryAttack(PartCategory.ArmL).Started && sword.CurrentPose == MechWeaponAnimator.PoseState.Fire,
                "Existing melee starts Sword Fire on admission rather than on delayed damage");
            sword.Tick(0.2f);
            CheckPose(sword, sets.weapons.Single(e => e.target == sword.Animations), reference, 0.2f, check);
            var cannon = equipment.GetEquipped(PartCategory.ShoulderL).GetComponent<MechWeaponAnimator>();
            cannon.PlayAttack(3f); cannon.Tick(0.3f);
            var pose = cannon.CurrentPose; var charge = player.GetComponent<MechEnergyController>().Current;
            check(player.GetComponent<MechLocomotionDebug>().TrySwap(1) && cannon.CurrentPose == pose
                && player.GetComponent<MechEnergyController>().Current == charge,
                "Heavy leg swap preserves weapon action and energy");
            foreach (string name in new[] { "arm_mace", "arm_crossbow" })
            {
                var replacement = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/" + name + ".prefab"));
                var part = replacement.GetComponent<Part>();
                var groundDriver = part.GetComponent<MechWeaponAnimator>();
                groundDriver.Tick(10f);
                check(!groundDriver.Animator.enabled && !groundDriver.CanAnimate && groundDriver.NormalizedTime == 0f,
                    name + " pickup has no running Animator or advancing neutral clock on the ground");
                // Parts carry no side, so name the slot each weapon is tested in.
                var slot = name == "arm_mace" ? PartCategory.ArmL : PartCategory.ArmR;
                var old = equipment.GetEquipped(slot);
                check(equipment.TryEquip(part, slot), name + " equips through the normal part system");
                if (old != null) detached.Add(old.gameObject);
                var driver = part.GetComponent<MechWeaponAnimator>();
                int projectiles = 0;
                Action<MechProjectile> onShot = shot => { projectiles++; Object.DestroyImmediate(shot.gameObject); };
                combat.ShotFired += onShot;
                if (name == "arm_mace") check(combat.TryAttack(PartCategory.ArmL).Started, "Mace button admits its damaging melee swing");
                else combat.TryAttack(PartCategory.ArmR);
                combat.ShotFired -= onShot;
                check(driver.CurrentPose == MechWeaponAnimator.PoseState.Fire && projectiles == (name == "arm_crossbow" ? 1 : 0),
                    name + " plays Fire with its configured projectile or melee behavior");
                driver.Tick(0.1f);
                CheckPose(driver, sets.weapons.Single(e => e.target == driver.Animations), reference, 0.1f, check);
                if (driver.Animations.reload != null)
                {
                    driver.Tick(0.3f);
                    check(driver.CurrentPose == MechWeaponAnimator.PoseState.Reload, "Arm crossbow uses its own Reload");
                }
                driver.Tick(3f);
                check(driver.CurrentPose == MechWeaponAnimator.PoseState.Neutral, name + " finishes back at Neutral");
            }
            equipment.GetEquipped(PartCategory.ArmR).GetComponent<MechWeaponAnimator>().PlayAttack(1f);
            var dropped = equipment.Unequip(PartCategory.ArmR);
            detached.Add(dropped.gameObject);
            var droppedDriver = dropped.GetComponent<MechWeaponAnimator>();
            check(!droppedDriver.Animator.enabled && !droppedDriver.CanAnimate
                && droppedDriver.CurrentPose == MechWeaponAnimator.PoseState.Neutral,
                "Dropping during Fire stops playback immediately and holds the rest pose");
            droppedDriver.Tick(0.1f);
            check(!droppedDriver.PlayAttack(1f), "Dropped weapon cannot start an equipped attack animation");
            check(equipment.TryEquip(dropped) && droppedDriver.PlayAttack(1f) && droppedDriver.Animator.enabled,
                "Re-equipped weapon can play immediately without waiting for the next Update");
            detached.Remove(dropped.gameObject);
        }
        finally
        {
            foreach (var go in detached) if (go != null) Object.DestroyImmediate(go);
            Object.DestroyImmediate(player); Object.DestroyImmediate(reference);
        }
    }

    static void CheckPose(MechWeaponAnimator driver, MechWeaponAnimationImportSettings.Entry entry,
        GameObject reference, float time, Action<bool, string> check)
    {
        entry.fire.SampleAnimation(reference, time);
        foreach (var path in AnimationUtility.GetCurveBindings(entry.fire).Select(b => b.path).Distinct())
        {
            var actual = driver.Animator.transform.Find(path); var expected = reference.transform.Find(path);
            check(Vector3.Distance(actual.localPosition, expected.localPosition) < 0.002f
                && Quaternion.Angle(actual.localRotation, expected.localRotation) < 0.2f,
                entry.target.name + " playback reproduces authored pose for " + path);
        }
    }
}
