using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Real CharacterController movement, pickup triggers, enemy probes and world contacts.
public static class DroppedPartCollisionChecks
{
    const string Pending = "DroppedPartCollisionChecks.Pending";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    static int frame = -1, checks;
    static bool running;
    static double deadline;
    [InitializeOnLoadMethod] static void Register() => EditorApplication.update += Tick;
    public static void RunBatch()
    {
        if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Disposable project only.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Check(bool value, string message)
    { if (!value) throw new Exception(message); checks++; Debug.Log("DROP COLLISION PASS: " + message); }
    static IEnumerator Frames(int count = 6)
    { int end = Time.frameCount + count; while (Time.frameCount < end) yield return null; }
    static void Tick()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (!running)
            {
                running = true; deadline = EditorApplication.timeSinceStartup + 180;
                PreMissionReliabilityChecks.BeginLogAudit(); steps.Push(Run());
            }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Dropped part checks timed out.");
            while (steps.Count > 0)
            {
                var step = steps.Peek();
                if (!step.MoveNext()) { steps.Pop(); continue; }
                if (step.Current is IEnumerator nested) { steps.Push(nested); continue; }
                return;
            }
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    static void Finish(Exception error)
    {
        SessionState.SetBool(Pending, false); PreMissionReliabilityChecks.EndLogAudit(); Time.captureFramerate = 0;
        if (error != null) Debug.LogException(error);
        Debug.Log("DROP COLLISION CHECKS " + (error == null ? "COMPLETE: " : "FAILED: ") + checks);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
    static GameObject Box(string name, Vector3 position, Vector3 size)
    {
        var node = GameObject.CreatePrimitive(PrimitiveType.Cube); node.name = name;
        node.transform.position = position; node.transform.localScale = size; return node;
    }
    static Part LooseBox(Vector3 position)
    {
        var node = new GameObject("Loose compound head"); node.SetActive(false); node.transform.position = position;
        node.AddComponent<BoxCollider>().size = new Vector3(2, 10, 4);
        var child = new GameObject("Originally EnemyBody collider"); child.transform.SetParent(node.transform, false);
        child.layer = LayerMask.NameToLayer("EnemyBody"); child.AddComponent<BoxCollider>().size = Vector3.one * .2f;
        var part = node.AddComponent<Part>(); part.isStartingPart = false;
        part.data = AssetDatabase.LoadAssetAtPath<PartData>("Assets/PartCatalogue/PartData_Head.asset");
        node.SetActive(true); node.GetComponent<Rigidbody>().constraints = RigidbodyConstraints.FreezeAll;
        return part;
    }
    static IEnumerator Clear()
    {
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()) Object.Destroy(root);
        yield return Frames();
    }
    static IEnumerator Run()
    {
        Application.runInBackground = true; Time.timeScale = 1; Time.captureFramerate = 60;
        foreach (var variant in new[] { "golem", "heavy" })
        {
            Box("World floor", new Vector3(0, -.25f, 0), new Vector3(100, .5f, 100));
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/FullPlayer_mech_" + variant + ".prefab"), new Vector3(0, 3, 0), Quaternion.identity);
            player.GetComponent<MechPlayerInput>().enabled = false;
            player.GetComponent<MechMovementController>().enabled = false;
            var controller = player.GetComponent<CharacterController>();
            var part = LooseBox(new Vector3(3, controller.bounds.center.y, 0));
            yield return Frames(12); Physics.SyncTransforms();
            var detector = player.GetComponentInChildren<PickupDetector>();
            Check(detector.NearbyParts.Contains(part) && player.GetComponent<EquipManager>().GetSelectedPart() == part,
                variant + " detects and selects a loose part through the real pickup trigger");
            controller.Move(Vector3.right * 6);
            Check(player.transform.position.x > 5.8f, variant + " walks through solid compound loot without obstruction");
            Box("World wall", new Vector3(10, 3, 0), new Vector3(1, 20, 20)); Physics.SyncTransforms();
            controller.Move(Vector3.right * 12);
            Check(player.transform.position.x < 9.6f && player.transform.position.x > 6,
                variant + " still collides with world walls");
            var equipment = player.GetComponent<EquipManager>();
            Check(equipment.TryEquip(part, PartCategory.Head) && part.GetComponent<Collider>().gameObject.layer == 0
                && part.transform.GetChild(0).gameObject.layer == LayerMask.NameToLayer("EnemyBody"), "Equipping restores each authored collider layer");
            equipment.Unequip(PartCategory.Head);
            Check(part.GetComponentsInChildren<Collider>().All(c => c.enabled && c.gameObject.layer == LayerMask.NameToLayer(Part.LoosePhysicsLayerName)),
                "Swapping a part back onto the ground reapplies non-blocking physics");
            part.GetComponent<BoxCollider>().size = Vector3.one * .5f;
            part.transform.position = new Vector3(20, 3, 0);
            var body = part.GetComponent<Rigidbody>(); body.constraints = RigidbodyConstraints.None; body.linearVelocity = Vector3.zero;
            yield return Frames(150);
            Check(part.transform.position.y > .1f && part.transform.position.y < .6f,
                variant + " drop still falls and settles on Default-layer terrain");
            yield return Clear();
        }
        Box("Enemy test floor", new Vector3(0, -.25f, 0), new Vector3(100, .5f, 100));
        var loot = LooseBox(new Vector3(3, 2, 0));
        var enemy = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Small Enemies/Enemy_mace.prefab"), new Vector3(0, 1.6f, 0), Quaternion.identity);
        enemy.GetComponent<EnemyGraphController>().enabled = false;
        enemy.GetComponent<Unity.Behavior.BehaviorGraphAgent>().enabled = false;
        enemy.GetComponent<EnemyAttack>().enabled = false;
        enemy.GetComponent<EnemyPartDrop>().enabled = false;
        var agent = enemy.GetComponent<EnemyAgent>(); agent.enabled = false;
        yield return Frames(); Physics.SyncTransforms();
        Check(!agent.TryProbe(Vector3.right, 6, out _), "An enemy spawned after loot does not treat it as a movement obstacle");
        var wall = Box("Wall behind loot", new Vector3(10, 3, 0), new Vector3(1, 20, 20)); Physics.SyncTransforms();
        Check(agent.TryProbe(Vector3.right, 12, out var hit) && hit.collider == wall.GetComponent<Collider>(), "Enemy probing still finds the wall behind ignored loot");
        typeof(EnemyAgent).GetMethod("Move", Private).Invoke(agent, new object[] { Vector3.right * 6 });
        Check(enemy.transform.position.x > 5.8f, "Existing enemy movement passes through dropped parts");
        var groundArgs = new object[] { new Vector3(3, 8, 0), 20f, default(RaycastHit) };
        bool grounded = (bool)typeof(EnemyAgent).GetMethod("RaycastGroundIgnoringSelf", Private).Invoke(agent, groundArgs);
        Check(grounded && Mathf.Abs(((RaycastHit)groundArgs[2]).point.y) < .01f, "Enemy ground sampling uses the floor beneath loot");
        loot.GetComponent<BoxCollider>().size = Vector3.one * .5f;
        loot.transform.position = enemy.transform.position + Vector3.up * 5;
        loot.GetComponent<Rigidbody>().constraints = RigidbodyConstraints.None;
        yield return Frames(180);
        if (loot.transform.position.y <= .1f || loot.transform.position.y >= .6f)
            foreach (var collider in enemy.GetComponentsInChildren<Collider>())
                if (collider.enabled && !collider.isTrigger)
                    Debug.Log("DROP BODY: " + collider.name + " part=" + collider.GetComponentInParent<Part>()
                        + " excluded=" + collider.excludeLayers.value + " bounds=" + collider.bounds);
        Check(loot.transform.position.y > .1f && loot.transform.position.y < .6f,
            "Falling loot passes through an enemy's physical body and rests on the floor (position " + loot.transform.position + ")");
    }
}
