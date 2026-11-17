using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class GreyboxArmWeaponSetup
{
    public const string DataFolder = "Assets/PartCatalogue/GreyboxArms";
    public const string PrefabFolder = "Assets/Prefabs/Mech/GreyboxArms";
    public const string ArtFolder = "Assets/Combat/GreyboxArms";
    public const string CataloguePath = DataFolder + "/ArmWeapons5-13.asset";
    public const string Arena = "Assets/Scenes/EnemyBehaviorPlaytest.unity";
    public static readonly string[] Names = { "Minigun", "Hook", "Hammer", "Spear", "Shotgun", "Supersword", "Scythe", "PunchyArm", "MetalBlades" };
    public static readonly float[,] Stats = {
        {9,0,400,50,10,2,4.5f,0,-5}, {30,0,30,1,.2f,.7f,8,0,-3}, {100,50,0,0,.7f,0,1,3,-3.5f},
        {90,90,0,0,1,0,3,.5f,-4}, {150,0,50,5,.8f,3,6,0,-3}, {250,250,0,0,.35f,0,2.5f,2.5f,-7},
        {100,100,0,0,.65f,0,1.5f,1.5f,-4}, {30,30,0,0,2,0,.7f,.5f,-1.5f}, {60,30,40,3,1,2,8,.5f,-3}
    };
    public static string DataPath(int index) => DataFolder + "/PartData_Arm_" + Names[index] + ".asset";
    public static string PrefabPath(int index) => PrefabFolder + "/arm_" + Names[index] + ".prefab";
    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int split = path.LastIndexOf('/'); Folder(path.Substring(0, split)); AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1));
    }
    [MenuItem("Tools/Mech/Greybox Arm Weapons/Create or Update Weapons 5-13")]
    public static void CreateAssets()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before authoring prototypes.");
        Folder(DataFolder); Folder(PrefabFolder); Folder(ArtFolder);
        var catalogue = AssetDatabase.LoadAssetAtPath<GreyboxArmWeaponCatalogue>(CataloguePath);
        if (catalogue == null) { catalogue = ScriptableObject.CreateInstance<GreyboxArmWeaponCatalogue>(); AssetDatabase.CreateAsset(catalogue, CataloguePath); }
        catalogue.weapons = new Part[9];
        var dimensions = new[] { new Vector3(.6f,.6f,1.2f), new Vector3(.45f,.45f,.9f), new Vector3(1.1f,1.1f,1.7f),
            new Vector3(.25f,.25f,3), new Vector3(.65f,.5f,1.3f), new Vector3(.9f,.35f,3.8f), new Vector3(1.6f,.25f,2), new Vector3(.8f,.8f,.8f), new Vector3(.5f,.5f,1.2f) };
        var speeds = new[] { 80f, 60, 0, 0, 75, 0, 0, 0, 65 };
        var sizes = new[] { .06f, .15f, 0, 0, .09f, 0, 0, 0, .12f };
        var widths = new[] { 2f, 2, 1.8f, .7f, 2, 4.5f, 3.5f, 1, 2 };
        for (int i = 0; i < 9; i++)
        {
            Color color = Color.HSVToRGB(i / 9f, .4f, .75f);
            string materialPath = ArtFolder + "/" + Names[i] + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                material.color = color; AssetDatabase.CreateAsset(material, materialPath);
            }
            string iconPath = ArtFolder + "/" + Names[i] + "Icon.asset";
            var icon = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
            if (icon == null)
            {
                var texture = new Texture2D(8,8,TextureFormat.RGBA32,false) { name = Names[i] + " Icon", filterMode = FilterMode.Point };
                var pixels = new Color[64]; Array.Fill(pixels, color); texture.SetPixels(pixels); texture.Apply(); AssetDatabase.CreateAsset(texture, iconPath);
                icon = Sprite.Create(texture, new Rect(0,0,8,8), Vector2.one * .5f, 8); icon.name = Names[i] + " Icon"; AssetDatabase.AddObjectToAsset(icon, texture);
            }
            var data = AssetDatabase.LoadAssetAtPath<PartData>(DataPath(i));
            if (data == null) { data = ScriptableObject.CreateInstance<PartData>(); AssetDatabase.CreateAsset(data, DataPath(i)); }
            data.partId = "armPrototype" + (i + 5); data.partName = i == 7 ? "Punchy Arm" : i == 8 ? "Metal Blades" : Names[i];
            data.description = "Greybox arm weapon " + (i + 5) + ". Exact design stats; placeholder cube and sphere visuals.";
            data.partType = PartType.Arm; data.restrictToPartType = true; data.icon = icon;
            bool ranged = i == 0 || i == 1 || i == 4 || i == 8;
            data.subtype = i == 8 ? PartSubtype.Splash : ranged ? PartSubtype.Projectile : PartSubtype.Melee;
            data.projectilePrefab = null; data.projectileColor = color;
            data.stats = new List<StatEntry>();
            var types = new[] { StatType.Damage, StatType.SplashDamage, StatType.Ammo, StatType.MagSize, StatType.FireRate, StatType.Reload, StatType.Range, StatType.SplashRadius, StatType.MoveSpeed };
            for (int s = 0; s < types.Length; s++) if (ranged || s != 2 && s != 3 && s != 5) data.stats.Add(new StatEntry { type = types[s], value = Stats[i,s] });
            if (ranged) { data.stats.Add(new StatEntry { type = StatType.ProjectileSpeed, value = speeds[i] }); data.stats.Add(new StatEntry { type = StatType.ProjectileSize, value = sizes[i] }); }
            EditorUtility.SetDirty(data);
            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(i)) != null;
            var root = exists ? PrefabUtility.LoadPrefabContents(PrefabPath(i)) : new GameObject("Arm " + (i + 5) + " - " + data.partName);
            try
            {
                var part = root.GetComponent<Part>(); if (part == null) part = root.AddComponent<Part>(); part.data = data; part.isEquipped = part.isStartingPart = false;
                part.authoredScale = Vector3.one * 2; // existing player partScale is 0.5; primitives stay metre-sized
                var config = root.GetComponent<GreyboxArmWeapon>(); if (config == null) config = root.AddComponent<GreyboxArmWeapon>(); config.weapon = (GreyboxArmWeapon.Weapon)(i + 5);
                if (!exists) config.meleeWidth = widths[i];
                var cube = root.transform.Find("Cube");
                if (cube == null) { cube = GameObject.CreatePrimitive(PrimitiveType.Cube).transform; cube.name = "Cube"; cube.SetParent(root.transform, false); }
                cube.localScale = dimensions[i]; cube.localPosition = Vector3.forward * (dimensions[i].z * .5f + .5f); cube.GetComponent<Renderer>().sharedMaterial = material;
                var muzzle = root.transform.Find("Muzzle"); if (muzzle == null) { muzzle = new GameObject("Muzzle").transform; muzzle.SetParent(root.transform, false); }
                muzzle.localPosition = Vector3.forward * (dimensions[i].z + .6f); part.muzzle = muzzle;
                var body = root.GetComponent<Rigidbody>(); if (body == null) body = root.AddComponent<Rigidbody>(); body.mass = 2; body.isKinematic = false; body.useGravity = true;
                var visual = root.GetComponent<WeaponVisualSettings>(); if (visual == null) visual = root.AddComponent<WeaponVisualSettings>(); visual.material = material; visual.color = color; visual.projectileTrail = visual.meleeTrail = false; visual.firingShake = 0; visual.impactSize = .2f;
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(i)); catalogue.weapons[i] = prefab.GetComponent<Part>();
            }
            finally { if (exists) PrefabUtility.UnloadPrefabContents(root); else Object.DestroyImmediate(root); }
        }
        EditorUtility.SetDirty(catalogue); AssetDatabase.SaveAssets();
        Debug.Log("GREYBOX ARM ASSETS COMPLETE: 9 weapons, exact design stats.");
    }
    [MenuItem("Tools/Mech/Greybox Arm Weapons/Connect Bench in Current Test Scene")]
    public static void ConnectBench()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        var bench = Object.FindAnyObjectByType<GreyboxArmWeaponBench>();
        if (bench == null) { var root = new GameObject("Greybox Arm Weapons 5-13"); Undo.RegisterCreatedObjectUndo(root, "Add greybox weapon bench"); bench = root.AddComponent<GreyboxArmWeaponBench>(); }
        bench.catalogue = AssetDatabase.LoadAssetAtPath<GreyboxArmWeaponCatalogue>(CataloguePath);
        var input = Object.FindAnyObjectByType<MechPlayerInput>(); bench.player = input != null ? input.GetComponent<EquipManager>() : null;
        EditorUtility.SetDirty(bench); EditorSceneManager.MarkSceneDirty(bench.gameObject.scene); Selection.activeGameObject = bench.gameObject;
    }
    public static void CreateReviewAssets()
    {
        if (!Application.isBatchMode || !System.IO.Path.GetFullPath(Application.dataPath).Contains(".quest-review")) throw new InvalidOperationException("Review project only.");
        CreateAssets(); EditorApplication.Exit(0);
    }
}
