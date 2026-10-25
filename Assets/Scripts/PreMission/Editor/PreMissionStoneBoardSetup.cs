using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Explicit M2 prototype creation. Only new, owned assets and PreMissionScene change.
public static class PreMissionStoneBoardSetup
{
    public const string DirectoryPath = "Assets/PreMission/StoneBoard";
    public const string BoardPath = DirectoryPath + "/QuestStoneBoard.prefab";
    const int BoardLayer = 26;
    [MenuItem("Tools/Mech/Pre-Mission/Upgrade M2 Physical Stone Board")]
    public static void UpgradeAssets()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play mode before upgrading the board.");
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Directory.CreateDirectory(DirectoryPath); AssetDatabase.Refresh();
        EditorSceneManager.OpenScene(PreMissionPrototypeSetup.ScenePath);
        EnsureAssets();
        var flow = Object.FindAnyObjectByType<PreMissionFlowController>();
        var board = Object.FindAnyObjectByType<PreMissionStoneBoard>();
        if (board == null) board = ((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BoardPath))).GetComponent<PreMissionStoneBoard>();
        var fields = new SerializedObject(flow);
        fields.FindProperty("stoneBoard").objectReferenceValue = board; fields.ApplyModifiedPropertiesWithoutUndo();
        var boardView = (GameObject)fields.FindProperty("boardView").objectReferenceValue;
        boardView.GetComponent<Image>().color = Color.clear;
        foreach (var graphic in boardView.GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = graphic.GetComponentInParent<Button>() != null;
        ((Transform)fields.FindProperty("noticeParent").objectReferenceValue).gameObject.SetActive(false);
        ((TMP_Text)fields.FindProperty("boardHint").objectReferenceValue).text = "Choose a stone.  Arrow keys to browse, Enter to inspect.";
        var oldCamera = GameObject.Find("UI Camera")?.GetComponent<Camera>();
        if (oldCamera != null)
        {
            oldCamera.enabled = false; oldCamera.tag = "Untagged";
            var listener = oldCamera.GetComponent<AudioListener>(); if (listener != null) listener.enabled = false;
        }
        RenderSettings.skybox = null; RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.20f, 0.23f, 0.25f);
        PrefabUtility.RecordPrefabInstancePropertyModifications(flow);
        foreach (var component in boardView.GetComponentsInChildren<Component>(true))
            if (component != null) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        EditorSceneManager.SaveScene(flow.gameObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("PRE-MISSION M2 PHYSICAL BOARD READY: " + PreMissionPrototypeSetup.ScenePath);
    }
    static void EnsureAssets()
    {
        var slab = Material("Board Slate", new Color(0.075f, 0.115f, 0.12f));
        var edge = Material("Board Frame", new Color(0.20f, 0.26f, 0.26f));
        var brass = Material("Inlaid Brass", new Color(0.40f, 0.33f, 0.19f));
        var highlight = Material("Selection Brass", new Color(0.84f, 0.68f, 0.36f), true);
        var small = Material("Small Stone", new Color(0.27f, 0.35f, 0.33f));
        var main = Material("Main Stone", new Color(0.42f, 0.37f, 0.28f));
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(DirectoryPath + "/PrototypeRock.asset");
        if (mesh == null) { mesh = RockMesh(); AssetDatabase.CreateAsset(mesh, DirectoryPath + "/PrototypeRock.asset"); }
        var ring = AssetDatabase.LoadAssetAtPath<Mesh>(DirectoryPath + "/Ring.asset");
        if (ring == null) { ring = RingMesh(); AssetDatabase.CreateAsset(ring, DirectoryPath + "/Ring.asset"); }
        var smallPrefab = StonePrefab(false, mesh, ring, small, highlight);
        var mainPrefab = StonePrefab(true, mesh, ring, main, highlight);
        if (File.Exists(BoardPath)) return; // Preserve later scene/prefab tuning on repeated upgrades.
        var root = new GameObject("Quest Stone Board") { layer = BoardLayer };
        var board = root.AddComponent<PreMissionStoneBoard>();
        Primitive("Board Slab", root.transform, new Vector3(0,-0.35f,0), new Vector3(17.8f,0.65f,8.7f), slab);
        Primitive("Inset Surface", root.transform, new Vector3(0,-0.01f,0), new Vector3(17.25f,0.12f,8.15f), slab);
        foreach (int sign in new[] { -1, 1 })
        {
            Primitive("Frame Side " + sign, root.transform, new Vector3(sign*8.82f,0.02f,0), new Vector3(0.22f,0.24f,8.8f), edge);
            Primitive("Frame End " + sign, root.transform, new Vector3(0,0.02f,sign*4.27f), new Vector3(17.8f,0.24f,0.22f), edge);
            Primitive("Inlay Side " + sign, root.transform, new Vector3(sign*8.57f,0.06f,0), new Vector3(0.025f,0.02f,8.25f), brass);
            Primitive("Inlay End " + sign, root.transform, new Vector3(0,0.06f,sign*4.05f), new Vector3(17.15f,0.02f,0.025f), brass);
        }
        foreach (float radius in new[] { 2.05f, 2.20f })
        {
            var mark = MeshObject("Reserved Main Stone Inlay", root.transform, ring, brass);
            mark.transform.localPosition = new Vector3(0,0.06f,0); mark.transform.localScale = new Vector3(radius,1,radius*0.78f);
        }
        var stones = new GameObject("Quest Stones"); stones.transform.SetParent(root.transform, false); stones.layer = BoardLayer;
        var camera = new GameObject("Quest Board Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
        camera.transform.SetParent(root.transform, false); camera.transform.localRotation = Quaternion.Euler(72f,0,0);
        camera.tag = "MainCamera"; camera.orthographic = true; camera.nearClipPlane = 0.1f; camera.farClipPlane = 65f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.035f,0.047f,0.05f);
        camera.cullingMask = 1 << BoardLayer; camera.allowHDR = false; camera.allowMSAA = true;
        Light("Warm Key", root.transform, new Vector3(42,-30,0), new Color(1f,0.88f,0.72f), 1.5f, true);
        Light("Cool Fill", root.transform, new Vector3(65,145,0), new Color(0.63f,0.78f,1f), 0.45f, false);
        var fields = new SerializedObject(board);
        fields.FindProperty("boardCamera").objectReferenceValue = camera;
        fields.FindProperty("stoneParent").objectReferenceValue = stones.transform;
        fields.FindProperty("smallStonePrefab").objectReferenceValue = smallPrefab.GetComponent<QuestStoneView>();
        fields.FindProperty("mainStonePrefab").objectReferenceValue = mainPrefab.GetComponent<QuestStoneView>();
        var designs = fields.FindProperty("compositions"); designs.arraySize = 3;
        var slots = new[] {
            new[] { new Vector2(-5.3f,2.05f), new Vector2(5.3f,2.05f), new Vector2(0,-3.15f), new Vector2(-5.3f,-2.05f), new Vector2(5.3f,-2.05f), new Vector2(0,3.2f) },
            new[] { new Vector2(-5.7f,1.9f), new Vector2(5.7f,1.9f), new Vector2(0,-3.15f), new Vector2(-5.4f,-2.05f), new Vector2(5.4f,-2.05f), new Vector2(0,3.2f) },
            new[] { new Vector2(-5.5f,2.15f), new Vector2(5.5f,2.15f), new Vector2(0,-3.15f), new Vector2(-5.6f,-2.05f), new Vector2(5.6f,-2.05f), new Vector2(0,3.2f) }
        };
        for (int i=0;i<3;i++)
        {
            var design = designs.GetArrayElementAtIndex(i); design.FindPropertyRelative("name").stringValue = new[] { "Surround", "Wide", "Crown" }[i];
            var positions = design.FindPropertyRelative("smallSlots"); positions.arraySize = 6;
            var angles = design.FindPropertyRelative("angles"); angles.arraySize = 6;
            for (int j=0;j<6;j++) { positions.GetArrayElementAtIndex(j).vector2Value = slots[i][j]; angles.GetArrayElementAtIndex(j).floatValue = j%2==0 ? -4 : 4; }
        }
        fields.ApplyModifiedPropertiesWithoutUndo(); board.RefreshCameraFrame();
        PrefabUtility.SaveAsPrefabAsset(root, BoardPath); Object.DestroyImmediate(root);
    }
    static GameObject StonePrefab(bool main, Mesh mesh, Mesh ring, Material material, Material highlight)
    {
        string path = DirectoryPath + (main ? "/MainQuestStone.prefab" : "/SmallQuestStone.prefab");
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (existing != null) return existing;
        var root = new GameObject(main ? "Main Quest Stone" : "Small Quest Stone") { layer = BoardLayer };
        var collider = root.AddComponent<BoxCollider>();
        collider.center = new Vector3(0, main ? 0.72f : 0.48f, 0); collider.size = main ? new Vector3(4.65f,1.5f,3f) : new Vector3(2.9f,1.02f,2f);
        var view = root.AddComponent<QuestStoneView>();
        var visual = new GameObject("Stone Visual") { layer = BoardLayer }; visual.transform.SetParent(root.transform, false);
        var rock = MeshObject("Faceted Rock", visual.transform, mesh, material);
        rock.transform.localScale = main ? new Vector3(2f,1.35f,1.35f) : new Vector3(1.25f,0.85f,0.85f);
        var label = new GameObject("Quest Inscription", typeof(TextMeshPro)).GetComponent<TextMeshPro>();
        label.gameObject.layer = BoardLayer; label.transform.SetParent(visual.transform, false);
        label.transform.localPosition = new Vector3(0,main ? 1.39f : 0.9f,0);
        label.transform.localRotation = Quaternion.Euler(90,0,0);
        label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/century-gothic-bold-4_ufonts.asset");
        label.rectTransform.sizeDelta = main ? new Vector2(3.8f,1.45f) : new Vector2(2.65f,1.35f);
        label.alignment = TextAlignmentOptions.Center; label.color = new Color(0.94f,0.91f,0.79f);
        label.enableAutoSizing = true; label.fontSizeMax = label.fontSize = main ? 3.5f : 2.8f; label.fontSizeMin = main ? 2.5f : 1.9f;
        label.raycastTarget = false;
        label.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        var selection = MeshObject("Selection Ring", root.transform, ring, highlight);
        selection.transform.localPosition = new Vector3(0,0.07f,0); selection.transform.localScale = main ? new Vector3(2.45f,1,1.8f) : new Vector3(1.6f,1,1.17f);
        selection.SetActive(false);
        var fields = new SerializedObject(view);
        fields.FindProperty("visual").objectReferenceValue = visual.transform;
        fields.FindProperty("rock").objectReferenceValue = rock.GetComponent<MeshRenderer>();
        fields.FindProperty("selectionRing").objectReferenceValue = selection;
        fields.FindProperty("label").objectReferenceValue = label;
        fields.ApplyModifiedPropertiesWithoutUndo();
        var prefab = PrefabUtility.SaveAsPrefabAsset(root,path); Object.DestroyImmediate(root); return prefab;
    }
    static Material Material(string name, Color color, bool unlit=false)
    {
        string path = DirectoryPath + "/" + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path); if (existing != null) return existing;
        var shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
        if (shader == null) throw new InvalidOperationException("M2 asset creation requires the project's URP package.");
        var material = new Material(shader) { name=name, color=color };
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness",0.12f);
        AssetDatabase.CreateAsset(material,path); return material;
    }
    static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material material)
    {
        var go = new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer)) { layer=BoardLayer };
        go.transform.SetParent(parent,false); go.GetComponent<MeshFilter>().sharedMesh=mesh; go.GetComponent<MeshRenderer>().sharedMaterial=material; return go;
    }
    static void Primitive(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube); go.name=name; go.layer=BoardLayer;
        go.transform.SetParent(parent,false); go.transform.localPosition=position; go.transform.localScale=scale;
        go.GetComponent<MeshRenderer>().sharedMaterial=material; Object.DestroyImmediate(go.GetComponent<Collider>());
    }
    static void Light(string name, Transform parent, Vector3 angle, Color color, float intensity, bool shadows)
    {
        var light=new GameObject(name,typeof(Light)).GetComponent<Light>(); light.transform.SetParent(parent,false);
        light.transform.localRotation=Quaternion.Euler(angle); light.type=LightType.Directional; light.color=color; light.intensity=intensity;
        light.cullingMask=1<<BoardLayer; light.shadows=shadows ? LightShadows.Soft : LightShadows.None;
    }
    static Mesh RockMesh()
    {
        var points=new List<Vector3>(); var random=new System.Random(713);
        for(int ring=0;ring<3;ring++) for(int i=0;i<8;i++)
        {
            float angle=(i*45f+ring*9f)*Mathf.Deg2Rad, radius=(ring==1 ? 1f : 0.72f)*(0.9f+(float)random.NextDouble()*0.18f);
            points.Add(new Vector3(Mathf.Cos(angle)*radius,new[]{0.04f,0.4f,0.78f}[ring]+(float)random.NextDouble()*0.09f,Mathf.Sin(angle)*radius));
        }
        points.Add(Vector3.zero); points.Add(new Vector3(0.04f,0.99f,-0.05f));
        var vertices=new List<Vector3>(); var triangles=new List<int>();
        void Face(int a,int b,int c)
        {
            var first=points[a]; var second=points[b]; var third=points[c];
            if(Vector3.Dot(Vector3.Cross(second-first,third-first),(first+second+third)/3f-new Vector3(0,0.4f,0))<0) { var saved=second; second=third; third=saved; }
            int start=vertices.Count; vertices.AddRange(new[]{first,second,third}); triangles.AddRange(new[]{start,start+1,start+2});
        }
        for(int i=0;i<8;i++)
        {
            int next=(i+1)%8; Face(24,next,i); Face(25,16+i,16+next);
            for(int r=0;r<2;r++) { Face(r*8+i,r*8+next,(r+1)*8+i); Face(r*8+next,(r+1)*8+next,(r+1)*8+i); }
        }
        var mesh=new Mesh { name="Prototype Faceted Rock" }; mesh.SetVertices(vertices); mesh.SetTriangles(triangles,0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
    }
    static Mesh RingMesh()
    {
        var vertices=new List<Vector3>(); var triangles=new List<int>();
        for(int i=0;i<64;i++) { float angle=i*Mathf.PI*2/64; var direction=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle)); vertices.Add(direction); vertices.Add(direction*0.976f); }
        for(int i=0;i<64;i++) { int a=i*2,b=((i+1)%64)*2; triangles.AddRange(new[]{a,a+1,b,b,a+1,b+1}); }
        var mesh=new Mesh { name="Prototype Stone Ring" }; mesh.SetVertices(vertices); mesh.SetTriangles(triangles,0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
    }
}
