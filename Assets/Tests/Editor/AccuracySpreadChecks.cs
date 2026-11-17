using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Real combat/equipment/projectiles and cursor prefab, isolated from live editor scenes.
public static class AccuracySpreadChecks
{
    const string Pending = "AccuracySpreadChecks.Pending";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static MechCombat combat;
    static MechTargetLockController targeting;
    static Camera view;
    static Part head;
    static PartData headData;
    static LockOnCursor cursor;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Debug.Log("ACCURACY PASS: " + message); }
    static void Call(object owner, string method) => owner.GetType().GetMethod(method, Private).Invoke(owner, null);
    static void Accuracy(float value) => headData.stats = new List<StatEntry> { new StatEntry { type = StatType.Accuracy, value = value } };
    static void ClearShots() { foreach (var shot in Object.FindObjectsByType<MechProjectile>()) Object.DestroyImmediate(shot.gameObject); }
    [InitializeOnLoadMethod] static void Register() => EditorApplication.playModeStateChanged += state =>
    { if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false)) Run(); };
    public static void RunBatch()
    {
        if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new Exception("Disposable project only.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Run()
    {
        SessionState.SetBool(Pending, false); AudioListener.volume = 0;
        try
        {
            PreMissionReliabilityChecks.BeginLogAudit();
            view = new GameObject("Accuracy camera").AddComponent<Camera>(); view.tag = "MainCamera";
            view.pixelRect = new Rect(40, 20, 1000, 700); view.fieldOfView = 60;
            view.transform.position = new Vector3(2, 3, -10);
            var player = new GameObject("Accuracy combat fixture"); player.SetActive(false);
            headData = ScriptableObject.CreateInstance<PartData>(); headData.partType = PartType.Head; headData.stats = new List<StatEntry>();
            var equipped = TestMech.Equip(player, (PartCategory.Head, headData),
                (PartCategory.ArmL, TestMech.Ranged("Small crossbow", PartType.Arm, PartSubtype.Projectile, 10, 1000, .2f, 40, 30, .05f)),
                (PartCategory.ArmR, TestMech.Ranged("Throwing axe", PartType.Arm, PartSubtype.Projectile, 10, 1000, .2f, 40, 30, .05f)),
                (PartCategory.ShoulderL, TestMech.Ranged("Cannon", PartType.Shoulder, PartSubtype.Splash, 10, 1000, .2f, 40, 30, .05f, 6)),
                (PartCategory.ShoulderR, TestMech.Ranged("Large crossbow", PartType.Shoulder, PartSubtype.Projectile, 10, 1000, .2f, 40, 30, .05f)));
            head = equipped[0]; combat = player.AddComponent<MechCombat>(); targeting = player.AddComponent<MechTargetLockController>();
            player.SetActive(true); combat.BindCamera(view); targeting.BindCamera(view); targeting.EnterFreeAim();
            // MechCombat binds the existing targeting component in Awake.
            typeof(MechCombat).GetField("targeting", Private).SetValue(combat, targeting);
            Percentages(); Projection(); RangeFractions(player); Projectiles(player); PrototypeShots(player); Cursor(player); Swaps(player); Collision(player); Capture();
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors(); PreMissionReliabilityChecks.EndLogAudit();
            Debug.Log("ACCURACY CHECKS COMPLETE: " + checks); EditorApplication.Exit(0);
        }
        catch (Exception error) { PreMissionReliabilityChecks.EndLogAudit(); Debug.LogException(error); EditorApplication.Exit(1); }
    }
    static void Percentages()
    {
        Check(AccuracySpread.Percent(null) == 100 && combat.AccuracyPercent == 100, "Missing head Accuracy retains legacy perfect aim");
        Accuracy(100); Check(combat.SpreadHalfAngle == 0, "100% Accuracy removes spread completely");
        Accuracy(50); Check(Mathf.Abs(combat.SpreadHalfAngle - 5f) < .001f, "50% Accuracy uses half of the restored 10-degree crosshair reference");
        Accuracy(0); Check(combat.SpreadHalfAngle == 10, "0% Accuracy uses the restored 10-degree crosshair reference");
        Accuracy(-10); Check(combat.AccuracyPercent == 0, "Negative Accuracy clamps to zero");
        Accuracy(110); Check(combat.AccuracyPercent == 100, "Loot rolls above 100 clamp to perfect accuracy");
        Accuracy(float.NaN); Check(combat.SpreadHalfAngle == 0, "Malformed Accuracy cannot create invalid projectile directions");
        var authored = AssetDatabase.LoadAssetAtPath<PartData>("Assets/PartCatalogue/PartData_Head.asset");
        Check(AccuracySpread.Percent(authored) == 20, "Existing authored head provides 20% Accuracy without modifying its asset");
    }
    static void RangeFractions(GameObject player)
    {
        Accuracy(20); var part = combat.GetPart(PartCategory.ArmL);
        float reference = AccuracySpread.ScreenRadius(view, combat.SpreadAngleOf(part));
        var muzzle = player.transform.TransformPoint(new Vector3(.65f, .5f, .65f));
        float range = combat.RangeMetresOf(part);
        Check(Mathf.Abs(range - 160) < .001f, "Existing Range still converts golem lengths into the projectile travel limit in metres");
        foreach (float fraction in new[] { 0f, .25f, .5f, 1f, 2f })
        {
            float angle = combat.ShotSpreadAngleOf(part, muzzle + Vector3.forward * range * fraction);
            float expected = reference * .25f * Mathf.Clamp01(fraction);
            Check(Mathf.Abs(AccuracySpread.ScreenRadius(view, angle) - expected) < .01f,
                "Ordinary weapon has exactly quarter-radius spread times distance/range at distance factor " + fraction);
        }
        float empty = combat.ShotSpreadAngleOf(part, muzzle + Vector3.forward, false);
        Check(Mathf.Abs(AccuracySpread.ScreenRadius(view, empty) - reference * .25f) < .01f,
            "Empty-space aim uses the full inner quarter-circle rather than inventing a nearby target");
        Check(AccuracySpread.DistanceFactor(-1, 20) == 0 && AccuracySpread.DistanceFactor(40, 20) == 1
            && AccuracySpread.DistanceFactor(float.NaN, 20) == 1 && AccuracySpread.DistanceFactor(1, 0) == 1,
            "Distance tightening clamps safely and malformed ranges cannot create invalid directions");
        foreach (string path in new[] { TestMech.PlayerPath, "Assets/Prefabs/Mech/Specialist/FullPlayer_mech_specialist.prefab" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<MechCombat>();
            Check((float)typeof(MechCombat).GetField("maximumSpreadAngle", Private).GetValue(prefab) == 10
                && (float)typeof(MechCombat).GetField("shotSpreadRadiusFraction", Private).GetValue(prefab) == .25f
                && (float)typeof(MechCombat).GetField("minigunSpreadRadiusFraction", Private).GetValue(prefab) == .5f,
                "Saved player prefab keeps the large circle and quarter/half-radius limits: " + path);
        }
        targeting.EnterFreeAim();
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position = view.transform.position + view.transform.forward * 14;
        Physics.SyncTransforms();
        var contact = AimSolver.AimPoint(targeting, view, player.transform, range, ~0, out bool hit);
        Check(hit && Vector3.Distance(contact, wall.transform.position) < 1, "Free-aim physical contact supplies a real distance for tightening");
        float contactAngle = combat.ShotSpreadAngleOf(part, contact, hit);
        Check(contactAngle < empty, "A nearby free-aim surface tightens spread just like a selected enemy");
        Object.DestroyImmediate(wall); Physics.SyncTransforms();
        AimSolver.AimPoint(targeting, view, player.transform, range, ~0, out hit);
        Check(!hit, "Empty-space aim is distinguished from a physical contact");
    }
    static void Projection()
    {
        Accuracy(20); var center = view.transform.TransformPoint(new Vector3(5, -2, 30));
        float radius = combat.SpreadRadiusPixels(view); Vector2 screen = view.WorldToScreenPoint(center);
        bool edges = true, interiors = true;
        for (int bearing = 0; bearing < 64; bearing++)
        {
            var edge = AccuracySpread.AimPoint(view, center, combat.SpreadHalfAngle, bearing / 64f, 1);
            edges &= Mathf.Abs(Vector2.Distance(view.WorldToScreenPoint(edge), screen) - radius) < .01f;
            var inner = AccuracySpread.AimPoint(view, center, combat.SpreadHalfAngle, bearing / 64f, .25f);
            interiors &= Mathf.Abs(Vector2.Distance(view.WorldToScreenPoint(inner), screen) - radius * .25f) < .01f;
        }
        Check(edges && interiors, "Every edge and radial sample matches the displayed circle, including off-center targets");
        var state = UnityEngine.Random.state; UnityEngine.Random.InitState(8271);
        float sum = 0, min = 1, max = 0; int[] sectors = new int[8]; bool bounded = true;
        for (int i = 0; i < 2048; i++)
        {
            Vector2 delta = (Vector2)view.WorldToScreenPoint(AccuracySpread.RandomAimPoint(view, center, combat.SpreadHalfAngle)) - screen;
            float fraction = delta.magnitude / radius; sum += fraction; min = Mathf.Min(min, fraction); max = Mathf.Max(max, fraction); bounded &= fraction <= 1.0001f;
            float phase = Mathf.Repeat(Mathf.Atan2(delta.y, delta.x) / (2 * Mathf.PI), 1); sectors[Mathf.Min(7, (int)(phase * 8))]++;
        }
        UnityEngine.Random.state = state;
        Check(bounded && min < .01f && max > .99f && Mathf.Abs(sum / 2048 - .5f) < .025f && Array.TrueForAll(sectors, n => n > 180),
            "Shots randomize all bearings and a distance uniformly from center to the maximum, without exceeding it");
        float original = view.fieldOfView; view.fieldOfView = 30;
        Check(combat.SpreadRadiusPixels(view) > radius, "Narrowing camera FOV enlarges the projected circle for the same gameplay spread");
        view.fieldOfView = original;
        view.orthographic = true; view.orthographicSize = 12;
        var point = AccuracySpread.AimPoint(view, center, combat.SpreadHalfAngle, .125f, 1);
        Check(Mathf.Abs(Vector2.Distance(view.WorldToScreenPoint(point), view.WorldToScreenPoint(center)) - combat.SpreadRadiusPixels(view)) < .01f,
            "Orthographic projection also keeps the displayed circle and aim offsets consistent");
        view.orthographic = false;
        Accuracy(100); var before = UnityEngine.Random.state; AccuracySpread.RandomAimPoint(view, center, 0);
        Check(JsonUtility.ToJson(before) == JsonUtility.ToJson(UnityEngine.Random.state), "Perfect accuracy does not consume shot-spread randomness");
    }
    static void Projectiles(GameObject player)
    {
        foreach (bool hard in new[] { false, true })
        {
            var enemy = GameObject.CreatePrimitive(PrimitiveType.Cube); enemy.transform.position = new Vector3(4, 3, 35); enemy.AddComponent<MechTargetable>();
            Physics.SyncTransforms(); targeting.ResumeTargeting(); Call(targeting, "LateUpdate");
            if (hard) targeting.ToggleHardLock();
            Check(targeting.CurrentTarget != null && targeting.IsHardLockMode == hard, (hard ? "Hard lock" : "Soft auto aim") + " retains its selected enemy");
            foreach (var slot in new[] { PartCategory.ArmL, PartCategory.ArmR, PartCategory.ShoulderL, PartCategory.ShoulderR })
            {
                Accuracy(20); bool inside = true, varied = false; Vector3 first = Vector3.zero;
                for (int i = 0; i < 16; i++)
                {
                    var part = combat.GetPart(slot); float range = part.Stats.GetStat(StatType.Range) * combat.MetresPerGolem;
                    var center = AimSolver.AimPoint(targeting, view, player.transform, range, ~0);
                    // This fixture has no authored muzzles; each shot uses the shared fallback.
                    Vector3 muzzle = player.transform.TransformPoint(new Vector3(.65f, .5f, .65f));
                    TestMech.Advance(player, 1); var shot = combat.TryAttack(slot).Projectile;
                    if (shot == null) throw new Exception("Fixture did not launch " + slot);
                    float t = Vector3.Dot(center - muzzle, view.transform.forward) / Vector3.Dot(shot.Direction, view.transform.forward);
                    Vector3 intercept = muzzle + shot.Direction * t;
                    float error = Vector2.Distance(view.WorldToScreenPoint(intercept), view.WorldToScreenPoint(center));
                    float expectedRadius = AccuracySpread.ScreenRadius(view, combat.SpreadAngleOf(part)) * .25f
                        * Mathf.Clamp01(Vector3.Distance(muzzle, center) / range);
                    inside &= error <= expectedRadius + .02f;
                    if (i == 0) first = shot.Direction; else varied |= Vector3.Angle(first, shot.Direction) > .1f;
                    Object.DestroyImmediate(shot.gameObject);
                }
                Check(inside && varied, slot + " real projectiles stay inside the distance-tightened inner quarter-circle under " + (hard ? "hard lock" : "auto aim"));
                Accuracy(100); TestMech.Advance(player, 1);
                var perfect = combat.TryAttack(slot).Projectile;
                Check(perfect != null && Vector3.Angle(perfect.Direction, enemy.GetComponent<MechTargetable>().AimPosition - player.transform.TransformPoint(new Vector3(.65f, .5f, .65f))) < .03f,
                    slot + " fires directly at the intended center with 100% Accuracy");
                ClearShots();
            }
            targeting.EnterFreeAim(); Object.DestroyImmediate(enemy);
        }
    }
    static void PrototypeShots(GameObject player)
    {
        Accuracy(20);
        var equipment = player.GetComponent<EquipManager>(); var original = equipment.Unequip(PartCategory.ArmL);
        original.gameObject.SetActive(false);
        var rotation = view.transform.rotation; var random = UnityEngine.Random.state; UnityEngine.Random.InitState(29641);
        // Shotgun now has an independent physical pellet cone; its center/cluster
        // checks live in WeaponBalanceChecks instead of the inner-circle fixture.
        foreach (int index in new[] { 0 })
        {
            var part = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(GreyboxArmWeaponSetup.PrefabPath(index))).GetComponent<Part>();
            part.KeepAuthoredStats();
            Check(equipment.TryEquip(part, PartCategory.ArmL), "Real " + part.data.partName + " prefab equips for projectile spread verification");
            float range = combat.RangeMetresOf(part); float limit = index == 0 ? .5f : .25f;
            Check(Mathf.Abs(range - (index == 0 ? 18 : 24)) < .001f && combat.ShotRadiusFractionOf(part) == limit,
                part.data.partName + " uses its authored range and " + limit + " reference-radius limit");
            var enemy = GameObject.CreatePrimitive(PrimitiveType.Cube); enemy.AddComponent<MechTargetable>();
            foreach (float distance in new[] { .25f, .5f, 1f })
            {
                Vector3 muzzle = part.muzzle.position; enemy.transform.position = muzzle + Vector3.forward * range * distance;
                view.transform.LookAt(enemy.transform.position); Physics.SyncTransforms(); targeting.ResumeTargeting(); Call(targeting, "LateUpdate");
                Vector3 center = AimSolver.AimPoint(targeting, view, player.transform, range, ~0, out bool contact);
                Check(contact && targeting.CurrentTarget == enemy.GetComponent<MechTargetable>(), part.data.partName + " resolves its enemy at " + distance + " of Range");
                float reference = AccuracySpread.ScreenRadius(view, combat.SpreadAngleOf(part)); float expected = reference * limit * distance;
                Check(Mathf.Abs(AccuracySpread.ScreenRadius(view, combat.ShotSpreadAngleOf(part, center, contact)) - expected) < .01f,
                    part.data.partName + " spread radius tightens exactly with real muzzle-to-enemy distance");
                bool bounded = true; float widest = 0; int count = 0;
                for (int i = 0; i < 16; i++)
                {
                    TestMech.Advance(player, 1); var result = combat.TryAttack(PartCategory.ArmL);
                    if (!result.Started) throw new Exception(part.data.partName + " did not fire during spread check");
                    foreach (var shot in Object.FindObjectsByType<MechProjectile>())
                    {
                        float t = Vector3.Dot(center - muzzle, view.transform.forward) / Vector3.Dot(shot.Direction, view.transform.forward);
                        float radius = Vector2.Distance(view.WorldToScreenPoint(muzzle + shot.Direction * t), view.WorldToScreenPoint(center));
                        bounded &= radius <= expected + .02f; widest = Mathf.Max(widest, radius / expected); count++;
                    }
                    ClearShots();
                }
                Check(bounded && widest > .7f && count == (index == 0 ? 16 : 96), part.data.partName
                    + " actual projectiles fill their allowed inner circle without exceeding it at distance factor " + distance);
                targeting.EnterFreeAim();
            }
            equipment.Unequip(PartCategory.ArmL); Object.DestroyImmediate(part.gameObject); Object.DestroyImmediate(enemy);
        }
        UnityEngine.Random.state = random; view.transform.rotation = rotation;
        original.gameObject.SetActive(true); Check(equipment.TryEquip(original, PartCategory.ArmL), "Spread test restores the original loadout");
    }
    static float PaintedWidth()
    {
        var art = cursor.GetComponent<Image>().rectTransform;
        Vector2 a = RectTransformUtility.WorldToScreenPoint(null, art.TransformPoint(new Vector3(-art.rect.width * .5f, 0, 0)));
        Vector2 b = RectTransformUtility.WorldToScreenPoint(null, art.TransformPoint(new Vector3(art.rect.width * .5f, 0, 0)));
        return Vector2.Distance(a, b) * .53125f;
    }
    static void Cursor(GameObject player)
    {
        var canvas = new GameObject("Accuracy Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080);
        var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/Combat HUD/Cursor.prefab"), canvas.transform);
        // The unrelated objective marker manager may create markers next frame; no gameplay tick is needed here.
        cursor = instance.GetComponent<LockOnCursor>(); cursor.Bind(targeting, view); Canvas.ForceUpdateCanvases();
        Accuracy(20); Call(cursor, "LateUpdate"); float wide = PaintedWidth();
        Check(Mathf.Abs(wide - Mathf.Max(12, combat.SpreadRadiusPixels(view) * 2)) < .05f, "Real cursor art accounts for transparent sprite margins, CanvasScaler and its minimum visible marker");
        Accuracy(80); Call(cursor, "LateUpdate");
        Check(PaintedWidth() <= wide + .05f && Mathf.Abs(PaintedWidth() - Mathf.Max(12, combat.SpreadRadiusPixels(view) * 2)) < .05f, "Higher Accuracy tightens the circle down to the minimum visible marker");
        var parent = new GameObject("Scaled parent", typeof(RectTransform)).GetComponent<RectTransform>(); parent.SetParent(canvas.transform, false); parent.localScale = new Vector3(1.4f, .8f, 1);
        instance.transform.SetParent(parent, false); Call(cursor, "LateUpdate");
        Check(Mathf.Abs(PaintedWidth() - Mathf.Max(12, combat.SpreadRadiusPixels(view) * 2)) < .05f, "Nested nonuniform UI scale does not distort the spread width");
        var marker = Object.FindAnyObjectByType<ObjectiveMarkerManager>().transform; Vector3 markerScale = marker.localScale;
        Accuracy(100); Call(cursor, "LateUpdate");
        Check(Mathf.Abs(PaintedWidth() - 12) < .05f && marker.localScale == markerScale && combat.SpreadHalfAngle == 0,
            "Perfect accuracy retains only a small aiming marker without scaling objective marker children");
        Accuracy(20); float fov = view.fieldOfView; view.fieldOfView = 40; Call(cursor, "LateUpdate");
        Check(Mathf.Abs(PaintedWidth() - Mathf.Max(12, combat.SpreadRadiusPixels(view) * 2)) < .05f, "Cursor responds immediately to camera FOV changes"); view.fieldOfView = fov;
        headData.stats = null; Call(cursor, "LateUpdate");
        Check(combat.AccuracyPercent == 100 && Mathf.Abs(PaintedWidth() - 12) < .05f, "A head missing the stat safely uses the perfect-aim marker");
        Accuracy(20);
    }
    static void Swaps(GameObject player)
    {
        var equipment = player.GetComponent<EquipManager>(); equipment.Unequip(PartCategory.Head);
        Check(combat.AccuracyPercent == 100, "Removing the head clears its Accuracy without retaining stale spread");
        var pickup = new GameObject("Rolled head").AddComponent<Part>(); pickup.data = headData;
        Check(pickup.RollLootStats(381) && equipment.TryEquip(pickup, PartCategory.Head), "A real rolled head equips through the existing equipment system");
        Check(Mathf.Abs(combat.AccuracyPercent - pickup.Stats.GetStat(StatType.Accuracy)) < .001f && headData.GetStat(StatType.Accuracy) == 20,
            "Combat uses the head's rolled stat and keeps the shared definition unchanged");
        Call(cursor, "LateUpdate"); Check(Mathf.Abs(PaintedWidth() - Mathf.Max(12, combat.SpreadRadiusPixels(view) * 2)) < .05f, "Head swapping updates the visible circle immediately");
        PartSwapDisplay.BasicStats(pickup, out string basic); Check(basic.Contains("Accuracy ") && basic.Contains("%"), "Head pickup comparisons expose the now-active Accuracy percentage");
        equipment.Unequip(PartCategory.Head); equipment.TryEquip(head, PartCategory.Head);
        pickup.gameObject.SetActive(false);
    }
    static void Collision(GameObject player)
    {
        Accuracy(100); targeting.EnterFreeAim(); view.transform.SetPositionAndRotation(new Vector3(0, .5f, -8), Quaternion.identity);
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position = new Vector3(0, .5f, 8); wall.transform.localScale = new Vector3(20, 20, .2f);
        Physics.SyncTransforms(); TestMech.Advance(player, 1); var shot = combat.TryAttack(PartCategory.ArmR).Projectile;
        typeof(MechProjectile).GetMethod("Step", Private).Invoke(shot, new object[] { 2f });
        Check(!shot.IsLive, "Accuracy never bypasses normal projectile wall collisions"); ClearShots();
        Object.DestroyImmediate(wall);
    }
    static void Capture()
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        var canvas = cursor.GetComponentInParent<Canvas>();
        var target = new RenderTexture(960, 540, 24); target.Create();
        view.targetTexture = target; view.rect = new Rect(0, 0, 1, 1); view.backgroundColor = new Color(.035f, .05f, .06f); view.clearFlags = CameraClearFlags.SolidColor;
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = view; canvas.planeDistance = 1;
        var label = new GameObject("Accuracy label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text)).GetComponent<Text>();
        label.transform.SetParent(canvas.transform, false); label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); label.fontSize = 22; label.alignment = TextAnchor.MiddleCenter; label.color = Color.white;
        label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(.5f, .85f); label.rectTransform.sizeDelta = new Vector2(700, 60);
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../AccuracySpreadCaptures")); Directory.CreateDirectory(folder);
        foreach (int value in new[] { 20, 50, 80, 100 })
        {
            Accuracy(value); label.text = "Head Accuracy " + value + "%";
            Canvas.ForceUpdateCanvases(); Call(cursor, "LateUpdate"); view.Render();
            var texture = new Texture2D(960, 540, TextureFormat.RGB24, false); var previous = RenderTexture.active; RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); texture.Apply(); RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(folder, "accuracy-" + value + ".png"), texture.EncodeToPNG()); Object.DestroyImmediate(texture);
        }
        view.targetTexture = null; target.Release(); Object.DestroyImmediate(target);
        Debug.Log("ACCURACY CAPTURES COMPLETE");
    }
}
