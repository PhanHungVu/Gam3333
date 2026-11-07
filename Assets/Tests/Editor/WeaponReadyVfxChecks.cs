using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Real attacks and item-owned cooldowns on the authored weapon HUD. Run through AbilityReadyPrefabChecks.
public static class WeaponReadyVfxChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
    static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
    static IEnumerator Frames(int count = 2) { int end = Time.frameCount + count; while (Time.frameCount < end) yield return null; }
    static IEnumerator Delay(float seconds) { float end = Time.time + seconds; while (Time.time < end) yield return null; }
    static AbilityReadyVfx Effect(WeaponsHudPod pod) => Get<AbilityReadyVfx>(pod, "readyVfx");
    static WeaponsHudPod Pod(MechWeaponsHud hud, PartCategory slot) => hud.Pods.Single(p => p.Weapon.Slot == slot);
    static void Fire(MechCombat combat, PartCategory slot, Action<bool, string> check)
    {
        var attack = combat.TryAttack(slot); check(attack.Started, "Normal combat accepts weapon attack on " + slot);
        if (attack.Projectile != null) Object.DestroyImmediate(attack.Projectile.gameObject);
    }
    // Simulate the clock passing between input and the HUD's LateUpdate. Keep a genuine duration,
    // unlike helpers that clear ReadyAt to zero and intentionally erase the completion cycle.
    static void Expire(Part part)
    {
        typeof(Part).GetField("readyStartedAt", Private).SetValue(part, Time.time - .2f);
        typeof(Part).GetField("<ReadyAt>k__BackingField", Private).SetValue(part, Time.time - .001f);
        typeof(Part).GetField("reloadEndsAt", Private).SetValue(part, 0f);
    }
    static Part Loose(PartData data, List<Part> cleanup)
    {
        var root = new GameObject(data.partName); root.SetActive(false);
        var part = root.AddComponent<Part>(); part.data = data; part.isStartingPart = false;
        root.SetActive(true); cleanup.Add(part); return part;
    }
    public static IEnumerator Run(Action<bool, string> check)
    {
        Time.timeScale = 1;
        var camera = new GameObject("Weapon ready aim camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 3, -6);
        var root = new GameObject("Weapon ready fixture"); root.SetActive(false); root.transform.position = Vector3.up * 3;
        var combat = root.AddComponent<MechCombat>();
        var sword = TestMech.Sword(5, .18f);
        var arm = TestMech.Ranged("Small Crossbow", PartType.Arm, PartSubtype.Projectile, 5, 30, .24f, 10, 60, .05f);
        var cannon = TestMech.WithMagazine(TestMech.Ranged("Cannon", PartType.Shoulder, PartSubtype.Splash, 5, 30, .1f, 10, 60, .05f, 1), 1, .35f);
        var crossbow = TestMech.Ranged("Large Crossbow", PartType.Shoulder, PartSubtype.Splash, 5, 30, .3f, 10, 60, .05f, 1);
        sword.icon = AssetDatabase.LoadAssetAtPath<PartData>("Assets/PartCatalogue/PartData_Arm_Sword.asset").icon;
        arm.icon = AssetDatabase.LoadAssetAtPath<PartData>("Assets/PartCatalogue/PartData_Arm_SmallCrossbow.asset").icon;
        cannon.icon = AssetDatabase.LoadAssetAtPath<PartData>("Assets/PartCatalogue/PartData_Shoulder_Cannon.asset").icon;
        crossbow.icon = AssetDatabase.LoadAssetAtPath<PartData>("Assets/PartCatalogue/PartData_Shoulder_LargeCrossbow.asset").icon;
        var parts = TestMech.Equip(root, (PartCategory.ArmL, sword), (PartCategory.ArmR, arm),
            (PartCategory.ShoulderL, cannon), (PartCategory.ShoulderR, crossbow));
        root.SetActive(true); combat.BindCamera(camera);
        var equipment = root.GetComponent<EquipManager>(); var cleanup = new List<Part>(parts);
        var ui = new GameObject("Weapon ready Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        var canvas = ui.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = ui.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        var hudSource = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/Combat HUD/CombatHUD.prefab").GetComponentInChildren<MechWeaponsHud>(true);
        var hud = Object.Instantiate(hudSource.gameObject, ui.transform, false).GetComponent<MechWeaponsHud>();
        var rect = (RectTransform)hud.transform; rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
        rect.pivot = new Vector2(.5f, 0); rect.anchoredPosition = new Vector2(0, 80);
        Call(hud, "BindPlayer", equipment);
        yield return Frames(3);
        check(hud.Pods.Count == 4 && hud.Pods.All(p => Effect(p) != null && Effect(p).PlayCount == 0),
            "All four weapon cells use the authored burst and initialize without flashing");
        foreach (var pod in hud.Pods) Fire(combat, pod.Weapon.Slot.Value, check);
        check(hud.Pods.All(p => Effect(p).PlayCount == 0) && Pod(hud, PartCategory.ShoulderL).IsCoolingDown,
            "Attacking starts actual cooldowns without prematurely emitting ready bursts");
        yield return Delay(.45f);
        check(hud.Pods.All(p => Effect(p).PlayCount == 1 && !p.IsCoolingDown),
            "Independent melee, arm, cannon-reload and crossbow completions each emit exactly one burst");
        check(hud.Pods.All(p => !Get<Image>(p, "cooldownLine").enabled && Get<Image>(p, "icon").color == Color.white),
            "Completion restores ready brightness and hides each rising cooldown sweep");
        foreach (var pod in hud.Pods)
        {
            var effect = Effect(pod);
            check(effect.IsPlaying && effect.Graphic.ParticleCount >= 35 && !effect.Graphic.raycastTarget
                && !effect.Graphic.maskable && effect.Graphic.transform.parent == canvas.transform,
                "The dense white column renders above pod masks without blocking UI on " + pod.Weapon.Slot);
        }
        Capture(canvas, hud, 1920, 1080, "weapon-ready-1920"); Capture(canvas, hud, 1280, 720, "weapon-ready-1280");
        yield return Delay(.7f);
        check(hud.Pods.All(p => Effect(p).PlayCount == 1 && !Effect(p).IsPlaying && Get<Image>(p, "icon").rectTransform.localScale == Vector3.one),
            "Remaining ready does not repeat the burst and icon pulses restore their exact scale");
        foreach (var pod in hud.Pods) pod.Bind(pod.Weapon);
        yield return Frames();
        check(hud.Pods.All(p => Effect(p).PlayCount == 1), "Refreshing already-ready icons fabricates no completions");

        var heldPod = Pod(hud, PartCategory.ArmR); int heldCount = Effect(heldPod).PlayCount;
        Fire(combat, PartCategory.ArmR, check); Expire(heldPod.Weapon); Fire(combat, PartCategory.ArmR, check);
        Call(heldPod, "Refresh");
        check(Effect(heldPod).PlayCount == heldCount + 1 && heldPod.IsCoolingDown,
            "Held fire preserves the previous ready cue even when the next shot starts before the HUD refreshes");
        yield return Delay(.3f);
        check(Effect(heldPod).PlayCount == heldCount + 2, "The next held-fire cooldown independently emits its own single completion");

        var swordPod = Pod(hud, PartCategory.ArmL);
        var duplicate = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(AbilityReadyPrefabSetup.WeaponPodPath), ui.transform).GetComponent<WeaponsHudPod>();
        duplicate.Bind(swordPod.Weapon); int count = Effect(swordPod).PlayCount;
        Fire(combat, PartCategory.ArmL, check); Expire(swordPod.Weapon); Call(swordPod, "Refresh"); Call(duplicate, "Refresh");
        check(Effect(swordPod).PlayCount + Effect(duplicate).PlayCount == count + 1,
            "Duplicate HUD views consume a physical weapon's completion only once");
        Object.DestroyImmediate(duplicate.gameObject);

        Fire(combat, PartCategory.ShoulderR, check);
        var oldEffect = Effect(swordPod); var oldColumn = oldEffect.Graphic;
        var spare = Loose(arm, cleanup); check(equipment.TryEquip(spare, PartCategory.ArmR), "An ordinary arm swap rebuilds the real weapon HUD");
        check(!oldEffect.IsPlaying && !oldColumn.enabled, "HUD rebuild immediately clears a playing external column");
        yield return Frames(3);
        check(hud.Pods.Count == 4 && hud.Pods.All(p => Effect(p).PlayCount == 0)
            && ui.GetComponentsInChildren<AbilityReadyBurstGraphic>(true).Length == 4,
            "Rebuilding during a cooldown creates no early flashes or orphan columns");
        yield return Delay(.35f);
        check(Effect(Pod(hud, PartCategory.ShoulderR)).PlayCount == 1
            && hud.Pods.Where(p => p.Weapon.Slot != PartCategory.ShoulderR).All(p => Effect(p).PlayCount == 0),
            "The rebuilt HUD observes the existing crossbow wait finishing on the correct icon only");

        var maceData = TestMech.Sword(5, .2f); maceData.partName = "Mace";
        var mace = Loose(maceData, cleanup); check(equipment.TryEquip(mace, PartCategory.ArmR), "A replacement melee weapon equips normally");
        Fire(combat, PartCategory.ArmR, check); yield return Delay(.28f);
        check(Effect(Pod(hud, PartCategory.ArmR)).PlayCount == 1, "The replaced right-arm melee weapon gets its own ready cue");

        var magazineData = TestMech.WithMagazine(TestMech.Ranged("Partial magazine", PartType.Arm, PartSubtype.Projectile, 5, 12, .12f, 10, 60, .05f), 3, .35f);
        var magazine = Loose(magazineData, cleanup); check(equipment.TryEquip(magazine, PartCategory.ArmR), "A magazine test weapon equips normally");
        Fire(combat, PartCategory.ArmR, check); check(combat.TryReload(PartCategory.ArmR), "The real partial-magazine reload is accepted");
        yield return Delay(.18f);
        check(magazine.IsReloading && Effect(Pod(hud, PartCategory.ArmR)).PlayCount == 0,
            "The shot interval ending cannot emit a burst while its manual reload is still running");
        yield return Delay(.25f);
        check(Effect(Pod(hud, PartCategory.ArmR)).PlayCount == 1, "The manual reload ending emits one genuine ready burst");

        var empty = Loose(TestMech.Ranged("Last round", PartType.Arm, PartSubtype.Projectile, 5, 1, .12f, 10, 60, .05f), cleanup);
        check(equipment.TryEquip(empty, PartCategory.ArmR), "The last-round test weapon equips normally");
        Fire(combat, PartCategory.ArmR, check); yield return Delay(.2f);
        check(empty.RemainingAmmo == 0 && Effect(Pod(hud, PartCategory.ArmR)).PlayCount == 0,
            "A depleted weapon emits no false ready burst when its final-shot wait ends");

        var originalSword = equipment.GetEquipped(PartCategory.ArmL);
        Fire(combat, PartCategory.ArmL, check);
        var swapped = Loose(arm, cleanup);
        check(equipment.TryEquip(swapped, PartCategory.ArmL) && equipment.TryEquip(originalSword, PartCategory.ArmL),
            "Swapping away and back preserves the original physical weapon");
        check(Effect(Pod(hud, PartCategory.ArmL)).PlayCount == 0, "Re-equipping a cooling weapon does not flash early");
        yield return Delay(.25f);
        check(Effect(Pod(hud, PartCategory.ArmL)).PlayCount == 1, "The re-equipped weapon's original cooldown completes once");
        Fire(combat, PartCategory.ArmL, check); hud.gameObject.SetActive(false); yield return Delay(.25f);
        hud.gameObject.SetActive(true); Call(hud, "BindPlayer", equipment); yield return Frames();
        check(hud.Pods.All(p => Effect(p).PlayCount == 0), "Reopening a HUD after an unseen completion does not replay stale ready cues");

        Object.DestroyImmediate(hud.gameObject); Object.DestroyImmediate(ui); Object.DestroyImmediate(root);
        foreach (var part in cleanup) if (part != null) Object.DestroyImmediate(part.gameObject);
        Object.DestroyImmediate(camera.gameObject); yield return Frames(3);
        check(Object.FindObjectsByType<AbilityReadyBurstGraphic>(FindObjectsInactive.Include).Length == 0,
            "Weapon HUD destruction leaves no external burst graphics behind");
    }

    static void Capture(Canvas canvas, MechWeaponsHud hud, int width, int height, string label)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        var camera = new GameObject("Weapon ready capture").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.035f, .045f, .06f);
        var target = new RenderTexture(width, height, 24); camera.targetTexture = target;
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
        var scaler = canvas.GetComponent<CanvasScaler>(); scaler.enabled = false; scaler.enabled = true;
        foreach (var pod in hud.Pods) { Effect(pod).Tick(0); Effect(pod).Graphic.SetAge(.12f); }
        Canvas.ForceUpdateCanvases(); camera.Render();
        var previous = RenderTexture.active; RenderTexture.active = target;
        var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
        string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "WeaponReadyCaptures"); Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, label + ".png"), texture.EncodeToPNG());
        RenderTexture.active = previous; canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null;
        camera.targetTexture = null; target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(texture); Object.DestroyImmediate(camera.gameObject);
    }
}
