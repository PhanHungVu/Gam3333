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

// Separate projectile + real retrieval/NPC hitboxes; disposable project only.
public static class RetrievalPiercingBoltChecks
{
    const string Pending = "RetrievalPiercingBoltChecks.Pending";
    static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    static readonly List<Object> owned = new List<Object>();
    static int checks, frame = -1; static bool running; static double deadline;
    static GameObject player; static RemotePartRetrieval remote; static RetrievalCombatEffects effects; static RetrievalBonusCooldowns gate;
    static RetrievalEffectCatalogue.Entry profile; static Camera camera; static RetrievalPiercingBolt last;
    static void Check(bool valid, string message) { if (!valid) throw new Exception(message); checks++; Debug.Log("RETRIEVAL BOLT PASS: " + message); }
    [InitializeOnLoadMethod] static void Register() => EditorApplication.update += Tick;
    public static void RunBatch()
    {
        if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new Exception("Disposable project only");
        foreach (var name in new[] { "Base/FullPlayer_mech_golem", "Base/FullPlayer_mech_heavy", "Specialist/FullPlayer_mech_specialist" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/" + name + ".prefab");
            var catalogue = prefab.GetComponent<RetrievalCombatEffects>().Catalogue;
            profile = catalogue.entries.Single(e => e.family == RetrievalWeaponFamily.LargeCrossbow);
            Check(catalogue.entries.Length == 6 && profile.part.partId == "backweaponA4" && profile.effect == RetrievalEffectKind.PiercingReturnBolt
                && profile.damageMultiplier == 1 && profile.hitRadius == .5f && profile.spawnHeight == 1.2f && profile.speedMultiplier == 1 && profile.boltVisualScale == 2,
                name + " maps only the canonical large crossbow to a 1x damage, 0.5m radius piercing bolt");
            Check(prefab.GetComponent<RemotePartRetrieval>().pickupRange == 20 && prefab.GetComponent<RetrievalBonusCooldowns>().bypassCooldownForTesting,
                name + " retains 20m range and the temporary bonus cooldown bypass");
        }
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (!running) { running = true; deadline = EditorApplication.timeSinceStartup + 240; PreMissionReliabilityChecks.BeginLogAudit(); steps.Push(Run()); }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Piercing retrieval checks timed out");
            while (steps.Count > 0) { var step = steps.Peek(); if (!step.MoveNext()) { steps.Pop(); continue; } if (step.Current is IEnumerator child) { steps.Push(child); continue; } return; }
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    static IEnumerator Frames(int count = 3) { int end = Time.frameCount + count; while (Time.frameCount < end) yield return null; }
    static IEnumerator Seconds(float duration) { double end = Time.timeAsDouble + duration; while (Time.timeAsDouble < end) yield return null; }
    static IEnumerator Complete()
    {
        double end = EditorApplication.timeSinceStartup + 8;
        while (remote.IsBusy || CombatHitStop.IsFrozen(player.transform)) { if (EditorApplication.timeSinceStartup > end) throw new Exception("Retrieval did not finish"); yield return null; }
        yield return Frames();
    }
    static IEnumerator Depart() { while (remote.IsBusy && !remote.HasDeparted) yield return null; }
    static IEnumerator Player(float x)
    {
        player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SpecialistPlayerIntegrationSetup.PlayerPath), new Vector3(x, .1f, 0), Quaternion.identity); owned.Add(player);
        yield return Frames(6); player.GetComponent<MechPlayerInput>().enabled = false; player.GetComponent<MechMovementController>().enabled = false;
        player.GetComponent<CombatHealth>().Team = CombatTeam.Player;
        remote = player.GetComponent<RemotePartRetrieval>(); effects = player.GetComponent<RetrievalCombatEffects>(); gate = player.GetComponent<RetrievalBonusCooldowns>();
        remote.enableRemoteTargeting = false; last = null; effects.BoltCreated += bolt => last = bolt;
        camera = player.GetComponent<MechPlayerRig>().GameplayCamera; camera.GetComponent<CameraControlThirdPerson>().enabled = false;
        camera.GetComponent<Unity.Cinemachine.CinemachineBrain>().enabled = false;
    }
    static Part Drop(float distance = 18)
    {
        var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/shoulder_crossbow.prefab")); owned.Add(root);
        var part = root.GetComponent<Part>(); part.isStartingPart = false;
        root.transform.position = player.transform.position + new Vector3(0, 2, distance); Physics.SyncTransforms();
        float bottom = root.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger && c.GetComponentInParent<Part>() == part).Min(c => c.bounds.min.y);
        root.transform.position += Vector3.up * (.08f - bottom); root.GetComponent<Rigidbody>().isKinematic = true;
        typeof(Part).GetMethod("InitializeAmmo", Hidden).Invoke(part, new object[] { 30, 1 });
        Physics.SyncTransforms(); return part;
    }
    static GameObject Enemy(string file, float z)
    {
        var parent = new GameObject("Held NPC fixture"); owned.Add(parent); parent.SetActive(false);
        var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Small Enemies/" + file + ".prefab"), parent.transform);
        root.transform.position = new Vector3(player.transform.position.x, 0, z);
        foreach (var script in root.GetComponentsInChildren<MonoBehaviour>(true)) if (!(script is CombatHealth || script is EquipManager || script is Part || script is EnemyAgent)) script.enabled = false;
        var fields = new SerializedObject(root.GetComponent<EnemyAgent>()); fields.FindProperty("externalDecisions").boolValue = true; fields.ApplyModifiedPropertiesWithoutUndo();
        foreach (var animator in root.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
        foreach (var agent in root.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) agent.enabled = false;
        parent.SetActive(true); root.GetComponent<CombatHealth>().SetMaximum(500, true); return root;
    }
    static CombatHealth Target(float z, float x = 0, CombatTeam team = CombatTeam.Enemy, int compound = 0)
    {
        var root = new GameObject("Bolt target " + team); owned.Add(root); root.transform.position = new Vector3(player.transform.position.x + x, 0, z);
        var collider = root.AddComponent<BoxCollider>(); collider.center = Vector3.up * 1.2f; collider.size = new Vector3(.5f, 1, .5f);
        for (int i = 0; i < compound; i++) { var child = new GameObject("Compound collider " + i); child.transform.SetParent(root.transform, false); var box = child.AddComponent<BoxCollider>(); box.center = collider.center; box.size = collider.size; }
        var health = root.AddComponent<CombatHealth>(); health.Team = team; health.SetMaximum(500, true); Physics.SyncTransforms(); return health;
    }
    static GameObject Wall(Vector3 point, Vector3 size)
    {
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(wall); wall.transform.position = point; wall.transform.localScale = size; Physics.SyncTransforms(); return wall;
    }
    static void Step(RetrievalPiercingBolt bolt, float dt) => typeof(RetrievalPiercingBolt).GetMethod("Step", Hidden).Invoke(bolt, new object[] { dt });
    static RetrievalPiercingBolt Fire(Part part, RetrievalEffectCatalogue.Entry? settings = null)
    {
        var entry = settings ?? profile;
        var bolt = RetrievalPiercingBolt.Fire(part, player.transform, new Vector3(player.transform.position.x, 1.2f, 18),
            new Vector3(player.transform.position.x, 1.2f, 0), 100, ~0, 4, entry); owned.Add(bolt.gameObject); return bolt;
    }
    static IEnumerator Run()
    {
        Time.timeScale = 1; Time.maximumDeltaTime = .05f; Application.runInBackground = true;
        var floor = Wall(Vector3.down * .25f, new Vector3(550, .5f, 200));
        var light = new GameObject("Sun").AddComponent<Light>(); owned.Add(light.gameObject); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(55, -30, 0);
        yield return Player(0);
        var enemies = new[] { Enemy("Enemy_sword", 14), Enemy("Enemy_crossbow", 10), Enemy("Enemy_mace", 6), Enemy("Enemy_Fodder", 3) };
        yield return Frames(4);
        var part = Drop(); part.GetComponent<Rigidbody>().isKinematic = false; yield return Seconds(1.5f); part.GetComponent<Rigidbody>().isKinematic = true;
        Physics.SyncTransforms(); Vector3 ground = RetrievalPiercingBolt.GroundOrigin(part.PickupPoint + Vector3.up * .08f, player.transform, profile.spawnHeight);
        typeof(Part).GetMethod("Consume", Hidden).Invoke(part, new object[] { 7f, false, 0f, 0 });
        float clock = part.ReadyAt; int ammo = part.RemainingAmmo, magazine = part.MagazineRounds, normalShots = 0, attacks = 0, bolts = 0;
        int shake = player.GetComponent<RetrievalFeedback>().ArrivalImpulseCount, fireShake = player.GetComponent<CombatFeedback>().FiringImpulseCount;
        float health = player.GetComponent<CombatHealth>().CurrentHealth;
        Action<MechProjectile, Vector3, Transform> normal = (s, p, o) => { if (o == player.transform) normalShots++; };
        CombatVisualEvents.ShotReleased += normal; player.GetComponent<MechCombat>().WeaponAttackStarted += p => attacks++;
        Action<RetrievalPiercingBolt> freeze = b => { bolts++; Time.timeScale = 0; };
        effects.BoltCreated += freeze;
        camera.transform.SetPositionAndRotation(player.transform.position + new Vector3(0, 4, -8), Quaternion.LookRotation(part.PickupPoint - player.transform.position - new Vector3(0, 4, -8)));
        remote.enableRemoteTargeting = true; remote.RefreshTarget(); var input = player.GetComponent<MechPlayerInput>(); input.enabled = true; input.AssumeCursorCaptured = true;
        Check(remote.Target == part && input.RequestPickup(), "Actual F entry retrieves a naturally settled real large crossbow through the enemy line at 18m"); remote.enableRemoteTargeting = false;
        Check(gate.IsReserved(RetrievalWeaponFamily.LargeCrossbow) && effects.BonusBoltCount == 0, "Anticipation reserves the large-crossbow bonus without firing it");
        yield return Depart(); yield return Frames(2);
        Check(last != null && last.IsLive && effects.BonusBoltCount == 1 && !gate.IsReserved(RetrievalWeaponFamily.LargeCrossbow), "Fast departure commits exactly one independent piercing bolt");
        Check(Vector3.Distance(last.transform.position, ground) < .01f && Mathf.Abs(ground.y - 1.2f) < .01f && last.Direction.z < -.99f,
            "Bolt starts above the original grounded pickup, not the raised weapon or player waist, and travels toward the player");
        Check(last.GetComponents<MechProjectile>().All(p => !p.enabled) && last.GetComponentInChildren<Part>() == null
            && last.GetComponentsInChildren<Renderer>().Length > 0, "Existing bolt model has one active movement driver and contains no extra equipment");
        int finished = 0; last.Finished += _ => finished++;
        Step(last, .03f); yield return Frames(2); Capture("piercing-flight");
        Check(enemies[0].GetComponent<CombatHealth>().CurrentHealth == 400 && last.IsLive, "First real enemy takes normal large-crossbow damage while the bolt keeps travelling");
        Step(last, .2f);
        Check(enemies.All(e => e.GetComponent<CombatHealth>().CurrentHealth == 400) && last.HitCount == 4 && !last.IsLive && finished == 1,
            "One swept bolt hits all four actual enemy types once each, including compound NPC hitboxes");
        Check(player.GetComponent<CombatHealth>().CurrentHealth == health && part.RemainingAmmo == ammo && part.MagazineRounds == magazine && part.ReadyAt == clock,
            "Piercing attack never damages its owner or spends normal ammo/magazine/cooldown state");
        var trails = Object.FindObjectsByType<CombatProjectileTrail>().Where(t => t.IsPlaying && !t.IsFollowing).ToArray();
        Check(trails.Length > 0 && trails.Any(t => t.Renderer.positionCount > 1), "Finished piercing bolt leaves real lingering trail geometry in the existing pool");
        Capture("piercing-tail"); effects.BoltCreated -= freeze; Time.timeScale = 1; yield return Complete(); input.enabled = false;
        Check(part.isEquipped && part.EquippedBy == player.GetComponent<EquipManager>() && normalShots == 0 && attacks == 0
            && player.GetComponent<CombatFeedback>().FiringImpulseCount == fireShake && player.GetComponent<RetrievalFeedback>().ArrivalImpulseCount == shake + 1,
            "Ordinary equip and one arrival shake finish without normal firing events, firing recoil or extra firing impulses");
        Check(effects.BonusBlastCount == 0 && effects.BonusBarrageCount == 0 && !player.GetComponent<RetrievalSpeedBuff>().Active, "Large crossbow does not trigger the other retrieval families' effects");
        var copy = Drop(); Check(remote.TryStart(copy), "Temporary bypass accepts another large crossbow retrieval"); yield return Complete(); yield return Seconds(.25f);
        Check(effects.BonusBoltCount == 2 && enemies.All(e => e.GetComponent<CombatHealth>().CurrentHealth == 300), "Another physical crossbow produces fresh piercing damage while bypass is enabled");
        gate.bypassCooldownForTesting = false; int count = effects.BonusBoltCount; var cooling = Drop(); Check(remote.TryStart(cooling), "F pickup still works during the restored normal large-crossbow cooldown"); yield return Complete();
        Check(cooling.isEquipped && effects.BonusBoltCount == count && gate.Remaining(RetrievalWeaponFamily.LargeCrossbow) > 28
            && gate.IsReady(RetrievalWeaponFamily.SmallCrossbow) && gate.IsReady(RetrievalWeaponFamily.Cannon), "All large-crossbow copies share one 30s timer without consuming other families' gates");
        CombatVisualEvents.ShotReleased -= normal;
        yield return CollisionRules();
        yield return Cancellation();
    }
    static IEnumerator CollisionRules()
    {
        yield return Player(60); Time.timeScale = 0;
        var part = Drop(); var first = Target(14, compound: 40); var second = Target(10); var behind = Target(4); var side = Target(10, x: 2);
        var wall = Wall(new Vector3(60, 1.2f, 8), new Vector3(6, 3, .2f));
        var bolt = Fire(part); int finished = 0; bolt.Finished += _ => finished++;
        Step(bolt, 1);
        Check(first.CurrentHealth == 400 && second.CurrentHealth == 400 && behind.CurrentHealth == 500 && side.CurrentHealth == 500,
            "A long sweep pierces more than 16 compound contacts, stops at solid terrain and leaves off-path/behind-wall targets unharmed");
        Check(!bolt.IsLive && finished == 1 && bolt.transform.position.z > 8 && bolt.transform.position.z < 8.7f, "Terrain stops the bolt at its radius-adjusted contact point with one finish event");
        Step(bolt, 1); Check(first.CurrentHealth == 400 && finished == 1, "A spent bolt cannot repeat damage or lifecycle callbacks");
        wall.SetActive(false); first.gameObject.SetActive(false); second.gameObject.SetActive(false);
        var ally = Target(8, team: CombatTeam.Player); var near = Target(14); var onStart = Target(18, compound: 24);
        bolt = Fire(part); player.GetComponent<CombatHealth>().Team = CombatTeam.Enemy; Step(bolt, 1); player.GetComponent<CombatHealth>().Team = CombatTeam.Player;
        Check(onStart.CurrentHealth == 400 && near.CurrentHealth == 400 && ally.CurrentHealth == 500 && behind.CurrentHealth == 500,
            "Initial overlaps hit once; friendly bodies block without damage using the launch-time team, even if the owner team changes");
        onStart.gameObject.SetActive(false); ally.gameObject.SetActive(false); near.gameObject.SetActive(false); behind.gameObject.SetActive(false);
        var overlapWall = Wall(new Vector3(60, 1.2f, 18), Vector3.one * .2f); bolt = Fire(part);
        Check(!bolt.IsLive && bolt.HitCount == 0, "Starting inside solid terrain immediately spends the bolt without damage through it"); overlapWall.SetActive(false);
        var far = Target(15); var shortProfile = profile; shortProfile.rangeMultiplier = .01f;
        bolt = Fire(part, shortProfile); Step(bolt, 1);
        Check(far.CurrentHealth == 500 && !bolt.IsLive && Mathf.Abs(bolt.transform.position.z - 17.2f) < .01f, "Configured weapon range can end a bolt before the player without hitting beyond that range"); far.gameObject.SetActive(false);
        var firstNormal = Target(6); var nextNormal = Target(10);
        var normal = RangedAttack.Fire(part, player.transform, new Vector3(60, 1.2f, .65f), new Vector3(60, 1.2f, 30), Vector3.forward, 80, ~0, null, null);
        typeof(MechProjectile).GetMethod("Step", Hidden).Invoke(normal, new object[] { 1f });
        Check(!normal.IsLive && firstNormal.CurrentHealth == 390 && nextNormal.CurrentHealth == 500,
            "Normal large-crossbow fire still stops on its first target with its existing direct+splash damage");
        firstNormal.gameObject.SetActive(false); nextNormal.gameObject.SetActive(false);
        var noVfx = Target(10); var feedback = player.GetComponent<CombatFeedback>(); feedback.enabled = false;
        bolt = Fire(part); Step(bolt, 1);
        Check(noVfx.CurrentHealth == 400 && bolt.HitCount == 1, "Disabling presentation does not suppress piercing gameplay damage"); noVfx.gameObject.SetActive(false); feedback.enabled = true;
        // A route is captured at launch; it does not home toward a player who moves afterwards.
        bolt = Fire(part); Vector3 direction = bolt.Direction;
        var controller = player.GetComponent<CharacterController>(); controller.enabled = false; player.transform.position += Vector3.right * 3; controller.enabled = true;
        Physics.SyncTransforms(); Step(bolt, 1);
        Check(bolt.Direction == direction && Mathf.Abs(bolt.transform.position.x - 60) < .001f && Mathf.Abs(bolt.transform.position.z) < .001f,
            "Bolt stops at its captured endpoint and does not turn or continue behind a moving player");
        Time.timeScale = 1; yield return Frames();
    }
    static IEnumerator Cancellation()
    {
        yield return Player(120); gate.bypassCooldownForTesting = false;
        var far = Drop(22); Check(!remote.TryStart(far) && gate.IsReady(RetrievalWeaponFamily.LargeCrossbow), "Out-of-range rejection never reserves a bolt");
        var hover = Drop(); Check(remote.TryStart(hover), "Hover-cancel fixture starts"); remote.Cancel();
        Check(effects.BonusBoltCount == 0 && gate.IsReady(RetrievalWeaponFamily.LargeCrossbow), "Cancelling before fast departure refunds the bonus without a bolt");
        var destroyed = Drop(); Check(remote.TryStart(destroyed), "Destroyed-drop fixture starts"); Object.Destroy(destroyed.gameObject); yield return Complete();
        Check(effects.BonusBoltCount == 0 && gate.IsReady(RetrievalWeaponFamily.LargeCrossbow), "Destroyed hovering drop releases its pending family token");
        var blocked = Drop(); Check(remote.TryStart(blocked), "Blocked-lift fixture starts");
        var roof = Wall(blocked.PickupPoint + Vector3.up * (remote.obstructionRadius + .02f + .4f), new Vector3(3, .1f, 3));
        yield return Complete(); roof.SetActive(false);
        Check(!blocked.isEquipped && effects.BonusBoltCount == 0 && gate.IsReady(RetrievalWeaponFamily.LargeCrossbow), "A blocked lift cancels before the bolt trigger and refunds the bonus");
        var disabled = Drop(); Check(remote.TryStart(disabled), "Disabled listener fixture starts"); effects.enabled = false; yield return Complete(); effects.enabled = true;
        Check(disabled.isEquipped && effects.BonusBoltCount == 0 && gate.IsReady(RetrievalWeaponFamily.LargeCrossbow), "Listener disable during anticipation cannot fire or replay a bolt on arrival");
        Action<RetrievalPiercingBolt> freeze = _ => Time.timeScale = 0; effects.BoltCreated += freeze;
        var fired = Drop(); Check(remote.TryStart(fired), "Post-launch cancellation fixture starts"); yield return Depart();
        var bolt = last; remote.Cancel();
        Check(bolt != null && bolt.IsLive && gate.Remaining(RetrievalWeaponFamily.LargeCrossbow) > 29 && !gate.IsReserved(RetrievalWeaponFamily.LargeCrossbow),
            "Cancelling equipment travel after firing retains its committed 30s gate and independent projectile");
        var victim = Target(10); Step(bolt, 1);
        Check(victim.CurrentHealth == 400 && !fired.isEquipped, "An already-fired independent bolt can complete even when the equipment pull is cancelled"); victim.gameObject.SetActive(false);
        Time.timeScale = 1; gate.bypassCooldownForTesting = true;
        var off = Drop(); Check(remote.TryStart(off), "Live-bolt listener-disable fixture starts"); yield return Depart(); bolt = last; effects.enabled = false;
        Check(!bolt.IsLive && effects.BonusBoltCount == 2, "Disabling the effect listener stops all of its live bolt entities"); remote.Cancel(); effects.enabled = true; Time.timeScale = 1;
        var dead = Drop(); Check(remote.TryStart(dead), "Owner-death-before-launch fixture starts"); player.GetComponent<CombatHealth>().ApplyDamage(100000);
        Check(!dead.IsRetrieving && effects.BonusBoltCount == 2 && !gate.IsReserved(RetrievalWeaponFamily.LargeCrossbow), "Owner death before launch creates no bolt and releases its reservation");
        yield return Player(180); gate.bypassCooldownForTesting = false; effects.BoltCreated += freeze;
        var launched = Drop(); Check(remote.TryStart(launched), "Owner-death-after-launch fixture starts"); yield return Depart(); bolt = last; player.GetComponent<CombatHealth>().ApplyDamage(100000);
        Check(!bolt.IsLive && gate.Remaining(RetrievalWeaponFamily.LargeCrossbow) > 29, "Owner death cleans up a launched bolt without refunding a used combat bonus");
        Time.timeScale = 1; yield return Frames();
    }
    static void Capture(string name)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
        string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "RetrievalBoltCaptures"); Directory.CreateDirectory(folder);
        var old = camera.targetTexture; var active = RenderTexture.active; Vector3 position = camera.transform.position; Quaternion rotation = camera.transform.rotation;
        var views = Object.FindObjectsByType<PartSwapWorldView>(); var alphas = views.Select(v => v.Group.alpha).ToArray();
        var target = new RenderTexture(1280, 720, 24); var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            foreach (var view in views) view.SetAlpha(0);
            Vector3 focus = new Vector3(0, 1, 8); Vector3 offset = new Vector3(12, 15, -8); camera.transform.SetPositionAndRotation(focus + offset, Quaternion.LookRotation(-offset)); camera.targetTexture = target;
            Canvas.ForceUpdateCanvases(); RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target }); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply(); File.WriteAllBytes(Path.Combine(folder, name + ".png"), texture.EncodeToPNG());
        }
        finally
        {
            camera.transform.SetPositionAndRotation(position, rotation); camera.targetTexture = old; RenderTexture.active = active;
            for (int i = 0; i < views.Length; i++) views[i].SetAlpha(alphas[i]); Object.Destroy(texture); Object.Destroy(target);
        }
    }
    static void Finish(Exception error)
    {
        Time.timeScale = 1; PreMissionReliabilityChecks.EndLogAudit(); SessionState.SetBool(Pending, false);
        if (error != null) Debug.LogException(error);
        Debug.Log("RETRIEVAL BOLT CHECKS " + (error == null ? "COMPLETE: " : "FAILED: ") + checks); EditorApplication.Exit(error == null ? 0 : 1);
    }
}
