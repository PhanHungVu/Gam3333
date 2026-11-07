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

public static class GreyCannonReplacementChecks
{
    const string Pending = "GreyCannonReplacementChecks.Pending";
    static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    static int checks, frame = -1; static bool running; static double deadline;
    static void Check(bool valid, string message) { if (!valid) throw new Exception(message); checks++; Debug.Log("GREY CANNON PASS: " + message); }
    static string Hash(string path) { using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path))); }
    static IEnumerator Frames(int n = 4) { int end = Time.frameCount + n; while (Time.frameCount < end) yield return null; }
    static string Output => Directory.GetParent(Application.dataPath).FullName;
    static string EnemySettings(string path)
    {
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(path); var attack = root.GetComponent<EnemyAttack>();
        var fields = new SerializedObject(attack); var weapons = fields.FindProperty("weapons");
        string result = fields.FindProperty("fireDuration").floatValue + ":" + fields.FindProperty("releaseFrame").floatValue;
        for (int i = 0; i < weapons.arraySize; i++)
        {
            var w = weapons.GetArrayElementAtIndex(i);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(w.FindPropertyRelative("weapon").objectReferenceValue, out string guid, out long id);
            result += ":" + id + ":" + w.FindPropertyRelative("damageMultiplier").floatValue + ":" + w.FindPropertyRelative("cooldownMultiplier").floatValue + ":" + w.FindPropertyRelative("projectileSpeedMultiplier").floatValue;
        }
        return result;
    }
    [InitializeOnLoadMethod] static void Register() => EditorApplication.update += Tick;
    public static void RunBatch()
    {
        if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new Exception("Disposable project only");
        var protectedPaths = Directory.GetFiles("Assets/PartCatalogue", "*", SearchOption.AllDirectories).Concat(Directory.GetFiles("Assets/LevelCatalogue", "*", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles("Assets/Models", "*", SearchOption.AllDirectories)).Where(p => !p.EndsWith(".meta"))
            .Concat(new[] { GreyCannonReplacementSetup.LegacyPath, GreyCannonReplacementSetup.ActivePath, "Assets/Anim/Weapons/Cannon.asset", "Assets/Anim/Weapons/Cannon_Fire.anim",
                "Assets/Anim/Weapons/Cannon_Neutral.anim", "Assets/Anim/Weapons/Cannon_Reload.anim", "Assets/Anim/Specialist/Cannon_Specialist.asset", GameplaySceneMaintenance.CursorPath,
                GameplaySceneMaintenance.CombatHudPath, "Assets/InputSystem_Actions.inputactions" }).ToArray();
        var hashes = protectedPaths.Select(Hash).ToArray();
        string elite = "Assets/Prefabs/Enemies/Elite Enemies/Enemy_Elite_Cannon.prefab"; string settings = EnemySettings(elite);
        GreyCannonReplacementSetup.Apply(); var changed = GreyCannonReplacementSetup.ChangedPaths.ToArray();
        File.WriteAllLines(Path.Combine(Output, "GreyCannonChangedAssets.txt"), changed);
        Check(protectedPaths.Select(Hash).SequenceEqual(hashes), "Archived cannon, source art, animation assets, stats, waves, HUD and controls are byte-for-byte preserved");
        Check(EnemySettings(elite) == settings, "Elite weapon component IDs, attack timing and damage/cooldown/projectile multipliers are preserved");
        var after = changed.Select(Hash).ToArray(); GreyCannonReplacementSetup.Apply();
        Check(changed.Select(Hash).SequenceEqual(after) && GreyCannonReplacementSetup.ChangedPaths.Count == 0, "Replacement is idempotent");
        foreach (var path in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }).Select(AssetDatabase.GUIDToAssetPath))
        {
            if (path == GreyCannonReplacementSetup.LegacyPath || path.Contains("/Old/")) continue;
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path); Inspect(root, path);
        }
        foreach (var path in AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" }).Select(AssetDatabase.GUIDToAssetPath))
        {
            var scene = EditorSceneManager.OpenScene(path);
            foreach (var root in scene.GetRootGameObjects()) Inspect(root, path + ": " + root.name);
        }
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Inspect(GameObject root, string context)
    {
        Check(root.GetComponentsInChildren<MeshFilter>(true).All(f => f.sharedMesh == null || !AssetDatabase.GetAssetPath(f.sharedMesh).EndsWith("/Cannon 1.glb")), context + " contains no old coloured cannon meshes");
        Check(root.GetComponentsInChildren<Transform>(true).All(t => !PrefabUtility.IsPrefabAssetMissing(t.gameObject) && GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0), context + " has no missing prefabs or scripts");
        foreach (var part in root.GetComponentsInChildren<Part>(true).Where(GreyCannonReplacementSetup.IsCannon))
        {
            var d = part.GetComponent<MechWeaponAnimator>();
            Check(!GreyCannonReplacementSetup.IsLegacy(part) && d != null && d.Animations.name == "Cannon_Specialist" && d.Animator.transform.IsChildOf(part.transform)
                && part.muzzle != null && part.muzzle.IsChildOf(part.transform), context + " cannon uses the grey model, its own animator and muzzle: " + part.name
                + ", legacy=" + GreyCannonReplacementSetup.IsLegacy(part) + ", set=" + (d != null && d.Animations != null ? d.Animations.name : "NULL")
                + ", animator=" + (d != null && d.Animator != null ? d.Animator.name : "NULL") + ", muzzle=" + (part.muzzle != null ? part.muzzle.name : "NULL"));
            Check(new[] { d.Animations.neutral, d.Animations.fire }.All(c => c != null && AnimationUtility.GetCurveBindings(c).All(b => AnimationUtility.GetAnimatedObject(d.Animator.gameObject, b) != null)), context + " cannon animation bindings resolve");
        }
        bool referencesArchive = false;
        foreach (var attack in root.GetComponentsInChildren<EnemyAttack>(true))
            Check(attack.Weapons.All(w => w != null && w.GetComponentInParent<EnemyAttack>(true) == attack), context + " retains live AI weapon bindings");
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c != null))
        {
            var fields = new SerializedObject(component); var p = fields.GetIterator();
            while (p.NextVisible(true))
                if (p.propertyType == SerializedPropertyType.ObjectReference && p.objectReferenceValue != null)
                    referencesArchive |= AssetDatabase.GetAssetPath(p.objectReferenceValue) == GreyCannonReplacementSetup.LegacyPath;
        }
        Check(!referencesArchive, context + " does not spawn/reference the archived cannon prefab");
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (!running) { running = true; deadline = EditorApplication.timeSinceStartup + 240; PreMissionReliabilityChecks.BeginLogAudit(); steps.Push(Run()); }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Grey cannon checks timed out");
            while (steps.Count > 0) { var step = steps.Peek(); if (!step.MoveNext()) { steps.Pop(); continue; } if (step.Current is IEnumerator nested) { steps.Push(nested); continue; } return; }
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    static void Finish(Exception error)
    {
        SessionState.SetBool(Pending, false); PreMissionReliabilityChecks.EndLogAudit(); Time.captureFramerate = 0;
        if (error != null) Debug.LogException(error);
        Debug.Log("GREY CANNON CHECKS " + (error == null ? "COMPLETE: " : "FAILED: ") + checks); EditorApplication.Exit(error == null ? 0 : 1);
    }
    static IEnumerator Clear()
    {
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) Object.Destroy(root);
        yield return Frames(5);
    }
    static IEnumerator Run()
    {
        Application.runInBackground = true; Time.captureFramerate = 60;
        if (Keyboard.current == null) InputSystem.AddDevice<Keyboard>(); if (Mouse.current == null) InputSystem.AddDevice<Mouse>();
        foreach (var path in new[] { SpecialistPlayerIntegrationSetup.PlayerPath, TestMech.PlayerPath })
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.position = Vector3.down * .25f; floor.transform.localScale = new Vector3(200, .5f, 200);
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), Vector3.up * 4, Quaternion.identity);
            player.GetComponent<MechPlayerInput>().enabled = false;
            var motor = player.GetComponent<MechMovementController>(); var eq = player.GetComponent<EquipManager>(); var combat = player.GetComponent<MechCombat>();
            for (int i = 0; i < 60; i++) { motor.Tick(default(MechLocomotionInput), 1f / 60); yield return Frames(1); }
            foreach (var slot in new[] { PartCategory.ShoulderL, PartCategory.ShoulderR })
            {
                var old = eq.Unequip(slot); if (old != null) Object.Destroy(old.gameObject);
                var part = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(GreyCannonReplacementSetup.ActivePath)).GetComponent<Part>();
                Check(eq.TryEquip(part, slot), player.name + " equips grey cannon on " + slot);
                yield return Frames();
                int ammo = part.RemainingAmmo; var shot = combat.TryAttack(slot);
                Check(shot.Started && shot.Projectile != null && shot.Projectile.Source == part && part.RemainingAmmo == ammo - 1 && part.IsReloading,
                    player.name + " grey cannon fires with existing ammo and reload stats on " + slot);
                Check(part.GetComponent<MechWeaponAnimator>().CurrentPose == MechWeaponAnimator.PoseState.Fire && part.GetComponent<MechWeaponAnimator>().CanAnimate,
                    player.name + " cannon plays authored firing animation on " + slot);
                if (shot.Projectile != null) Object.Destroy(shot.Projectile.gameObject);
            }
            Capture(player, Path.GetFileNameWithoutExtension(path)); yield return Clear();
        }
        var target = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SpecialistPlayerIntegrationSetup.PlayerPath), new Vector3(0, 2, 12), Quaternion.identity);
        target.GetComponent<MechPlayerInput>().enabled = false;
        var enemy = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Elite Enemies/Enemy_Elite_Cannon.prefab"), Vector3.zero, Quaternion.identity);
        var agent = enemy.GetComponent<EnemyAgent>(); agent.enabled = false;
        typeof(EnemyAgent).GetField("target", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(agent, target.transform);
        enemy.GetComponent<EnemyGraphController>().enabled = false; enemy.GetComponent<Unity.Behavior.BehaviorGraphAgent>().enabled = false;
        var attack = enemy.GetComponent<EnemyAttack>(); Physics.SyncTransforms();
        Check(attack.Weapons.Count == 2 && attack.Weapons.All(w => w != null && w.CanAnimate && w.Animations.name == "Cannon_Specialist"), "Elite still owns both grey cannon attack lanes");
        Check(attack.TryAttack(), "Elite admits a grey cannon attack");
        foreach (var w in attack.Weapons) w.Tick(1.1f); attack.Tick(1.1f);
        var projectiles = Object.FindObjectsByType<MechProjectile>().Where(p => p.Source != null && p.Source.GetComponentInParent<EnemyAttack>() == attack).ToArray();
        Check(attack.Releases == 1 && projectiles.Length == 1, "Elite firing animation releases its actual cannon projectile");
        foreach (var projectile in projectiles) Object.Destroy(projectile.gameObject);
        Capture(enemy, "Enemy_Elite_Cannon");
        typeof(EnemyPartDrop).GetMethod("FillFromCarriedWeapons", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(enemy.GetComponent<EnemyPartDrop>(), null);
        var authoredDrops = new SerializedObject(enemy.GetComponent<EnemyPartDrop>()).FindProperty("partPrefabs");
        Check(authoredDrops.arraySize == 2 && Enumerable.Range(0, authoredDrops.arraySize).All(i => AssetDatabase.GetAssetPath(authoredDrops.GetArrayElementAtIndex(i).objectReferenceValue) == GreyCannonReplacementSetup.ActivePath),
            "Inspector Fill From Carried Weapons resolves both embedded cannons to the new loose pickup");
        var drops = new SerializedObject(enemy.GetComponent<EnemyPartDrop>()); drops.FindProperty("dropChancePercent").floatValue = 100; drops.ApplyModifiedPropertiesWithoutUndo();
        enemy.GetComponent<CombatHealth>().ApplyDamage(1000000); yield return Frames(2);
        Check(Object.FindObjectsByType<Part>().Any(p => p.transform.parent == null && !p.isEquipped && GreyCannonReplacementSetup.IsCannon(p)
            && p.GetComponent<MechWeaponAnimator>().Animations.name == "Cannon_Specialist"), "Actual enemy death drops a loose grey cannon");
        yield return Clear();
    }
    static void Capture(GameObject actor, string name)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
        var light = new GameObject("Cannon review light").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(45, -30, 0);
        var camera = new GameObject("Cannon review camera").AddComponent<Camera>(); camera.fieldOfView = 40;
        camera.transform.position = actor.transform.position + new Vector3(7, 4, 9); camera.transform.LookAt(actor.transform.position + Vector3.up);
        var rt = new RenderTexture(1280, 720, 24); RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
        var previous = RenderTexture.active; RenderTexture.active = rt; var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
        string folder = Path.Combine(Output, "GreyCannonCaptures"); Directory.CreateDirectory(folder); File.WriteAllBytes(Path.Combine(folder, name + ".png"), tex.EncodeToPNG());
        RenderTexture.active = previous; rt.Release(); Object.Destroy(rt); Object.Destroy(tex); Object.Destroy(camera.gameObject); Object.Destroy(light.gameObject);
    }
}
