using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Real-prefab, real-physics checks. Never run against the working project.
public static class MeleePushChecks
{
    const string Pending = "MeleePushChecks.Pending";
    static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    static int checks, frame = -1;
    static bool running;
    static double deadline;
    static GameObject player;
    static MechCombat combat;
    static EquipManager equipment;
    static void Check(bool valid, string message) { if (!valid) throw new Exception(message); checks++; Debug.Log("MELEE PUSH PASS: " + message); }
    [InitializeOnLoadMethod] static void Register() => EditorApplication.update += Tick;
    public static void RunBatch()
    {
        if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new Exception("Disposable project only");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (!running) { running = true; deadline = EditorApplication.timeSinceStartup + 300; PreMissionReliabilityChecks.BeginLogAudit(); steps.Push(Run()); }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Melee push timed out");
            while (steps.Count > 0) { var step = steps.Peek(); if (!step.MoveNext()) { steps.Pop(); continue; } if (step.Current is IEnumerator child) { steps.Push(child); continue; } return; }
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    static void Finish(Exception error)
    {
        running = false; SessionState.SetBool(Pending, false); PreMissionReliabilityChecks.EndLogAudit();
        if (error != null) Debug.LogException(error);
        Debug.Log("MELEE PUSH CHECKS " + (error == null ? "COMPLETE: " : "FAILED: ") + checks); EditorApplication.Exit(error == null ? 0 : 1);
    }
    static IEnumerator Frames(int count = 3) { int end = Time.frameCount + count; while (Time.frameCount < end) yield return null; }
    static IEnumerator Seconds(float seconds) { double end = Time.timeAsDouble + seconds; while (Time.timeAsDouble < end) yield return null; }
    static GameObject Enemy(string file = "Small Enemies/Enemy_sword", Vector3? offset = null)
    {
        var parent = new GameObject("Controlled push victim"); parent.SetActive(false);
        var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/" + file + ".prefab"), parent.transform);
        root.transform.position = player.transform.position + (offset ?? Vector3.forward * 3);
        var fields = new SerializedObject(root.GetComponent<EnemyAgent>()); fields.FindProperty("externalDecisions").boolValue = true; fields.ApplyModifiedPropertiesWithoutUndo();
        foreach (var script in root.GetComponentsInChildren<MonoBehaviour>(true))
            if (script is EnemyAttack || script is EnemyGraphAttack || script is Unity.Behavior.BehaviorGraphAgent || script is EnemyGraphController) script.enabled = false;
        foreach (var nav in root.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) nav.enabled = false;
        root.GetComponent<EnemyPartDrop>().enabled = false;
        parent.SetActive(true); root.GetComponent<CombatHealth>().SetMaximum(500, true); return root;
    }
    static void DestroyEnemy(GameObject root) => Object.Destroy(root.transform.parent.gameObject);
    static void ClearCorpses() { foreach (var corpse in Object.FindObjectsByType<CombatRagdoll>()) Object.DestroyImmediate(corpse.gameObject); }
    static DamageResult Hit(GameObject enemy, Part source = null, float damage = 10)
    {
        Physics.SyncTransforms();
        return new CombatBlast().ResolveBox(enemy.transform.position, Vector3.one, player.transform.rotation, damage,
            player.transform, ~0, player.transform.position + Vector3.up, source: source);
    }
    static void DropChance(GameObject enemy, float chance)
    {
        var drop = enemy.GetComponent<EnemyPartDrop>(); var fields = new SerializedObject(drop);
        fields.FindProperty("dropChancePercent").floatValue = chance; fields.FindProperty("partPrefabs").arraySize = 1;
        fields.FindProperty("partPrefabs").GetArrayElementAtIndex(0).objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/arm_sword.prefab").GetComponent<Part>();
        fields.ApplyModifiedPropertiesWithoutUndo(); drop.enabled = true;
    }
    static IEnumerator Run()
    {
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Physical floor"; floor.transform.position = new Vector3(0, -.5f, 0); floor.transform.localScale = new Vector3(300, 1, 300);
        player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SpecialistPlayerIntegrationSetup.PlayerPath), new Vector3(0, .1f, 0), Quaternion.identity);
        yield return Frames(6); player.GetComponent<MechPlayerInput>().enabled = false; player.GetComponent<MechMovementController>().enabled = false;
        player.GetComponent<MechTargetLockController>().enabled = false; player.GetComponent<CombatHealth>().Team = CombatTeam.Player;
        combat = player.GetComponent<MechCombat>(); equipment = player.GetComponent<EquipManager>();
        foreach (string file in new[] { "arm_sword", "arm_mace" })
        {
            var weapon = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/" + file + ".prefab")).GetComponent<Part>();
            var previous = equipment.GetEquipped(PartCategory.ArmL); Check(equipment.TryEquip(weapon, PartCategory.ArmL), file + " equips on the actual player");
            if (previous != null) Object.Destroy(previous.gameObject);
            var enemy = Enemy(); yield return Frames(4); var health = enemy.GetComponent<CombatHealth>(); var agent = enemy.GetComponent<EnemyAgent>();
            var colliders = enemy.GetComponentsInChildren<Collider>().ToDictionary(c => c, c => c.enabled);
            Check(combat.TryAttack(PartCategory.ArmL).Started && health.CurrentHealth < 500, file + " ordinary attack damages the real enemy");
            Check(agent.IsMeleePushed && Mathf.Abs(agent.MeleePushVelocity.z - 20f / 3f) < .01f && agent.MeleePushVelocity.y == 0, file + " uses the previous horizontal force with an upright grounded shove");
            Check(Object.FindObjectsByType<CombatRagdoll>().Length == 0 && enemy.GetComponentsInChildren<Rigidbody>().Length == 0, file + " surviving hit creates no ragdoll, physics copy or tumbling body");
            var initial = enemy.transform.position; var rotation = enemy.transform.rotation; yield return Seconds(.2f);
            Check(enemy.transform.position.z > initial.z + .5f && Quaternion.Angle(rotation, enemy.transform.rotation) < .01f, file + " actually moves in the strike direction without rotating");
            Check(enemy.GetComponent<MechTargetable>().isActiveAndEnabled && colliders.All(p => p.Key.enabled == p.Value), file + " preserves targetability and the existing hitboxes");
            yield return Seconds(.85f);
            Check(!agent.IsMeleePushed && agent.isActiveAndEnabled && health.isActiveAndEnabled, file + " ends the shove with the same living actor and motor");
            DestroyEnemy(enemy); yield return Frames();
        }
        var sword = equipment.GetEquipped(PartCategory.ArmL);
        player.transform.rotation = Quaternion.Euler(0, 90, 0);
        var victim = Enemy(offset: Vector3.right * 3); yield return Frames(4); Hit(victim, sword);
        Check(victim.GetComponent<EnemyAgent>().MeleePushVelocity.x > 6.6f && Mathf.Abs(victim.GetComponent<EnemyAgent>().MeleePushVelocity.z) < .01f, "Rotated strikes push in the actual captured strike direction");
        for (int i = 0; i < 6; i++) Hit(victim, sword);
        Check(victim.GetComponent<EnemyAgent>().MeleePushVelocity.magnitude <= 22.01f, "Repeated hits are bounded instead of stacking unlimited speed");
        victim.SetActive(false); victim.SetActive(true);
        Check(!victim.GetComponent<EnemyAgent>().IsMeleePushed && victim.GetComponent<EnemyAgent>().Velocity == Vector3.zero, "Pooling clears the shove and actor velocity");
        DestroyEnemy(victim); yield return Frames();

