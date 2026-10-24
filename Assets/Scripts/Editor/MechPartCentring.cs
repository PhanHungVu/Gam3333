using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// One-off tool: centres the arm and shoulder parts on their attachment point, so a part attaches exactly
// at its socket on either side. Arms attach at their ball joint; shoulders (no joint) at their sideways
// visual centre. Sockets move to where each default part attaches today, so the default loadout looks the
// same. Run 1 (read-only report) first, then 2. Commit before running 2: git is the undo.
public static class MechPartCentring
{
    const string PartsFolder = "Assets/Prefabs/Mech/Base/Parts/";
    const string PlayerPath = "Assets/Prefabs/Mech/Base/FullPlayer_mech_base.prefab";
    const string ReportPath = "Logs/part-centring-report.txt";
    static readonly string[] Arms = { "arm_sword", "arm_mace", "arm_throwingaxe", "arm_crossbow" };
    static readonly string[] Shoulders = { "shoulder_cannon", "shoulder_crossbow" };
    // Places that hold instances of these part prefabs; they are adjusted so nothing visibly moves.
    static readonly string[] HoldingPrefabs = { "Assets/Prefabs/Enemies/Enemy_mace.prefab", "Assets/Prefabs/Enemies/Enemy_crossbow.prefab" };
    static readonly string[] HoldingScenes = { "Assets/Scenes/Merge Test.unity", "Assets/Scenes/ZooScene.unity" };

    [MenuItem("Tools/Mech/Parts/1. Report Arm And Shoulder Offsets")]
    public static void Report()
    {
        var report = new StringBuilder();
        report.AppendLine("Offsets in each part's own space (the part root is where it attaches today)");
        foreach (var name in Arms.Concat(Shoulders))
        {
            var root = PrefabUtility.LoadPrefabContents(PartsFolder + name + ".prefab");
            try
            {
                var joint = Joint(root.transform);
                var bounds = VisualBounds(root.transform);
                report.AppendLine($"{name}: joint {(joint != null ? V(AnchorOffset(root.transform)) : "none")}, " +
                    $"visual centre {V(root.transform.InverseTransformPoint(bounds.center))}, visual size {V(bounds.size)}, " +
                    $"attachment offset {V(AnchorOffset(root.transform))}");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        var player = PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            report.AppendLine("Player sockets, in their parent's space, and the default part's joint:");
            foreach (var socket in player.GetComponentsInChildren<Socket>(true))
            {
                var part = socket.GetComponentInChildren<Part>(true);
                var parent = socket.transform.parent;
                string attach = part != null && IsLimb(socket.socketType)
                    ? V(parent.InverseTransformPoint(part.transform.TransformPoint(AnchorOffset(part.transform)))) : "-";
                report.AppendLine($"{socket.name} ({socket.socketType}): socket {V(socket.transform.localPosition)}, " +
                    $"part {(part != null ? part.name : "none")}, attaches at {attach}");
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(player); }
        Directory.CreateDirectory("Logs");
        File.WriteAllText(ReportPath, report.ToString());
        Debug.Log("Part centring report written to " + ReportPath + "\n" + report);
    }

    [MenuItem("Tools/Mech/Parts/2. Centre Arm And Shoulder Parts")]
    public static void Apply()
    {
        if (!EditorUtility.DisplayDialog("Centre arm and shoulder parts",
            "This edits the 4 arm and 2 shoulder prefabs, the player prefab, both enemy prefabs and the Merge Test and Zoo scenes. " +
            "Commit first: git is the undo.", "Centre them", "Cancel")) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var offsets = new Dictionary<string, Vector3>();
        // 1. Each part prefab: move everything except the root marker so the attachment point sits on the root.
        foreach (var name in Arms.Concat(Shoulders))
        {
            string path = PartsFolder + name + ".prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var offset = AnchorOffset(root.transform);
                Shift(root.transform, -offset);
                offsets[AssetDatabase.AssetPathToGUID(path)] = offset;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        // 2. Player: each limb socket moves to where its default part attaches today, and that copy is centred.
        var player = PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            foreach (var socket in player.GetComponentsInChildren<Socket>(true))
            {
                if (!IsLimb(socket.socketType)) continue;
                var part = socket.GetComponentInChildren<Part>(true);
                if (part == null) continue;
                var offset = AnchorOffset(part.transform);
                socket.transform.position = part.transform.TransformPoint(offset);
                Shift(part.transform, -offset);
                part.transform.localPosition = Vector3.zero;
            }
            PrefabUtility.SaveAsPrefabAsset(player, PlayerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(player); }
        // 3. Placed instances (enemy arms, scene pickups) keep their current look.
        foreach (var path in HoldingPrefabs)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try { if (Compensate(root.transform, offsets) > 0) PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        string current = EditorSceneManager.GetActiveScene().path;
        foreach (var path in HoldingScenes)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            int moved = 0;
            foreach (var go in scene.GetRootGameObjects()) moved += Compensate(go.transform, offsets);
            if (moved > 0) EditorSceneManager.SaveScene(scene);
        }
        if (!string.IsNullOrEmpty(current)) EditorSceneManager.OpenScene(current, OpenSceneMode.Single);
        Report();
        Debug.Log("Arm and shoulder parts centred; the new offsets are in " + ReportPath);
    }

    static Transform Joint(Transform part) =>
        part.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith("Blue Golem Joint"));

    static bool IsLimb(PartCategory slot) => slot == PartCategory.ArmL || slot == PartCategory.ArmR
        || slot == PartCategory.ShoulderL || slot == PartCategory.ShoulderR;

    // Where the part attaches, in its own space: an arm at its ball joint; a shoulder part at its
    // sideways visual centre only (z is sideways for these sockets), keeping its own height and depth.
    static Vector3 AnchorOffset(Transform part)
    {
        var joint = Joint(part);
        if (joint != null) return part.InverseTransformPoint(joint.position);
        return new Vector3(0f, 0f, part.InverseTransformPoint(VisualBounds(part).center).z);
    }

    // Moves the part's contents, not its root marker sphere.
    static void Shift(Transform root, Vector3 delta)
    {
        foreach (Transform child in root)
            if (child.name != "Sphere") child.localPosition += delta;
    }

    // An instance's contents moved toward its root, so the root moves the other way by the same amount.
    static int Compensate(Transform root, Dictionary<string, Vector3> offsets)
    {
        int moved = 0;
        foreach (var part in root.GetComponentsInChildren<Part>(true))
        {
            if (PrefabUtility.GetNearestPrefabInstanceRoot(part.gameObject) != part.gameObject) continue;
            var source = PrefabUtility.GetCorrespondingObjectFromSource(part.gameObject);
            if (source == null) continue;
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
            if (!offsets.TryGetValue(guid, out var offset)) continue;
            part.transform.position += part.transform.TransformVector(offset);
            moved++;
        }
        return moved;
    }

    static Bounds VisualBounds(Transform root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true).Where(r => r.name != "Sphere").ToArray();
        if (renderers.Length == 0) return new Bounds(root.position, Vector3.zero);
        var bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        return bounds;
    }

    static string V(Vector3 v) => $"({v.x:0.###}, {v.y:0.###}, {v.z:0.###})";
}
