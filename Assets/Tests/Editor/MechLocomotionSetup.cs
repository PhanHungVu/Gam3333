using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Explicit setup for the isolated validation copy; never runs on import.
public static class MechLocomotionSetup
{
    const string PlayerPath = "Assets/Prefabs/Mech/Base/FullPlayer_mech_base.prefab";
    const string ModuleFolder = "Assets/Prefabs/Mech/Locomotion";

    public static void RunBatch()
    {
        BuildAssets();
        EditorApplication.Exit(0);
    }

    public static void RunSceneBatch()
    {
        BuildAssets();
        MechLocomotionSceneChecks.RunBatch();
    }

    static void BuildAssets()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated batch project.");
        if (!AssetDatabase.IsValidFolder(ModuleFolder)) AssetDatabase.CreateFolder("Assets/Prefabs/Mech", "Locomotion");
        var baseLegs = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Mech/Base/Parts/Old/legs_base.prefab");
        if (baseLegs.GetComponent<LocomotionModule>() == null) baseLegs.AddComponent<BipedLocomotionModule>();
        PrefabUtility.SaveAsPrefabAsset(baseLegs, "Assets/Prefabs/Mech/Base/Parts/Old/legs_base.prefab");
        PrefabUtility.UnloadPrefabContents(baseLegs);

        Part[] prefabs = { BuildModule(LocomotionType.Biped), BuildModule(LocomotionType.Hover), BuildModule(LocomotionType.Tank) };
        var player = PrefabUtility.LoadPrefabContents(PlayerPath);
        if (player.GetComponent<MechEquipmentLocomotion>() == null) player.AddComponent<MechEquipmentLocomotion>();
        foreach (var part in player.GetComponentsInChildren<Part>(true))
            if (part.partType == PartType.Legs && part.GetComponent<LocomotionModule>() == null)
                part.gameObject.AddComponent<BipedLocomotionModule>();
        var debug = player.GetComponent<MechLocomotionDebug>();
        if (debug == null) debug = player.AddComponent<MechLocomotionDebug>();
        var serialized = new SerializedObject(debug);
        var choices = serialized.FindProperty("modulePrefabs");
        choices.arraySize = prefabs.Length;
        for (int i = 0; i < prefabs.Length; i++) choices.GetArrayElementAtIndex(i).objectReferenceValue = prefabs[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SaveAsPrefabAsset(player, PlayerPath);
        PrefabUtility.UnloadPrefabContents(player);
        AssetDatabase.SaveAssets();
        Debug.Log("LOCOMOTION ASSETS COMPLETE");
    }

    static Part BuildModule(LocomotionType type)
    {
        string name = type + "Module";
        var root = new GameObject(name);
        var part = root.AddComponent<Part>();
        part.isStartingPart = false;
        string dataPath = ModuleFolder + "/" + name + ".asset";
        var data = AssetDatabase.LoadAssetAtPath<PartData>(dataPath);
        if (data == null)
        {
            data = ScriptableObject.CreateInstance<PartData>();
            data.partId = "locomotion_" + type.ToString().ToLowerInvariant();
            data.partName = type + " Lower Body";
            data.description = "Milestones 1-3 placeholder locomotion part.";
            AssetDatabase.CreateAsset(data, dataPath);
        }
        data.partType = PartType.Legs;
        part.data = data;
        string materialPath = ModuleFolder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", type == LocomotionType.Biped ? new Color(0.3f, 0.5f, 0.8f)
                : type == LocomotionType.Hover ? new Color(0.1f, 0.75f, 0.8f) : new Color(0.85f, 0.5f, 0.16f));
            AssetDatabase.CreateAsset(material, materialPath);
        }
        if (type == LocomotionType.Biped)
        {
            root.AddComponent<BipedLocomotionModule>();
            Cube(root, "Left Leg", new Vector3(0, -0.35f, -0.6f), new Vector3(0.5f, 0.7f, 0.45f));
            Cube(root, "Right Leg", new Vector3(0, -0.35f, 0.6f), new Vector3(0.5f, 0.7f, 0.45f));
        }
        else if (type == LocomotionType.Hover)
        {
            root.AddComponent<HoverLocomotionModule>();
            Cube(root, "Hover Platform", new Vector3(0, -0.2f, 0), new Vector3(1.3f, 0.3f, 2.3f));
            Cube(root, "Left Thruster", new Vector3(0, -0.48f, -0.9f), new Vector3(0.75f, 0.35f, 0.35f));
            Cube(root, "Right Thruster", new Vector3(0, -0.48f, 0.9f), new Vector3(0.75f, 0.35f, 0.35f));
        }
        else
        {
            root.AddComponent<TankLocomotionModule>();
            Cube(root, "Axle", new Vector3(0, -0.2f, 0), new Vector3(0.8f, 0.3f, 1.8f));
            Cube(root, "Left Track", new Vector3(0, -0.45f, -0.9f), new Vector3(1.6f, 0.5f, 0.45f));
            Cube(root, "Right Track", new Vector3(0, -0.45f, 0.9f), new Vector3(1.6f, 0.5f, 0.45f));
        }
        foreach (var renderer in root.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = material;
        var asset = PrefabUtility.SaveAsPrefabAsset(root, ModuleFolder + "/" + name + ".prefab");
        Object.DestroyImmediate(root);
        return asset.GetComponent<Part>();
    }

    static void Cube(GameObject root, string name, Vector3 position, Vector3 scale)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(root.transform, false);
        // Existing torso Legs socket is offset behind/above the root capsule,
        // with model forward along +X. Keep the pivot and align only our cubes.
        cube.transform.localPosition = position + new Vector3(0.78f, -1f, -0.065f);
        cube.transform.localScale = scale;
    }
}
