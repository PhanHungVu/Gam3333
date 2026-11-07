using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class SpecialistPlayerIntegrationChecks
{
    const string Pending = "SpecialistPlayerIntegrationChecks.Pending";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    static int frame = -1, checks; static bool running; static double deadline;
    static void Check(bool valid, string message) { if (!valid) throw new Exception(message); checks++; Debug.Log("SPECIALIST PASS: " + message); }
    static string Hash(string path) { using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path))); }
    static IEnumerator Frames(int n = 4) { int end = Time.frameCount + n; while (Time.frameCount < end) yield return null; }
    static string SocketSignature(GameObject root) => string.Join("\n", root.GetComponentsInChildren<Socket>(true).OrderBy(s => s.socketType).Select(s =>
        s.socketType + ":" + AnimationUtility.CalculateTransformPath(s.transform, root.transform) + ":" + s.transform.localPosition.ToString("R") + ":" + s.transform.localRotation.ToString("R") + ":" + s.transform.localScale.ToString("R")));
    static string RootIds(GameObject root) => string.Join(";", root.GetComponents<Component>().Select(c =>
    { AssetDatabase.TryGetGUIDAndLocalFileIdentifier(c, out string guid, out long id); return c.GetType().Name + ":" + id; }));
    [InitializeOnLoadMethod] static void Register() => EditorApplication.update += Tick;
    public static void RunBatch()
    {
        if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new Exception("Disposable project only");
        var protectedPaths = new[] { "Assets/PartCatalogue", "Assets/Models/Specialist", "Assets/Prefabs/Enemies", "Assets/LevelCatalogue", "Assets/Prefabs/Mech/Base/Parts" }
            .SelectMany(p => Directory.GetFiles(p, "*", SearchOption.AllDirectories)).Where(p => !p.EndsWith(".meta")
                && p.Replace('\\','/') != SpecialistPlayerIntegrationSetup.SmallCrossbowPath
                && p.Replace('\\','/') != "Assets/Prefabs/Mech/Base/Parts/shoulder_cannon.prefab").Concat(new[] {
                "Assets/Prefabs/Mech/Base/FullPlayer_mech_heavy.prefab", GameplaySceneMaintenance.CursorPath,
                GameplaySceneMaintenance.CombatHudPath, "Assets/Anim/Locomotion/Biped_Fast.controller", "Assets/Anim/Specialist/Cannon_Specialist.asset" }).ToArray();
        var protectedHashes = protectedPaths.Select(Hash).ToArray();
        string sockets = SocketSignature(AssetDatabase.LoadAssetAtPath<GameObject>(SpecialistPlayerIntegrationSetup.PlayerPath));
        string rootIds = RootIds(AssetDatabase.LoadAssetAtPath<GameObject>(SpecialistPlayerIntegrationSetup.LegacyPlayerPath));
        var crossbowShape = CrossbowShape();
        SpecialistPlayerIntegrationSetup.Apply();
        Check(protectedPaths.Select(Hash).SequenceEqual(protectedHashes), "Heavy demo, model imports, weapon/part stats, unrelated weapon prefabs, mission waves, cannon audio and shared HUD layout are unchanged");
        var normalizedShape = CrossbowShape();
        Check(crossbowShape.Length == normalizedShape.Length && crossbowShape.Zip(normalizedShape, Vector3.Distance).All(d => d < .001f),
            "Normalizing the crossbow rig preserves its neutral mesh geometry and size");
        Check(SocketSignature(AssetDatabase.LoadAssetAtPath<GameObject>(SpecialistPlayerIntegrationSetup.PlayerPath)) == sockets, "The teammate's Specialist socket parents, positions, rotations and scales are preserved");
        Check(SocketSignature(AssetDatabase.LoadAssetAtPath<GameObject>(SpecialistPlayerIntegrationSetup.LegacyPlayerPath)) == sockets, "Regular golem receives the same complete authored socket assembly");
        Check(RootIds(AssetDatabase.LoadAssetAtPath<GameObject>(SpecialistPlayerIntegrationSetup.LegacyPlayerPath)) == rootIds, "Regular golem keeps its root/component file IDs for existing scene references");
        var edited = new[] { SpecialistPlayerIntegrationSetup.PlayerPath, SpecialistPlayerIntegrationSetup.LegacyPlayerPath, "Assets/Scenes/ZooScene.unity", "Assets/Anim/Specialist/Blaster_Specialist.asset",
            SpecialistPlayerIntegrationSetup.SmallCrossbowPath, SpecialistPlayerIntegrationSetup.LargeCrossbowPath, SpecialistPlayerIntegrationSetup.RigidCrossbowMeshPath, "Assets/Prefabs/Mech/Base/Parts/shoulder_cannon.prefab" };
        var after = edited.Select(Hash).ToArray(); SpecialistPlayerIntegrationSetup.Apply();
        Check(edited.Select(Hash).SequenceEqual(after), "Reapplying the integration is idempotent");
        Check(AssetDatabase.LoadAssetAtPath<MechWeaponAnimationSet>("Assets/Anim/Specialist/Blaster_Specialist.asset").attackSound == null,
            "Blaster's unrelated crossbow sound is left as an exposed empty audio connection");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static Vector3[] CrossbowShape()
    {
        var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SpecialistPlayerIntegrationSetup.SmallCrossbowPath)); root.SetActive(false);
        try
        {
            var driver = root.GetComponent<MechWeaponAnimator>(); driver.Animations.neutral.SampleAnimation(driver.Animator.gameObject, 0);
            var filter = driver.Animator.GetComponentsInChildren<MeshFilter>().Single();
            return filter.sharedMesh.vertices.Select(v => root.transform.InverseTransformPoint(filter.transform.TransformPoint(v))).ToArray();
        }
        finally { Object.DestroyImmediate(root); }
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (!running) { running = true; deadline = EditorApplication.timeSinceStartup + 360; PreMissionReliabilityChecks.BeginLogAudit(); steps.Push(Run()); }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Specialist integration timed out");
            while (steps.Count > 0)
            {
                var step = steps.Peek(); if (!step.MoveNext()) { steps.Pop(); continue; }
                if (step.Current is IEnumerator nested) { steps.Push(nested); continue; } return;
            }
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    static void Finish(Exception error)
    {
        SessionState.SetBool(Pending, false); PreMissionReliabilityChecks.EndLogAudit(); Time.captureFramerate = 0;
        if (error != null) Debug.LogException(error);
        Debug.Log("SPECIALIST CHECKS " + (error == null ? "COMPLETE: " : "FAILED: ") + checks); EditorApplication.Exit(error == null ? 0 : 1);
    }
    static IEnumerator Clear()
    {
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) Object.Destroy(root);
        yield return Frames(5);
    }
    static void CheckHud(string context)
    {
        var player = Object.FindAnyObjectByType<MechPlayerRig>();
        Check(Object.FindObjectsByType<LockOnCursor>().Length == 1 && Object.FindObjectsByType<MechHealthHud>().Length == 1
            && Object.FindObjectsByType<MechWeaponsHud>().Length == 1 && Object.FindObjectsByType<MechAbilitiesHud>().Length == 1
            && Object.FindObjectsByType<DirectionalDamageHud>().Length == 1 && Object.FindObjectsByType<MechEnergyHud>().Length == 0,
            context + " has one complete shared HUD with directional damage and no retired stamina UI");
        Check(Object.FindAnyObjectByType<MechWeaponsHud>().Equipment == player.GetComponent<EquipManager>()
            && Object.FindAnyObjectByType<MechHealthHud>().Health == player.GetComponent<CombatHealth>(), context + " HUD binds to the active player");
    }
    static IEnumerator Run()
    {
        Application.runInBackground = true; Time.captureFramerate = 60;
        if (Keyboard.current == null) InputSystem.AddDevice<Keyboard>(); if (Mouse.current == null) InputSystem.AddDevice<Mouse>();
        foreach (string path in new[] { SpecialistPlayerIntegrationSetup.PlayerPath, SpecialistPlayerIntegrationSetup.LegacyPlayerPath })
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.position = Vector3.down * .25f; floor.transform.localScale = Vector3.one * 400; floor.transform.localScale = new Vector3(400, .5f, 400);
            var light = new GameObject("Review light").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(45, -30, 0);
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), new Vector3(0, 4, 0), Quaternion.identity);
            player.GetComponent<MechPlayerInput>().enabled = false; player.GetComponent<MechWeaponIdle>().enabled = false;
            var motor = player.GetComponent<MechMovementController>();
            for (int i = 0; i < 60; i++) { motor.Tick(default(MechLocomotionInput), 1f / 60); yield return Frames(1); }
            CheckHud(player.name);
            var equipment = player.GetComponent<EquipManager>(); var feedback = player.GetComponent<QuickDashFeedback>(); var heading = new GameObject("Review heading").transform;
            Check(motor.CurrentModule is BipedLocomotionModule && equipment.GetEquipped(PartCategory.Legs).GetComponent<MechLegAnimator>().Animator.runtimeAnimatorController.name == "Biped_Specialist"
                && player.GetComponentsInChildren<MeshFilter>().Any(m => m.sharedMesh != null && AssetDatabase.GetAssetPath(m.sharedMesh).EndsWith("/TORSO_Specialist.fbx")), player.name + " runs the new torso/head/legs and Biped animation module");
            motor.Tick(new MechLocomotionInput { quickBoost = true, move = Vector2.up }, heading, .02f);
            Check(motor.IsQuickBoosting && motor.AnimationData.Speed > 47 && feedback.DustBurstCount == 1, "Current explosive dash and accepted-start dust are configured on " + player.name);
            for (int i = 0; i < 40; i++) { motor.Tick(default(MechLocomotionInput), heading, .02f); yield return Frames(1); }
            Check(feedback.DustBurstCount == 6 && feedback.PoolSize == 12, "Six spaced dash puffs finish using the bounded pool");
            Check(equipment.GetEquipped(PartCategory.ArmL) != null && equipment.GetEquipped(PartCategory.ArmR) == null,
                "Sword-only regular starter remains distinct from the Heavy demo");
            if (path == SpecialistPlayerIntegrationSetup.PlayerPath) yield return CheckWeapons(player, heading);
            yield return Clear();
        }
        foreach (string scene in new[] { "ZooScene", "KOTHTest", "EscortTest", "EliminationTest" })
        {
            EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/" + scene + ".unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return Frames(45); CheckHud(scene);
            var player = Object.FindAnyObjectByType<MechPlayerRig>(); player.GetComponent<MechPlayerInput>().enabled = false;
            Check(player.name == "FullPlayer_mech_specialist" && player.GetComponent<QuickDashFeedback>() != null, scene + " uses the integrated Specialist player");
            if (scene == "ZooScene")
            {
                var waves = Object.FindAnyObjectByType<ZooEnemyWaves>(); var roster = (WaveData)new SerializedObject(waves).FindProperty("roster").objectReferenceValue;
                Check(roster.name == "WaveData_Zoo" && roster.enemies.Any(e => e.prefab.name == "Enemy_Fodder") && roster.enemies.Length == 10,
                    "Zoo retains the teammate's ten-entry showcase roster, Fodder and switches");
            }
        }
    }
    static IEnumerator CheckWeapons(GameObject player, Transform heading)
    {
        var equipment = player.GetComponent<EquipManager>(); var motor = player.GetComponent<MechMovementController>();
        var combat = player.GetComponent<MechCombat>();
        var review = new GameObject("Weapon fit review").AddComponent<Camera>(); review.fieldOfView = 40;
        string[] paths = { "arm_sword", "arm_mace", "arm_throwingaxe", "arm_crossbow", "shoulder_cannon", "shoulder_crossbow" };
        var assets = paths.Select(n => "Assets/Prefabs/Mech/Base/Parts/" + n + ".prefab").Concat(new[] { SpecialistPlayerIntegrationSetup.BlasterPath, SpecialistPlayerIntegrationSetup.CannonPath });
        foreach (string path in assets)
        {
            foreach (var slot in new[] { PartCategory.ArmL, PartCategory.ArmR, PartCategory.ShoulderL, PartCategory.ShoulderR })
            {
                var old = equipment.Unequip(slot); if (old != null) { old.gameObject.SetActive(false); Object.Destroy(old.gameObject); }
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path); var type = prefab.GetComponent<Part>().partType;
            var slots = type == PartType.Arm ? new[] { PartCategory.ArmL, PartCategory.ArmR } : new[] { PartCategory.ShoulderL, PartCategory.ShoulderR };
            foreach (var slot in slots)
            {
                var part = Object.Instantiate(prefab).GetComponent<Part>(); Check(equipment.TryEquip(part, slot), prefab.name + " equips into " + slot);
            }
            yield return Frames(5);
            foreach (var slot in slots)
            {
                var part = equipment.GetEquipped(slot); var socket = part.transform.parent.GetComponent<Socket>(); var driver = part.GetComponent<MechWeaponAnimator>();
                Check(socket != null && socket.socketType == slot && OnMount(part), prefab.name + " matches the authored " + slot + " attachment, including its rig compatibility pose");
                Check(driver != null && driver.CanAnimate && !driver.Animator.applyRootMotion && new[] { driver.Animations.neutral, driver.Animations.fire }.All(c => c != null
                    && AnimationUtility.GetCurveBindings(c).All(b => AnimationUtility.GetAnimatedObject(driver.Animator.gameObject, b) != null)), prefab.name + " visual bindings resolve without root motion");
                if (prefab.name == "arm_crossbow" || prefab.name == "shoulder_crossbow" || prefab.name == "shoulder_cannon")
                {
                    bool shoulder = type == PartType.Shoulder;
                    var canonical = SpecialistPlayerIntegrationSetup.MountPose(shoulder ? SpecialistPlayerIntegrationSetup.CannonPath : SpecialistPlayerIntegrationSetup.BlasterPath,
                        shoulder ? "Mount_Shoulder" : "Mount_Arm");
                    canonical.rotation = Quaternion.Euler(0, 180, 0) * canonical.rotation;
                    var bone = driver.Animator.GetComponentsInChildren<Transform>().Single(t => t.name == "WeapRoot");
                    Vector3 scale = Vector3.Scale(player.transform.lossyScale, part.authoredScale) * equipment.partScale;
                    float lift = prefab.name == "shoulder_crossbow" ? .6f : .75f;
                    Vector3 clearance = shoulder ? player.transform.rotation * new Vector3(slot == PartCategory.ShoulderL ? -.45f : .45f, lift, 0)
                        : socket.transform.rotation * Vector3.Scale(scale, Vector3.down * .4f);
                    // The torso's authored idle tilts these offsets as the bone moves; attachment itself stays exact (OnMount).
                    Check(Vector3.Distance(bone.position, socket.transform.position + socket.transform.rotation * Vector3.Scale(scale, canonical.position) + clearance) < (shoulder ? .16f : .003f)
                        && Quaternion.Angle(bone.rotation, socket.transform.rotation * canonical.rotation) < .1f,
                        prefab.name + " neutral rig mount has the requested height/side clearance and facing on " + slot
                        + ", delta=" + player.transform.InverseTransformDirection(bone.position - socket.transform.position).ToString("R")
                        + ", socket=" + socket.transform.rotation.eulerAngles.ToString("R")
                        + ", angle=" + Quaternion.Angle(bone.rotation, socket.transform.rotation * canonical.rotation));
                }
                if (type == PartType.Shoulder)
                {
                    var anchor = prefab.name == "shoulder_cannon_specialist" ? part.transform : driver.Animator.GetComponentsInChildren<Transform>().Single(t => t.name == "WeapRoot");
                    Vector3 clearance = player.transform.InverseTransformDirection(anchor.position - socket.transform.position);
                    float side = slot == PartCategory.ShoulderL ? -1 : 1;
                    bool nativeCannon = prefab.name == "shoulder_cannon_specialist";
                    float lift = prefab.name == "shoulder_crossbow" ? .6f : .75f;
                    Check(nativeCannon ? clearance.magnitude < .0001f
                            : Mathf.Abs(clearance.x - side * .45f) < .08f && Mathf.Abs(clearance.y - lift) < .08f && Mathf.Abs(clearance.z) < .16f,
                        prefab.name + (nativeCannon ? " uses the original merged socket position on " : " has its requested clearance in player axes on ") + slot + ": " + clearance.ToString("R")
                        + ", socket=" + socket.transform.rotation.eulerAngles.ToString("R"));
                }
                if (prefab.name.Contains("crossbow") || prefab.name.Contains("cannon") || prefab.name.Contains("blaster"))
                    Check(part.muzzle != null && Vector3.Dot(part.muzzle.position - socket.transform.position, player.transform.forward) > .1f,
                        prefab.name + " barrel sits forward of the " + slot + " socket");
                if (part.muzzle != null) Debug.Log($"SPECIALIST MOUNT: {prefab.name} {slot}, socket={player.transform.InverseTransformPoint(socket.transform.position)}, muzzle={player.transform.InverseTransformPoint(part.muzzle.position)}, muzzle forward={player.transform.InverseTransformDirection(part.muzzle.forward)}");
                driver.PlayAttack(1f); driver.Tick(.2f);
                Check(OnMount(part),
                    prefab.name + " firing keeps the attachment fixed on " + slot);
            }
            Capture(review, player.transform, prefab.name + "-fire", new Vector3(6, 3, 8));
            foreach (var slot in slots) equipment.GetEquipped(slot).GetComponent<MechWeaponAnimator>().Tick(2);
            Capture(review, player.transform, prefab.name + "-rest", new Vector3(6, 3, -8));
            if (prefab.name == "arm_crossbow")
            {
                foreach (var slot in slots)
                {
                    var driver = equipment.GetEquipped(slot).GetComponent<MechWeaponAnimator>();
                    var filter = driver.Animator.GetComponentsInChildren<MeshFilter>().Single();
                    Vector3 lengths = MeshAxisLengths(filter);
                    driver.PlayAttack(.8f);
                    for (int i = 0; i < 32; i++)
                    {
                        driver.Tick(.04f);
                        Check(Vector3.Distance(lengths, MeshAxisLengths(filter)) < .001f, "Small crossbow retains its proportions through fire/reload/return on " + slot + " sample " + i);
                    }
                    for (int i = 0; i < 12; i++)
                    {
                        driver.Animations.neutral.SampleAnimation(driver.Animator.gameObject, i * driver.Animations.neutral.length / 12);
                        Check(Vector3.Distance(lengths, MeshAxisLengths(filter)) < .001f, "Small crossbow idle rotation cannot stretch its geometry on " + slot + " sample " + i);
                    }
                    driver.Tick(.2f);
                }
            }
            for (int i = 0; i < 12; i++) { motor.Tick(new MechLocomotionInput { move = Vector2.up }, heading, 1f / 60); yield return Frames(1); }
            Check(slots.All(s => OnMount(equipment.GetEquipped(s))), prefab.name + " follows animated sockets while walking");
            motor.Tick(default(MechLocomotionInput), heading, .1f); yield return Frames(40);
            foreach (var slot in slots)
            {
                var part = equipment.GetEquipped(slot); bool placeholder = part.data.partId == "weaponBlaster";
                var attack = combat.TryAttack(slot);
                Check(placeholder ? !attack.Started && part.RemainingAmmo == 0 : attack.Started, prefab.name + " gameplay admission on " + slot + (placeholder ? " preserves the empty-stat Blaster placeholder" : " works"));
                if (attack.Projectile != null) Object.Destroy(attack.Projectile.gameObject);
                yield return Frames(65);
            }
            if (prefab.name == "arm_crossbow" || prefab.name == "shoulder_crossbow")
            {
                var authored = prefab.GetComponent<Part>().muzzle;
                foreach (var slot in slots)
                {
                    var part = equipment.Unequip(slot);
                    Check(Vector3.Distance(part.muzzle.localPosition, authored.localPosition) < .0001f && Quaternion.Angle(part.muzzle.localRotation, authored.localRotation) < .01f,
                        prefab.name + " restores its authored muzzle when dropped from " + slot);
                    Check(equipment.TryEquip(part, slot) && Vector3.Dot(part.muzzle.forward, player.transform.forward) > .9f,
                        prefab.name + " restores the player-only forward muzzle when re-equipped on " + slot);
                }
            }
        }
        Object.Destroy(review.gameObject);
    }
    static Vector3 MeshAxisLengths(MeshFilter filter)
    {
        var matrix = filter.transform.localToWorldMatrix;
        return new Vector3(matrix.GetColumn(0).magnitude, matrix.GetColumn(1).magnitude, matrix.GetColumn(2).magnitude);
    }
    static bool OnMount(Part part)
    {
        var socket = part.transform.parent.GetComponent<Socket>();
        var pose = socket.AttachmentFor(part);
        var owner = part.EquippedBy;
        Vector3 scale = Vector3.Scale(owner.transform.lossyScale, part.authoredScale) * owner.partScale;
        return Vector3.Distance(part.transform.position, socket.transform.position + socket.transform.rotation * Vector3.Scale(scale, pose.position)) < .0001f
            && Quaternion.Angle(part.transform.rotation, socket.transform.rotation * pose.rotation) < .02f;
    }
    static void Capture(Camera camera, Transform player, string label, Vector3 offset)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
        camera.transform.position = player.position + offset; camera.transform.LookAt(player.position + Vector3.up * .5f);
        var target = new RenderTexture(1280, 720, 24); RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        var previous = RenderTexture.active; RenderTexture.active = target;
        var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
        string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "SpecialistFitCaptures"); Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, label + ".png"), texture.EncodeToPNG()); RenderTexture.active = previous; target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(texture);
    }
}
