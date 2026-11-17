using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Uses the existing combat arena in an unsaved, isolated project. No source scene changes.
public static class WeaponBalanceChecks
{
    const string Pending = "WeaponBalanceChecks.Pending";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static GameObject player;
    static MechCombat combat;
    static EquipManager equipment;
    static Camera view;
    static MeleeBoxPreview preview;
    static bool capturePending;
    static double captureDeadline;
    static readonly List<GameObject> dummies = new List<GameObject>();
    static readonly Vector3 Origin = new Vector3(1000, 6, 1000);
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Debug.Log("WEAPON BALANCE PASS: " + message); }
    static bool Near(float a, float b) => Mathf.Abs(a - b) < .01f;
    static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Private).SetValue(obj, value);
    static void Call(object obj, string method) => obj.GetType().GetMethod(method, Private).Invoke(obj, null);
    static void Step(MechProjectile shot, float dt) => typeof(MechProjectile).GetMethod("Step", Private).Invoke(shot, new object[] { dt });
    [InitializeOnLoadMethod] static void Register()
    {
        EditorApplication.playModeStateChanged += state =>
        { if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false)) Run(); };
        EditorApplication.update += CaptureWhenReady;
    }
    public static void RunBatch()
    {
        if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).Contains(".quest-review")) throw new Exception("Disposable project only.");
        EditorSceneManager.OpenScene(GreyboxArmWeaponSetup.Arena); SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static Part Equip(string path, PartCategory slot)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path); var part = Object.Instantiate(prefab).GetComponent<Part>(); part.KeepAuthoredStats();
        Check(equipment.TryEquip(part, slot), "Existing physical prefab equips through normal sockets: " + part.data.partName);
        foreach (var loose in Object.FindObjectsByType<Part>()) if (!loose.isEquipped && loose.GetComponentInParent<CombatHealth>() == null) loose.gameObject.SetActive(false);
        return part;
    }
    static CombatHealth Dummy(Vector3 point, Vector3? size = null, CombatTeam team = CombatTeam.Enemy, int extraColliders = 0)
    {
        var root = GameObject.CreatePrimitive(PrimitiveType.Cube); root.name = "Balance target"; root.transform.position = point;
        root.transform.localScale = size ?? new Vector3(.5f, 3, .5f);
        var health = root.AddComponent<CombatHealth>(); health.Team = team; health.SetMaximum(1000, true);
        for (int i = 0; i < extraColliders; i++) { var child = new GameObject("Compound collider"); child.transform.SetParent(root.transform, false); child.transform.localPosition = Vector3.forward * (i * .015f); child.AddComponent<BoxCollider>(); }
        dummies.Add(root); Physics.SyncTransforms(); return health;
    }
    static void Clear()
    {
        foreach (var dummy in dummies) if (dummy != null) Object.DestroyImmediate(dummy); dummies.Clear();
        foreach (var shot in Object.FindObjectsByType<MechProjectile>()) Object.DestroyImmediate(shot.gameObject);
        combat.Melee.Cancel(); TestMech.Advance(player, 20); Physics.SyncTransforms();
    }
    static void Run()
    {
        SessionState.SetBool(Pending, false); AudioListener.volume = 0;
        try
        {
            PreMissionReliabilityChecks.BeginLogAudit();
            player = Object.FindAnyObjectByType<MechPlayerInput>().gameObject;
            player.GetComponent<MechPlayerInput>().enabled = false; player.GetComponent<CombatFeedback>().enabled = false;
            player.GetComponent<MechMovementController>().enabled = false; player.GetComponent<CharacterController>().enabled = false;
            player.transform.SetPositionAndRotation(Origin, Quaternion.identity);
            combat = player.GetComponent<MechCombat>(); equipment = player.GetComponent<EquipManager>(); preview = player.GetComponent<MeleeBoxPreview>();
            player.GetComponent<CombatHealth>().Team = CombatTeam.Player;
            var head = equipment.GetEquipped(PartCategory.Head); var headData = Object.Instantiate(head.data); head.data = headData;
            headData.stats = new List<StatEntry> { new StatEntry { type = StatType.Accuracy, value = 100 } };
            view = new GameObject("Balance camera").AddComponent<Camera>(); combat.BindCamera(view);
            var targeting = player.GetComponent<MechTargetLockController>(); targeting.BindCamera(view); targeting.EnterFreeAim();
            Piercing(); Shotgun(headData); Dps(); Hitboxes(); Clear();
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                // The EnteredPlayMode callback runs before the first render frame.
                // Warm it before Camera.Render, otherwise a valid query/mesh can
                // produce a silently blank image in batch mode.
                foreach (var director in Object.FindObjectsByType<SpawnDirector>()) director.enabled = false;
                foreach (var agent in Object.FindObjectsByType<EnemyAgent>()) agent.gameObject.SetActive(false);
                capturePending = true; captureDeadline = EditorApplication.timeSinceStartup + 30; return;
            }
            Finish();
        }
        catch (Exception error) { PreMissionReliabilityChecks.EndLogAudit(); Debug.LogException(error); EditorApplication.Exit(1); }
    }
    static void CaptureWhenReady()
    {
        if (!capturePending || !EditorApplication.isPlaying) return;
        try
        {
            if (Time.frameCount < 2)
            { if (EditorApplication.timeSinceStartup > captureDeadline) throw new Exception("Graphics pipeline did not initialize for hitbox capture."); return; }
            capturePending = false; Capture(); Clear(); Finish();
        }
        catch (Exception error) { capturePending = false; PreMissionReliabilityChecks.EndLogAudit(); Debug.LogException(error); EditorApplication.Exit(1); }
    }
    static void Finish()
    {
        PreMissionReliabilityChecks.AssertNoUnexpectedErrors(); PreMissionReliabilityChecks.EndLogAudit();
        Debug.Log("WEAPON BALANCE CHECKS COMPLETE: " + checks); EditorApplication.Exit(0);
    }
    static MechProjectile Fire(Part part, PartCategory slot = PartCategory.ShoulderR)
    {
        view.transform.SetPositionAndRotation(part.muzzle.position, Quaternion.identity); TestMech.Advance(player, 10); Physics.SyncTransforms();
        var shot = combat.TryAttack(slot).Projectile; Check(shot != null && shot.IsLive, "Normal attack launches a live projectile"); return shot;
    }
    static void Piercing()
    {
        var part = Equip("Assets/Prefabs/Mech/Base/Parts/shoulder_crossbow.prefab", PartCategory.ShoulderR); var data = part.Stats;
        Check(data.partType == PartType.Shoulder && data.subtype == PartSubtype.Projectile && data.piercingProjectile && data.maximumPiercedTargets == 0
            && data.GetStat(StatType.Damage) == 180 && data.GetStat(StatType.SplashDamage) == 0 && data.GetStat(StatType.SplashRadius) == 0
            && data.GetStat(StatType.ProjectileSpeed) == 160 && Near(data.GetStat(StatType.ProjectileSize), .07f), "Large Crossbow keeps its shoulder slot, speed and size, with 180 direct-only damage and unlimited piercing");
        Vector3 muzzle = part.muzzle.position;
        var first = Dummy(muzzle + Vector3.forward * 5, extraColliders: 18); var second = Dummy(muzzle + Vector3.forward * 5.3f);
        var third = Dummy(muzzle + Vector3.forward * 15); var side = Dummy(muzzle + new Vector3(1.5f, 0, 5));
        var shot = Fire(part); int impacts = 0, finished = 0; shot.ImpactResolved += impact => { impacts++; Check(impact.Radius == 0 && impact.Hits == 1, "Piercing hit reports one direct impact without any AoE"); }; shot.Finished += _ => finished++;
        Step(shot, .2f);
        Check(new[] { first, second, third }.All(h => Near(h.CurrentHealth, 820)) && side.CurrentHealth == 1000 && impacts == 3 && finished == 0 && shot.IsLive,
            "Fast bolt pierces close/compound targets once each, expands the cast buffer, and leaves off-axis enemies untouched");
        Step(shot, .4f); Check(!shot.IsLive && finished == 1, "Piercing still expires at the normal 80m travel limit, with one finish event"); Clear();

        first = Dummy(muzzle + Vector3.forward * 5, extraColliders: 3); second = Dummy(muzzle + Vector3.forward * 10);
        first.SetMaximum(100, true); second.SetMaximum(100, true); shot = Fire(part); Step(shot, .2f);
        Check(first.IsDead && second.IsDead && shot.IsLive, "Killing/deactivating compound enemies mid-cast preserves subsequent contacts; first=" + first.CurrentHealth + ", second=" + second.CurrentHealth + ", live=" + shot.IsLive); Clear();

        first = Dummy(muzzle + Vector3.forward * 4, new Vector3(1, 3, 8), extraColliders: 3); shot = Fire(part);
        for (int i = 0; i < 12; i++) Step(shot, .002f);
        Check(Near(first.CurrentHealth, 820) && shot.IsLive, "Remaining inside a thick compound enemy across overlap/cast frames never damages it twice"); Clear();

        first = Dummy(muzzle + Vector3.forward * 5); second = Dummy(muzzle + Vector3.forward * 15);
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position = muzzle + Vector3.forward * 10; wall.transform.localScale = new Vector3(5, 8, .1f); dummies.Add(wall);
        shot = Fire(part); Step(shot, .2f);
        Check(Near(first.CurrentHealth, 820) && second.CurrentHealth == 1000 && !shot.IsLive, "A thin wall stops a fast bolt before targets behind it in the same swept step"); Clear();

        first = Dummy(muzzle + Vector3.forward * 5, team: CombatTeam.Player); second = Dummy(muzzle + Vector3.forward * 10); shot = Fire(part); Step(shot, .2f);
        Check(first.CurrentHealth == 1000 && second.CurrentHealth == 1000 && !shot.IsLive, "Friendly bodies keep existing blocking behavior and cannot take friendly fire"); Clear();

        first = Dummy(muzzle + Vector3.forward * 5, team: CombatTeam.Neutral); second = Dummy(muzzle + Vector3.forward * 10); shot = Fire(part); Step(shot, .2f);
        Check(Near(first.CurrentHealth, 820) && second.CurrentHealth == 1000 && !shot.IsLive, "Damageable neutral scenery takes its normal direct hit but still blocks piercing"); Clear();

        first = Dummy(muzzle + Vector3.forward * 79); second = Dummy(muzzle + Vector3.forward * 81); shot = Fire(part); Step(shot, 1);
        Check(Near(first.CurrentHealth, 820) && second.CurrentHealth == 1000 && !shot.IsLive, "Enemies inside range are hit while enemies beyond 80m are not"); Clear();

        first = Dummy(muzzle + Vector3.forward * 5); second = Dummy(muzzle + Vector3.forward * 10); third = Dummy(muzzle + Vector3.forward * 15);
        shot = Fire(part); shot.ConfigurePiercing(true, 2); Step(shot, .2f);
        Check(Near(first.CurrentHealth, 820) && Near(second.CurrentHealth, 820) && third.CurrentHealth == 1000 && !shot.IsLive, "Configurable limit stops the bolt on its second distinct target"); Clear();
    }

    static void Shotgun(PartData headData)
    {
        var part = Equip(GreyboxArmWeaponSetup.PrefabPath(4), PartCategory.ArmL); Vector3 muzzle = part.muzzle.position;
        var config = part.GetComponent<GreyboxArmWeapon>();
        Check(config.ProjectileCount == 6 && config.pelletSpreadAngle == 8, "Existing Shotgun has six pellets and a separately configurable eight-degree half-angle");
        var random = UnityEngine.Random.state;
        foreach (float distance in new[] { 4f, 20f })
        {
            view.transform.SetPositionAndRotation(muzzle, Quaternion.identity); Physics.SyncTransforms();
            var enemy = Dummy(muzzle + Vector3.forward * distance, new Vector3(.1f, 1, .1f)); enemy.gameObject.AddComponent<MechTargetable>();
            var targeting = player.GetComponent<MechTargetLockController>(); targeting.ResumeTargeting(); Call(targeting, "LateUpdate");
            var rays = new List<MechProjectile>(); Action<MechProjectile> collect = s => rays.Add(s); combat.ShotFired += collect;
            UnityEngine.Random.InitState(1771); TestMech.Advance(player, 10); combat.TryAttack(PartCategory.ArmL); combat.ShotFired -= collect;
            float widest = rays.Max(s => Vector3.Angle(s.Direction, Vector3.forward));
            Check(rays.Count == 6 && widest > 4 && widest <= 8.01f, "Pellet cone stays wide at " + distance + "m with perfect head precision; widest=" + widest);
            targeting.EnterFreeAim(); Clear();
        }
        headData.stats[0] = new StatEntry { type = StatType.Accuracy, value = 20 };
        view.transform.SetPositionAndRotation(muzzle, Quaternion.identity);
        Vector3 center = AimSolver.AimPoint(null, view, player.transform, 24, ~0, out bool contact);
        UnityEngine.Random.InitState(5119); Vector3 attackCenter = AccuracySpread.RandomAimPoint(view, center, combat.ShotSpreadAngleOf(part, center, contact));
        var expected = new Vector3[6]; for (int i = 0; i < 6; i++) expected[i] = AccuracySpread.RandomConeDirection(attackCenter - muzzle, 8);
        var actual = new List<MechProjectile>(); Action<MechProjectile> save = s => actual.Add(s); combat.ShotFired += save;
        UnityEngine.Random.InitState(5119); TestMech.Advance(player, 10); combat.TryAttack(PartCategory.ArmL); combat.ShotFired -= save;
        Check(actual.Count == 6 && actual.Select((s, i) => Vector3.Angle(s.Direction, expected[i]) < .05f).All(b => b), "Head error is sampled once for the shared blast center, then six independent pellet directions");
        headData.stats[0] = new StatEntry { type = StatType.Accuracy, value = 100 }; UnityEngine.Random.state = random; Clear();
    }
    static void Dps()
    {
        float Sustained(PartData d, float damage) => damage * d.MagazineSize / ((d.MagazineSize - 1) * d.ShotInterval + Mathf.Max(d.ShotInterval, d.GetStat(StatType.Reload)));
        var large = AssetDatabase.LoadAssetAtPath<PartData>("Assets/PartCatalogue/PartData_Shoulder_LargeCrossbow.asset");
        Check(Near(Sustained(large, 180), 72), "Large Crossbow sustains 72 single-target DPS");
        var mini = AssetDatabase.LoadAssetAtPath<PartData>(GreyboxArmWeaponSetup.DataPath(0));
        var shotgun = AssetDatabase.LoadAssetAtPath<PartData>(GreyboxArmWeaponSetup.DataPath(4));
        var metal = AssetDatabase.LoadAssetAtPath<PartData>(GreyboxArmWeaponSetup.DataPath(8));
        Check(Near(Sustained(mini, 9), 65.21739f) && Near(Sustained(shotgun, 150), 93.75f) && Near(Sustained(metal, 90), 67.5f), "Minigun, Shotgun and Metal Blades match the requested reload-aware DPS");
        foreach (var pair in new[] { (3,90f), (5,87.5f), (6,65f) })
        { var data = AssetDatabase.LoadAssetAtPath<PartData>(GreyboxArmWeaponSetup.DataPath(pair.Item1)); Check(Near(data.GetStat(StatType.Damage) * data.GetStat(StatType.FireRate), pair.Item2), data.partName + " matches the requested direct melee DPS"); }
    }

    static void Hitboxes()
    {
        foreach (string path in new[] { TestMech.PlayerPath, "Assets/Prefabs/Mech/Specialist/FullPlayer_mech_specialist.prefab" })
            Check(!(bool)typeof(MeleeBoxPreview).GetField("showMeleeBox", Private).GetValue(AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<MeleeBoxPreview>()), "Melee debugging remains off by default on " + path);
        Set(preview, "showMeleeBox", true);
        var paths = new[] { "Assets/Prefabs/Mech/Base/Parts/arm_sword.prefab", "Assets/Prefabs/Mech/Base/Parts/arm_mace.prefab",
            GreyboxArmWeaponSetup.PrefabPath(2), GreyboxArmWeaponSetup.PrefabPath(3), GreyboxArmWeaponSetup.PrefabPath(5), GreyboxArmWeaponSetup.PrefabPath(6), GreyboxArmWeaponSetup.PrefabPath(7) };
        for (int i = 0; i < paths.Length; i++)
        {
            var part = Equip(paths[i], PartCategory.ArmL);
            player.transform.SetPositionAndRotation(Origin + Vector3.right * i * 20, Quaternion.Euler(0, i * 17, 0));
            var planned = combat.Melee.PreviewHitboxes(part, player.transform.rotation); var enemy = Dummy(planned.Center);
            var snapshots = new List<MeleeAttack.HitboxSnapshot>(); Action<MeleeAttack.HitboxSnapshot> record = s => snapshots.Add(s); combat.Melee.HitboxesResolved += record;
            TestMech.Advance(player, 20); combat.TryAttack(PartCategory.ArmL);
            if (part.GetComponent<GreyboxArmWeapon>()?.weapon == GreyboxArmWeapon.Weapon.PunchyArm) { combat.Melee.Tick(.2f); combat.Melee.Tick(.2f); }
            combat.Melee.HitboxesResolved -= record; Call(preview, "LateUpdate"); var actual = snapshots.Last();
            Check(snapshots.Count == (i == 6 ? 3 : 1) && Vector3.Distance(actual.Center, planned.Center) < .001f && actual.Size == planned.Size
                && Quaternion.Angle(actual.Rotation, planned.Rotation) < .01f && Near(actual.SplashRadius, planned.SplashRadius), part.data.partName + " captures the exact real box/rotation/secondary sphere for every hit event");
            float expectedDamage = part.Stats.GetStat(StatType.Damage) * (i == 6 ? 3 : 1);
            Check(Near(enemy.CurrentHealth, 1000 - expectedDamage), part.data.partName + " debug visualization leaves damage unchanged and does not stack splash");
            var debugRoot = (GameObject)typeof(MeleeBoxPreview).GetField("root", Private).GetValue(preview);
            var displayed = debugRoot.GetComponentsInChildren<MeshRenderer>().FirstOrDefault(r => r.name.Contains("Direct") && Vector3.Distance(r.transform.position, actual.Center) < .001f);
            Check(displayed != null && displayed.transform.lossyScale == actual.Size && Quaternion.Angle(displayed.transform.rotation, actual.Rotation) < .01f
                && debugRoot.GetComponentsInChildren<Collider>(true).All(c => !c.enabled), part.data.partName + " Game-view mesh matches actual query dimensions and never blocks physics");
            if (actual.SplashRadius > 0) Check(debugRoot.GetComponentsInChildren<MeshRenderer>().Any(r => r.name.Contains("Splash") && Near(r.transform.lossyScale.x, actual.SplashRadius * 2)
                && Vector3.Distance(r.transform.position, actual.SplashCenter) < .001f), part.data.partName + " draws its separate orange secondary area at the real query position");
            Vector3 retained = displayed.transform.position; player.transform.position += Vector3.right * 3; player.transform.rotation = Quaternion.Euler(0, 135, 0); Call(preview, "LateUpdate");
            Check(displayed.transform.position == retained, "Resolved " + part.data.partName + " hitbox stays at its actual world hit position as the player moves"); Clear();
        }
        Set(preview, "showMeleeBox", false); Call(preview, "LateUpdate");
        var root = (GameObject)typeof(MeleeBoxPreview).GetField("root", Private).GetValue(preview);
        Check(!root.activeSelf, "Inspector toggle completely hides the reusable debug objects");
        Set(preview, "showMeleeBox", true); Set(preview, "showHitboxesOnlyDuringAttacks", false); Call(preview, "LateUpdate");
        Check(root.GetComponentsInChildren<MeshRenderer>().Any(r => r.name == "Idle 1 Direct"), "Optional continuous preview uses the current equipped weapon even between attacks");
        Set(preview, "showHitboxesOnlyDuringAttacks", true);
    }
    static void Capture()
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        foreach (var canvas in Object.FindObjectsByType<Canvas>()) canvas.gameObject.SetActive(false);
        player.transform.SetPositionAndRotation(Origin, Quaternion.identity); Equip(GreyboxArmWeaponSetup.PrefabPath(2), PartCategory.ArmL);
        TestMech.Advance(player, 20); Check(combat.TryAttack(PartCategory.ArmL).Started, "Render fixture performs a real Hammer attack"); Call(preview, "LateUpdate");
        view.transform.SetPositionAndRotation(Origin + new Vector3(8, 11, -10), Quaternion.LookRotation(new Vector3(-8, -10, 14)));
        view.backgroundColor = new Color(.05f, .07f, .08f); view.clearFlags = CameraClearFlags.SolidColor; view.fieldOfView = 55;
        var target = new RenderTexture(960, 540, 24); view.targetTexture = target; view.Render();
        var previous = RenderTexture.active; RenderTexture.active = target; var texture = new Texture2D(960, 540, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); texture.Apply(); RenderTexture.active = previous;
        var pixels = texture.GetPixels32();
        Check(pixels.Count(p => p.r > 30 && p.r > p.g * 1.4f && p.r > p.b * 1.4f) > 1000
            && pixels.Count(p => p.r > p.g * 1.3f && p.g > 20 && p.g > p.b * 1.3f) > 1000,
            "Game-view capture contains substantial red direct and orange splash geometry, not a blank render");
        File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath, "../weapon-balance-melee-hitboxes.png")), texture.EncodeToPNG());
        view.targetTexture = null; target.Release(); Object.DestroyImmediate(texture); Object.DestroyImmediate(target);
    }
}
