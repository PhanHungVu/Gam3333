using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

// Real existing scene, HUD, bench keys, shoulder actions and physical Zoo pickups.
public static class ShoulderWeaponArenaChecks
{
    const string Pending = "ShoulderWeaponArenaChecks.Pending";
    static readonly Stack<IEnumerator> work = new Stack<IEnumerator>();
    static int checks, frame = -1;
    static bool running, pendingInput;
    static double deadline;
    static Keyboard keyboard;
    static KeyboardState keyState;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Debug.Log("SHOULDER ARENA PASS: " + message); }
    [InitializeOnLoadMethod] static void Register() => EditorApplication.update += Tick;
    public static void RunBatch()
    {
        if (!Application.isBatchMode || !Application.dataPath.Contains(".quest-review")) throw new Exception("Disposable project only.");
        EditorSceneManager.OpenScene(GreyboxZooWeaponSetup.Zoo); Physics.SyncTransforms();
        var group = GameObject.Find("parts").transform.Find(GreyboxShoulderWeaponSetup.ZooGroup);
        Check(group != null && group.GetComponentsInChildren<Part>().Length == 7, "Zoo has seven separate physical new Shoulder pickups beside existing weapons");
        foreach (var part in group.GetComponentsInChildren<Part>())
        {
            var cube = part.transform.Find("Cube").GetComponent<Renderer>().bounds;
            bool floor = Physics.RaycastAll(cube.center + Vector3.up * 3, Vector3.down, 30, ~0, QueryTriggerInteraction.Ignore)
                .Any(h => h.collider.GetComponentInParent<Part>() == null && h.point.y <= cube.min.y + .2f);
            Check(floor && part.GetComponent<Rigidbody>() != null && part.GetComponent<GreyboxShoulderWeapon>().keepAuthoredStats,
                "Zoo " + part.data.partName + " has a floor, loose-body physics and exact test stats");
        }
        SessionState.SetInt(Pending + ".Initial", checks);
        EditorSceneManager.OpenScene(GreyboxArmWeaponSetup.Arena); SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Pending,false) || !EditorApplication.isPlaying || EditorApplication.isCompiling || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (!running) { running = true; checks = SessionState.GetInt(Pending + ".Initial",0); deadline = EditorApplication.timeSinceStartup + 150; PreMissionReliabilityChecks.BeginLogAudit(); work.Push(Run()); }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Shoulder arena timeout.");
            while (work.Count > 0) { var step = work.Peek(); if (!step.MoveNext()) { work.Pop(); continue; } if (step.Current is IEnumerator child) { work.Push(child); continue; } return; }
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    static void Finish(Exception error)
    {
        SessionState.SetBool(Pending,false); InputSystem.onBeforeUpdate -= Feed; Time.captureFramerate = 0; PreMissionReliabilityChecks.EndLogAudit();
        if (error != null) Debug.LogException(error);
        Debug.Log("SHOULDER ARENA CHECKS " + (error == null ? "COMPLETE: " : "FAILED: ") + checks); EditorApplication.Exit(error == null ? 0 : 1);
    }
    static void Feed()
    {
        if (pendingInput && InputState.currentUpdateType == InputUpdateType.Dynamic) { InputSystem.QueueStateEvent(keyboard, keyState); pendingInput = false; }
    }
    static IEnumerator Frames(int n) { int until = Time.frameCount + n; while (Time.frameCount < until) yield return null; }
    static IEnumerator Keys(params Key[] keys) { keyState = new KeyboardState(keys); pendingInput = true; yield return Frames(2); }
    static void AimAt(MechTargetLockController targeting, CombatHealth target)
    {
        targeting.SetLockMode(MechTargetLockController.LockMode.CameraLockOn);
        typeof(MechTargetLockController).GetMethod("SetTarget", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(targeting, new object[] { target.GetComponent<MechTargetable>() });
    }
    static IEnumerator Run()
    {
        Application.runInBackground = true; Time.captureFramerate = 60; AudioListener.volume = 0;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        var input = Object.FindAnyObjectByType<MechPlayerInput>(); input.enabled = false;
        foreach (var director in Object.FindObjectsByType<SpawnDirector>()) director.enabled = false;
        foreach (var agent in Object.FindObjectsByType<EnemyAgent>()) agent.gameObject.SetActive(false);
        var playtest = Object.FindAnyObjectByType<EnemyBehaviorPlaytest>(); if (playtest != null) playtest.enabled = false;
        yield return Frames(4);
        var player = input.gameObject; player.GetComponent<CombatFeedback>().enabled = false;
        var combat = player.GetComponent<MechCombat>(); var bench = Object.FindAnyObjectByType<GreyboxArmWeaponBench>();
        Check(bench != null && bench.shoulderCatalogue != null && bench.shoulderCatalogue.weapons.Length == 9, "Existing arena bench is connected to all nine Shoulder weapons");
        bench.player = player.GetComponent<EquipManager>(); bench.shoulderMode = true; bench.keyboardShortcuts = false;
        for (int i = 0; i < 9; i++)
        {
            bench.selected = i; Check(bench.Equip(PartCategory.ShoulderL), "Existing bench equips left Shoulder " + bench.Selected.data.partName);
            yield return Frames(2);
            var part = bench.player.GetEquipped(PartCategory.ShoulderL);
            var pod = Object.FindObjectsByType<WeaponsHudPod>().FirstOrDefault(p => p.Weapon == part);
            Check(pod != null && pod.CurrentIcon == part.Stats.icon && !part.HasRolledStats && WeaponsHudPod.KeyLabel(part.Slot.Value) == "Q", "Shared HUD reads the real Shoulder part, icon, Q binding and exact test stats");
        }
        keyboard = InputSystem.AddDevice<Keyboard>(); InputSystem.onBeforeUpdate += Feed;
        bench.keyboardShortcuts = true; bench.selected = 2;
        yield return Keys(Key.RightBracket); yield return Keys(); Check(bench.selected == 3, "Existing bracket key selects a Shoulder weapon in Shoulder mode");
        yield return Keys(Key.Period); yield return Keys(); Check(bench.player.GetEquipped(PartCategory.ShoulderR).data == bench.Selected.data, "Existing period key equips the right Shoulder, preserving Arm slots");
        yield return Keys(Key.Backslash); yield return Keys(); Check(Object.FindObjectsByType<Part>().Any(p => !p.isEquipped && p.data == bench.Selected.data && !p.HasRolledStats), "Existing backslash key drops a separate physical Shoulder pickup");
        bench.keyboardShortcuts = false; bench.ClearLooseWeapons(); yield return Frames(3);
        input.AssumeCursorCaptured = true; input.enabled = true;
        var detector = player.GetComponentInChildren<PickupDetector>();
        var originalArms = new[] { bench.player.GetEquipped(PartCategory.ArmL), bench.player.GetEquipped(PartCategory.ArmR) };
        for (int i = 2; i < 9; i++)
        {
            bench.selected = i; var pickup = bench.SpawnSelected();
            // Put this disposable physical item inside the actual pickup trigger.
            // The bench's three-metre drop offset is not a promise of pickup reach.
            pickup.GetComponent<Rigidbody>().isKinematic = true;
            pickup.transform.position += detector.GetComponent<Collider>().bounds.center - pickup.GetComponentInChildren<Collider>().bounds.center;
            Physics.SyncTransforms(); yield return Frames(8);
            for (int attempt = 0; bench.player.GetSelectedPart() != pickup && attempt < 32; attempt++)
            { detector.CycleSelection(); yield return Frames(2); }
            Check(detector.NearbyParts.Contains(pickup) && bench.player.GetSelectedPart() == pickup,
                "Normal nearby detection selects the physical " + pickup.data.partName + " pickup");
            bool left = i % 2 == 0; yield return Keys(left ? Key.Digit1 : Key.Digit2); yield return Keys();
            Check(pickup.isEquipped && pickup.EquippedBy == bench.player
                && bench.player.GetEquipped(left ? PartCategory.ShoulderL : PartCategory.ShoulderR) == pickup
                && bench.player.GetEquipped(PartCategory.ArmL) == originalArms[0] && bench.player.GetEquipped(PartCategory.ArmR) == originalArms[1],
                "Real " + (left ? "1" : "2") + " swaps " + pickup.data.partName + " into its Shoulder without replacing an Arm");
            bench.ClearLooseWeapons(); yield return Frames(3);
        }
        bench.selected = 6; bench.EquipLeft(); bench.ClearLooseWeapons(); yield return Frames(3);
        var katyusha = bench.player.GetEquipped(PartCategory.ShoulderL); yield return Keys(); yield return Frames(3);
        int before = katyusha.RemainingAmmo;
        int shotCount=0;var salvoFrames=new HashSet<int>();Action<MechProjectile> salvoShot=s=>{if(s.Source==katyusha){shotCount++;salvoFrames.Add(Time.frameCount);}};
        combat.ShotFired+=salvoShot;yield return Keys(Key.Q);
        Check(before - katyusha.RemainingAmmo == 10 && katyusha.IsReloading && katyusha.MagazineRounds == 10,
            "One real Q press launches a full ten-round Katyusha magazine and immediately starts reload");
        Check(shotCount==10&&salvoFrames.Count==1,"All ten input-driven rockets launch in the same simulation frame");
        yield return Frames(330);
        Check(shotCount==10&&!katyusha.IsReloading&&katyusha.RemainingAmmo==90,"Holding Q past five seconds never repeats the press-activated salvo");
        yield return Keys();yield return Keys(Key.Q);yield return Keys();
        Check(shotCount==20&&katyusha.RemainingAmmo==80,"A second Q press after reload launches exactly one further full salvo");
        combat.ShotFired-=salvoShot;input.enabled=false;
        var targeting=player.GetComponent<MechTargetLockController>();
        foreach(int targetCount in new[]{1,5,15})
        {
            foreach(var shot in Object.FindObjectsByType<MechProjectile>())Object.Destroy(shot.gameObject);
            bench.selected=6;bench.SpawnShoulderTargets(targetCount);yield return Frames(3);
            Check(bench.ShoulderTestTargets.Count==targetCount&&bench.ShoulderTestTargets.All(h=>h.Team==CombatTeam.Enemy&&h.MaximumHealth==10000),
                "The existing bench creates the "+targetCount+"-target repeatable Shoulder comparison without enemy wave entries");
            var aim=bench.ShoulderTestTargets.OrderBy(h=>(h.transform.position-player.transform.position).sqrMagnitude).First();
            AimAt(targeting,aim);TestMech.Advance(player,20);Check(combat.TryAttack(PartCategory.ShoulderL).Started,"Katyusha fires into the "+targetCount+"-target test setup");
            yield return Frames(4);CaptureSalvo(player,targetCount);yield return Frames(70);
            float damage=bench.ShoulderTestTargets.Sum(h=>h.MaximumHealth-h.CurrentHealth);
            Check(damage>0&&bench.ShoulderTestTargets.All(h=>h.MaximumHealth-h.CurrentHealth<=400.01f),
                "Each enemy stays within ten legitimate rocket hits in the "+targetCount+"-target encounter; total damage="+damage);
            Debug.Log("SHOULDER BALANCE ENCOUNTER: Katyusha; targets="+targetCount+"; total damage="+damage+"; nominal encounter DPS="+(damage/5));
        }
        bench.ClearShoulderTargets();yield return Frames(2);
        bench.selected=3;bench.SpawnShoulderTargets(5);bench.EquipLeft();yield return Frames(3);
        Check(combat.TryAttack(PartCategory.ShoulderL).Started,"The ring comparison uses the same actual Shoulder attack path");yield return Frames(65);
        Check(bench.ShoulderTestTargets.Take(4).All(h=>Mathf.Abs(h.CurrentHealth-9790)<.01f)&&bench.ShoulderTestTargets[4].CurrentHealth==10000,
            "The live arena ring deals 210 to its four band targets and spares its safe-center control");
        bench.ClearShoulderTargets();yield return Frames(2);
        bench.selected=8;bench.EquipRight();var tesla=bench.player.GetEquipped(PartCategory.ShoulderR);
        foreach(int targetCount in new[]{1,3,5})
        {
            bench.SpawnShoulderTargets(targetCount==3?5:targetCount);
            for(int i=targetCount;i<bench.ShoulderTestTargets.Count;i++)bench.ShoulderTestTargets[i].gameObject.SetActive(false);
            yield return Frames(3);AimAt(targeting,bench.ShoulderTestTargets[0]);TestMech.Advance(player,20);
            Check(combat.TryAttack(PartCategory.ShoulderR).Started&&tesla.GetComponent<GreyboxShoulderWeapon>().LastHitCount==targetCount
                &&bench.ShoulderTestTargets.Take(targetCount).All(h=>Mathf.Abs(h.CurrentHealth-9996)<.01f),
                "One Tesla pulse deals four each to the "+targetCount+"-enemy comparison");
        }
        bench.EquipRight();tesla=bench.player.GetEquipped(PartCategory.ShoulderR);bench.SpawnShoulderTargets(5);yield return Frames(3);
        AimAt(targeting,bench.ShoulderTestTargets[0]);var pulseTimes=new List<float>();Action<Part> pulse=p=>{if(p==tesla)pulseTimes.Add(Time.time);};combat.WeaponAttackStarted+=pulse;
        input.enabled=true;yield return Keys();yield return Frames(3);yield return Keys(Key.E);
        int until=Time.frameCount+550;while(pulseTimes.Count<35&&Time.frameCount<until)yield return Frames(1);
        Check(pulseTimes.Count==35&&tesla.RemainingAmmo==35&&tesla.IsReloading&&Mathf.Abs(tesla.CooldownDuration-3)<.01f,
            "Held real E fires 35 Tesla pulses, consuming 35 rounds, then starts the three-second reload");
        float elapsed=pulseTimes[34]-pulseTimes[0];
        Check(elapsed>=6.79f&&elapsed<7.5f,"The 35-pulse magazine lasts about seven seconds at the configured 5/s cadence; measured="+elapsed);
        Check(bench.ShoulderTestTargets.All(h=>Mathf.Abs(h.CurrentHealth-9860)<.01f)
            &&tesla.GetComponentsInChildren<LineRenderer>().Length==5,"Rapid pulses damage five enemies once each and reuse just five beam renderers");
        var podTesla=Object.FindObjectsByType<WeaponsHudPod>().First(p=>p.Weapon==tesla);
        Check(podTesla.IsCoolingDown&&podTesla.CooldownProgress<1,"The rapid weapon still exposes its real reload through the unchanged cooldown HUD");
        yield return Frames(140);Check(pulseTimes.Count==35,"No Tesla pulse escapes the three-second reload");
        yield return Frames(56);Check(pulseTimes.Count>=36&&pulseTimes.Count<=37,"Holding E automatically resumes Tesla after reload");
        yield return Keys();int stopped=pulseTimes.Count;yield return Frames(25);
        Check(pulseTimes.Count==stopped,"Releasing E stops Tesla pulse activation");combat.WeaponAttackStarted-=pulse;input.enabled=false;
        Capture(player, bench, combat);bench.ClearShoulderTargets();
    }
    static void Capture(GameObject player, GreyboxArmWeaponBench bench, MechCombat combat)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        foreach (var canvas in Object.FindObjectsByType<Canvas>()) canvas.gameObject.SetActive(false);
        // These are disposable presentation overrides, not changes to saved tuning.
        bench.selected = 3; bench.EquipLeft(); var ring = bench.player.GetEquipped(PartCategory.ShoulderL).GetComponent<GreyboxShoulderWeapon>(); ring.ringSweepSeconds = 3;
        bench.selected = 8; bench.EquipRight(); var coil = bench.player.GetEquipped(PartCategory.ShoulderR).GetComponent<GreyboxShoulderWeapon>(); coil.lightningSeconds = 3;
        TestMech.Advance(player,10); combat.TryAttack(PartCategory.ShoulderL); combat.TryAttack(PartCategory.ShoulderR);
        var camera = new GameObject("Shoulder greybox preview").AddComponent<Camera>(); camera.transform.position = player.transform.position + new Vector3(18,35,-42); camera.transform.LookAt(player.transform.position + Vector3.forward*5);
        camera.fieldOfView = 65; camera.backgroundColor = new Color(.04f,.055f,.07f); camera.clearFlags = CameraClearFlags.SolidColor;
        var target = new RenderTexture(1280,720,24); camera.targetTexture = target; camera.Render();
        var previous=RenderTexture.active;RenderTexture.active=target;var texture=new Texture2D(1280,720,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,1280,720),0,0);texture.Apply();RenderTexture.active=previous;
        File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../shoulder-greybox-ring-tesla.png")),texture.EncodeToPNG());
        camera.targetTexture=null;target.Release();Object.Destroy(texture);Object.Destroy(target);Object.Destroy(camera.gameObject);
    }
    static void CaptureSalvo(GameObject player,int count)
    {
        if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)return;
        var camera=new GameObject("Salvo comparison camera").AddComponent<Camera>();camera.transform.position=player.transform.position+new Vector3(16,24,-8);
        camera.transform.LookAt(player.transform.position+player.transform.forward*15);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.04f,.055f,.07f);
        var target=new RenderTexture(1280,720,24);camera.targetTexture=target;camera.Render();var previous=RenderTexture.active;RenderTexture.active=target;
        var texture=new Texture2D(1280,720,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,1280,720),0,0);texture.Apply();RenderTexture.active=previous;
        File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../shoulder-salvo-"+count+"-targets.png")),texture.EncodeToPNG());
        camera.targetTexture=null;target.Release();Object.Destroy(texture);Object.Destroy(target);Object.Destroy(camera.gameObject);
    }
}
