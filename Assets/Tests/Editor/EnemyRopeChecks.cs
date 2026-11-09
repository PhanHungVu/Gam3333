using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static partial class EnemyBehaviorChecks
{
    public static void RunRopeBatch()
    {
        string root = Directory.GetParent(Application.dataPath).FullName;
        if (!Application.isBatchMode || !Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Disposable project only.");
        EnemyRosterSetup.UpgradeAssets(); EnemyRosterSetup.UpgradeAssets();
        SessionState.SetBool(Pending + ".PullOnly", false);
        SessionState.SetBool(Pending + ".Arena", false); SessionState.SetBool(Pending + ".Rope", true);
        GraphicsSettings.defaultRenderPipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset"); QualitySettings.renderPipeline = null;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    public static void RunPullOnlyBatch()
    {
        RunRopeBatch();
        SessionState.SetBool(Pending + ".PullOnly", true);
    }
    static Transform RopePart(EnemyRopeAttack attack, string name) => (Transform)new SerializedObject(attack).FindProperty(name).objectReferenceValue;
    static Vector3 HeldRock(EnemyRopeAttack attack) => RopePart(attack, "ropeAnchor").position
        + attack.transform.rotation * new SerializedObject(attack).FindProperty("heldRockOffset").vector3Value;
    static LineRenderer RopeLine(EnemyRopeAttack attack) => attack.transform.Find("Rock rope").GetComponent<LineRenderer>();
    static bool TetherAttached(EnemyRopeAttack attack)
    {
        var line = RopeLine(attack); var rock = RopePart(attack, "rock");
        return Vector3.Distance(line.GetPosition(0), RopePart(attack, "ropeAnchor").position) < .005f
            && Vector3.Distance(line.GetPosition(line.positionCount - 1), rock.position) <= .451f;
    }
    static IEnumerator RunRope()
    {
        PreMissionReliabilityChecks.BeginLogAudit(); Application.runInBackground = true; Time.captureFramerate = 60; QualitySettings.vSyncCount = 0;
        captures = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "EnemyRopeCaptures"); Directory.CreateDirectory(captures);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.position = new Vector3(0, -.25f, 0); floor.transform.localScale = new Vector3(240, .5f, 240);
        var light = new GameObject("Rope check sunlight").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.5f; light.transform.rotation = Quaternion.Euler(55, -30, 0);
        player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PartSwapWorldUiSetup.PlayerPath), new Vector3(0, 1.6f, 0), Quaternion.identity);
        player.GetComponent<MechPlayerInput>().enabled = false; player.GetComponent<MechMovementController>().enabled = false;
        player.GetComponent<MechTargetLockController>().EnterFreeAim();
        var navigation = new GameObject("Shared rope navigation"); grid = navigation.AddComponent<NavGrid>(); field = navigation.AddComponent<FlowField>();
        yield return Frames(4); health = player.GetComponent<CombatHealth>(); health.SetMaximum(10000, true);

        var actor = SpawnRole(8, 80); var attack = actor.GetComponent<EnemyRopeAttack>(); yield return Frames(12);
        var rock = RopePart(attack, "rock"); var art = rock.GetComponentInChildren<MeshRenderer>();
        Check(rock.name == "Rope rock pivot" && actor.GetComponentsInChildren<Transform>(true).Count(t => t.name == "Rock art proxy") == 1,
            "Repeated roster setup keeps one centered rope-rock proxy");
        var meshSize = Vector3.Scale(art.GetComponent<MeshFilter>().sharedMesh.bounds.size, art.transform.lossyScale);
        Check((rock.lossyScale - Vector3.one).sqrMagnitude < .00001f && Mathf.Abs(Mathf.Max(meshSize.x, meshSize.y, meshSize.z) - 1.2f) < .001f
            && Vector3.Distance(art.bounds.center, rock.position) < .001f && rock.GetComponentsInChildren<Collider>(true).All(c => !c.enabled),
            "The rock is centered, fits its collision radius and does not inherit the elite's stretched body scale");
        bool held = true;
        foreach (float yaw in new[] { 0f, 45f, 135f, 260f })
        {
            actor.transform.rotation = Quaternion.Euler(0, yaw, 0); actor.transform.position += Vector3.right;
            yield return Frames(2);
            held &= Vector3.Distance(rock.position, HeldRock(attack)) < .005f && TetherAttached(attack)
                && Vector3.Distance(art.bounds.center, rock.position) < .001f;
        }
        Check(held, "Walking and turning keep the idle rock and rope attached to the visible rig"); Capture("rope-idle", actor);
        yield return Remove(actor);

        ResetPlayer(); actor = SpawnRole(8, 18); attack = actor.GetComponent<EnemyRopeAttack>();
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Windup, 4, "Rope windup missing");
        yield return Frames(30); Check(TetherAttached(attack) && attack.Impacts == 0, "The windup lifts the rock while its tether remains on the rig"); Capture("rope-windup", actor);
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Attack, 3, "Rope flight missing");
        yield return Frames(18); Check(TetherAttached(attack), "The flight tether joins the rig to the centered airborne rock"); Capture("rope-flight", actor);
        float hp = health.CurrentHealth;
        yield return Until(() => attack.Impacts == 1, 3, "Rope impact missing");
        Check(health.CurrentHealth == hp - attack.Damage, "Landing still applies exactly one shared-rule damage hit");
        var landed = attack.LandedPosition; player.transform.position += Vector3.right * 8; Physics.SyncTransforms();
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Pull, 2, "Rope pull missing");
        float distance = Steering.Flat(actor.transform.position - landed).magnitude; bool fixedRock = true;
        for (int i = 0; i < 45; i++)
        {
            yield return Frames(1);
            fixedRock &= Vector3.Distance(RopePart(attack, "rock").position, landed) < .005f && TetherAttached(attack);
        }
        Check(fixedRock && Steering.Flat(actor.transform.position - landed).magnitude < distance - 4,
            "The rock stays fixed as the elite pulls along the taut tether instead of tracking a moved player"); Capture("rope-pull", actor);
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Recovery, 4, "Rope recovery missing");
        Check(Steering.Flat(actor.transform.position - landed).magnitude >= 1.98f && Steering.Flat(actor.transform.position - landed).magnitude < 2.2f,
            "Pull stops at its two-unit clearance without overshooting the rock; distance=" + Steering.Flat(actor.transform.position - landed).magnitude);
        yield return Frames(52); Check(Vector3.Distance(RopePart(attack, "rock").position, HeldRock(attack)) < .005f && TetherAttached(attack),
            "Recovery returns the rock to its attached held position"); Capture("rope-recovered", actor);
        player.SetActive(false); yield return Frames(5);
        Check(attack.CurrentStage == EnemyGraphAttack.Stage.Idle && !RopeLine(attack).enabled
            && Vector3.Distance(RopePart(attack, "rock").position, HeldRock(attack)) < .005f,
            "Losing the target during recovery cancels the abandoned rope instead of leaving it stretched out; stage=" + attack.CurrentStage
                + "; visible=" + RopeLine(attack).enabled + "; held gap=" + Vector3.Distance(RopePart(attack, "rock").position, HeldRock(attack)));
        player.SetActive(true); yield return Remove(actor);

        foreach (var stage in new[] { EnemyGraphAttack.Stage.Windup, EnemyGraphAttack.Stage.Attack, EnemyGraphAttack.Stage.Pull, EnemyGraphAttack.Stage.Recovery })
        {
            ResetPlayer(); actor = SpawnRole(8, 18); attack = actor.GetComponent<EnemyRopeAttack>();
            yield return Until(() => attack.CurrentStage == stage, 8, "Interruption stage missing: " + stage);
            int impacts = attack.Impacts; hp = health.CurrentHealth; actor.Graph.enabled = false; yield return Frames(70);
            Check(attack.CurrentStage == EnemyGraphAttack.Stage.Idle && attack.Impacts == impacts && health.CurrentHealth == hp
                && !RopeLine(attack).enabled && Vector3.Distance(RopePart(attack, "rock").position, HeldRock(attack)) < .005f,
                "Graph cancellation restores the held rock, stops damage and keeps the rope hidden: " + stage);
            if (stage == EnemyGraphAttack.Stage.Pull)
            {
                actor.gameObject.SetActive(false); yield return Frames(2); actor.gameObject.SetActive(true); yield return Frames(2);
                actor.Graph.enabled = true;
                yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Windup, 10, "Pooled rope did not restart");
                yield return Frames(3); Check(TetherAttached(attack) && RopeLine(attack).enabled, "Re-enabled actors start a fresh attached rope attack");
            }
            yield return Remove(actor);
        }

        ResetPlayer();
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position = new Vector3(0, 6, 9); wall.transform.localScale = new Vector3(10, 12, 1);
        Physics.SyncTransforms(); actor = SpawnRole(8, 18); attack = actor.GetComponent<EnemyRopeAttack>(); hp = health.CurrentHealth;
        yield return Until(() => attack.Impacts == 1, 5, "Wall interception missing");
        Check(attack.LandedPosition.y > 4 && attack.LandedPosition.z > 9.5f && health.CurrentHealth == hp,
            "A wall intercept preserves the airborne contact height and blocks damage through the wall"); Capture("rope-wall-impact", actor);
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Recovery, 4, "Wall pull did not stop");
        Check(actor.transform.position.z > 10 && attack.Impacts == 1, "The pull stops on the near side of the blocking wall");
        yield return Remove(actor); Object.Destroy(wall); yield return Frames(2);

        ResetPlayer();
        var platform = GameObject.CreatePrimitive(PrimitiveType.Cube); platform.transform.position = new Vector3(0, 1.5f, 0); platform.transform.localScale = new Vector3(8, 3, 8);
        player.transform.position = new Vector3(0, 4.6f, 0); Physics.SyncTransforms(); actor = SpawnRole(8, 18); actor.transform.position = new Vector3(0, 1, 18); attack = actor.GetComponent<EnemyRopeAttack>();
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Windup, 4, "Raised-floor windup missing"); yield return Frames(3);
        var warning = actor.transform.Find("Attack warning").GetComponent<LineRenderer>();
        Check(Mathf.Abs(attack.LandedPosition.y - 3.6f) < .01f && Mathf.Abs(warning.GetPosition(0).y - 3.06f) < .01f,
            "The landing target and telegraph sample the player's raised floor instead of the thrower's floor");
        yield return Until(() => attack.Impacts == 1, 4, "Raised-floor impact missing");
        Check(Mathf.Abs(attack.LandedPosition.y - 3.6f) < .05f, "The rock lands on the elevated surface without a vertical snap"); Capture("rope-raised-floor", actor);
        yield return Remove(actor); Object.Destroy(platform); yield return Frames(2);

        ResetPlayer(); actor = SpawnRole(8, 18); attack = actor.GetComponent<EnemyRopeAttack>();
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Attack, 4, "Death cancellation flight missing"); hp = health.CurrentHealth;
        actor.GetComponent<CombatHealth>().ApplyDamage(10000); yield return Frames(90);
        Check(!actor.gameObject.activeSelf && attack.Impacts == 0 && health.CurrentHealth == hp
            && !RopePart(attack, "rock").gameObject.activeInHierarchy && !RopeLine(attack).enabled,
            "Enemy death cancels the flight and leaves no active rock or tether behind");
        yield return Remove(actor);

        ResetPlayer(); player.transform.position += Vector3.right * 30; Physics.SyncTransforms();
        actor = SpawnRole(8, 18); attack = actor.GetComponent<EnemyRopeAttack>();
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Attack, 4, "Initial-overlap flight missing");
        var launch = RopePart(attack, "rock").position;
        var obstruction = GameObject.CreatePrimitive(PrimitiveType.Cube); obstruction.transform.position = launch; obstruction.transform.localScale = Vector3.one * 4;
        Physics.SyncTransforms();
        yield return Until(() => attack.Impacts == 1, 2, "Initial-overlap interception missing");
        Check(Vector3.Distance(attack.LandedPosition, launch) < 1,
            "A rock already overlapping scenery stops at its launch point instead of jumping to world origin: " + attack.LandedPosition);
        yield return Remove(actor); Object.Destroy(obstruction); yield return Frames(2);
        if (!SessionState.GetBool(Pending + ".PullOnly", false)) yield return OrbitCases();
    }
}
