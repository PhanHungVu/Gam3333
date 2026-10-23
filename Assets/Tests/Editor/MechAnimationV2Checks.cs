using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class MechAnimationV2Checks
{
    public static void Run(Action<bool, string> check)
    {
        var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TestMech.PlayerPath),
            new Vector3(40000, 1.6f, 0), Quaternion.identity);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.transform.position = new Vector3(40000, -0.5f, 0);
        floor.transform.localScale = new Vector3(500, 1, 500);
        var view = new GameObject("V2 animation check view");
        var motor = player.GetComponent<MechMovementController>();
        var equipment = player.GetComponent<EquipManager>();
        var swap = player.GetComponent<MechLocomotionDebug>();
        MechLegAnimator Driver() => equipment.GetEquipped(PartCategory.Legs).GetComponent<MechLegAnimator>();
        try
        {
            TestMech.StopPlayerInput(motor);
            Physics.SyncTransforms();
            var neutral = Clip("Biped_Neutral");
            var jump = Clip("Biped_Jump");
            check(neutral != null && AnimationUtility.GetCurveBindings(neutral).Length > 0,
                "Fast Neutral now uses the authored motion from the current export");
            check(jump != null && AnimationUtility.GetCurveBindings(jump).Length > 0, "Fast Jump now uses the authored motion from the current export");
            foreach (string state in new[] { "Neutral", "Boost", "BoostStop", "Jump" })
            {
                var fastAction = MechBipedAnimationImport.ActionClip("Fast", state);
                var heavyAction = MechBipedAnimationImport.ActionClip("Heavy", state);
                check(heavyAction != null && heavyAction != fastAction && AnimationUtility.GetCurveBindings(heavyAction).Length > 0,
                    "Heavy " + state + " has its own saved authored motion");
                check(heavyAction.isLooping == (state == "Neutral" || state == "Boost"), "Heavy " + state + " has the correct loop setting");
            }
            check(Clip("Boost").isLooping && !Clip("BoostStop").isLooping && Clip("Biped_Run").isLooping,
                "Walk/boost loop while boost-stop is a one-shot despite the GLB's looping import defaults");
            Step(player, view.transform, 90);
            CheckPose(Driver(), MechLegAnimator.PoseState.Neutral, "Biped_Neutral", check);
            check(Driver().Animator.name == "golem2_v2", "Animated modules use the v2 model instance");
            Step(player, view.transform, 100, Vector2.up);
            CheckPose(Driver(), MechLegAnimator.PoseState.Locomotion, "Biped_Run", check);
            check(Mathf.Abs(Driver().Animator.GetFloat("PlaybackSpeed") - 1f) < 0.03f, "Normal walk uses normal clip cadence");
            Step(player, view.transform, 100, Vector2.up, sprint: true);
            CheckPose(Driver(), MechLegAnimator.PoseState.Locomotion, "Biped_Run", check);
            check(Mathf.Abs(motor.AnimationData.Speed - 6f) < 0.1f && Mathf.Abs(Driver().Animator.GetFloat("PlaybackSpeed") - 1f) < 0.03f,
                "Removed sprint retains the ordinary Fast walk clip and cadence");

            Step(player, view.transform, 90);
            Step(player, view.transform, 6, quick: true);
            CheckPose(Driver(), MechLegAnimator.PoseState.Boost, "Boost", check);
            check(motor.IsQuickBoosting, "Short dash uses Boost while the existing dash timer is active");
            Step(player, view.transform, 16);
            CheckPose(Driver(), MechLegAnimator.PoseState.BoostStop, "BoostStop", check);
            Step(player, view.transform, 80);
            CheckPose(Driver(), MechLegAnimator.PoseState.Neutral, "Biped_Neutral", check);
            CheckNeutralPose(Driver(), check);

            Step(player, view.transform, 65, assault: true);
            CheckPose(Driver(), MechLegAnimator.PoseState.Boost, "Boost", check);
            check(motor.IsAssaultBoosting && Driver().Animator.GetCurrentAnimatorStateInfo(0).normalizedTime > 1f,
                "Sustained boost loops the same motion beyond one clip duration");
            var position = player.transform.position;
            var velocity = motor.Velocity;
            float phase = Driver().NormalizedPhase;
            check(swap.TrySwap(1), "Heavy module can be equipped during an active boost");
            CheckPose(Driver(), MechLegAnimator.PoseState.Boost, "Heavy_Boost", check);
            check(player.transform.position == position && motor.Velocity == velocity && Mathf.Abs(Driver().NormalizedPhase - phase) < 0.02f,
                "Boost swap preserves player motion and animation phase");
            Step(player, view.transform, 16);
            CheckPose(Driver(), MechLegAnimator.PoseState.BoostStop, "Heavy_BoostStop", check);
            phase = Driver().NormalizedPhase;
            check(swap.TrySwap(0), "Fast module can be restored partway through boost-stop");
            CheckPose(Driver(), MechLegAnimator.PoseState.BoostStop, "BoostStop", check);
            check(Mathf.Abs(Driver().NormalizedPhase - phase) < 0.02f, "Swapping during boost-stop continues its remaining one-shot instead of replaying it");
            Step(player, view.transform, 15, Vector2.up);
            CheckPose(Driver(), MechLegAnimator.PoseState.Locomotion, "Biped_Run", check);
            check(motor.AnimationData.Speed > 5.9f, "Movement can resume out of boost-stop without a gameplay input lock");

            Step(player, view.transform, 10, assault: true);
            Step(player, view.transform, 6);
            CheckPose(Driver(), MechLegAnimator.PoseState.BoostStop, "BoostStop", check);
            Step(player, view.transform, 6, jump: true);
            CheckPose(Driver(), MechLegAnimator.PoseState.Jump, "Biped_Jump", check);
            check(!motor.IsGrounded, "Jump interrupts the stop reaction while existing jump physics remains in control");
            Step(player, view.transform, 100);
            CheckPose(Driver(), MechLegAnimator.PoseState.Neutral, "Biped_Neutral", check);
            check(motor.IsGrounded, "Landing returns Jump to Neutral");

            Step(player, view.transform, 15, assault: true);
            Step(player, view.transform, 6);
            Step(player, view.transform, 6, quick: true);
            CheckPose(Driver(), MechLegAnimator.PoseState.Boost, "Boost", check);
            check(motor.IsQuickBoosting, "A new dash interrupts boost-stop immediately");
            player.GetComponent<MechEquipmentLocomotion>().enabled = false;
            check(!Driver().Animator.enabled, "Disabling the equipment bridge stops action playback");
            player.GetComponent<MechEquipmentLocomotion>().enabled = true;
            CheckPose(Driver(), MechLegAnimator.PoseState.Boost, "Boost", check);
            Step(player, view.transform, 120);
            swap.TrySwap(1);
            Step(player, view.transform, 100, Vector2.up, sprint: true);
            CheckPose(Driver(), MechLegAnimator.PoseState.Locomotion, "Heavy_Walk.001", check);
            check(Mathf.Abs(motor.AnimationData.Speed - 3f) < 0.1f && Mathf.Abs(Driver().Animator.GetFloat("PlaybackSpeed") - 1f) < 0.03f,
                "Removed sprint retains the ordinary Heavy walk clip and cadence");
            Step(player, view.transform, 90);
            CheckPose(Driver(), MechLegAnimator.PoseState.Neutral, "Heavy_Neutral", check);
            Step(player, view.transform, 6, jump: true);
            CheckPose(Driver(), MechLegAnimator.PoseState.Jump, "Heavy_Jump", check);
            Step(player, view.transform, 100);
            CheckPose(Driver(), MechLegAnimator.PoseState.Neutral, "Heavy_Neutral", check);
        }
        finally { Object.DestroyImmediate(player); Object.DestroyImmediate(floor); Object.DestroyImmediate(view); }
    }

    static AnimationClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AnimationClip>(MechBipedAnimationImport.Folder + "/" + name + ".anim");

    public static void CheckNeutralPose(MechLegAnimator driver, Action<bool, string> check)
    {
        check(driver.CurrentPose == MechLegAnimator.PoseState.Neutral, "Stopped module selects its authored Neutral");
        var clip = driver.Animator.GetCurrentAnimatorClipInfo(0).Single(c => c.weight > 0.99f).clip;
        var reference = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TestMech.LegsModel));
        try
        {
            clip.SampleAnimation(reference, driver.NormalizedPhase * clip.length);
            foreach (string path in new[] { "Armature/Root/Body", "Armature/Root/Body/Head", "Armature/Root/Leg.L",
                "Armature/Root/Leg.L/Foot.L", "Armature/Root/Leg.R", "Armature/Root/Leg.R/Foor.R" })
            {
                var actual = driver.Animator.transform.Find(path);
                var expected = reference.transform.Find(path);
                check(Vector3.Distance(actual.localPosition, expected.localPosition) < 0.002f
                    && Quaternion.Angle(actual.localRotation, expected.localRotation) < 0.15f,
                    "Idle matches " + clip.name + " for " + path);
            }
        }
        finally { Object.DestroyImmediate(reference); }
    }

    static void CheckPose(MechLegAnimator driver, MechLegAnimator.PoseState pose, string clip, Action<bool, string> check)
    {
        check(driver.CurrentPose == pose, "Presentation selects " + pose);
        for (int layer = 0; layer < 3; layer++)
        {
            float weight = driver.Animator.GetCurrentAnimatorClipInfo(layer).Where(c => c.clip.name == clip).Sum(c => c.weight);
            check(weight > 0.98f, "Layer " + layer + " plays " + clip + " for " + pose + " (weight=" + weight + ")");
        }
    }

    static void Step(GameObject player, Transform view, int count, Vector2 move = default, bool sprint = false,
        bool quick = false, bool assault = false, bool jump = false)
    {
        var motor = player.GetComponent<MechMovementController>();
        var equipment = player.GetComponent<EquipManager>();
        for (int i = 0; i < count; i++)
        {
            motor.Tick(new MechLocomotionInput { move = move, sprint = sprint, quickBoost = quick && i == 0,
                assaultBoost = assault, jump = jump && i == 0 }, view, 0.02f);
            var driver = equipment.GetEquipped(PartCategory.Legs).GetComponent<MechLegAnimator>();
            driver.ApplyParameters(0.02f); driver.Animator.Update(0.02f);
            player.GetComponent<MechUpperBodyAnimation>().Evaluate(0.02f);
        }
    }
}
