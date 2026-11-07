using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class PickupHighlightChecks
{
    const string Pending = "PickupHighlightChecks.Pending";
    static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    static int frame = -1, checks;
    static bool running;
    static double deadline;
    static void Check(bool valid, string message)
    { if (!valid) throw new Exception(message); checks++; Debug.Log("PICKUP PASS: " + message); }
    [InitializeOnLoadMethod] static void Register() => EditorApplication.update += Tick;
    public static void RunBatch()
    {
        if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Disposable project only.");
        PickupHighlightSetup.Apply();
        string hash = Convert.ToBase64String(File.ReadAllBytes(PickupHighlightSetup.PrefabPath));
        PickupHighlightSetup.Apply();
        Check(hash == Convert.ToBase64String(File.ReadAllBytes(PickupHighlightSetup.PrefabPath)), "Setup preserves an existing authored effect prefab");
        Check(AssetDatabase.LoadAssetAtPath<GameObject>(PickupHighlightSetup.PrefabPath).GetComponentsInChildren<Collider>(true).Length == 0,
            "The reusable prefab adds no colliders");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static IEnumerator Frames(int count = 2)
    { int end = Time.frameCount + count; while (Time.frameCount < end) yield return null; }
    static void Tick()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (!running)
            {
                running = true; deadline = EditorApplication.timeSinceStartup + 120;
                PreMissionReliabilityChecks.BeginLogAudit(); steps.Push(Run());
            }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Pickup highlight checks timed out.");
            while (steps.Count > 0)
            {
                var step = steps.Peek(); if (!step.MoveNext()) { steps.Pop(); continue; }
                if (step.Current is IEnumerator nested) { steps.Push(nested); continue; }
                return;
            }
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    static IEnumerator Run()
    {
        Application.runInBackground = true; Time.timeScale = 1; Time.captureFramerate = 60;
        var camera = new GameObject("Pickup review camera").AddComponent<Camera>(); camera.tag = "MainCamera";
        camera.backgroundColor = new Color(.045f, .055f, .065f); camera.clearFlags = CameraClearFlags.SolidColor;
        camera.transform.position = new Vector3(5, 4, -7); camera.transform.LookAt(Vector3.up); camera.fieldOfView = 45;
        var light = new GameObject("Pickup review light").AddComponent<Light>(); light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(40, -35, 0); light.intensity = 1.3f;
        var actor = Equipment("Pickup test owner"); actor.transform.position = Vector3.right * 15;
        var prefab = Resources.Load<PickupHighlight>(PickupHighlight.ResourceName);
        Check(prefab != null, "The reusable prefab is available to runtime drops through Resources");
        foreach (string path in new[] {
            SpecialistPlayerIntegrationSetup.CannonPath, SpecialistPlayerIntegrationSetup.SmallCrossbowPath, SpecialistPlayerIntegrationSetup.LargeCrossbowPath,
            "Assets/Prefabs/Mech/Base/Parts/arm_sword.prefab", "Assets/Prefabs/Mech/Base/Parts/arm_mace.prefab", "Assets/Prefabs/Mech/Base/Parts/arm_throwingaxe.prefab",
            SpecialistPlayerIntegrationSetup.BlasterPath })
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var part = Object.Instantiate(source, Vector3.up, Quaternion.identity).GetComponent<Part>();
            part.GetComponent<Rigidbody>().isKinematic = true;
            yield return Frames();
            var effect = part.GetComponentInChildren<PickupHighlight>(true);
            Check(effect != null && effect.IsVisible && effect.OutlineCount > 0, source.name + " automatically shows its loose-part star and mesh outline");
            Check(part.GetComponentsInChildren<Collider>(true).Length == source.GetComponentsInChildren<Collider>(true).Length,
                source.name + " highlight does not add collision shapes");
            int originalMeshes = source.GetComponentsInChildren<MeshFilter>().Length;
            int outlines = effect.OutlineCount;
            var slot = part.partType == PartType.Shoulder ? PartCategory.ShoulderL : PartCategory.ArmL;
            for (int cycle = 0; cycle < 3; cycle++)
            {
                Check(actor.TryEquip(part, slot) && !effect.gameObject.activeSelf && !effect.IsVisible
                    && part.GetComponentsInChildren<MeshFilter>().Length == originalMeshes,
                    source.name + " equip hides the effect immediately and keeps active weapon mesh searches clean, cycle " + cycle);
                if (path == SpecialistPlayerIntegrationSetup.CannonPath && cycle == 0)
                {
                    var visuals = part.GetComponent<WeaponVisualSettings>(); visuals.PlayWeaponFireVfx();
                    var systems = part.GetComponentsInChildren<ParticleSystem>(true);
                    Check(visuals.weaponFireVfx != null && systems.Length > 0 && systems.All(s => s.isPlaying) && !effect.IsVisible,
                        "The merged grey-cannon firing VFX plays while the equipped pickup marker stays hidden");
                }
                Check(actor.Unequip(slot) == part, source.name + " can be dropped again, cycle " + cycle);
                part.transform.SetPositionAndRotation(Vector3.up, Quaternion.identity);
                part.GetComponent<Rigidbody>().isKinematic = true;
                yield return Frames();
                if (path == SpecialistPlayerIntegrationSetup.CannonPath && cycle == 0)
                {
                    effect.RebuildOutline(); yield return Frames();
                    Check(effect.OutlineCount == outlines, "Rebuilding the cannon outline ignores the merged fire-particle renderers");
                }
                Check(effect.IsVisible && effect.OutlineCount == outlines && part.GetComponentsInChildren<PickupHighlight>(true).Length == 1,
                    source.name + " drop reuses one effect without accumulating outlines, cycle " + cycle);
            }
            Object.Destroy(part.gameObject); yield return Frames();
        }
        Object.Destroy(actor.gameObject); yield return Frames();

        // A non-Part prop with nonuniform scale exercises reuse and real shader output.
        var prop = GameObject.CreatePrimitive(PrimitiveType.Cube); prop.name = "Reusable highlight prop";
        prop.transform.position = Vector3.up; prop.transform.localScale = new Vector3(2, .8f, 1);
        prop.transform.rotation = Quaternion.Euler(0, 25, 0);
        var effectOnProp = Object.Instantiate(prefab, prop.transform, false);
        yield return Frames();
        Check(prop.GetComponent<Part>() == null && effectOnProp.Target == prop.transform && effectOnProp.IsVisible && effectOnProp.OutlineCount == 1,
            "Dragging the prefab under a non-Part prop automatically outlines that parent");
        var sparkleFields = new SerializedObject(effectOnProp).FindProperty("sparkles");
        for (int i = 0; i < sparkleFields.arraySize; i++) ((MeshRenderer)sparkleFields.GetArrayElementAtIndex(i).objectReferenceValue).enabled = false;
        var outlinePixels = Capture(camera, "outline-only");
        effectOnProp.gameObject.SetActive(false);
        var plainPixels = Capture(camera, "plain-prop");
        int changed = 0;
        for (int i = 0; i < outlinePixels.Length; i++) if (outlinePixels[i].r > plainPixels[i].r + 12) changed++;
        Check(changed > 80, "The outline shader visibly draws the silhouette in URP (" + changed + " brighter pixels)");
        effectOnProp.gameObject.SetActive(true); yield return Frames(30); Capture(camera, "reusable-prop-highlight");
        camera.transform.position += Vector3.back * 100; yield return Frames();
        Check(!effectOnProp.IsVisible, "Distant effects fade and hide beyond their configured range");
        camera.transform.position -= Vector3.back * 100; yield return Frames();
        Check(effectOnProp.IsVisible && effectOnProp.OutlineCount == 1, "Returning or re-enabling restores the existing outline");
        Object.Destroy(prop); yield return Frames();

        // Real death/drop events: only the loose loot gets the effect, never the corpse snapshot.
        var enemy = Equipment("Pickup death review");
        enemy.transform.position = new Vector3(-1, 1, 0);
        var health = enemy.gameObject.AddComponent<CombatHealth>(); enemy.gameObject.AddComponent<CombatDeathRagdoll>();
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule); body.transform.SetParent(enemy.transform, false);
        var carried = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SpecialistPlayerIntegrationSetup.CannonPath), enemy.transform).GetComponent<Part>();
        Check(enemy.TryEquip(carried, PartCategory.ShoulderL) && carried.GetComponentInChildren<PickupHighlight>(true) == null,
            "Enemy-carried weapons do not create loose-loot markers");
        var dropper = enemy.gameObject.AddComponent<EnemyPartDrop>();
        var fields = new SerializedObject(dropper);
        fields.FindProperty("partPrefabs").arraySize = 1;
        fields.FindProperty("partPrefabs").GetArrayElementAtIndex(0).objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(SpecialistPlayerIntegrationSetup.CannonPath).GetComponent<Part>();
        fields.FindProperty("dropChancePercent").floatValue = 100;
        fields.FindProperty("popVelocity").floatValue = 0; fields.ApplyModifiedPropertiesWithoutUndo();
        health.ApplyDamage(health.MaximumHealth);
        yield return Frames();
        var loot = Object.FindObjectsByType<Part>().Single(p => !p.isEquipped);
        loot.GetComponent<Rigidbody>().isKinematic = true; loot.transform.position = new Vector3(1.5f, .4f, 0);
        var corpse = Object.FindAnyObjectByType<CombatRagdoll>();
        Check(corpse != null && corpse.GetComponentInChildren<PickupHighlight>(true) == null
            && loot.GetComponentInChildren<PickupHighlight>().IsVisible, "Death leaves an unmarked corpse and a separately highlighted loose drop");
        yield return Frames(20); Capture(camera, "corpse-and-highlighted-drop");
        foreach (string shader in new[] { "Lithomachia/Pickup Star", "Lithomachia/Pickup Outline" })
            Check(!ShaderUtil.ShaderHasError(Shader.Find(shader)), shader + " compiles without errors");
        Object.Destroy(enemy.gameObject);
    }
    static EquipManager Equipment(string name)
    {
        var root = new GameObject(name);
        foreach (PartCategory slot in Enum.GetValues(typeof(PartCategory)))
        {
            var socket = new GameObject(slot.ToString()).AddComponent<Socket>(); socket.socketType = slot;
            socket.transform.SetParent(root.transform, false);
            socket.transform.localPosition = new Vector3(slot == PartCategory.ShoulderL ? -.7f : .7f, 1, 0);
        }
        return root.AddComponent<EquipManager>();
    }
    static Color32[] Capture(Camera camera, string label)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) throw new Exception("Pickup visual checks need graphics.");
        var target = new RenderTexture(1280, 720, 24);
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        var previous = RenderTexture.active; RenderTexture.active = target;
        var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
        string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "PickupHighlightCaptures"); Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, label + ".png"), texture.EncodeToPNG());
        var pixels = texture.GetPixels32(); RenderTexture.active = previous; target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(texture);
        return pixels;
    }
    static void Finish(Exception error)
    {
        SessionState.SetBool(Pending, false); PreMissionReliabilityChecks.EndLogAudit(); Time.captureFramerate = 0;
        if (error != null) Debug.LogException(error);
        Debug.Log("PICKUP CHECKS " + (error == null ? "COMPLETE: " : "FAILED: ") + checks);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
