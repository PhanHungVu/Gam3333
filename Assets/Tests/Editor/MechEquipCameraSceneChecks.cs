using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Actual shared player, physical dropped parts and live Cinemachine brain.
// No saved scene/prefab changes. Runs without graphics in a disposable project.
public static class MechEquipCameraSceneChecks
{
    const string Pending = "MechEquipCameraSceneChecks.Pending";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static IEnumerator sequence;
    static int lastFrame = -1, checks;
    static double deadline;
    [InitializeOnLoadMethod] static void Register() => EditorApplication.update += Step;
    public static void RunBatch()
    {
        string root = Directory.GetParent(Application.dataPath).FullName;
        if (!Application.isBatchMode || !Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Run only in a disposable batch project.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Check(bool valid, string message)
    { if (!valid) throw new Exception(message); checks++; Debug.Log("EQUIP CAMERA PASS: " + message); }
    static void Step()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling || lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try
        {
            if (sequence == null) { deadline = EditorApplication.timeSinceStartup + 120; sequence = Run(); }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Equip camera checks timed out");
            if (sequence.MoveNext()) return;
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    static void Finish(Exception error)
    {
        PreMissionReliabilityChecks.EndLogAudit(); Time.captureFramerate = 0;
        SessionState.SetBool(Pending, false);
        if (error != null) Debug.LogException(error);
        Debug.Log("EQUIP CAMERA CHECKS " + (error == null ? "COMPLETE: " : "FAILED: ") + checks);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
    static IEnumerator Run()
    {
        PreMissionReliabilityChecks.BeginLogAudit(); Application.runInBackground = true; Time.captureFramerate = 60;
        int layer = LayerMask.NameToLayer(Part.LoosePhysicsLayerName);
        Check(layer >= 8, "Project defines the dedicated MechPart user layer");
        Check(!Physics.GetIgnoreLayerCollision(0, layer), "Loose parts retain physical contact with the Default-layer world");
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.transform.position = new Vector3(0, -0.25f, 0); floor.transform.localScale = new Vector3(100, 0.5f, 100);
        var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PartSwapWorldUiSetup.PlayerPath), new Vector3(0, 1.6f, 0), Quaternion.identity);
        player.GetComponent<MechPlayerInput>().enabled = false;
        player.GetComponent<MechMovementController>().enabled = false;
        player.GetComponent<MechTargetLockController>().EnterFreeAim();
        var equipment = player.GetComponent<EquipManager>();
        var view = player.GetComponent<MechPlayerRig>().GameplayCamera;
        var control = view.GetComponent<CameraControlThirdPerson>();
        // Even a future all-layer camera mask must continue to ignore loose parts.
        typeof(CameraControlThirdPerson).GetField("obstacleLayers", Private).SetValue(control, (LayerMask)~0);
        var rig = (CinemachineCamera)typeof(CameraControlThirdPerson).GetField("cinemachineCamera", Private).GetValue(control);
        var follow = rig.GetComponent<CinemachineThirdPersonFollow>();
        for (int i = 0; i < 40; i++) yield return null;
        float baseline = Vector3.Distance(view.transform.position, player.transform.position);
        Check(baseline > 12.5f && control.ObstacleAvoidanceEnabled, "Live camera starts at normal third-person distance with avoidance enabled");
        Check((follow.AvoidObstacles.CollisionFilter.value & (1 << layer)) == 0, "Camera excludes only the pickup layer from its configured mask");
        string[] names = { "head_base", "arm_throwingaxe", "arm_sword", "Old/torso_base", "Old/legs_base" };
        PartCategory[] slots = { PartCategory.Head, PartCategory.ArmL, PartCategory.ArmR, PartCategory.Torso, PartCategory.Legs };
        for (int p = 0; p < names.Length; p++)
        {
            var incoming = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/" + names[p] + ".prefab"), new Vector3(3, 0.5f, 2), Quaternion.identity).GetComponent<Part>();
            incoming.isStartingPart = false;
            var previous = equipment.GetEquipped(slots[p]);
            Check(previous != null && equipment.TryEquip(incoming, slots[p]), "Real replacement succeeds for " + slots[p]);
            var ownColliders = previous.GetComponentsInChildren<Collider>(true).Where(c => c.GetComponentInParent<Part>() == previous).ToArray();
            if (ownColliders.Length > 0)
                Check(ownColliders.All(c => c.gameObject.layer == layer), "Dropped " + slots[p] + " collider children use MechPart");
            else Debug.Log("EQUIP CAMERA: starting " + slots[p] + " has no own authored collider; no collider is invented by the camera fix");
            var body = previous.GetComponent<Rigidbody>();
            Check(body != null && !body.isKinematic && body.useGravity && body.detectCollisions
                && (ownColliders.Length == 0 || ownColliders.Any(c => c.enabled)), "Dropped " + slots[p] + " retains its original loose-part physics");
            float minimum = float.MaxValue;
            for (int i = 0; i < 90; i++)
            {
                yield return null;
                minimum = Mathf.Min(minimum, Vector3.Distance(view.transform.position, player.transform.position));
                if (follow.CurrentObstacle != null && follow.CurrentObstacle.GetComponentInParent<Part>() != null)
                    throw new Exception("Loose part obstructed the camera: " + follow.CurrentObstacle);
            }
            Check(minimum >= baseline - 0.1f, slots[p] + " swap keeps normal framing throughout the physical drop (minimum " + minimum.ToString("F2") + ")");
        }
        var droppedHead = Object.FindObjectsByType<Part>().First(p => p.partType == PartType.Head && !p.isEquipped);
        Check(equipment.TryEquip(droppedHead, PartCategory.Head)
            && droppedHead.GetComponentsInChildren<Collider>(true).All(c => c.gameObject.layer == 0 && !c.enabled), "Reattachment restores the head's authored layers and disabled equipped colliders");
        // A loose part does not mask a real structure behind it.
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = player.transform.position + Vector3.back * 5; wall.transform.localScale = new Vector3(20, 20, 0.2f);
        Physics.SyncTransforms();
        for (int i = 0; i < 5; i++) yield return null;
        Check(follow.CurrentObstacle == wall.GetComponent<Collider>()
            && Vector3.Distance(view.transform.position, player.transform.position) < baseline - 2, "Actual wall still pulls the same camera forward while loose parts remain nearby");
        Object.Destroy(wall);
        for (int i = 0; i < 90; i++) yield return null;
        Check(follow.CurrentObstacle == null && Mathf.Abs(Vector3.Distance(view.transform.position, player.transform.position) - baseline) < 0.1f, "Clearing the wall restores normal framing");
        var custom = new GameObject("Custom-layer part"); custom.SetActive(false); custom.transform.position = new Vector3(4, 1, 3);
        var normalCollider = custom.AddComponent<BoxCollider>();
        var child = new GameObject("Authored EnemyBody collider"); child.transform.SetParent(custom.transform, false); child.layer = LayerMask.NameToLayer("EnemyBody");
        var customCollider = child.AddComponent<BoxCollider>();
        var customPart = custom.AddComponent<Part>(); customPart.data = droppedHead.data; customPart.isStartingPart = false;
        custom.SetActive(true);
        Check(normalCollider.gameObject.layer == layer && customCollider.gameObject.layer == layer, "Every loose collider uses the non-blocking pickup layer");
        Check(equipment.TryEquip(customPart, PartCategory.Head) && normalCollider.gameObject.layer == 0
            && customCollider.gameObject.layer == LayerMask.NameToLayer("EnemyBody"), "Attachment restores Default without changing custom child layers");
        equipment.Unequip(PartCategory.Head);
        Check(normalCollider.gameObject.layer == layer && normalCollider.enabled && customCollider.enabled
            && customCollider.gameObject.layer == layer, "Repeated drop restores colliders on the non-blocking pickup layer");
    }
}