        player.transform.rotation = Quaternion.identity;
        victim = Enemy(); yield return Frames(4); var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = victim.transform.position + Vector3.forward * 2; wall.transform.localScale = new Vector3(12, 8, .2f);
        Hit(victim, sword); float wallZ = wall.transform.position.z; yield return Seconds(.8f);
        Check(victim.transform.position.z < wallZ - .5f && !victim.GetComponent<EnemyAgent>().IsMeleePushed, "The forced movement stops before a wall instead of tunnelling through it");
        Object.Destroy(wall); DestroyEnemy(victim); yield return Frames();

        victim = Enemy(); yield return Frames(4); var victimHealth = victim.GetComponent<CombatHealth>();
        victimHealth.Team = CombatTeam.Player;
        Check(Hit(victim, sword).Hits == 0 && !victim.GetComponent<EnemyAgent>().IsMeleePushed, "Rejected friendly damage applies no shove");
        victimHealth.Team = CombatTeam.Enemy;
        wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position = player.transform.position + Vector3.forward * 1.5f; wall.transform.localScale = new Vector3(8, 8, .2f);
        Check(Hit(victim, sword).Hits == 0 && !victim.GetComponent<EnemyAgent>().IsMeleePushed, "A wall-blocked swing applies neither damage nor force");
        Object.Destroy(wall); yield return Frames();
        Check(Hit(victim).Hits == 1 && !victim.GetComponent<EnemyAgent>().IsMeleePushed, "Other box damage does not accidentally gain melee force");
        var settings = new SerializedObject(combat); settings.FindProperty("meleePush").boolValue = false; settings.ApplyModifiedPropertiesWithoutUndo();
        Check(Hit(victim, sword).Hits == 1 && !victim.GetComponent<EnemyAgent>().IsMeleePushed, "Inspector switch preserves melee damage while disabling the shove");
        settings.FindProperty("meleePush").boolValue = true; settings.ApplyModifiedPropertiesWithoutUndo();
        DestroyEnemy(victim); yield return Frames();

