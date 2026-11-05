using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Real player rig, fixed player position and no look input: changing the selected
// enemy's bearing must not repeatedly change the camera's vertical framing.
public static class MechCameraStabilityChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Set(object owner, string field, object value) => owner.GetType().GetField(field, Private).SetValue(owner, value);
    static T Get<T>(object owner, string field) => (T)owner.GetType().GetField(field, Private).GetValue(owner);
    static void Call(object owner, string method, params object[] args) => owner.GetType().GetMethod(method, Private).Invoke(owner, args);
    struct Sample
    {
        public string Name;
        public bool Hard;
        public float PitchSpan, HeightSpan, Pitch, YawTravel;
    }

    public static void Run(Action<bool, string> check)
    {
        var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TestMech.PlayerPath), new Vector3(37000, 20, 0), Quaternion.identity);
        player.GetComponent<MechPlayerInput>().enabled = false;
        player.GetComponent<MechMovementController>().enabled = false;
        var targeting = player.GetComponent<MechTargetLockController>();
        var combat = player.GetComponent<MechCombat>();
        var view = player.GetComponent<MechPlayerRig>().GameplayCamera;
        var control = view.GetComponent<CameraControlThirdPerson>();
        var rig = Get<CinemachineCamera>(control, "cinemachineCamera");
        var enemyObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var enemy = enemyObject.AddComponent<MechTargetable>();
        enemyObject.AddComponent<EnemyAgent>().enabled = false;
        Set(control, "avoidObstacles", false); // Isolate framing from scenery collision.
        var timer = typeof(MeleeAttack).GetProperty("CameraTimer");
        try
        {
            void Place(float bearing)
            {
                float angle = bearing * Mathf.Deg2Rad;
                enemyObject.transform.position = player.transform.position + new Vector3(Mathf.Sin(angle) * 2.4f, -.6f, Mathf.Cos(angle) * 2.4f);
                Physics.SyncTransforms();
            }
            void Frame(bool attacking, Vector2 look = default)
            {
                timer.SetValue(combat.Melee, attacking ? .45f : 0f);
                Call(control, "UpdateCameraOrbit", look, 1f / 60);
                Call(control, "ApplySettings");
                // Force an up-to-date target pose when sampling multiple simulated
                // frames in one editor callback. Orbit smoothing still uses 1/60 s.
                rig.InternalUpdateCameraState(Vector3.up, -1);
                view.transform.SetPositionAndRotation(rig.State.GetFinalPosition(), rig.State.GetFinalOrientation());
            }
            Sample Measure(bool hard, bool attacking, int side)
            {
                targeting.EnterFreeAim(); control.BindTarget(player.transform); Place(side * 59.5f);
                targeting.ResumeTargeting(); Call(targeting, "SetTarget", enemy);
                if (targeting.IsHardLockMode != hard) targeting.ToggleHardLock();
                for (int i = 0; i < 180; i++) Frame(attacking);
                var sample = new Sample { Name = (hard ? "hard" : "soft") + (attacking ? " melee" : " idle") + (side < 0 ? " left" : " right"), Hard = hard };
                float low = float.PositiveInfinity, high = float.NegativeInfinity;
                float lowY = low, highY = high, priorYaw = Get<float>(control, "cameraYaw");
                for (int i = 0; i < 360; i++)
                {
                    // Soft lock straddles the old 60-degree gate. Hard lock also
                    // crosses sides rapidly, exercising its turn toward the enemy.
                    float angle = hard ? ((i / 30) % 2 == 0 ? 95 : -95) : side * ((i / 30) % 2 == 0 ? 59.5f : 60.5f);
                    Place(angle); Frame(attacking);
                    float pitch = Get<float>(control, "cameraPitch");
                    low = Mathf.Min(low, pitch); high = Mathf.Max(high, pitch);
                    lowY = Mathf.Min(lowY, view.transform.position.y); highY = Mathf.Max(highY, view.transform.position.y);
                    float yaw = Get<float>(control, "cameraYaw"); sample.YawTravel += Mathf.Abs(Mathf.DeltaAngle(priorYaw, yaw)); priorYaw = yaw;
                    sample.Pitch = pitch;
                }
                sample.PitchSpan = high - low; sample.HeightSpan = highY - lowY;
                Debug.Log($"SIDE CAMERA: {sample.Name}; pitch span={sample.PitchSpan:F3} degrees; height span={sample.HeightSpan:F3}; final pitch={sample.Pitch:F3}; yaw travel={sample.YawTravel:F1}");
                return sample;
            }

            var samples = new List<Sample>();
            foreach (bool hard in new[] { false, true })
                foreach (bool attacking in new[] { false, true })
                    foreach (int side in new[] { -1, 1 }) samples.Add(Measure(hard, attacking, side));
            foreach (var sample in samples)
            {
                check(sample.PitchSpan < .1f && sample.HeightSpan < .05f,
                    sample.Name + " stays vertically stable while a nearby enemy crosses side angles");
                // Soft (free camera) keeps the camera's own Initial Pitch, whatever it is tuned to.
                float initialPitch = Get<float>(control, "initialPitch");
                check(sample.Hard ? sample.YawTravel > 500 && sample.Pitch > 30 : sample.YawTravel < .01f && Mathf.Abs(sample.Pitch - initialPitch) < .01f,
                    sample.Name + " retains the intended manual soft aim or elevated hard-lock tracking; yawTravel=" + sample.YawTravel + "; pitch=" + sample.Pitch);
            }

            targeting.EnterFreeAim(); control.BindTarget(player.transform);
            for (int i = 0; i < 180; i++) { Place(i % 2 == 0 ? 89 : -89); Frame(false); }
            check(Mathf.Abs(Get<float>(control, "cameraPitch") - 8) < .01f, "Nearby side enemies cannot change free-aim pitch");
            Frame(false, Vector2.up * 20);
            check(Get<float>(control, "cameraPitch") < 8, "The stable camera still accepts manual vertical look");
        }
        finally
        {
            Object.DestroyImmediate(enemyObject);
            Object.DestroyImmediate(player);
        }
    }
}
