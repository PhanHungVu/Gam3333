using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Replaces presentation only. Root components, role, tuning, weapons and drop tables survive.
public static class SmallEnemyFodderSetup
{
    [MenuItem("Tools/Mech/Enemies/Use Fodder Body For All Small Enemies")]
    public static void Apply()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before authoring enemy assets.");
        var template = PrefabUtility.LoadPrefabContents(FodderModelSetup.FodderPath);
        try
        {
            foreach (string path in EnemyRosterSetup.Roster.Take(5))
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    ReplaceBody(root, template);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(template); }
        AssetDatabase.SaveAssets();
        Debug.Log("SMALL FODDER READY: sword, mace, hammer, axe and spinner use the Zoo body; existing ranged Fodder retained.");
    }

    static void ReplaceBody(GameObject root, GameObject template)
    {
        root.transform.position = Vector3.zero;
        if (root.GetComponent<EnemyFodderAnimation>() != null)
        {
            FodderModelSetup.ConfigureFodder(root);
            return;
        }
        var attack = root.GetComponent<EnemyAttack>();
        var weapons = attack.Weapons.Select(w => w.GetComponent<Part>()).Distinct().ToArray();
        var scale = weapons.Select(p => p.transform.lossyScale).ToArray();
        var rotation = weapons.Select(p => Quaternion.Inverse(root.transform.rotation) * p.transform.rotation).ToArray();
        foreach (var weapon in weapons) weapon.transform.SetParent(root.transform, true);
        var oldVisual = root.transform.Find("VisualMech");
        if (oldVisual != null) Object.DestroyImmediate(oldVisual.gameObject);
        var visual = Object.Instantiate(template.transform.Find("VisualMech").gameObject, root.transform, false).transform;
        visual.name = "VisualMech";
        // Only the body comes from the template. Its cannon is not a melee loadout.
        foreach (var part in visual.GetComponentsInChildren<Part>(true)) Object.DestroyImmediate(part.gameObject);
        FodderModelSetup.ConfigureFodder(root);
        var mount = visual.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Socket_Shoulder");
        var body = FodderModelSetup.BodyBounds(root);
        for (int i = 0; i < weapons.Length; i++)
        {
            var weapon = weapons[i];
            var holder = new GameObject("Weapon Mount " + (i + 1)).transform;
            holder.SetParent(mount, false);
            weapon.transform.SetParent(holder, false);
            weapon.transform.rotation = root.transform.rotation * rotation[i];
            var inherited = holder.lossyScale;
            weapon.transform.localScale = new Vector3(scale[i].x / Mathf.Abs(inherited.x), scale[i].y / Mathf.Abs(inherited.y), scale[i].z / Mathf.Abs(inherited.z));
            weapon.transform.localPosition = Vector3.zero;
            weapon.isEquipped = true;
            // Keep each existing weapon at its original size, repositioned above the
            // four-legged body. A separate holder leaves the weapon's animated rig intact.
            var bounds = WeaponBounds(weapon);
            float side = weapons.Length == 1 ? 0 : (i - (weapons.Length - 1) * .5f) * body.size.x * .8f;
            var target = root.transform.TransformPoint(new Vector3(body.center.x + side, body.max.y, body.center.z + .15f));
            target.y += bounds.extents.y * .65f;
            holder.position += target - bounds.center;
        }
        foreach (var component in new Component[] { root.GetComponent<EnemySpinnerAttack>(), root.GetComponent<EnemyHopPresentation>() })
        {
            if (component == null) continue;
            var fields = new SerializedObject(component); fields.FindProperty("visualRoot").objectReferenceValue = visual;
            fields.ApplyModifiedPropertiesWithoutUndo();
        }
        // The old body's pose copier was destroyed. Refit only the new body's
        // hurtbox/aim point; weapon colliders remain owned by their existing Parts.
        FodderModelSetup.ConfigureFodder(root);
    }

    static Bounds WeaponBounds(Part part)
    {
        bool found = false; Bounds bounds = default;
        foreach (var mesh in part.GetComponentsInChildren<MeshFilter>(true))
        {
            var renderer = mesh.GetComponent<MeshRenderer>();
            if (mesh.sharedMesh == null || renderer == null || !renderer.enabled || !mesh.gameObject.activeInHierarchy) continue;
            var b = renderer.bounds;
            if (!found) { bounds = b; found = true; } else bounds.Encapsulate(b);
        }
        if (!found) throw new InvalidOperationException("No visible melee mesh on " + part.name);
        return bounds;
    }

    public static void RunBatch()
    {
        try { Apply(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
