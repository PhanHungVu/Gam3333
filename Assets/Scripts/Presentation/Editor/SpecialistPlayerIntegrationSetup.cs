using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class SpecialistPlayerIntegrationSetup
{
    public const string PlayerPath = "Assets/Prefabs/Mech/Specialist/FullPlayer_mech_specialist.prefab";
    public const string LegsPath = "Assets/Prefabs/Mech/Specialist/BipedSpecialist_Module.prefab";
    public const string BlasterPath = "Assets/Prefabs/Mech/Specialist/arm_blaster_specialist.prefab";
    public const string CannonPath = "Assets/Prefabs/Mech/Specialist/shoulder_cannon_specialist.prefab";
    public const string SmallCrossbowPath = "Assets/Prefabs/Mech/Base/Parts/arm_crossbow.prefab";
    public const string LargeCrossbowPath = "Assets/Prefabs/Mech/Base/Parts/shoulder_crossbow.prefab";
    public const string RigidCrossbowMeshPath = "Assets/Anim/Weapons/ArmCrossbow_RigidMesh.asset";

    [MenuItem("Tools/Mech/Integrate Specialist Player Model")]
    public static void Apply()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before integrating player assets.");
        NormalizeSmallCrossbow();
        ConfigureLegacyWeapons(PlayerPath);
        // The old regular golem (FullPlayer_mech_golem) is retired: the specialist is the default player.
        // These existing tools also cover the new player, so later repair/tuning passes retain it.
        QuickDashPolishSetup.Apply();
        GameplaySceneMaintenance.Apply();
        // The imported Blaster has no authored gameplay stats or dedicated sound yet.
        // Remove its crossbow sound assignment; leave the exposed audio slot available.
        var audio = AssetDatabase.LoadAssetAtPath<MechWeaponAnimationSet>("Assets/Anim/Specialist/Blaster_Specialist.asset");
        if (audio != null && audio.attackSound != null
            && AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(audio.attackSound)) == "b5b86aa5770aafb4ba08e87050f8ce94")
        {
            audio.attackSound = null; EditorUtility.SetDirty(audio);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("SPECIALIST PLAYER INTEGRATED: regular golem visual, authored sockets, shared HUD and current dash. Heavy demo art and weapon stats preserved.");
    }

    static void ConfigureLegacyWeapons(string playerPath)
    {
        Pose shoulderMount = MountPose(CannonPath, "Mount_Shoulder");
        var cannonMount = shoulderMount;
        // The two models use opposite visual forward axes despite similarly named mount bones.
        cannonMount.rotation = Quaternion.Euler(0, 180, 0) * cannonMount.rotation;
        var nativeCannon = AssetDatabase.LoadAssetAtPath<GameObject>(CannonPath).GetComponent<MechWeaponAnimator>().Animations;
        var largeCrossbow = CompatibilityPose("shoulder_crossbow", cannonMount);
        var shoulders = new[] { CompatibilityPose("shoulder_cannon", cannonMount), largeCrossbow,
            new Socket.WeaponAttachment { animations = nativeCannon } };
        Pose armMount = MountPose(BlasterPath, "Mount_Arm");
        armMount.rotation = Quaternion.Euler(0, 180, 0) * armMount.rotation;
        var crossbowMount = CompatibilityPose("arm_crossbow", armMount);
        crossbowMount.position += Vector3.down * .4f; // 0.2 world units at the regular player's partScale.
        Pose smallMuzzle = CrossbowMuzzlePose(SmallCrossbowPath), largeMuzzle = CrossbowMuzzlePose(LargeCrossbowPath);
        crossbowMount.overrideMuzzle = true; crossbowMount.muzzlePosition = smallMuzzle.position; crossbowMount.muzzleEulerAngles = smallMuzzle.rotation.eulerAngles;
        shoulders[1].overrideMuzzle = true; shoulders[1].muzzlePosition = largeMuzzle.position; shoulders[1].muzzleEulerAngles = largeMuzzle.rotation.eulerAngles;
        var arms = new[] { crossbowMount };
        var root = PrefabUtility.LoadPrefabContents(playerPath);
        var posed = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(playerPath)); posed.SetActive(false);
        try
        {
            // The torso controller changes the imported bones' rest orientation on its first frame.
            // Sample a disposable copy so clearance uses gameplay axes without editing authored bones.
            var torso = posed.GetComponentsInChildren<Animator>(true).Single(a => a.runtimeAnimatorController != null && a.runtimeAnimatorController.name == "Torso_Specialist");
            torso.runtimeAnimatorController.animationClips[0].SampleAnimation(torso.gameObject, 0);
            bool changed = false;
            foreach (var socket in root.GetComponentsInChildren<Socket>(true))
            {
                bool shoulder = socket.socketType == PartCategory.ShoulderL || socket.socketType == PartCategory.ShoulderR;
                bool arm = socket.socketType == PartCategory.ArmL || socket.socketType == PartCategory.ArmR;
                if (!shoulder && !arm) continue;
                var poses = shoulder ? shoulders : arms;
                var fields = new SerializedObject(socket); var entries = fields.FindProperty("weaponAttachments"); entries.arraySize = poses.Length;
                for (int i = 0; i < poses.Length; i++)
                {
                    var entry = entries.GetArrayElementAtIndex(i);
                    entry.FindPropertyRelative("animations").objectReferenceValue = poses[i].animations;
                    float side = socket.socketType == PartCategory.ShoulderL ? -1 : 1;
                    // Socket axes follow the imported bone orientation, rather than the player's axes.
                    var posedSocket = posed.GetComponentsInChildren<Socket>(true).Single(s => s.socketType == socket.socketType);
                    // The remodeled cannon already fits its authored socket. Only legacy rigs need clearance.
                    // Keep the large crossbow's side spacing, with 0.15 world units less lift at partScale 0.5.
                    float lift = poses[i].animations == largeCrossbow.animations ? 1.2f : 1.5f;
                    Vector3 clearance = shoulder && poses[i].animations != nativeCannon
                        ? Quaternion.Inverse(posedSocket.transform.rotation) * posed.transform.rotation * new Vector3(side * .9f, lift, 0)
                        : Vector3.zero;
                    entry.FindPropertyRelative("position").vector3Value = poses[i].position + clearance;
                    entry.FindPropertyRelative("eulerAngles").vector3Value = poses[i].eulerAngles;
                    entry.FindPropertyRelative("overrideMuzzle").boolValue = poses[i].overrideMuzzle;
                    entry.FindPropertyRelative("muzzlePosition").vector3Value = poses[i].muzzlePosition;
                    entry.FindPropertyRelative("muzzleEulerAngles").vector3Value = poses[i].muzzleEulerAngles;
                }
                changed |= fields.ApplyModifiedPropertiesWithoutUndo();
            }
            if (changed) PrefabUtility.SaveAsPrefabAsset(root, playerPath);
        }
        finally { Object.DestroyImmediate(posed); PrefabUtility.UnloadPrefabContents(root); }
    }

    static Pose CrossbowMuzzlePose(string path)
    {
        // These legacy rigs put the marker at the stock/body, rather than the forward bolt tip.
        // Measure the neutral model on a copy so authored default bone poses stay untouched.
        var template = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path)); template.SetActive(false);
        try
        {
            var part = template.GetComponent<Part>(); var driver = template.GetComponent<MechWeaponAnimator>();
            driver.Animations.neutral.SampleAnimation(driver.Animator.gameObject, 0);
            var filters = driver.Animator.GetComponentsInChildren<MeshFilter>(true);
            var filter = filters.FirstOrDefault(f => f.name.StartsWith("CrossbowBolt", StringComparison.OrdinalIgnoreCase)) ?? filters.Single();
            var points = filter.sharedMesh.vertices.Select(v => template.transform.InverseTransformPoint(filter.transform.TransformPoint(v))).ToArray();
            var bounds = new Bounds(points[0], Vector3.zero); foreach (var point in points) bounds.Encapsulate(point);
            var tip = bounds.center; tip.x = bounds.max.x - .03f;
            var position = part.muzzle.parent.InverseTransformPoint(template.transform.TransformPoint(tip));
            var rotation = Quaternion.Inverse(part.muzzle.parent.rotation) * template.transform.rotation * Quaternion.LookRotation(Vector3.right, Vector3.up);
            return new Pose(position, rotation);
        }
        finally { Object.DestroyImmediate(template); }
    }

    static void NormalizeSmallCrossbow()
    {
        var root = PrefabUtility.LoadPrefabContents(SmallCrossbowPath);
        try
        {
            var driver = root.GetComponent<MechWeaponAnimator>(); var rig = driver.Animator.transform;
            if (Vector3.Distance(rig.localScale, Vector3.one) < .0001f)
            {
                if (MatchCrossbowCollider(rig.GetComponentsInChildren<MeshFilter>().Single())) PrefabUtility.SaveAsPrefabAsset(root, SmallCrossbowPath);
                return;
            }
            driver.Animations.neutral.SampleAnimation(rig.gameObject, 0);
            var mount = rig.GetComponentsInChildren<Transform>().Single(t => t.name == "WeapRoot");
            var filter = rig.GetComponentsInChildren<MeshFilter>().Single();
            Matrix4x4 previousMesh = filter.transform.localToWorldMatrix;
            Vector3 previousMount = mount.position; Vector3 previousMuzzle = root.GetComponent<Part>().muzzle.position;
            // Nonuniform scale above rotating bones sheared the mesh during idle/reload.
            // Bake the rest shape into a separate mesh; animate the original clips under a unit-scale rig.
            rig.localScale = Vector3.one; rig.position += previousMount - mount.position;
            Matrix4x4 correction = filter.transform.worldToLocalMatrix * previousMesh;
            var baked = Object.Instantiate(filter.sharedMesh); baked.name = "Arm Crossbow Rigid Geometry";
            var vertices = baked.vertices; var normals = baked.normals; var tangents = baked.tangents;
            Matrix4x4 normalMatrix = correction.inverse.transpose;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = correction.MultiplyPoint3x4(vertices[i]);
            for (int i = 0; i < normals.Length; i++) normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
            for (int i = 0; i < tangents.Length; i++) { Vector3 v = correction.MultiplyVector(tangents[i]).normalized; tangents[i] = new Vector4(v.x, v.y, v.z, tangents[i].w); }
            baked.vertices = vertices; baked.normals = normals; baked.tangents = tangents; baked.RecalculateBounds();
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(RigidCrossbowMeshPath);
            if (saved == null) { AssetDatabase.CreateAsset(baked, RigidCrossbowMeshPath); saved = baked; }
            else { EditorUtility.CopySerialized(baked, saved); EditorUtility.SetDirty(saved); Object.DestroyImmediate(baked); }
            filter.sharedMesh = saved;
            MatchCrossbowCollider(filter);
            root.GetComponent<Part>().muzzle.position = previousMuzzle;
            PrefabUtility.SaveAsPrefabAsset(root, SmallCrossbowPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static bool MatchCrossbowCollider(MeshFilter filter)
    {
        var box = filter.GetComponent<BoxCollider>(); var bounds = filter.sharedMesh.bounds;
        if (box == null || Vector3.Distance(box.center, bounds.center) < .0001f && Vector3.Distance(box.size, bounds.size) < .0001f) return false;
        box.center = bounds.center; box.size = bounds.size; return true;
    }

    static void ConfigureCannonMuzzle()
    {
        const string path = "Assets/Prefabs/Mech/Base/Parts/shoulder_cannon.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var driver = root.GetComponent<MechWeaponAnimator>(); driver.Animations.neutral.SampleAnimation(driver.Animator.gameObject, 0);
            var filter = driver.Animator.GetComponentsInChildren<MeshFilter>().Single();
            var points = filter.sharedMesh.vertices.Select(v => root.transform.InverseTransformPoint(filter.transform.TransformPoint(v))).ToArray();
            var bounds = new Bounds(points[0], Vector3.zero); foreach(var point in points) bounds.Encapsulate(point);
            // The legacy rig's barrel points along part-local +X; the native shoulder rotates it forward.
            var tip = bounds.center; tip.x = bounds.max.x - .03f;
            var muzzle = root.GetComponent<Part>().muzzle; Vector3 local = muzzle.parent.InverseTransformPoint(root.transform.TransformPoint(tip));
            Quaternion rotation = Quaternion.Inverse(muzzle.parent.rotation) * root.transform.rotation * Quaternion.LookRotation(Vector3.right, Vector3.up);
            if (Vector3.Distance(muzzle.localPosition, local) < .0001f && Quaternion.Angle(muzzle.localRotation, rotation) < .01f) return;
            muzzle.localPosition = local; muzzle.localRotation = rotation;
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static Socket.WeaponAttachment CompatibilityPose(string name, Pose reference)
    {
        string path = "Assets/Prefabs/Mech/Base/Parts/" + name + ".prefab";
        Pose old = MountPose(path, "WeapRoot");
        Quaternion rotation = reference.rotation * Quaternion.Inverse(old.rotation);
        return new Socket.WeaponAttachment {
            animations = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<MechWeaponAnimator>().Animations,
            position = reference.position - rotation * old.position, eulerAngles = rotation.eulerAngles
        };
    }

    public static Pose MountPose(string prefabPath, string boneName)
    {
        var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath)); root.SetActive(false);
        try
        {
            var driver = root.GetComponent<MechWeaponAnimator>();
            driver.Animations.neutral.SampleAnimation(driver.Animator.gameObject, 0);
            var bone = driver.Animator.GetComponentsInChildren<Transform>(true).Single(t => t.name == boneName);
            return new Pose(root.transform.InverseTransformPoint(bone.position), Quaternion.Inverse(root.transform.rotation) * bone.rotation);
        }
        finally { Object.DestroyImmediate(root); }
    }

}
