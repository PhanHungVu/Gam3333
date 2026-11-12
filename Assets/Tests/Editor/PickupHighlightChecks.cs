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
        var tuning = new SerializedObject(prefab);
        Check(tuning.FindProperty("outlineWidth").floatValue == 3.5f && tuning.FindProperty("sparkleSize").floatValue == .95f
            && tuning.FindProperty("minimumSparklePixels").floatValue == 28, "Saved pickup prefab has the thicker outline and larger world/screen-space glint");
        var enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PickupHighlightSetup.EnemyPrefabPath);
        Check(enemyPrefab != null && PrefabUtility.GetPrefabAssetType(enemyPrefab) == PrefabAssetType.Variant
            && enemyPrefab.GetComponentsInChildren<Collider>(true).Length == 0, "Enemy gold glint reuses the highlight as a collision-free prefab variant");
        DropPlanChecks();
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
        var effectFields = new SerializedObject(effectOnProp); effectFields.FindProperty("drawOutline").boolValue = false; effectFields.ApplyModifiedPropertiesWithoutUndo();
        yield return Frames();
        Check(effectOnProp.IsVisible && effectOnProp.OutlineCount == 0, "Live Draw Outline tuning removes only the outline while retaining the glint");
        effectFields.Update(); effectFields.FindProperty("drawOutline").boolValue = true; effectFields.ApplyModifiedPropertiesWithoutUndo();
        yield return Frames();
        Check(effectOnProp.IsVisible && effectOnProp.OutlineCount == 1, "Re-enabling outline tuning restores one shell without duplicates");
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
        yield return Frames();
        var carrierGlint = enemy.GetComponentInChildren<PickupHighlight>(true);
        Check(dropper.WillDropPart && dropper.PlannedDrop.data == carried.data && carrierGlint != null && carrierGlint.IsVisible
            && carrierGlint.OutlineCount == 0 && carrierGlint.Target == enemy.transform, "A live guaranteed loot carrier shows the sparkle-only marker on its own meshes");
        var star = carrierGlint.GetComponentsInChildren<MeshRenderer>().First(r => r.enabled);
        var block = new MaterialPropertyBlock(); star.GetPropertyBlock(block); var gold = block.GetColor("_Color");
        Check(gold.r > gold.g && gold.g > gold.b && gold.a > 0, "Carrier marker uses its gold tint without changing the shared white pickup material");
        Capture(camera, "gold-loot-carrier");
        health.ApplyDamage(health.MaximumHealth);
        yield return Frames();
        var loot = Object.FindObjectsByType<Part>().Single(p => !p.isEquipped);
        loot.GetComponent<Rigidbody>().isKinematic = true; loot.transform.position = new Vector3(1.5f, .4f, 0);
        var corpse = Object.FindAnyObjectByType<CombatRagdoll>();
        Check(corpse != null && corpse.GetComponentInChildren<PickupHighlight>(true) == null
            && loot.GetComponentInChildren<PickupHighlight>().IsVisible, "Death leaves an unmarked corpse and a separately highlighted loose drop");
        Check(corpse.GetComponentsInChildren<Renderer>(true).All(r => r.sharedMaterials.All(m => m == null || m.shader.name != "Lithomachia/Pickup Star")),
            "The gold carrier star is hidden before corpse renderers are copied");
        yield return Frames(20); Capture(camera, "corpse-and-highlighted-drop");
        foreach (string shader in new[] { "Lithomachia/Pickup Star", "Lithomachia/Pickup Outline" })
            Check(!ShaderUtil.ShaderHasError(Shader.Find(shader)), shader + " compiles without errors");
        Object.Destroy(enemy.gameObject);
        foreach (var corpseToClear in Object.FindObjectsByType<CombatRagdoll>()) Object.Destroy(corpseToClear.gameObject);
        foreach (var loosePart in Object.FindObjectsByType<Part>()) if (!loosePart.isEquipped) Object.Destroy(loosePart.gameObject);
        yield return Frames();
        yield return RealCarrierChecks(camera);
    }
    static EnemyPartDrop PlannedEnemy(float chance, Part[] parts)
    {
        var root = new GameObject("Controlled drop plan"); root.SetActive(false);
        root.AddComponent<CombatHealth>(); var drop = root.AddComponent<EnemyPartDrop>();
        var fields = new SerializedObject(drop); fields.FindProperty("dropChancePercent").floatValue = chance;
        fields.FindProperty("showDropMarker").boolValue = false;
        var list = fields.FindProperty("partPrefabs"); list.arraySize = parts.Length;
        for (int i = 0; i < parts.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = parts[i];
        fields.ApplyModifiedPropertiesWithoutUndo(); root.SetActive(true); return drop;
    }
    static void DropPlanChecks()
    {
        var parts = new[] { "sword", "mace" }.Select(n => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/arm_" + n + ".prefab").GetComponent<Part>()).ToArray();
        int promised = 0, empty = 0;
        for (int seed = 0; seed < 24; seed++)
        {
            UnityEngine.Random.InitState(seed);
            bool passes = UnityEngine.Random.value * 100 < 30;
            Part expected = passes ? parts[UnityEngine.Random.Range(0, parts.Length)] : null;
            UnityEngine.Random.InitState(seed);
            var drop = PlannedEnemy(30, parts);
            Check(drop.WillDropPart == passes && drop.PlannedDrop == expected, "Spawn plan preserves the configured chance and item selection, seed " + seed);
            var random = UnityEngine.Random.state;
            for (int read = 0; read < 20; read++)
                if (drop.WillDropPart != passes || drop.PlannedDrop != expected) throw new Exception("Drop promise changed between marker reads.");
            Check(UnityEngine.Random.state.Equals(random) && drop.PlannedDrop == expected, "Repeated marker reads never reroll loot, seed " + seed);
            int contrarySeed = 0;
            while (contrarySeed < 1000)
            {
                UnityEngine.Random.InitState(contrarySeed);
                if ((UnityEngine.Random.value * 100 < 30) != passes) break;
                contrarySeed++;
            }
            UnityEngine.Random.InitState(contrarySeed);
            drop.GetComponent<CombatHealth>().ApplyDamage(1000);
            Check(passes ? drop.LastDrop != null && drop.LastDrop.data == expected.data : drop.LastDrop == null,
                "Death honors the visible promise even when combat changed RNG, seed " + seed);
            if (passes) promised++; else empty++;
            var loot = drop.LastDrop;
            Check(drop.ResolveDeathDrop() == loot && !drop.WillDropPart, "Death callbacks are idempotent and cannot keep a live carrier marker, seed " + seed);
            if (loot != null) Object.DestroyImmediate(loot.gameObject);
            Object.DestroyImmediate(drop.gameObject);
        }
        Check(promised > 0 && empty > 0, "Seeded plan fixtures exercise both carriers and ordinary enemies");
        foreach (var setting in new[] { (0f, parts), (100f, new Part[0]), (100f, new Part[] { null }) })
        {
            var drop = PlannedEnemy(setting.Item1, setting.Item2);
            Check(!drop.WillDropPart && drop.PlannedDrop == null, "Zero chance/empty/null loot cannot advertise a promised drop");
            drop.GetComponent<CombatHealth>().ApplyDamage(1000);
            Check(drop.LastDrop == null, "Invalid or disabled loot plans remain empty on death");
            Object.DestroyImmediate(drop.gameObject);
        }
    }
    static IEnumerator RealCarrierChecks(Camera camera)
    {
        var gate = new GameObject("Real Fodder loot review"); gate.SetActive(false);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FodderModelSetup.FodderPath);
        var marked = Object.Instantiate(prefab, gate.transform); var normal = Object.Instantiate(prefab, gate.transform);
        marked.transform.position = new Vector3(-1.5f, 1, 0); normal.transform.position = new Vector3(1.5f, 1, 0);
        foreach (var root in new[] { marked, normal })
        {
            root.GetComponent<EnemyGraphController>().enabled = false;
            root.GetComponent<Unity.Behavior.BehaviorGraphAgent>().enabled = false;
            root.GetComponent<EnemyAgent>().enabled = false; root.GetComponent<EnemyAttack>().enabled = false;
            var fields = new SerializedObject(root.GetComponent<EnemyPartDrop>());
            fields.FindProperty("dropChancePercent").floatValue = root == marked ? 100 : 0; fields.ApplyModifiedPropertiesWithoutUndo();
        }
        camera.transform.position = new Vector3(0, 3.3f, -8); camera.transform.LookAt(new Vector3(0, 1.4f, 0));
        gate.SetActive(true); yield return Frames(5);
        var drop = marked.GetComponent<EnemyPartDrop>(); var marker = marked.GetComponentInChildren<PickupHighlight>(true);
        Check(drop.WillDropPart && marker != null && marker.IsVisible && marker.OutlineCount == 0,
            "Actual new Fodder body automatically shows its guaranteed-drop gold star without outlining the enemy");
        Check(!normal.GetComponent<EnemyPartDrop>().WillDropPart && normal.GetComponentInChildren<PickupHighlight>(true) == null,
            "Ordinary Fodder enemies create no misleading loot marker");
        Capture(camera, "fodder-gold-carrier-and-normal");
        marked.SetActive(false);
        Check(!marker.gameObject.activeSelf && !marker.IsVisible, "Disabling a carrier hides the reused marker immediately");
        marked.GetComponent<CombatHealth>().SetMaximum(100, true); marked.SetActive(true); yield return Frames(3);
        Check(marked.GetComponentsInChildren<PickupHighlight>(true).Length == 1 && marker.IsVisible && drop.WillDropPart,
            "A pooled life gets a fresh loot plan and reuses one marker");
        var planned = drop.PlannedDrop; marked.GetComponent<CombatHealth>().ApplyDamage(1000); yield return Frames();
        Check(drop.LastDrop != null && drop.LastDrop.data == planned.data && !marker.IsVisible,
            "The real carrier drops its promised equipped weapon and clears the star");
        foreach (var corpse in Object.FindObjectsByType<CombatRagdoll>())
        {
            Check(corpse.GetComponentsInChildren<Renderer>(true).All(r => r.sharedMaterials.All(m => m == null || m.shader.name != "Lithomachia/Pickup Star")),
                "Real Fodder corpses never inherit the live gold marker");
            Object.Destroy(corpse.gameObject);
        }
        Object.Destroy(gate); foreach (var part in Object.FindObjectsByType<Part>()) if (!part.isEquipped) Object.Destroy(part.gameObject);
        yield return Frames();
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
