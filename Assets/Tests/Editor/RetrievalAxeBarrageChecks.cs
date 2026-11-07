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

// Real axe projectile prefab, actual retrieval/arrival, and real health/collision rules.
public static class RetrievalAxeBarrageChecks
{
    const string Pending = "RetrievalAxeBarrageChecks.Pending";
    static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    static readonly List<Object> owned = new List<Object>();
    static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks, frame = -1; static bool running; static double deadline;
    static GameObject player; static RemotePartRetrieval remote; static RetrievalCombatEffects effects; static RetrievalBonusCooldowns gate; static Camera camera;
    [InitializeOnLoadMethod] static void Register() => EditorApplication.update += Tick;
    static void Check(bool valid, string message) { if (!valid) throw new Exception(message); checks++; Debug.Log("RETRIEVAL AXE PASS: " + message); }
    public static void RunBatch()
    {
        if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new Exception("Disposable project only");
        foreach (string name in new[] { "Base/FullPlayer_mech_golem", "Base/FullPlayer_mech_heavy", "Specialist/FullPlayer_mech_specialist" })
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/" + name + ".prefab");
            Check(root.GetComponent<RemotePartRetrieval>().pickupRange == 20 && root.GetComponent<RetrievalBonusCooldowns>().bypassCooldownForTesting,
                name + " uses 20m retrieval and preserves the requested temporary cooldown bypass");
            var catalogue = root.GetComponent<RetrievalCombatEffects>().Catalogue;
            Check(catalogue.entries.Single(e => e.family == RetrievalWeaponFamily.ThrowingAxe).effect == RetrievalEffectKind.ArrivalBarrage
                && catalogue.entries.Single(e => e.family == RetrievalWeaponFamily.ThrowingAxe).projectileCount == 24
                && catalogue.entries.Single(e => e.family == RetrievalWeaponFamily.ThrowingAxe).hitRadius == .4f, name + " connects the dense 24-axe barrage and forgiving collision radius");
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
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Axe barrage timed out");
            while (steps.Count > 0)
            { var step = steps.Peek(); if (!step.MoveNext()) { steps.Pop(); continue; } if (step.Current is IEnumerator nested) { steps.Push(nested); continue; } return; }
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    static IEnumerator Frames(int count = 3) { int end = Time.frameCount + count; while (Time.frameCount < end) yield return null; }
    static IEnumerator Complete()
    {
        double until = EditorApplication.timeSinceStartup + 8;
        while (remote.IsBusy || CombatHitStop.IsFrozen(player.transform)) { if (EditorApplication.timeSinceStartup > until) throw new Exception("Flight did not finish"); yield return null; }
        yield return Frames();
    }
    static IEnumerator ShotsFinished(List<MechProjectile> shots)
    {
        double until = EditorApplication.timeSinceStartup + 5;
        while (shots.Any(s => s != null && s.IsLive) || CombatHitStop.IsFrozen(player.transform))
        { if (EditorApplication.timeSinceStartup > until) throw new Exception("Barrage projectiles did not finish"); yield return null; }
        yield return Frames();
    }
    static IEnumerator Player(float x)
    {
        player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SpecialistPlayerIntegrationSetup.PlayerPath), new Vector3(x, .1f, 0), Quaternion.identity); owned.Add(player);
        yield return Frames(6); player.GetComponent<MechMovementController>().enabled = false; player.GetComponent<MechPlayerInput>().enabled = false; player.GetComponent<CombatHealth>().Team = CombatTeam.Player;
        remote = player.GetComponent<RemotePartRetrieval>(); effects = player.GetComponent<RetrievalCombatEffects>(); gate = player.GetComponent<RetrievalBonusCooldowns>(); remote.enableRemoteTargeting = false;
        camera = player.GetComponent<MechPlayerRig>().GameplayCamera; camera.GetComponent<CameraControlThirdPerson>().enabled = false; camera.GetComponent<Unity.Cinemachine.CinemachineBrain>().enabled = false;
    }
    static Part Drop(string file = "arm_throwingaxe", float distance = 18)
    {
        var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/" + file + ".prefab")); owned.Add(root);
        var part = root.GetComponent<Part>(); part.isStartingPart = false; root.transform.position = player.transform.position + new Vector3(0, .6f, distance); root.GetComponent<Rigidbody>().isKinematic = true;
        typeof(Part).GetMethod("InitializeAmmo", Hidden).Invoke(part, new object[] { Mathf.RoundToInt(part.Stats.GetStat(StatType.Ammo)), part.Stats.MagazineSize });
        Physics.SyncTransforms(); return part;
    }
    static CombatHealth Target(Vector3 offset, CombatTeam team = CombatTeam.Enemy, bool compound = true)
    {
        var root = GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(root); root.name = "Barrage target " + team;
        var feet=player.transform.position; feet.y=player.GetComponent<CharacterController>().bounds.min.y;
        root.transform.position = feet + offset + Vector3.up * 1.2f; root.transform.localScale = new Vector3(.7f, 2.4f, .7f);
        if (compound) for (int i = 0; i < 3; i++) { var child = new GameObject("Overlapping hitbox"); child.transform.SetParent(root.transform, false); child.AddComponent<BoxCollider>(); }
        var health = root.AddComponent<CombatHealth>(); health.Team = team; health.SetMaximum(500, true); return health;
    }
    static IEnumerator Run()
    {
        Time.timeScale = 1; Time.maximumDeltaTime = .05f; Application.runInBackground = true;
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(floor); floor.transform.position = Vector3.down * .25f; floor.transform.localScale = new Vector3(250, .5f, 150);
        var light = new GameObject("Sun").AddComponent<Light>(); owned.Add(light.gameObject); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(55, -30, 0);
        yield return Player(0);
        var targets = new List<CombatHealth>();
        for (int i = 0; i < 8; i++) targets.Add(Target(Quaternion.AngleAxis(i * 45, Vector3.up) * Vector3.forward * 3.5f));
        var shots = new List<MechProjectile>(); var directions = new List<Vector3>(); int normalShots = 0, retrievalShots = 0, attacks = 0, impacts = 0; bool afterEquip = true, noParts = true, correctHeight=true;
        Action<MechProjectile, Vector3> created = (shot, direction) =>
        {
            shots.Add(shot); directions.Add(direction); owned.Add(shot.gameObject);
            afterEquip &= shot.Source != null && shot.Source.isEquipped && shot.Source.EquippedBy == player.GetComponent<EquipManager>();
            noParts &= shot.GetComponentInChildren<Part>(true) == null;
            correctHeight &= Mathf.Abs(shot.transform.position.y-player.GetComponent<CharacterController>().bounds.min.y-1.2f)<.001f;
            shot.ImpactResolved += _ => impacts++;
        };
        effects.BarrageProjectileCreated += created;
        Action<MechProjectile, Vector3, Transform> regular = (s, m, o) => { if (o == player.transform) normalShots++; };
        Action<MechProjectile, Vector3, Transform> retrieval = (s, m, o) => { if (o == player.transform) retrievalShots++; };
        CombatVisualEvents.ShotReleased += regular; CombatVisualEvents.RetrievalShotReleased += retrieval;
        player.GetComponent<MechCombat>().WeaponAttackStarted += _ => attacks++;
        var axe = Drop(); int ammo = axe.RemainingAmmo, magazine = axe.MagazineRounds;
        camera.transform.SetPositionAndRotation(new Vector3(0, 4, -8), Quaternion.LookRotation(axe.transform.position - new Vector3(0, 4, -8)));
        remote.enableRemoteTargeting = true; remote.RefreshTarget();
        Check(remote.Target == axe, "Actual dropped axe model is selectable by the camera-centre pickup query at 18m"); remote.enableRemoteTargeting = false;
        typeof(Part).GetMethod("Consume", Hidden).Invoke(axe, new object[] { 5f, false, 0f, 0 }); float readyAt = axe.ReadyAt;
        int itemCount = Object.FindObjectsByType<Part>().Count(p => p.Stats == axe.Stats);
        int arrivalShake = player.GetComponent<RetrievalFeedback>().ArrivalImpulseCount;
        int firingShake = player.GetComponent<CombatFeedback>().FiringImpulseCount;
        Action<Part, PartCategory> pause = (p, s) => Time.timeScale = 0; remote.Arrived += pause;
        Check(remote.TryStart(axe), "Axe pull starts from 18m within the new 20m range");
        while (remote.IsBusy && remote.Phase != RetrievalPhase.Hovering) yield return null;
        Check(shots.Count == 0 && targets.All(t => t.CurrentHealth == 500), "Lift/hover creates no premature axes or damage");
        while (remote.IsBusy) yield return null; remote.Arrived -= pause;
        Check(shots.Count == 24 && effects.LastBarrageAxes == 24 && effects.BonusBarrageCount == 1, "One successful arrival creates exactly 24 temporary axes");
        Check(afterEquip && noParts && Object.FindObjectsByType<Part>().Count(p => p.Stats == axe.Stats) == itemCount, "Axes start after equip and never create additional equipment/pickups");
        Check(correctHeight && player.transform.position.y-player.GetComponent<CharacterController>().bounds.min.y>.5f,
            "Axe plane is measured from actual feet even when the Specialist root pivot is elevated");
        Check(directions.Count == 24 && directions.Select(d => Mathf.RoundToInt(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg / 15)).Distinct().Count() == 24
            && directions.All(d => Mathf.Abs(d.y) < .001f && Mathf.Abs(d.magnitude - 1) < .001f), "24 horizontal unit directions cover the full circle in 15-degree steps");
        Check(shots.All(s => s != null && s.IsLive && s.Subtype == PartSubtype.Projectile && s.OwnerTeam == CombatTeam.Player && s.GetComponentsInChildren<Renderer>().Length > 0),
            "The barrage uses live existing axe models/projectiles with the player's team");
        foreach (var shot in shots) typeof(MechProjectile).GetMethod("Step", Hidden).Invoke(shot, new object[] { .012f });
        yield return Frames(2); Capture(); Time.timeScale = 1;
        yield return ShotsFinished(shots);
        Check(targets.All(t => t.CurrentHealth == 450) && impacts == 8, "All eight directions damage their targets once for normal axe damage, including overlapping colliders");
        Check(axe.RemainingAmmo == ammo && axe.MagazineRounds == magazine && axe.ReadyAt == readyAt, "Free barrage consumes no ammo, magazine round or existing weapon cooldown");
        Check(normalShots == 0 && attacks == 0 && retrievalShots == 24 && player.GetComponent<CombatFeedback>().FiringImpulseCount == firingShake,
            "Retrieval trail feedback avoids normal firing events/recoil and 24 firing camera impulses");
        Check(player.GetComponent<RetrievalFeedback>().ArrivalImpulseCount == arrivalShake + 1, "Barrage retains one normal weapon-arrival shake");
        Check(gate.bypassCooldownForTesting && gate.Remaining(RetrievalWeaponFamily.ThrowingAxe) == 0, "Requested temporary bypass applies to the new axe family");
        shots.Clear(); var copy = Drop(); Check(remote.TryStart(copy), "Another axe is retrievable immediately with bypass enabled"); yield return Complete(); yield return ShotsFinished(shots);
        Check(shots.Count == 24 && effects.BonusBarrageCount == 2 && targets.All(t => t.CurrentHealth == 400), "Repeated axe copies produce fresh 24-direction damage while bypassed");
        gate.bypassCooldownForTesting = false; int countBefore = shots.Count; var cooling = Drop(); Check(remote.TryStart(cooling), "Ordinary axe retrieval still works after restoring the normal gate"); yield return Complete();
        Check(shots.Count == countBefore && effects.BonusBarrageCount == 2 && targets.All(t => t.CurrentHealth == 400) && gate.Remaining(RetrievalWeaponFamily.ThrowingAxe) > 25,
            "All axe copies share the retained 30s timer when bypass is off");
        Check(gate.IsReady(RetrievalWeaponFamily.Sword) && gate.IsReady(RetrievalWeaponFamily.Mace) && gate.IsReady(RetrievalWeaponFamily.Cannon), "Axe cooldown does not consume the other configured family timers");
        var bow = Drop("arm_crossbow"); Check(remote.TryStart(bow), "Small crossbow remains retrievable"); yield return Complete();
        Check(shots.Count == countBefore && effects.LastBarrageAxes == 0, "Shared Projectile subtype does not misidentify a small crossbow as an axe");
        effects.BarrageProjectileCreated -= created; CombatVisualEvents.ShotReleased -= regular; CombatVisualEvents.RetrievalShotReleased -= retrieval;
        yield return WallsTeamsAndExpiry(); yield return Cancellation();
        foreach (var item in owned) if (item != null) Object.Destroy(item); yield return Frames(4);
    }
    static IEnumerator WallsTeamsAndExpiry()
    {
        yield return Player(40); gate.bypassCooldownForTesting = false;
        var friend = Target(Vector3.forward * 3.5f, CombatTeam.Player); var behindFriend = Target(Vector3.forward * 7);
        var left = Target(Vector3.left * 4.5f); var back = Target(Vector3.back * 4.5f); var shielded = Target(Vector3.right * 4.5f);
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(wall);var feet=player.transform.position;feet.y=player.GetComponent<CharacterController>().bounds.min.y;
        wall.transform.position = feet + new Vector3(.65f, 1.5f, 0); wall.transform.localScale = new Vector3(.1f, 3, 1.4f);
        var shots = new List<MechProjectile>(); int impacts = 0;
        effects.BarrageProjectileCreated += (s, d) => { shots.Add(s); owned.Add(s.gameObject); s.ImpactResolved += _ => impacts++; };
        float hp = player.GetComponent<CombatHealth>().CurrentHealth; var axe = Drop();
        Check(remote.TryStart(axe), "Wall/team barrage fixture starts"); yield return Complete(); yield return ShotsFinished(shots);
        Check(left.CurrentHealth == 450 && back.CurrentHealth == 450 && friend.CurrentHealth == 500 && behindFriend.CurrentHealth == 500,
            "Unblocked axes damage enemies while friendly bodies block without friendly fire");
        Check(shielded.CurrentHealth == 500 && player.GetComponent<CombatHealth>().CurrentHealth == hp, "Center-to-muzzle sweep prevents firing through a nearby wall and excludes owner");
        Check(shots.Count == 24 && shots.All(s => s == null || !s.IsLive) && impacts <= 24, "Impacted and empty-direction axes clean up within their finite authored range/lifetime");
        Check(gate.Remaining(RetrievalWeaponFamily.ThrowingAxe) > 28 && !gate.IsReserved(RetrievalWeaponFamily.ThrowingAxe), "Normal axe family cooldown commits once on successful arrival");
    }
    static IEnumerator Cancellation()
    {
        yield return Player(80); gate.bypassCooldownForTesting = false; int spawned = 0; effects.BarrageProjectileCreated += (s, d) => spawned++;
        var far = Drop(distance: 24); Check(!remote.TryStart(far) && gate.IsReady(RetrievalWeaponFamily.ThrowingAxe), "Rejected out-of-range axe consumes no bonus reservation");
        var hover = Drop(); Check(remote.TryStart(hover), "Axe anticipation reserves its bonus");
        Check(gate.IsReserved(RetrievalWeaponFamily.ThrowingAxe) && gate.Remaining(RetrievalWeaponFamily.ThrowingAxe) == 0, "Axe holds a pending token without starting its timer before arrival"); remote.Cancel();
        Check(spawned == 0 && !hover.IsRetrieving && gate.IsReady(RetrievalWeaponFamily.ThrowingAxe), "Cancelled hover spawns no projectiles and releases the pending bonus");
        var flying = Drop(); Check(remote.TryStart(flying), "Axe flight cancellation fixture starts");
        while (remote.IsBusy && !remote.HasDeparted) yield return null; remote.Cancel();
        Check(spawned == 0 && !flying.IsRetrieving && gate.IsReady(RetrievalWeaponFamily.ThrowingAxe), "Cancelled fast flight leaves the arrival barrage ready");
        var disabled = Drop(); Check(remote.TryStart(disabled), "Axe listener-disable fixture starts"); effects.enabled = false; yield return Complete(); effects.enabled = true;
        Check(disabled.isEquipped && spawned == 0 && gate.IsReady(RetrievalWeaponFamily.ThrowingAxe), "Disabled effect releases pending token and cannot replay the barrage after re-enable");
        var blocked = Drop(); Check(remote.TryStart(blocked), "Axe terrain cancellation fixture starts");
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(wall); wall.transform.position = player.transform.position + new Vector3(0, 1.5f, 10); wall.transform.localScale = new Vector3(8, 3, .2f);
        Physics.SyncTransforms(); yield return Complete(); wall.SetActive(false);
        Check(!blocked.isEquipped && !blocked.IsRetrieving && spawned == 0 && gate.IsReady(RetrievalWeaponFamily.ThrowingAxe), "New terrain obstruction cancels the pull without a barrage or cooldown");
        var dead = Drop(); Check(remote.TryStart(dead), "Axe owner-death fixture starts"); player.GetComponent<CombatHealth>().ApplyDamage(100000);
        Check(!dead.IsRetrieving && spawned == 0 && gate.IsReady(RetrievalWeaponFamily.ThrowingAxe), "Owner death refunds the pending axe bonus and creates no projectiles");
    }
    static void Capture()
    {
        string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "RetrievalAxeCaptures"); Directory.CreateDirectory(folder);
        var before = camera.targetTexture; var active = RenderTexture.active; Vector3 position = camera.transform.position; Quaternion rotation = camera.transform.rotation;
        var views = Object.FindObjectsByType<PartSwapWorldView>(); var alphas = views.Select(v => v.Group.alpha).ToArray();
        var target = new RenderTexture(1280, 720, 24); var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        try
        {
            foreach (var view in views) view.SetAlpha(0);
            Vector3 focus = player.transform.position + Vector3.up; Vector3 offset = new Vector3(6, 12, -6); camera.transform.SetPositionAndRotation(focus + offset, Quaternion.LookRotation(-offset)); camera.targetTexture = target;
            Canvas.ForceUpdateCanvases(); RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target }); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply(); File.WriteAllBytes(Path.Combine(folder, "dense-axes.png"), texture.EncodeToPNG());
        }
        finally
        {
            camera.transform.SetPositionAndRotation(position, rotation); camera.targetTexture = before; RenderTexture.active = active; for (int i = 0; i < views.Length; i++) views[i].SetAlpha(alphas[i]); Object.Destroy(texture); Object.Destroy(target);
        }
    }
    static void Finish(Exception error)
    {
        Time.timeScale = 1; PreMissionReliabilityChecks.EndLogAudit(); SessionState.SetBool(Pending, false); if (error != null) Debug.LogException(error);
        Debug.Log("RETRIEVAL AXE CHECKS " + (error == null ? "COMPLETE: " : "FAILED: ") + checks); EditorApplication.Exit(error == null ? 0 : 1);
    }
}
