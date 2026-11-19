using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class GreyboxShoulderWeaponSetup
{
    public const string DataFolder = "Assets/PartCatalogue/GreyboxShoulders";
    public const string PrefabFolder = "Assets/Prefabs/Mech/GreyboxShoulders";
    public const string ArtFolder = "Assets/Combat/GreyboxShoulders";
    public const string CataloguePath = DataFolder + "/ShoulderWeapons1-9.asset";
    public const string ZooGroup = "Shoulder Weapons 3-9 - Ground Pickups";
    public static readonly string[] Names = { "Shield", "GiantRock", "FishingLine", "Mortar", "Katyusha", "Tentacles", "TeslaCoil" };
    public static readonly string[] Labels = { "Shield", "Giant Rock on a Rope", "Fishing Line", "Mortar", "Katyusha", "Tentacles", "Tesla Coil" };
    // Damage, Splash, Ammo, Mag, FireRate, Reload, Range(golems), AoE(m), Move(%).
    public static readonly float[,] Stats = {
        {0,0,0,0,.5f,0,0,0,-4}, {210,0,0,0,1f/3,0,7,1,-6}, {10,0,50,10,.7f,2,7,0,-2},
        {100,100,25,1,1,5,20,5,-3}, {20,20,100,10,.2f,5,15,2,-7}, {50,0,0,0,1.5f,0,4,0,-3}, {4,0,70,35,5,3,7,0,0}
    };
    public static string DataPath(int index) => DataFolder + "/PartData_Shoulder_" + Names[index] + ".asset";
    public static string PrefabPath(int index) => PrefabFolder + "/shoulder_" + Names[index] + ".prefab";
    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int split = path.LastIndexOf('/'); Folder(path.Substring(0, split)); AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1));
    }
    static Transform Primitive(Transform root, string name, PrimitiveType type, Vector3 size, Material mat, bool collision)
    {
        var shape = root.Find(name);
        if (shape == null) { shape = GameObject.CreatePrimitive(type).transform; shape.name = name; shape.SetParent(root, false); }
        shape.localScale = size; shape.GetComponent<Renderer>().sharedMaterial = mat;
        if (!collision && shape.GetComponent<Collider>() != null) Object.DestroyImmediate(shape.GetComponent<Collider>());
        return shape;
    }
    [MenuItem("Tools/Mech/Greybox Shoulder Weapons/Create or Update Weapons 3-9")]
    public static void CreateAssets()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        Folder(DataFolder); Folder(PrefabFolder); Folder(ArtFolder);
        var catalogue = AssetDatabase.LoadAssetAtPath<GreyboxShoulderWeaponCatalogue>(CataloguePath);
        if (catalogue == null) { catalogue = ScriptableObject.CreateInstance<GreyboxShoulderWeaponCatalogue>(); AssetDatabase.CreateAsset(catalogue, CataloguePath); }
        catalogue.weapons = new Part[9];
        catalogue.weapons[0] = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Specialist/shoulder_cannon_specialist.prefab").GetComponent<Part>();
        catalogue.weapons[1] = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/shoulder_crossbow.prefab").GetComponent<Part>();
        var types = new[] { StatType.Damage, StatType.SplashDamage, StatType.Ammo, StatType.MagSize, StatType.FireRate, StatType.Reload, StatType.Range, StatType.SplashRadius, StatType.MoveSpeed };
        var sizes = new[] { new Vector3(2.4f,2.8f,.3f), new Vector3(.9f,.6f,1.3f), new Vector3(.3f,.3f,1.4f), new Vector3(.9f,.9f,1.7f), new Vector3(1.2f,.8f,1.6f), new Vector3(.9f,.7f,.9f), new Vector3(.7f,1.4f,.7f) };
        for (int i = 0; i < 7; i++)
        {
            string matPath = ArtFolder + "/" + Names[i] + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Standard")); mat.color = Color.HSVToRGB(i / 7f, .45f, .8f); AssetDatabase.CreateAsset(mat, matPath); }
            string iconPath = ArtFolder + "/" + Names[i] + "Icon.asset"; var icon = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
            if (icon == null)
            {
                var texture = new Texture2D(8,8,TextureFormat.RGBA32,false) { name = Labels[i] + " Icon", filterMode = FilterMode.Point };
                var pixels = new Color[64]; Array.Fill(pixels, mat.color); texture.SetPixels(pixels); texture.Apply(); AssetDatabase.CreateAsset(texture, iconPath);
                icon = Sprite.Create(texture, new Rect(0,0,8,8), Vector2.one * .5f, 8); icon.name = Labels[i] + " Icon"; AssetDatabase.AddObjectToAsset(icon, texture);
            }
            var data = AssetDatabase.LoadAssetAtPath<PartData>(DataPath(i));
            if (data == null) { data = ScriptableObject.CreateInstance<PartData>(); AssetDatabase.CreateAsset(data, DataPath(i)); }
            data.partId = "shoulderPrototype" + (i + 3); data.partName = Labels[i]; data.description = "Greybox shoulder weapon " + (i + 3) + ".";
            data.partType = PartType.Shoulder; data.restrictToPartType = true; data.icon = icon;
            data.subtype = i == 0 ? PartSubtype.Special : i == 1 || i == 5 ? PartSubtype.Melee : i == 3 || i == 4 ? PartSubtype.Splash : PartSubtype.Projectile;
            data.stats = new List<StatEntry>();
            for (int s = 0; s < types.Length; s++)
                if (!(i == 0 || i == 1 || i == 5) || s != 2 && s != 3 && s != 5) data.stats.Add(new StatEntry { type = types[s], value = Stats[i,s] });
            if (i == 2 || i == 3 || i == 4)
            { data.stats.Add(new StatEntry { type = StatType.ProjectileSpeed, value = i == 2 ? 120 : i == 3 ? 40 : 65 }); data.stats.Add(new StatEntry { type = StatType.ProjectileSize, value = i == 2 ? .025f : i == 3 ? .2f : .12f }); }
            data.projectilePrefab = null; data.projectileColor = mat.color; EditorUtility.SetDirty(data);
            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(i)) != null;
            var root = exists ? PrefabUtility.LoadPrefabContents(PrefabPath(i)) : new GameObject("Shoulder " + (i + 3) + " - " + Labels[i]);
            try
            {
                var part = root.GetComponent<Part>(); if (part == null) part = root.AddComponent<Part>(); part.data = data; part.isStartingPart = part.isEquipped = false; part.authoredScale = Vector3.one * 2;
                var config = root.GetComponent<GreyboxShoulderWeapon>(); if (config == null) config = root.AddComponent<GreyboxShoulderWeapon>(); config.weapon = (GreyboxShoulderWeapon.Weapon)(i + 3);
                if (i == 4) { config.salvoFanAngle = 8; config.salvoLaunchSpacing = .3f; }
                if (i == 6) { config.chainDistance = 4; config.maximumChainTargets = 5; }
                config.debugMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Combat/Prototype Combat.mat");
                var cube = Primitive(root.transform, "Cube", PrimitiveType.Cube, sizes[i], mat, true); cube.localPosition = Vector3.forward * (sizes[i].z * .5f + .5f);
                var muzzle = root.transform.Find("Muzzle"); if (muzzle == null) { muzzle = new GameObject("Muzzle").transform; muzzle.SetParent(root.transform, false); }
                muzzle.localPosition = Vector3.forward * (sizes[i].z + .6f); part.muzzle = muzzle;
                if (i == 0) config.shieldVisual = cube;
                if (i == 1)
                {
                    config.rockVisual = Primitive(root.transform, "Rock", PrimitiveType.Sphere, Vector3.one, mat, false); config.rockVisual.gameObject.SetActive(false);
                    var rope = root.transform.Find("Rope"); if (rope == null) { rope = new GameObject("Rope", typeof(LineRenderer)).transform; rope.SetParent(root.transform, false); }
                    config.rope = rope.GetComponent<LineRenderer>(); config.rope.useWorldSpace = true; config.rope.sharedMaterial = config.debugMaterial;
                    config.rope.startWidth = config.rope.endWidth = .06f; config.rope.enabled = false;
                }
                if (i == 5) { config.tentacleVisual = Primitive(root.transform, "Tentacle", PrimitiveType.Cube, Vector3.one, mat, false); config.tentacleVisual.gameObject.SetActive(false); }
                var body = root.GetComponent<Rigidbody>(); if (body == null) body = root.AddComponent<Rigidbody>(); body.mass = 3; body.useGravity = true; body.isKinematic = false;
                var visual = root.GetComponent<WeaponVisualSettings>(); if (visual == null) visual = root.AddComponent<WeaponVisualSettings>(); visual.material = mat; visual.color = mat.color; visual.projectileTrail = visual.meleeTrail = false; visual.firingShake = 0; visual.impactSize = .2f;
                catalogue.weapons[i + 2] = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(i)).GetComponent<Part>();
            }
            finally { if (exists) PrefabUtility.UnloadPrefabContents(root); else Object.DestroyImmediate(root); }
        }
        EditorUtility.SetDirty(catalogue); AssetDatabase.SaveAssets();
        Debug.Log("SHOULDER ASSETS COMPLETE: existing Cannon/Crossbow plus 7 separate primitive weapons.");
    }
    [MenuItem("Tools/Mech/Greybox Shoulder Weapons/Connect Existing Test Bench")]
    public static void ConnectBench()
    {
        var bench = Object.FindAnyObjectByType<GreyboxArmWeaponBench>(); if (bench == null) throw new InvalidOperationException("Open EnemyBehaviorPlaytest with its existing weapon bench.");
        bench.shoulderCatalogue = AssetDatabase.LoadAssetAtPath<GreyboxShoulderWeaponCatalogue>(CataloguePath);
        EditorUtility.SetDirty(bench); EditorSceneManager.MarkSceneDirty(bench.gameObject.scene);
    }
    [MenuItem("Tools/Mech/Greybox Shoulder Weapons/Place New Pickups in Zoo")]
    public static void PlaceZooPickups()
    {
        if (Application.isPlaying || SceneManagerPath() != GreyboxZooWeaponSetup.Zoo) throw new InvalidOperationException("Open ZooScene outside Play Mode first.");
        var parts = GameObject.Find("parts"); if (parts == null) throw new InvalidOperationException("Zoo parts display missing.");
        var group = parts.transform.Find(ZooGroup);
        if (group == null) { group = new GameObject(ZooGroup).transform; group.SetParent(parts.transform, false); group.SetPositionAndRotation(Vector3.zero, Quaternion.identity); }
        for (int i = 0; i < 7; i++)
        {
            var definition = AssetDatabase.LoadAssetAtPath<PartData>(DataPath(i));
            var part = group.GetComponentsInChildren<Part>(true).FirstOrDefault(p => p.data == definition);
            if (part == null) part = ((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(i)), group)).GetComponent<Part>();
            Vector3 cell = new Vector3(45 + i % 3 * 6, 0, -52 + i / 3 * 7); var cube = part.transform.Find("Cube");
            part.name = "Shoulder " + (i + 3) + " " + Labels[i]; part.transform.SetPositionAndRotation(cell - Vector3.forward * cube.localPosition.z + Vector3.up * (cube.localScale.y * .5f + .08f), Quaternion.identity); part.transform.localScale = Vector3.one;
            part.GetComponent<GreyboxShoulderWeapon>().keepAuthoredStats = true;
            PrefabUtility.RecordPrefabInstancePropertyModifications(part.transform); PrefabUtility.RecordPrefabInstancePropertyModifications(part.GetComponent<GreyboxShoulderWeapon>());
            var label = group.Find("Label " + i); if (label == null) { label = new GameObject("Label " + i, typeof(TextMesh)).transform; label.SetParent(group, false); }
            label.SetPositionAndRotation(cell + new Vector3(0,.15f,-2.7f), Quaternion.Euler(50,0,0));
            var text = label.GetComponent<TextMesh>(); text.text = "S" + (i + 3) + " " + Labels[i]; text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.fontSize = 48; text.characterSize = .07f;
            text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center; text.color = new Color(.12f,.15f,.18f); text.GetComponent<Renderer>().sharedMaterial = text.font.material;
        }
        EditorSceneManager.MarkSceneDirty(parts.scene);
    }
    static string SceneManagerPath() => UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
    public static void CreateForReview()
    {
        if (!Application.isBatchMode || !Application.dataPath.Contains(".quest-review")) throw new InvalidOperationException("Disposable project only.");
        CreateAssets(); EditorSceneManager.OpenScene(GreyboxArmWeaponSetup.Arena); ConnectBench(); EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorSceneManager.OpenScene(GreyboxZooWeaponSetup.Zoo); PlaceZooPickups(); PlaceZooPickups(); EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log("SHOULDER SETUP COMPLETE"); EditorApplication.Exit(0);
    }
}
