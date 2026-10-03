using System;
using System.Linq;
using System.Reflection;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Exercises the actual scene camera/brain; all temporary poses stay in the isolated play session.
public static class MechCameraCollisionSceneChecks
{
    const string Pending = "MechCameraCollisionSceneChecks.Pending";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static CameraControlThirdPerson control;
    static CinemachineThirdPersonFollow follow;
    static Camera view;
    static SphereCollider probe;
    static int phase;
    static float next = -1f;
    static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Private).SetValue(obj, value);
    static T Get<T>(object obj, string field) => (T)obj.GetType().GetField(field, Private).GetValue(obj);
    static void Check(bool ok, string message)
    { if (!ok) throw new Exception(message); Debug.Log("CAMERA SCENE PASS: " + message); }

    [InitializeOnLoadMethod]
    static void Register() => EditorApplication.update += Update;
    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Isolated batch project only.");
        EditorSceneManager.OpenScene("Assets/Scenes/JhMapScene.unity");
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }

    static void Update()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (next < 0f) next = Time.time + 1f;
        if (Time.time < next) return;
        try
        {
            if (phase == 0)
            {
                var platforms = Object.FindObjectsByType<MeshFilter>()
                    .Where(f => f.name == "platform" || f.name.StartsWith("platform (", StringComparison.Ordinal)).ToArray();
                Check(platforms.Length == 8, "All eight JhMapScene platforms are present");
                foreach (var filter in platforms)
                {
                    var collider = filter.GetComponent<MeshCollider>();
                    Check(collider != null && collider.enabled && !collider.isTrigger && collider.sharedMesh == filter.sharedMesh,
                        filter.name + " inherits its solid mesh collider in the real scene");
                }
                control = Object.FindAnyObjectByType<CameraControlThirdPerson>();
                Check(control != null && control.isActiveAndEnabled && control.Target != null,
                    "JhMapScene uses the active player camera with a bound target");
                view = control.GetComponent<Camera>();
                var rig = Get<CinemachineCamera>(control, "cinemachineCamera");
                follow = rig.GetComponent<CinemachineThirdPersonFollow>();
                foreach (var enemy in Object.FindObjectsByType<EnemyAttack>()) enemy.enabled = false;
                foreach (var enemy in Object.FindObjectsByType<EnemyAgent>()) enemy.enabled = false;
                control.Target.GetComponent<MechMovementController>().enabled = false;
                control.Target.GetComponent<CharacterController>().enabled = false;
                control.Target.GetComponent<MechTargetLockController>().EnterFreeAim();
                Set(control, "cameraYaw", 0f); Set(control, "cameraPitch", 0f);
                Set(control, "shoulderHeight", 0f); Set(control, "lateralOffsetDistance", 0f);
                Set(control, "movementFeedback", false);
                var profile = Get<object>(control, "normalProfile");
                profile.GetType().GetField("targetOffset").SetValue(profile, Vector3.zero);
                profile.GetType().GetField("damping").SetValue(profile, Vector3.zero);
                Set(control, "normalProfile", profile);
                Bounds bounds = platforms[0].GetComponent<Renderer>().bounds;
                control.Target.position = bounds.center + Vector3.forward * (bounds.extents.z + 2f);
                rig.PreviousStateIsValid = false;
                probe = view.gameObject.AddComponent<SphereCollider>(); probe.isTrigger = true;
                Debug.Log("CAMERA SCENE platform bounds: " + bounds + "; player pose: " + control.Target.position);
                Physics.SyncTransforms();
            }
            else if (phase == 1)
            {
                var obstacle = follow.CurrentObstacle;
                Check(follow.AvoidObstacles.Enabled && obstacle != null
                    && Vector3.Distance(view.transform.position, control.Target.position) < follow.CameraDistance - 1f,
                    "Live scene camera pulls forward when its normal position lies behind a platform");
                probe.radius = follow.AvoidObstacles.CameraRadius - 0.02f;
                Check(!Physics.ComputePenetration(probe, view.transform.position, Quaternion.identity,
                    obstacle, obstacle.transform.position, obstacle.transform.rotation, out _, out _),
                    "Rendered camera and its clearance sphere remain outside the blocking structure");
                control.Target.position = new Vector3(100000f, 100f, 100000f);
                Physics.SyncTransforms();
            }
            else
            {
                Check(follow.CurrentObstacle == null
                    && Mathf.Abs(Vector3.Distance(view.transform.position, control.Target.position) - follow.CameraDistance) < 0.1f,
                    "Live camera returns to its full follow distance in open space");
                Debug.Log("CAMERA SCENE CHECKS COMPLETE");
                SessionState.SetBool(Pending, false);
                EditorApplication.Exit(0);
            }
            phase++;
            next = Time.time + 1.5f;
        }
        catch (Exception e) { SessionState.SetBool(Pending, false); Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
