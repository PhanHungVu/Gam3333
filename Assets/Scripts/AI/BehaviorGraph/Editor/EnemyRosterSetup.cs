using System;
using System.Linq;
using Lithomachia.EnemyBehavior;
using Unity.Behavior;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class EnemyRosterSetup
{
    public const string Small = "Assets/Prefabs/Enemies/Small Enemies/";
    public const string Elite = "Assets/Prefabs/Enemies/Elite Enemies/";
    public const string Graphs = "Assets/AI/Behavior/Graphs/";
    public const string Roaming = "Assets/LevelCatalogue/WaveData_Roaming.asset";
    public const string Survival = "Assets/LevelCatalogue/WaveData_Survival.asset";
    public static readonly string[] Roster = { Small+"Enemy_sword.prefab", Small+"Enemy_mace.prefab", Small+"Enemy_hammer.prefab", Small+"Enemy_throwingAxe.prefab", Small+"Enemy_spinner.prefab", Small+"Enemy_crossbow.prefab", Elite+"Enemy_Elite_Cannon.prefab", Elite+"Enemy_Elite_RockRope.prefab", Elite+"Enemy_Elite_RockRopePull.prefab" };
    static void Value(SerializedObject owner, string name, float value) => owner.FindProperty(name).floatValue = value;
    static void Flag(SerializedObject owner, string name, bool value) => owner.FindProperty(name).boolValue = value;
    static T Add<T>(GameObject root) where T : Component => root.GetComponent<T>() ?? root.AddComponent<T>();
    [MenuItem("Tools/Mech/Enemies/Set Up Small and Elite Behavior Roster")]
    public static void UpgradeAssets()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before authoring assets.");
        EnemyGraphAuthoring.Folder("Assets/AI/Behavior/Presentation");
        var warningMaterial = MaterialAsset("Prototype Warning", new Color(1, .4f, .12f, .85f));
        var ropeMaterial = MaterialAsset("Prototype Rope", Color.gray);
        var acquire = Chain("Acquire and Approach", typeof(DetectEnemyPlayerAction), typeof(ApproachEnemyAction));
        var weapon = Chain("Telegraphed Weapon Attack", typeof(RequestEnemyAttackAction), typeof(WaitEnemyAttackAction));
        var recovery = Chain("Recover", typeof(RecoverEnemyAction));
        var wander = Chain("Wander Until Detection", typeof(WanderEnemyAction));
        BehaviorGraph Main(string name, bool special, bool pull) => EnemyGraphAuthoring.Ensure(Graphs+name+".asset", true, g =>
        {
            var choose = g.Builtin("SelectorComposite", 0, 140); g.Link(g.Start, choose);
            var approach = g.Subgraph(acquire, -220, 300); var idle = g.Subgraph(wander, 400, 300);
            g.Link(choose, approach); g.Link(choose, idle);
            object tail = approach;
            if (special)
            {
                var wind = g.Node(typeof(WindupEnemySpecialAction), -220, 450); var execute = g.Node(typeof(ExecuteEnemySpecialAction), -220, 600);
                g.Link(tail, wind); g.Link(wind, execute); tail = execute;
                if (pull) { var move = g.Node(typeof(PullEnemyRockAction), -220, 750); g.Link(tail, move); tail = move; }
            }
            else { var fire = g.Subgraph(weapon, -220, 450); g.Link(tail, fire); tail = fire; }
            g.Link(tail, g.Subgraph(recovery, -220, 900));
        });
        var melee = Main("Small Type 1 - Overhead", false, false);
        var ranged = Main("Small Type 3 - Crossbow", false, false);
        var heavy = Main("Elite Type 1 - Heavy Ranged", false, false);
        var spinner = Main("Small Type 2 - Spinner", true, false);
        var rope = Main("Elite Type 2 - Rock Rope", true, true);
        var orbit = Main("Elite Type 2 - Orbiting Rock", true, false);
        CloneIfMissing(Roster[1], Roster[2]); CloneIfMissing(Roster[1], Roster[4]); CloneIfMissing(Roster[6], Roster[7]);
        // Preserve the previous throw-and-pull actor before converting the original.
        CloneIfMissing(Roster[7], Roster[8]);
        for (int i = 0; i < Roster.Length; i++)
        {
            var root = PrefabUtility.LoadPrefabContents(Roster[i]);
            try
            {
                var role = (EnemyBehaviorRole)i;
                bool custom = role == EnemyBehaviorRole.Spinner || role == EnemyBehaviorRole.RockRope || role == EnemyBehaviorRole.RockRopePull;
                var agent = new SerializedObject(root.GetComponent<EnemyAgent>()); Flag(agent, "externalDecisions", true);
                float[] speed = { 6, 6, 4.5f, 6, 4, 5, 3.5f, 3, 3 };
                float[] range = { 2, 2.2f, 2.3f, 3.2f, 8, 8, 18, 6.5f, 18 };
                Value(agent, "maxSpeed", speed[i]); Value(agent, "attackRange", range[i]); Value(agent, "arriveRadius", Mathf.Max(12, range[i]+6));
                if (i == 7) agent.FindProperty("pathingMode").enumValueIndex = (int)PathingMode.FlowField;
                Value(agent, "detectRadius", i >= 6 ? 65 : 40); Value(agent, "loseRadius", i >= 6 ? 85 : 60); agent.ApplyModifiedPropertiesWithoutUndo();
                var attackComponent = root.GetComponent<EnemyAttack>(); var attack = new SerializedObject(attackComponent);
                Flag(attack, "externalRequests", true); Flag(attack, "independentWeapons", false);
                Flag(attack, "forceOverheadMelee", role == EnemyBehaviorRole.ThrowingAxe); Flag(attack, "respectAttackRangeAtRelease", i < 4);
                Value(attack, "aimSpreadDegrees", role == EnemyBehaviorRole.Crossbow ? 12 : 0);
                Value(attack, "fireDuration", role == EnemyBehaviorRole.HeavyRanged ? 1.4f : role == EnemyBehaviorRole.Hammer ? 1.1f : role == EnemyBehaviorRole.Crossbow ? 1f : 0.7f);
                Value(attack, "releaseFrame", i == 2 || i == 5 || i == 6 ? 0.7f : 0.6f);
                var weapons = attack.FindProperty("weapons");
                if (custom) weapons.arraySize = 0;
                else if (role == EnemyBehaviorRole.HeavyRanged)
                {
                    var cannons = root.GetComponentsInChildren<MechWeaponAnimator>(true).Where(w => w.GetComponent<Part>() is Part p
                        && p.Stats != null && p.partType == PartType.Shoulder && p.Stats.subtype == PartSubtype.Splash).ToArray();
                    if (cannons.Length == 0) throw new InvalidOperationException("Heavy elite has no existing shoulder cannon");
                    weapons.arraySize = cannons.Length;
                    for (int n = 0; n < cannons.Length; n++) weapons.GetArrayElementAtIndex(n).FindPropertyRelative("weapon").objectReferenceValue = cannons[n];
                }
                float[] damage = { .15f, .25f, .35f, .3f, 0, .2f, .45f, 0, 0 };
                float[] cooldown = { 1.5f, 1.5f, 2, 3, 1, 3.2f, 1.5f, 1, 1 };
                for (int n = 0; n < weapons.arraySize; n++)
                {
                    var entry = weapons.GetArrayElementAtIndex(n); entry.FindPropertyRelative("damageMultiplier").floatValue = damage[i];
                    entry.FindPropertyRelative("cooldownMultiplier").floatValue = cooldown[i];
                    entry.FindPropertyRelative("projectileSpeedMultiplier").floatValue = role == EnemyBehaviorRole.Crossbow ? .45f : role == EnemyBehaviorRole.HeavyRanged ? .5f : 1;
                }
                attack.ApplyModifiedPropertiesWithoutUndo();
                var brain = Add<BehaviorGraphAgent>(root); brain.Graph = i < 4 ? melee : i == 4 ? spinner : i == 5 ? ranged : i == 6 ? heavy : i == 7 ? orbit : rope;
                var controller = new SerializedObject(Add<EnemyGraphController>(root)); controller.FindProperty("role").enumValueIndex = i;
                Value(controller, "minimumRange", i == 7 ? 4.5f : i == 5 ? 5 : i >= 6 ? 11 : 0); controller.ApplyModifiedPropertiesWithoutUndo();
                var threat = new SerializedObject(Add<EnemyThreatMarker>(root)); threat.FindProperty("material").objectReferenceValue = warningMaterial; threat.ApplyModifiedPropertiesWithoutUndo();
                var visual = root.GetComponentsInChildren<Transform>(true).First(t => t.name == "VisualMech");
                if (custom)
                {
                    var ordinary = root.GetComponent<EnemyWeaponTelegraph>(); if (ordinary != null) Object.DestroyImmediate(ordinary);
                    if (role == EnemyBehaviorRole.Spinner)
                    {
                        var special = new SerializedObject(Add<EnemySpinnerAttack>(root)); special.FindProperty("visualRoot").objectReferenceValue = visual;
                        Value(special, "damage", 18); Value(special, "cooldown", 3.5f); special.ApplyModifiedPropertiesWithoutUndo();
                        var hop = new SerializedObject(Add<EnemyHopPresentation>(root)); hop.FindProperty("visualRoot").objectReferenceValue = visual; hop.ApplyModifiedPropertiesWithoutUndo();
                    }
                    else
                    {
                        var held = RopeRock(root);
                        var special = new SerializedObject(Add<EnemyRopeAttack>(root)); special.FindProperty("rock").objectReferenceValue = held;
                        special.FindProperty("ropeAnchor").objectReferenceValue = visual.GetComponentsInChildren<Transform>(true).First(t => t.name == "shoulder_socket_r");
                        special.FindProperty("ropeMaterialTemplate").objectReferenceValue = ropeMaterial;
                        special.FindProperty("behavior").enumValueIndex = i == 7 ? 1 : 0;
                        Value(special, "flightDuration", i == 7 ? .7f : .95f); Value(special, "arcHeight", i == 7 ? 2 : 5);
                        Value(special, "damage", 35); Value(special, "windupDuration", 1.1f); Value(special, "cooldown", i == 7 ? 7 : 6); special.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
                else Add<EnemyWeaponTelegraph>(root);
                if (role == EnemyBehaviorRole.Hammer) HammerProxy(root);
                var health = new SerializedObject(root.GetComponent<CombatHealth>()); float[] hp = { 100, 120, 160, 90, 120, 80, 320, 400, 400 };
                Value(health, "maximumHealth", hp[i]); health.ApplyModifiedPropertiesWithoutUndo();
                var cost = new SerializedObject(root.GetComponent<EnemySpawnCost>()); cost.FindProperty("cost").intValue = i < 4 ? 1 : i < 6 ? 2 : 10; cost.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, Roster[i]);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        Wave(Roaming, false); Wave(Survival, true); Wave("Assets/LevelCatalogue/WaveData_KOTH.asset", true);
        UpdateSavedArenaRoster();
        AssetDatabase.SaveAssets(); Debug.Log("SMALL AND ELITE BEHAVIOR ROSTER READY: 9 prefabs, reusable subgraphs, roaming/survival presets");
    }
    static void UpdateSavedArenaRoster()
    {
        // Refresh only the Inspector roster, preserving the authored scene setup.
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(EnemyBehaviorArenaSetup.ScenePath) == null) return;
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(EnemyBehaviorArenaSetup.ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(EnemyBehaviorArenaSetup.ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
        bool preserveUnsaved = scene.isDirty;
        try
        {
            foreach (var root in scene.GetRootGameObjects()) foreach (var controls in root.GetComponentsInChildren<EnemyBehaviorPlaytest>(true))
            {
                var fields = new SerializedObject(controls); var roster = fields.FindProperty("roster"); roster.arraySize = Roster.Length;
                for (int i = 0; i < roster.arraySize; i++) roster.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(Roster[i]);
                fields.ApplyModifiedPropertiesWithoutUndo();
            }
            // An open, dirty scene belongs to the designer: keep its edits in
            // memory and let their normal save include this roster update.
            if (preserveUnsaved) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            else if (!UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, EnemyBehaviorArenaSetup.ScenePath))
                throw new InvalidOperationException("Could not update the saved arena's enemy roster.");
        }
        finally { if (opened) UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true); }
    }
    static BehaviorGraph Chain(string name, params Type[] actions) => EnemyGraphAuthoring.Ensure(Graphs+"Shared/"+name+".asset", false, g =>
    { object previous = g.Start; for (int i = 0; i < actions.Length; i++) { var node = g.Node(actions[i], 0, 140*(i+1)); g.Link(previous, node); previous = node; } });
    static void CloneIfMissing(string source, string target)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(target) != null) return;
        var root = PrefabUtility.LoadPrefabContents(source);
        try { root.name = System.IO.Path.GetFileNameWithoutExtension(target); PrefabUtility.SaveAsPrefabAsset(root, target); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    static Transform RopeRock(GameObject root)
    {
        var pivot = root.transform.Find("Rope rock pivot");
        var art = pivot != null ? pivot.Find("Rock art proxy") : root.transform.Find("Rock art proxy");
        if (pivot == null) { pivot = new GameObject("Rope rock pivot").transform; pivot.SetParent(root.transform, false); }
        // The elite root is non-uniformly scaled. Keep its throwable at its actual
        // 0.6-unit collision radius instead of stretching the mesh with the body.
        var scale = root.transform.lossyScale;
        pivot.localRotation = Quaternion.identity;
        pivot.localScale = new Vector3(1 / scale.x, 1 / scale.y, 1 / scale.z);
        pivot.localPosition = Vector3.zero;
        if (art == null)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Map Elements/rocks-low.prefab");
            art = (source != null ? (GameObject)PrefabUtility.InstantiatePrefab(source, pivot) : GameObject.CreatePrimitive(PrimitiveType.Sphere)).transform;
            art.name = "Rock art proxy";
        }
        art.SetParent(pivot, false); art.localPosition = Vector3.zero; art.localRotation = Quaternion.identity; art.localScale = Vector3.one;
        var meshes = art.GetComponentsInChildren<MeshFilter>(true);
        bool found = false; var bounds = new Bounds();
        foreach (var mesh in meshes)
        {
            if (mesh.sharedMesh == null) continue;
            var b = mesh.sharedMesh.bounds; var matrix = pivot.worldToLocalMatrix * mesh.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                var point = matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; } else bounds.Encapsulate(point);
            }
        }
        if (!found || bounds.size.sqrMagnitude < 0.0001f) throw new InvalidOperationException("Rope rock has no usable geometry.");
        float fit = 1.2f / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        art.localScale = Vector3.one * fit; art.localPosition = -bounds.center * fit;
        foreach (var collider in art.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        return pivot;
    }
    static Material MaterialAsset(string name, Color color)
    {
        string path = "Assets/AI/Behavior/Presentation/" + name + ".mat";
        var result = AssetDatabase.LoadAssetAtPath<Material>(path); if (result != null) return result;
        result = new Material(Shader.Find("Universal Render Pipeline/Unlit")); result.SetColor("_BaseColor", color);
        result.SetFloat("_Cull", 0); AssetDatabase.CreateAsset(result, path); return result;
    }
    static void HammerProxy(GameObject root)
    {
        foreach (var animator in root.GetComponent<EnemyAttack>().Weapons)
        {
            if (animator.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Hammer head proxy")) continue;
            var mesh = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault();
            var rigid = animator.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => f.sharedMesh != null && f.gameObject.activeInHierarchy && f.GetComponent<Renderer>() != null)
                .OrderByDescending(f => f.sharedMesh.bounds.size.sqrMagnitude).FirstOrDefault();
            if ((mesh == null || mesh.sharedMesh == null) && rigid == null)
                throw new InvalidOperationException("Hammer proxy requires an equipped weapon mesh");
            var source = mesh != null ? mesh.transform : rigid.transform;
            var bounds = mesh != null ? mesh.sharedMesh.bounds : rigid.sharedMesh.bounds;
            var tip = source.TransformPoint(new Vector3(bounds.max.x, bounds.center.y, bounds.center.z));
            var parent = mesh != null && mesh.bones.Length > 0 ? mesh.bones.OrderBy(b => (b.position-tip).sqrMagnitude).First() : source;
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube); block.name = "Hammer head proxy"; Object.DestroyImmediate(block.GetComponent<Collider>());
            block.transform.position = tip; block.transform.rotation = source.rotation; block.transform.localScale = Vector3.one * .7f;
            block.transform.SetParent(parent, true); block.GetComponent<Renderer>().sharedMaterial = source.GetComponent<Renderer>().sharedMaterial;
        }
    }
    static void Wave(string path, bool survival)
    {
        var data = AssetDatabase.LoadAssetAtPath<WaveData>(path);
        if (data == null) { data = ScriptableObject.CreateInstance<WaveData>(); AssetDatabase.CreateAsset(data, path); }
        data.type = survival ? WaveType.KOTH : WaveType.Roaming; data.spawnInterval = survival ? .2f : .5f;
        data.timerReset = survival ? 30 : 120; data.resourceTotal = survival ? 100 : 50; data.maxConcurrentEnemies = survival ? 32 : 24;
        float basic = survival ? 10.5f : 13.5f, otherSmall = survival ? 14 : 18, elite = (survival ? 30f : 10f) / (Roster.Length - 6);
        data.enemies = Roster.Select((path, i) => new WaveEnemy { prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path), weight = i < 4 ? basic : i < 6 ? otherSmall : elite }).ToArray();
        EditorUtility.SetDirty(data);
    }
}
