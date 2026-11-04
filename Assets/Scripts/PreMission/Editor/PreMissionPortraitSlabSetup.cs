using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Update owned presentation assets while keeping imported art and quest data intact.
public static class PreMissionPortraitSlabSetup
{
    const string Folder = PreMissionStoneBoardSetup.DirectoryPath;
    public const string FontPath = "Assets/Fonts/medieval_sharp/MedievalSharp Quest SDF.asset";
    public const string BackPath = Folder + "/BackSlab.prefab";
    static readonly QuestMissionType[] Types = { QuestMissionType.KingOfTheHill, QuestMissionType.Escort, QuestMissionType.Elimination };
    [MenuItem("Tools/Mech/Pre-Mission/Use Portrait Quest Slabs and Medieval Font")]
    public static void UpgradeAssets()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before updating the quest UI.");
        var font = Font();
        UpgradeStone(false, font); UpgradeStone(true, font);
        var back = BackPrefab(font);
        EditPrefab("Assets/Prefabs/UI/PreMission/PreMissionUI.prefab", root =>
        {
            ApplyFont(root, font);
            var fields = new SerializedObject(root.GetComponent<PreMissionFlowController>());
            var board = (GameObject)fields.FindProperty("boardView").objectReferenceValue;
            var heading = board.transform.Find("Board Heading"); if (heading != null) heading.gameObject.SetActive(false);
            ((TMP_Text)fields.FindProperty("boardMessage").objectReferenceValue).gameObject.SetActive(false);
            var exit = (Button)fields.FindProperty("exitButton").objectReferenceValue;
            var exitRect = (RectTransform)exit.transform;
            exitRect.anchorMax = exitRect.anchorMin + new Vector2(.18f, .12f);
            var label = exit.GetComponentInChildren<TMP_Text>(true); label.text = "BACK";
            var slabButton = exit.GetComponent<PreMissionSlabButton>();
            if (slabButton == null) slabButton = exit.gameObject.AddComponent<PreMissionSlabButton>();
            var buttonFields = new SerializedObject(slabButton);
            buttonFields.FindProperty("modelPrefab").objectReferenceValue = back;
            buttonFields.ApplyModifiedPropertiesWithoutUndo();
            AddTemporaryProgressButton(board.transform, root.GetComponent<PreMissionFlowController>(), font);
        });
        EditPrefab("Assets/Prefabs/UI/PreMission/QuestNotice.prefab", root => ApplyFont(root, font), optional: true);
        EditPrefab(PreMissionStoneBoardSetup.BoardPath, root =>
        {
            ApplyFont(root, font);
            var fields = new SerializedObject(root.GetComponent<PreMissionStoneBoard>());
            var motion = new SerializedObject(root.GetComponent<PreMissionStoneMotion>());
            motion.FindProperty("mainClearanceDuration").floatValue = .85f;
            motion.FindProperty("smallInspectionSize").floatValue = 2.7f;
            motion.ApplyModifiedPropertiesWithoutUndo();
            // Portrait footprints also drive terrain clearance, picking and the
            // main landing relocation. Keep a valid authored fallback for them.
            var compositions = fields.FindProperty("compositions");
            var slots = new[] { new Vector2(-6.8f, 0), new Vector2(-3.6f, 2.2f), new Vector2(-3.6f, -2.2f),
                new Vector2(6.8f, 0), new Vector2(3.6f, 2.2f), new Vector2(3.6f, -2.2f) };
            for (int i = 0; i < compositions.arraySize; i++)
            {
                var composition = compositions.GetArrayElementAtIndex(i);
                var points = composition.FindPropertyRelative("smallSlots"); var angles = composition.FindPropertyRelative("angles");
                points.arraySize = angles.arraySize = slots.Length;
                for (int n = 0; n < slots.Length; n++) { points.GetArrayElementAtIndex(n).vector2Value = slots[n]; angles.GetArrayElementAtIndex(n).floatValue = 0; }
            }
            fields.ApplyModifiedPropertiesWithoutUndo();
        });
        AssetDatabase.SaveAssets();
        Debug.Log("PORTRAIT QUEST UI READY: supplied category lettering, data-driven mission types/logos, MedievalSharp and physical Back slab");
    }
    static TMP_FontAsset Font()
    {
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (existing != null) { PopulateFont(existing); ConfigureFallback(existing); return existing; }
        var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/medieval_sharp/MedievalSharp-Book.ttf");
        if (source == null) throw new InvalidOperationException("Import MedievalSharp-Book.ttf first.");
        var font = TMP_FontAsset.CreateFontAsset(source, 90, 9, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, false);
        font.name = "MedievalSharp Quest SDF";
        AssetDatabase.CreateAsset(font, FontPath);
        AssetDatabase.AddObjectToAsset(font.material, font);
        foreach (var atlas in font.atlasTextures) AssetDatabase.AddObjectToAsset(atlas, font);
        PopulateFont(font);
        ConfigureFallback(font);
        EditorUtility.SetDirty(font); return font;
    }
    static void PopulateFont(TMP_FontAsset font) => font.TryAddCharacters(string.Concat(Enumerable.Range(32, 95).Select(i => (char)i)) + "–—•←→", out _);
    static void ConfigureFallback(TMP_FontAsset font)
    {
        if (font.fallbackFontAssetTable == null) font.fallbackFontAssetTable = new List<TMP_FontAsset>();
        var fallback = TMP_Settings.defaultFontAsset;
        if (fallback != null && fallback != font && !font.fallbackFontAssetTable.Contains(fallback)) font.fallbackFontAssetTable.Add(fallback);
        EditorUtility.SetDirty(font);
    }
    static void ApplyFont(GameObject root, TMP_FontAsset font)
    {
        foreach (var label in root.GetComponentsInChildren<TMP_Text>(true))
        { label.font = font; label.fontSharedMaterial = font.material; }
    }
    static void UpgradeStone(bool main, TMP_FontAsset font)
    {
        EditPrefab(Folder + (main ? "/MainQuestStone.prefab" : "/SmallQuestStone.prefab"), root =>
        {
            var fields = new SerializedObject(root.GetComponent<QuestStoneView>());
            var visual = (Transform)fields.FindProperty("visual").objectReferenceValue;
            var rock = (MeshRenderer)fields.FindProperty("rock").objectReferenceValue;
            var size = main ? new Vector3(3.2f, .65f, 4.1f) : new Vector3(1.55f, .36f, 2.35f);
            rock.transform.localScale = size;
            rock.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Folder + (main ? "/Art Main Slab.mat" : "/Art Slab.mat"));
            if(main)
            {
                rock.sharedMaterial.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(PreMissionArtTableSetup.SourceDirectory + "/BigSlabTex.png"));
                var variants = fields.FindProperty("slabVariants");
                var meshes = Enumerable.Range(0, variants.arraySize).Select(i => (Mesh)variants.GetArrayElementAtIndex(i).objectReferenceValue);
                FitPaintedAtlasRegion(rock.sharedMaterial, new Rect(.82f, .12f, .12f, .65f), meshes);
            }
            var collider = root.GetComponent<BoxCollider>();
            collider.size = new Vector3(size.x + .18f, main ? 1.2f : .78f, size.z + .18f);
            collider.center = new Vector3(0, main ? .58f : .36f, 0);
            var ring = (GameObject)fields.FindProperty("selectionRing").objectReferenceValue;
            ring.transform.localScale = new Vector3((size.x + .45f) * .5f, 1, (size.z + .45f) * .5f);
            var label = (TMP_Text)fields.FindProperty("label").objectReferenceValue;
            label.font = font; label.fontSharedMaterial = font.material; label.text = main ? "ELIMINATION" : "KOTH";
            label.transform.localRotation = Quaternion.Euler(90, 0, 0);
            label.transform.localPosition = new Vector3(0, size.y + .025f, main ? .48f : .24f);
            label.rectTransform.sizeDelta = new Vector2(size.x * .85f, main ? .48f : .32f);
            label.fontSize = label.fontSizeMax = main ? 3.1f : 2f; label.fontSizeMin = main ? 2.3f : 1.2f;
            label.enableAutoSizing = true; label.textWrappingMode = TextWrappingModes.NoWrap;
            label.alignment = TextAlignmentOptions.Center; label.color = new Color(.9f, .87f, .76f);
            var old = visual.Find("Category Lettering"); if (old != null) Object.DestroyImmediate(old.gameObject);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PreMission/New Quest Stone/" + (main ? "mainQuestText.glb" : "sideQuestText.glb"));
            if (source == null) throw new InvalidOperationException("Import the supplied quest lettering first.");
            var heading = FittedModel(source, visual, "Category Lettering", main ? 2.35f : 1.25f,
                new Vector3(0, size.y + .025f, main ? 1.35f : .78f), FadeMaterial("Slab Lettering", null, new Color(.95f, .93f, .87f)));
            fields.FindProperty("categoryInscription").objectReferenceValue = heading.gameObject;
            var logosRoot = visual.Find("Mission Logos"); if (logosRoot != null) Object.DestroyImmediate(logosRoot.gameObject);
            logosRoot = new GameObject("Mission Logos").transform; logosRoot.SetParent(visual, false);
            var logos = fields.FindProperty("missionLogos"); logos.arraySize = Types.Length;
            for (int i = 0; i < Types.Length; i++)
            {
                var logoSource = AssetDatabase.LoadAssetAtPath<GameObject>(PreMissionMissionLogoSetup.SourceDirectory + "/Logo" + (i + 1) + ".glb");
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(PreMissionMissionLogoSetup.SourceDirectory + "/Logo" + (i + 1) + "Tex.png");
                var logo = FittedModel(logoSource, logosRoot, Types[i] + " Logo", main ? 1.45f : .83f,
                    new Vector3(0, size.y + .025f, main ? -.78f : -.5f), FadeMaterial("Slab Mission Logo " + (i + 1), texture, Color.white));
                var entry = logos.GetArrayElementAtIndex(i); entry.FindPropertyRelative("missionType").enumValueIndex = (int)Types[i];
                entry.FindPropertyRelative("root").objectReferenceValue = logo.gameObject; logo.gameObject.SetActive(i == (main ? 2 : 0));
            }
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            { child.gameObject.layer = root.layer; PrefabUtility.RecordPrefabInstancePropertyModifications(child.gameObject); }
            fields.ApplyModifiedPropertiesWithoutUndo();
        });
    }
    static Transform FittedModel(GameObject source, Transform parent, string name, float width, Vector3 position, Material material)
    {
        if (source == null) throw new InvalidOperationException("Missing model for " + name);
        var fit = new GameObject(name).transform; fit.SetParent(parent, false);
        var orientation = new GameObject("Reading Orientation").transform; orientation.SetParent(fit, false);
        orientation.localRotation = Quaternion.Euler(0, -90, 0);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(source, orientation);
        foreach (var collider in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
        var bounds = new Bounds(); bool found = false;
        foreach (var filter in fit.GetComponentsInChildren<MeshFilter>(true))
        {
            var local = filter.sharedMesh.bounds; var matrix = fit.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            var part = new Bounds(matrix.MultiplyPoint3x4(local.center), Vector3.zero);
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                part.Encapsulate(matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents, new Vector3(x, y, z))));
            if (found) bounds.Encapsulate(part); else { bounds = part; found = true; }
        }
        if (!found) throw new InvalidOperationException("No mesh in " + name);
        float scale = width / bounds.size.x; fit.localScale = Vector3.one * scale;
        fit.localPosition = position - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) * scale;
        foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
            renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = name != "Category Lettering";
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }
        return fit;
    }
    static Material FadeMaterial(string name, Texture texture, Color color)
    {
        string path = Folder + "/" + name + ".mat";
        bool lettering = name == "Slab Lettering";
        bool back = name == "Back Slab";
        var shader = Shader.Find(lettering ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
        else if (lettering) material.shader = shader;
        material.color = color; material.SetTexture("_BaseMap", texture);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .2f);
        material.SetFloat("_Blend", 0); material.SetFloat("_AlphaClip", 0);
        if (material.HasProperty("_BlendModePreserveSpecular")) material.SetFloat("_BlendModePreserveSpecular", 0);
        material.SetFloat("_Surface", back ? 0 : 1); material.SetFloat("_SrcBlend", (float)(back ? BlendMode.One : BlendMode.SrcAlpha)); material.SetFloat("_DstBlend", (float)(back ? BlendMode.Zero : BlendMode.OneMinusSrcAlpha));
        material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); material.SetFloat("_DstBlendAlpha", (float)(back ? BlendMode.Zero : BlendMode.OneMinusSrcAlpha));
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        // These solid meshes also fade during inspection. Keep their own rear
        // faces from painting over the raised front surfaces while fully shown.
        material.SetFloat("_ZWrite", 1); material.SetFloat("_Cull", (float)CullMode.Off);
        if (material.HasProperty("_ReceiveShadows")) material.SetFloat("_ReceiveShadows", lettering ? 0 : 1);
        if (lettering) material.EnableKeyword("_RECEIVE_SHADOWS_OFF"); else material.DisableKeyword("_RECEIVE_SHADOWS_OFF");
        if(back) material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT"); else material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetOverrideTag("RenderType", back ? "Opaque" : "Transparent"); material.renderQueue = (int)(back ? RenderQueue.Geometry : RenderQueue.Transparent);
        if(back) material.SetShaderPassEnabled("DepthOnly", true);
        EditorUtility.SetDirty(material); return material;
    }
    static void AddTemporaryProgressButton(Transform board, PreMissionFlowController flow, TMP_FontAsset font)
    {
        const string name = "TEMP - Advance Quest Progress";
        var old = board.Find(name); if (old != null) Object.DestroyImmediate(old.gameObject);
        var root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(PreMissionQuestTestButton));
        root.layer = 5; root.transform.SetParent(board, false);
        var rect = (RectTransform)root.transform;
        rect.anchorMin = new Vector2(.73f, .88f); rect.anchorMax = new Vector2(.98f, .97f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var image = root.GetComponent<Image>(); image.color = new Color(.24f, .29f, .25f, .96f);
        var button = root.GetComponent<Button>(); button.targetGraphic = image;
        var label = new GameObject("Progress Label", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        label.gameObject.layer = 5; label.transform.SetParent(root.transform, false);
        label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(8, 4); label.rectTransform.offsetMax = new Vector2(-8, -4);
        label.font = font; label.fontSharedMaterial = font.material; label.text = "TEST: +1 Quest (0/3)";
        label.alignment = TextAlignmentOptions.Center; label.fontSize = label.fontSizeMax = 22; label.fontSizeMin = 14;
        label.enableAutoSizing = true; label.color = new Color(.96f, .93f, .84f); label.raycastTarget = false;
        var fields = new SerializedObject(root.GetComponent<PreMissionQuestTestButton>());
        fields.FindProperty("flow").objectReferenceValue = flow;
        fields.FindProperty("button").objectReferenceValue = button;
        fields.FindProperty("label").objectReferenceValue = label;
        fields.ApplyModifiedPropertiesWithoutUndo();
    }
    static GameObject BackPrefab(TMP_FontAsset font)
    {
        var root = new GameObject("Back Slab"); root.layer = 26;
        try
        {
            var rock = new GameObject("Slab", typeof(MeshFilter), typeof(MeshRenderer)); rock.transform.SetParent(root.transform, false);
            rock.transform.localScale = new Vector3(3, .24f, 1.1f);
            rock.GetComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/HUD Slab1 Mesh.asset");
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(PreMissionArtTableSetup.SourceDirectory + "/SlabTex.png");
            var material = FadeMaterial("Back Slab", texture, Color.white);
            FitPaintedAtlasRegion(material, new Rect(.36f, .35f, .25f, .28f), new[] { rock.GetComponent<MeshFilter>().sharedMesh });
            rock.GetComponent<MeshRenderer>().sharedMaterial = material; rock.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var text = new GameObject("Back", typeof(TextMeshPro)).GetComponent<TextMeshPro>(); text.transform.SetParent(root.transform, false);
            text.transform.localRotation = Quaternion.Euler(90, 0, 0); text.transform.localPosition = new Vector3(0, .27f, 0);
            text.font = font; text.fontSharedMaterial = font.material; text.text = "BACK"; text.color = new Color(.92f, .88f, .77f);
            text.fontSize = 5; text.alignment = TextAlignmentOptions.Center; text.rectTransform.sizeDelta = new Vector2(2.5f, .7f);
            foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 26;
            return PrefabUtility.SaveAsPrefabAsset(root, BackPath);
        }
        finally { Object.DestroyImmediate(root); }
    }
    static void FitPaintedAtlasRegion(Material material, Rect painted, System.Collections.Generic.IEnumerable<Mesh> meshes)
    {
        var min = Vector2.positiveInfinity; var max = Vector2.negativeInfinity;
        foreach(var mesh in meshes) if(mesh != null) foreach(var uv in mesh.uv)
        { min = Vector2.Min(min, uv); max = Vector2.Max(max, uv); }
        var extent = max - min;
        if(extent.x <= .0001f || extent.y <= .0001f) throw new InvalidOperationException("Slab UVs have no usable area.");
        // The supplied atlases include a UV debug grid outside the painted stone.
        // Fit the owned material into its painted patch; imported art stays intact.
        var scale = new Vector2(painted.width / extent.x, painted.height / extent.y);
        var offset = painted.min - Vector2.Scale(min, scale);
        material.SetTextureScale("_BaseMap", scale); material.SetTextureOffset("_BaseMap", offset);
        if(material.HasProperty("_MainTex"))
        {
            material.SetTexture("_MainTex", material.GetTexture("_BaseMap"));
            material.SetTextureScale("_MainTex", scale); material.SetTextureOffset("_MainTex", offset);
        }
        EditorUtility.SetDirty(material);
    }
    static void EditPrefab(string path, Action<GameObject> edit, bool optional = false)
    {
        if (optional && AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return;
        var root = PrefabUtility.LoadPrefabContents(path);
        try { edit(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
