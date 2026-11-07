using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class MeleePhysicsChecks
{
    const string Pending = "MeleePhysicsChecks.Pending";
    static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    static int checks, frame = -1; static bool running; static double deadline;
    static GameObject player; static MechCombat combat; static EquipManager equipment; static Camera camera;
    static void Check(bool valid, string message) { if (!valid) throw new Exception(message); checks++; Debug.Log("MELEE PHYSICS PASS: " + message); }
    [InitializeOnLoadMethod] static void Register() => EditorApplication.update += Tick;
    public static void RunBatch()
    {
        if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new Exception("Disposable project only");
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
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Melee physics timed out");
            while (steps.Count > 0) { var step = steps.Peek(); if (!step.MoveNext()) { steps.Pop(); continue; } if (step.Current is IEnumerator child) { steps.Push(child); continue; } return; }
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    static IEnumerator Frames(int count = 3) { int end = Time.frameCount + count; while (Time.frameCount < end) yield return null; }
    static IEnumerator Seconds(float seconds) { double end = Time.timeAsDouble + seconds; while (Time.timeAsDouble < end) yield return null; }
    static IEnumerator Recover(EnemyMeleeReaction reaction)
    {
        double end = Time.timeAsDouble + 4;
        while (reaction != null && reaction.IsKnockedDown) { if (Time.timeAsDouble > end) throw new Exception("Enemy did not recover"); yield return null; }
        yield return Frames();
    }
    static IEnumerator Player(float x)
    {
        player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SpecialistPlayerIntegrationSetup.PlayerPath), new Vector3(x, .1f, 0), Quaternion.identity);
        yield return Frames(6); player.GetComponent<MechPlayerInput>().enabled = false; player.GetComponent<MechMovementController>().enabled = false;
        player.GetComponent<MechTargetLockController>().enabled = false; player.GetComponent<CombatHealth>().Team = CombatTeam.Player;
        combat = player.GetComponent<MechCombat>(); equipment = player.GetComponent<EquipManager>();
        camera = player.GetComponent<MechPlayerRig>().GameplayCamera; camera.GetComponent<CameraControlThirdPerson>().enabled = false;
        camera.GetComponent<Unity.Cinemachine.CinemachineBrain>().enabled = false;
    }
    static GameObject Enemy(string file = "Small Enemies/Enemy_sword", Vector3? offset = null)
    {
        var parent = new GameObject("Controlled melee victim"); parent.SetActive(false);
        var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/" + file + ".prefab"), parent.transform);
        root.transform.position = new Vector3(player.transform.position.x, 0, 0) + (offset ?? Vector3.forward * 3);
        var fields = new SerializedObject(root.GetComponent<EnemyAgent>()); fields.FindProperty("externalDecisions").boolValue = true; fields.ApplyModifiedPropertiesWithoutUndo();
        foreach (var script in root.GetComponentsInChildren<MonoBehaviour>(true))
            if (script is EnemyAttack || script is EnemyGraphAttack || script is Unity.Behavior.BehaviorGraphAgent || script is EnemyGraphController) script.enabled = false;
        foreach (var nav in root.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) nav.enabled = false;
        root.GetComponent<EnemyPartDrop>().enabled = false;
        parent.SetActive(true); root.GetComponent<CombatHealth>().SetMaximum(500, true); return root;
    }
    static void DestroyEnemy(GameObject root) { if (root != null) Object.Destroy(root.transform.parent != null ? root.transform.parent.gameObject : root); }
    static void ClearBodies() { foreach (var corpse in Object.FindObjectsByType<CombatRagdoll>()) corpse.Dispose(); }
    static void Hit(GameObject enemy, Part source, Quaternion? rotation = null, float damage = 10)
    {
        var reaction = enemy.GetComponent<EnemyMeleeReaction>();
        Vector3 center = reaction != null && reaction.Ragdoll != null ? reaction.Ragdoll.CoreBody.worldCenterOfMass : enemy.transform.position;
        Physics.SyncTransforms(); new CombatBlast().ResolveBox(center, Vector3.one, rotation ?? player.transform.rotation, damage,
            player.transform, ~0, player.transform.position + Vector3.up, source: source);
    }
    static Part Drop(string file, Vector3 offset)
    {
        var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/" + file + ".prefab"));
        var part = root.GetComponent<Part>(); part.isStartingPart = false;
        root.transform.position = player.transform.position + offset; root.GetComponent<Rigidbody>().isKinematic = true; Physics.SyncTransforms();
        float bottom = root.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger && c.GetComponentInParent<Part>() == part).Min(c => c.bounds.min.y);
        root.transform.position += Vector3.up * (.08f - bottom); Physics.SyncTransforms(); return part;
    }
    static IEnumerator Run()
    {
        Time.timeScale = 1; Time.maximumDeltaTime = .05f; Application.runInBackground = true;
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.position = Vector3.down * .25f; floor.transform.localScale = new Vector3(650, .5f, 300);
        var light = new GameObject("Sun").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(55, -30, 0);
        yield return Player(0);
        var enemy = Enemy(); yield return Frames(4);
        var health = enemy.GetComponent<CombatHealth>(); var agent = enemy.GetComponent<EnemyAgent>(); var targetable = enemy.GetComponent<MechTargetable>();
        var rendererStates = enemy.GetComponentsInChildren<Renderer>(true).ToDictionary(r => r, r => r.enabled);
        var colliderStates = enemy.GetComponentsInChildren<Collider>(true).ToDictionary(c => c, c => c.enabled);
        var disabledAttack = enemy.GetComponent<EnemyAttack>();
        var sword = equipment.GetEquipped(PartCategory.ArmL);
        Check(combat.TryAttack(PartCategory.ArmL).Started, "Real equipped sword starts its ordinary melee attack");
        var reaction = enemy.GetComponent<EnemyMeleeReaction>(); var proxy = reaction != null ? reaction.Ragdoll : null;
        Check(health.CurrentHealth == 500 - sword.Stats.GetStat(StatType.Damage) && reaction != null && reaction.IsKnockedDown && proxy != null,
            "An accepted surviving sword hit creates temporary physical knockdown without changing damage");
        Check(!agent.enabled && !disabledAttack.enabled && health.isActiveAndEnabled && targetable.isActiveAndEnabled,
            "Knockdown pauses enemy control without disabling the living health or lock target");
        Check(proxy.IsTemporary && proxy.BodyCount > 1 && proxy.BodyCount <= 14 && proxy.GetComponentsInChildren<ConfigurableJoint>().Length == proxy.BodyCount - 1,
            "Actual enemy model uses the existing connected multi-body physics rig");
        Check(proxy.GetComponentsInChildren<CombatHealth>().Length == 0 && proxy.GetComponentsInChildren<Part>().Length == 0
            && proxy.Bodies.All(b => b.GetComponentInParent<CombatHealth>() == health && b.gameObject.layer != CombatRagdoll.CorpseLayer),
            "Physical hitboxes resolve to the same living enemy; the copy creates no extra health or equipment");
        Check(proxy.Bodies.All(b => Mathf.Abs(b.linearVelocity.z - 20f / 3f) < .01f && Mathf.Abs(b.linearVelocity.y - 2f) < .01f),
            "Surviving sword hit applies two-thirds of the original push and lift in the captured strike direction");
        Vector3 start = enemy.transform.position;
        yield return Seconds(.2f);
        Debug.Log("MELEE MOTION OBSERVATION start=" + start.ToString("F3") + "; root=" + enemy.transform.position.ToString("F3")
            + "; core=" + proxy.CoreBody.position.ToString("F3") + "; renderedCore=" + proxy.CoreBody.transform.position.ToString("F3")
            + "; aim=" + targetable.AimPosition.ToString("F3") + "; velocity=" + proxy.CoreBody.linearVelocity.ToString("F3"));
        Capture("surviving-melee", enemy.transform.position);
        Check(enemy.transform.position.z > start.z + .8f && Vector3.Distance(targetable.AimPosition, proxy.AimPoint.position) < .01f,
            "Logical enemy position and lock aim follow the physically moving body");
        Check(proxy.Bodies.All(b => b.transform.lossyScale == Vector3.one && b.position.y > -3 && !float.IsNaN(b.position.x)),
            "Imported model scales stay out of the physics proxies and remain stable");
        Physics.SyncTransforms();
        Check(Physics.OverlapSphere(proxy.CoreBody.position, 2, ~0, QueryTriggerInteraction.Ignore).Any(c => c.GetComponentInParent<CombatHealth>() == health),
            "A downed enemy remains hittable through its visible physical body");
        Capture("surviving-melee", enemy.transform.position);
        float before = health.CurrentHealth; Hit(enemy, sword);
        Check(health.CurrentHealth == before - 10 && reaction.ImpactCount == 2 && reaction.Ragdoll == proxy && proxy.Bodies.All(b => b.linearVelocity.magnitude <= 22.01f),
            "Another melee hit reuses the same rig, hits once and bounds combined impulses");
        var witnessCorpse = CombatRagdoll.Spawn(player.transform, Vector3.zero);
        yield return Recover(reaction);
        Check(reaction.RecoveryCount == 1 && agent.enabled && EnemyAgent.All.Contains(agent) && !disabledAttack.enabled && health.CurrentHealth == before - 10,
            "Recovery returns the original enemy to its existing AI list and preserves health and previously disabled attacks");
        Check(rendererStates.All(p => p.Key == null || p.Key.enabled == p.Value) && colliderStates.All(p => p.Key == null || p.Key.enabled == p.Value)
            && reaction.Ragdoll == null && Mathf.Abs(enemy.transform.position.y - agent.PivotHeight) < .04f,
            "Recovery restores the original renderer/collider states and stands on terrain at the new position");
        Check(witnessCorpse != null && Physics.GetIgnoreCollision(witnessCorpse.Bodies[0].GetComponent<Collider>(),
            colliderStates.First(p => p.Value && !p.Key.isTrigger).Key), "Recovered enemy colliders still ignore persistent corpse physics");
        witnessCorpse.Dispose();
        DestroyEnemy(enemy); yield return Frames();
        yield return Seconds(Mathf.Max(0, sword.ReadyAt - Time.time) + .05f);
        player.transform.rotation = Quaternion.Euler(0, 90, 0);
        enemy = Enemy(offset: Vector3.right * 3); yield return Frames(4);
        Check(combat.TryAttack(PartCategory.ArmL).Started && enemy.GetComponent<EnemyMeleeReaction>().Ragdoll.Bodies.All(b => Mathf.Abs(b.linearVelocity.x - 20f / 3f) < .01f && Mathf.Abs(b.linearVelocity.z) < .01f),
            "Rotating the attacker rotates the push with the actual melee strike");
        DestroyEnemy(enemy); yield return Frames(); yield return Seconds(Mathf.Max(0, sword.ReadyAt - Time.time) + .05f);
        yield return KillingBlows();
        yield return ReturningWeapons();
        yield return CleanupAndRules();
        yield return BudgetsAndLiveGraph();
    }
    static IEnumerator KillingBlows()
    {
        var enemy = Enemy(offset: Vector3.right * 3); yield return Frames(4); var health = enemy.GetComponent<CombatHealth>(); health.SetMaximum(1, true);
        var drop = enemy.GetComponent<EnemyPartDrop>(); var fields = new SerializedObject(drop);
        fields.FindProperty("dropChancePercent").floatValue = 100; fields.FindProperty("partPrefabs").arraySize = 1;
        fields.FindProperty("partPrefabs").GetArrayElementAtIndex(0).objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/arm_sword.prefab").GetComponent<Part>(); fields.ApplyModifiedPropertiesWithoutUndo();
        int deaths = 0; bool dropBeforeLaunch = false;
        // Death-ragdoll listener is first, this observer second, loot's own listener last.
        health.Died += () => { deaths++; dropBeforeLaunch = drop.LastDrop != null && enemy.GetComponent<CombatDeathRagdoll>().LastRagdoll != null
            && enemy.GetComponent<CombatDeathRagdoll>().LastRagdoll.Bodies.All(b => b.linearVelocity.x < 17); };
        drop.enabled = true;
        var parts = new HashSet<Part>(Object.FindObjectsByType<Part>());
        Check(combat.TryAttack(PartCategory.ArmL).Started && health.IsDead, "Ordinary sword killing blow still kills through CombatHealth");
        var corpse = enemy.GetComponent<CombatDeathRagdoll>().LastRagdoll;
        Check(corpse != null && !corpse.IsTemporary && corpse.Bodies.All(b => b.linearVelocity.x > 17 && b.linearVelocity.y > 6.9f),
            "Killing blow launches the corpse much harder and higher in the strike direction");
        Check(deaths == 1 && Object.FindObjectsByType<Part>().Count(p => !parts.Contains(p) && !p.isEquipped) == 1,
            "Launch preserves exactly one death event and the normal guaranteed part drop");
        var loot = drop.LastDrop;
        Check(dropBeforeLaunch && loot != null && loot.transform.parent == null && loot.GetComponent<Rigidbody>().linearVelocity == Vector3.up * 3,
            "Loot is resolved at death before the melee launch, without inheriting body velocity or parenting");
        int lootLayer = LayerMask.NameToLayer(Part.LoosePhysicsLayerName);
        Check(corpse.HiddenLootRendererCount > 0 && drop.DroppedCarriedPart != null && corpse.Bodies.All(b => (b.GetComponent<Collider>().excludeLayers & (1 << lootLayer)) != 0),
            "Dropped weapon's corpse copy is hidden and every ragdoll collider excludes loose loot contacts");
        Check(drop.ResolveDeathDrop() == loot && drop.ResolveDeathDrop() == loot && Object.FindObjectsByType<Part>().Count(p => !parts.Contains(p) && !p.isEquipped) == 1,
            "Repeated death resolution cannot reroll or duplicate the successful drop");
        Vector3 lootStart = loot.transform.position;
        Vector3 initial = corpse.CoreBody.position; yield return Seconds(.15f);
        Check(corpse.CoreBody.position.x > initial.x + 1 && corpse.CoreBody.position.y > initial.y + .3f, "Killed enemy visibly flies horizontally and upward under real physics");
        Check(Vector3.ProjectOnPlane(loot.transform.position - lootStart, Vector3.up).magnitude < .2f
            && Vector3.ProjectOnPlane(loot.GetComponent<Rigidbody>().linearVelocity, Vector3.up).magnitude < .2f,
            "Fresh loot remains at the kill location while the nearby corpse flies away under real physics");
        Capture("killing-melee", corpse.CoreBody.position); DestroyEnemy(enemy); ClearBodies(); yield return Frames();
        enemy = Enemy(offset: Vector3.right * 3); yield return Frames(4); ConfigureDrop(enemy, 100); var sword = equipment.GetEquipped(PartCategory.ArmL); Hit(enemy, sword);
        var reaction = enemy.GetComponent<EnemyMeleeReaction>(); corpse = reaction.Ragdoll; health = enemy.GetComponent<CombatHealth>(); health.SetMaximum(1, true);
        yield return Seconds(.1f); int count = CombatRagdoll.ActiveCount;
        Hit(enemy, sword);
        Check(health.IsDead && enemy.GetComponent<CombatDeathRagdoll>().LastRagdoll == corpse && !corpse.IsTemporary
            && corpse.transform.parent == null && CombatRagdoll.ActiveCount == count,
            "Killing a knocked-down enemy promotes that same physical body, with no duplicate corpse or upright snapshot");
        Check(corpse.Bodies.All(b => b.gameObject.layer == CombatRagdoll.CorpseLayer && b.linearVelocity.x > 17 && b.linearVelocity.y > 6.9f),
            "Promoted corpse gets the stronger melee launch and returns to the existing corpse query layer");
        Check(enemy.GetComponent<EnemyPartDrop>().LastDrop != null && corpse.HiddenLootRendererCount > 0,
            "Killing an already-down enemy drops loot and removes the matching weapon from its existing physical copy");
        DestroyEnemy(enemy); ClearBodies(); yield return Frames();
        enemy = Enemy(offset: Vector3.right * 3); yield return Frames(4); ConfigureDrop(enemy, 0); enemy.GetComponent<CombatHealth>().SetMaximum(1, true);
        Hit(enemy, sword);
        Check(enemy.GetComponent<EnemyPartDrop>().LastDrop == null && enemy.GetComponent<CombatDeathRagdoll>().LastRagdoll.HiddenLootRendererCount == 0,
            "A failed loot roll spawns no pickup and leaves the carried weapon visible on the corpse");
        enemy.SetActive(true); enemy.GetComponent<CombatHealth>().SetMaximum(1, true); ConfigureDrop(enemy, 100); Hit(enemy, sword);
        Check(enemy.GetComponent<EnemyPartDrop>().LastDrop != null,
            "A pooled enemy gets a new independent loot roll on its next life");
        DestroyEnemy(enemy); ClearBodies(); yield return Frames();
    }
    static void ConfigureDrop(GameObject enemy, float chance)
    {
        var drop = enemy.GetComponent<EnemyPartDrop>(); var fields = new SerializedObject(drop);
        fields.FindProperty("dropChancePercent").floatValue = chance; fields.FindProperty("partPrefabs").arraySize = 1;
        fields.FindProperty("partPrefabs").GetArrayElementAtIndex(0).objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/arm_sword.prefab").GetComponent<Part>();
        fields.ApplyModifiedPropertiesWithoutUndo(); drop.enabled = true;
    }
    static IEnumerator ReturningWeapons()
    {
        yield return Player(80);
        foreach (string file in new[] { "arm_sword", "arm_mace" })
        {
            var enemy = Enemy(offset: Vector3.forward * 8); yield return Frames(4);
            var remote = player.GetComponent<RemotePartRetrieval>(); remote.enableRemoteTargeting = false;
            var part = Drop(file, new Vector3(0, .5f, 14)); bool observed = false; int hits = 0; CombatRagdoll physical = null;
            Debug.Log("RETURN MELEE OBSERVATION " + file + "; root=" + part.transform.position.ToString("F3") + "; pickup=" + part.PickupPoint.ToString("F3"));
            Action<Part, Vector3, Vector3> inspect = (p, from, to) =>
            {
                var reaction = enemy.GetComponent<EnemyMeleeReaction>(); if (reaction == null || reaction.Ragdoll == null || observed) return;
                observed = true; hits = reaction.ImpactCount; physical = reaction.Ragdoll;
                Check(physical.Bodies.All(b => b.linearVelocity.z < -6 && Mathf.Abs(Vector3.ProjectOnPlane(b.linearVelocity, Vector3.up).magnitude - 20f / 3f) < .01f
                    && Mathf.Abs(b.linearVelocity.y - 2f) < .01f), file + " F-return uses the reduced push/lift toward the player along the weapon's incoming path");
            };
            remote.Travelled += inspect;
            Check(remote.TryStart(part), file + " accepts its real returning-damage retrieval");
            while (remote.IsBusy || CombatHitStop.IsFrozen(player.transform)) yield return null; remote.Travelled -= inspect;
            Debug.Log("RETURN MELEE RESULT " + file + "; observed=" + observed + "; hits=" + hits + "; pathHits=" + player.GetComponent<RetrievalCombatEffects>().LastPathHits
                + "; equipped=" + part.isEquipped + "; hp=" + enemy.GetComponent<CombatHealth>().CurrentHealth);
            Check(observed && hits == 1 && physical != null && part.isEquipped && player.GetComponent<RetrievalCombatEffects>().LastPathHits == 1,
                file + " retrieves/equips while its damage path applies one physical impulse per enemy");
            DestroyEnemy(enemy); yield return Frames();
            enemy = Enemy(offset: Vector3.forward * 8); yield return Frames(4); ConfigureDrop(enemy, 100); enemy.GetComponent<CombatHealth>().SetMaximum(1, true);
            part = Drop(file, new Vector3(0, .5f, 14));
            Check(remote.TryStart(part), file + " returning killing-blow fixture starts");
            while (remote.IsBusy || CombatHitStop.IsFrozen(player.transform)) yield return null;
            var corpse = enemy.GetComponent<CombatDeathRagdoll>().LastRagdoll;
            Check(enemy.GetComponent<CombatHealth>().IsDead && corpse != null && corpse.Bodies.Any(b => b.linearVelocity.z < -15 && b.linearVelocity.y > 4),
                file + " F-return killing blow sends the corpse flying toward the player with added lift");
            Check(enemy.GetComponent<EnemyPartDrop>().LastDrop != null && corpse.HiddenLootRendererCount > 0
                && Vector3.ProjectOnPlane(enemy.GetComponent<EnemyPartDrop>().LastDrop.GetComponent<Rigidbody>().linearVelocity, Vector3.up).magnitude < .2f,
                file + " F-return leaves rolled loot behind and removes its visual copy from the launched body");
            DestroyEnemy(enemy); ClearBodies(); yield return Frames();
        }
    }
    static IEnumerator CleanupAndRules()
    {
        yield return Player(160); var sword = equipment.GetEquipped(PartCategory.ArmL);
        var enemy = Enemy(); yield return Frames(4);
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position = new Vector3(160, 2, 1.5f); wall.transform.localScale = new Vector3(6, 4, .2f); Physics.SyncTransforms();
        Hit(enemy, sword);
        Check(enemy.GetComponent<CombatHealth>().CurrentHealth == 500 && enemy.GetComponent<EnemyMeleeReaction>() == null, "Wall-blocked melee damage applies no force or reaction");
        wall.SetActive(false); enemy.GetComponent<CombatHealth>().Team = CombatTeam.Player; Hit(enemy, sword);
        Check(enemy.GetComponent<EnemyMeleeReaction>() == null, "Friendly-fire rejection never applies melee physics"); enemy.GetComponent<CombatHealth>().Team = CombatTeam.Enemy;
        var fields = new SerializedObject(combat); fields.FindProperty("meleePhysics").boolValue = false; fields.ApplyModifiedPropertiesWithoutUndo(); Hit(enemy, sword);
        Check(enemy.GetComponent<CombatHealth>().CurrentHealth == 490 && enemy.GetComponent<EnemyMeleeReaction>() == null, "Inspector toggle disables force while preserving ordinary melee damage");
        fields.FindProperty("meleePhysics").boolValue = true; fields.ApplyModifiedPropertiesWithoutUndo(); Hit(enemy, sword);
        var reaction = enemy.GetComponent<EnemyMeleeReaction>(); Check(reaction.IsKnockedDown, "Re-enabled melee force affects the same living enemy");
        enemy.SetActive(false); yield return Frames(); enemy.SetActive(true); enemy.GetComponent<CombatHealth>().SetMaximum(500, true); yield return Frames();
        Check(!reaction.IsKnockedDown && reaction.Ragdoll == null && enemy.GetComponent<EnemyAgent>().enabled
            && EnemyAgent.All.Count(a => a == enemy.GetComponent<EnemyAgent>()) == 1, "Disable/re-enable removes temporary physics and restores one pooled enemy without duplicate AI registration");
        Hit(enemy, sword); reaction.enabled = false;
        Check(!reaction.IsKnockedDown && enemy.GetComponent<EnemyAgent>().enabled, "Disabling the reaction directly safely restores a living enemy");
        DestroyEnemy(enemy); ClearBodies(); yield return Frames();
        foreach (string file in new[] { "Small Enemies/Enemy_crossbow", "Small Enemies/Enemy_mace", "Small Enemies/Enemy_Fodder", "Elite Enemies/Enemy_Elite_RockRope" })
        {
            enemy = Enemy(file); yield return Frames(4); Hit(enemy, sword); reaction = enemy.GetComponent<EnemyMeleeReaction>();
            Check(reaction != null && reaction.Ragdoll != null && reaction.Ragdoll.BodyCount > 0, file + " supports the same physical melee reaction");
            yield return Seconds(.12f);
            Check(reaction.Ragdoll.Bodies.All(b => !float.IsNaN(b.position.x) && b.position.y > -3), file + " stays physically stable after the impulse");
            DestroyEnemy(enemy); ClearBodies(); yield return Frames();
        }
    }
    static IEnumerator BudgetsAndLiveGraph()
    {
        yield return Player(240); var sword = equipment.GetEquipped(PartCategory.ArmL);
        var first = Enemy(offset: Vector3.forward * 3); var second = Enemy(offset: Vector3.forward * 8); yield return Frames(4);
        var a = first.AddComponent<EnemyMeleeReaction>(); var b = second.AddComponent<EnemyMeleeReaction>();
        var fields = new SerializedObject(a); fields.FindProperty("maximumActive").intValue = 1; fields.ApplyModifiedPropertiesWithoutUndo();
        fields = new SerializedObject(b); fields.FindProperty("maximumActive").intValue = 1; fields.ApplyModifiedPropertiesWithoutUndo();
        Time.timeScale = 0; a.Apply(Vector3.forward * 10 + Vector3.up * 3, .9f); b.Apply(Vector3.forward * 10 + Vector3.up * 3, .9f);
        Check(a.Ragdoll.BodyCount > 1 && b.Ragdoll != null && b.Ragdoll.BodyCount == 1,
            "At the configured articulated-body budget, another enemy still gets a one-body physical knockback fallback");
        var visuals = b.Ragdoll.GetComponentsInChildren<Renderer>().ToDictionary(r => r, r => r.transform.position);
        b.Ragdoll.Bodies[0].position += Vector3.right * 2; b.Ragdoll.SynchronizeVisuals();
        Check(visuals.All(p => Vector3.Distance(p.Key.transform.position, p.Value + Vector3.right * 2) < .01f),
            "Single-body fallback moves every visual piece together rather than leaving un-driven limbs behind");
        a.enabled = b.enabled = false; Time.timeScale = 1; DestroyEnemy(first); DestroyEnemy(second); ClearBodies(); yield return Frames();
        var bare = new GameObject("Rendererless fallback enemy"); bare.transform.position = new Vector3(240, 1, 3);
        bare.AddComponent<CombatHealth>().Team = CombatTeam.Enemy; bare.AddComponent<EnemyAgent>(); var fallback = bare.AddComponent<EnemyMeleeReaction>(); yield return Frames();
        Vector3 start = bare.transform.position; fallback.Apply(Vector3.forward * 10, .5f); yield return Seconds(.15f);
        Check(fallback.Ragdoll == null && fallback.IsKnockedDown && bare.transform.position.z > start.z + .5f,
            "An enemy without a renderable rig still receives swept knockback instead of silently ignoring force");
        yield return Recover(fallback); Check(bare.GetComponent<EnemyAgent>().enabled, "Root-knockback fallback restores its original motor"); Object.Destroy(bare); yield return Frames();
        var enemy = Enemy(); var attack = enemy.GetComponent<EnemyAttack>(); var brain = enemy.GetComponent<EnemyGraphController>(); var graph = enemy.GetComponent<Unity.Behavior.BehaviorGraphAgent>();
        attack.enabled = graph.enabled = brain.enabled = true; yield return Frames(6);
        Check(brain.IsReady && graph.Graph != null && graph.Graph.IsRunning, "Actual enemy behavior graph is running before the hit");
        Hit(enemy, sword); var reaction = enemy.GetComponent<EnemyMeleeReaction>();
        Check(reaction.IsKnockedDown && !brain.enabled && !graph.enabled && !attack.enabled, "Melee hit suspends the actual graph and attack execution");
        yield return Recover(reaction); yield return Frames(6);
        Check(brain.IsReady && brain.enabled && graph.enabled && graph.Graph.IsRunning && attack.enabled,
            "The same enemy's graph restarts and its existing attacks resume after recovery");
        Hit(enemy, sword);
        var outcome = MissionOutcomeController.Ensure(player.scene); outcome.ReportSuccess(); yield return Frames(4);
        Check(!reaction.IsKnockedDown && !enemy.GetComponent<EnemyAgent>().enabled && !brain.enabled && !graph.enabled && !attack.enabled,
            "Terminal mission state cleans up knockdown without re-enabling enemy AI after the results screen");
    }
    static void Capture(string name, Vector3 focus)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
        string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "MeleePhysicsCaptures"); Directory.CreateDirectory(folder);
        var old = camera.targetTexture; var active = RenderTexture.active; Vector3 position = camera.transform.position; Quaternion rotation = camera.transform.rotation;
        var target = new RenderTexture(1280, 720, 24); var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            Vector3 offset = new Vector3(9, 6, -10); camera.transform.SetPositionAndRotation(focus + offset, Quaternion.LookRotation(-offset)); camera.targetTexture = target;
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target }); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply(); File.WriteAllBytes(Path.Combine(folder, name + ".png"), texture.EncodeToPNG());
        }
        finally { camera.transform.SetPositionAndRotation(position, rotation); camera.targetTexture = old; RenderTexture.active = active; Object.Destroy(texture); Object.Destroy(target); }
    }
    static void Finish(Exception error)
    {
        Time.timeScale = 1; PreMissionReliabilityChecks.EndLogAudit(); SessionState.SetBool(Pending, false);
        if (error != null) Debug.LogException(error);
        Debug.Log("MELEE PHYSICS CHECKS " + (error == null ? "COMPLETE: " : "FAILED: ") + checks); EditorApplication.Exit(error == null ? 0 : 1);
    }
}