        player.transform.rotation = Quaternion.Euler(0, 90, 0);
        victim = Enemy(offset: Vector3.right * 3); yield return Frames(4); DropChance(victim, 100); victimHealth = victim.GetComponent<CombatHealth>(); victimHealth.SetMaximum(1, true);
        int deaths = 0; victimHealth.Died += () => deaths++;
        var beforeParts = new HashSet<Part>(Object.FindObjectsByType<Part>());
        Check(Hit(victim, sword).Kills == 1 && deaths == 1, "Melee killing blow still reports exactly one death");
        var corpse = victim.GetComponent<CombatDeathRagdoll>().LastRagdoll; var loot = victim.GetComponent<EnemyPartDrop>().LastDrop;
        Check(corpse != null && corpse.PoseLocked && corpse.BodyCount == 1 && corpse.Bodies[0].constraints == RigidbodyConstraints.FreezeRotation,
            "Killing blow launches the whole model with locked rotation and no articulated tumble");
        var body = corpse.Bodies[0]; var bodyRotation = body.rotation; var bodyStart = body.position;
        Check(body.linearVelocity.x > 17.9f && body.linearVelocity.y > 6.9f && body.angularVelocity == Vector3.zero, "Killing launch retains the stronger 18 horizontal / 7 lift velocity without torque");
        Check(loot != null && loot.transform.parent == null && loot.GetComponent<Rigidbody>().linearVelocity == Vector3.up * 3 && corpse.HiddenLootRendererCount > 0,
            "Successful loot drops independently before launch and its corpse weapon duplicate is hidden");
        Check(Object.FindObjectsByType<Part>().Count(p => !beforeParts.Contains(p) && !p.isEquipped) == 1
            && victim.GetComponent<EnemyPartDrop>().ResolveDeathDrop() == loot, "Death callbacks cannot duplicate the successful drop");
        var lootStart = loot.transform.position; yield return Seconds(.2f);
        Check(body.position.x > bodyStart.x + 2 && body.position.y > bodyStart.y + .5f && Quaternion.Angle(bodyRotation, body.rotation) < .01f,
            "Launched body visibly travels horizontally/upward and retains its pose under real physics");
        Check(Vector3.ProjectOnPlane(loot.transform.position - lootStart, Vector3.up).magnitude < .2f, "Corpse cannot knock the pickup away with it");
        DestroyEnemy(victim); ClearCorpses(); yield return Frames();

        victim = Enemy(); yield return Frames(4); Hit(victim, sword); // Nonlethal hit must not leave a pending death override.
        victim.GetComponent<CombatHealth>().SetMaximum(1, true); Hit(victim);
        Check(victim.GetComponent<CombatDeathRagdoll>().LastRagdoll != null && !victim.GetComponent<CombatDeathRagdoll>().LastRagdoll.PoseLocked,
            "An unrelated later death retains its existing death presentation rather than a stale melee launch");
        DestroyEnemy(victim); ClearCorpses(); yield return Frames();

        foreach (string file in new[] { "Small Enemies/Enemy_crossbow", "Small Enemies/Enemy_mace", "Small Enemies/Enemy_Fodder", "Elite Enemies/Enemy_Elite_RockRope" })
        {
            victim = Enemy(file); yield return Frames(4); Physics.SyncTransforms();
            // Imported roles have different root-to-hitbox offsets; test accepted contact at the actual hurtbox.
            var hurtbox = victim.GetComponentsInChildren<Collider>().First(c => c.enabled && !c.isTrigger && c.GetComponentInParent<CombatHealth>() == victim.GetComponent<CombatHealth>());
            var contact = hurtbox.bounds.center;
            var result = new CombatBlast().ResolveBox(contact, Vector3.one, player.transform.rotation, 10, player.transform, ~0, contact, source: sword);
            Check(result.Hits == 1 && victim.GetComponent<EnemyAgent>().IsMeleePushed && Object.FindObjectsByType<CombatRagdoll>().Length == 0, file + " supports the same upright push without a temporary rig");
            DestroyEnemy(victim); yield return Frames();
        }
        victim = Enemy(); yield return Frames(4); var controller = victim.GetComponent<EnemyGraphController>(); controller.enabled = true; controller.Graph.enabled = true;
        yield return Frames(8); Hit(victim, sword); yield return Seconds(1);
        Check(controller.enabled && controller.Graph.enabled && controller.Graph.Graph.IsRunning && !victim.GetComponent<EnemyAgent>().IsMeleePushed,
            "The actual behavior graph remains usable after the shove without disable/restore cycles");
        DestroyEnemy(victim); yield return Frames();
    }
}
