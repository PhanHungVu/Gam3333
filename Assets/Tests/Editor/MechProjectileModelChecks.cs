using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class MechProjectileModelChecks
{
    // Called by the real-input scene check, after URP is running. This keeps
    // both projectiles at their actual authored world size for visual review.
    public static void CaptureModels()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
        var center = new Vector3(80000f, 2f, 0f);
        var bolt = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TestMech.BoltPath), center + Vector3.left * 0.6f, Quaternion.identity);
        var axe = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TestMech.AxePath), center + Vector3.right * 0.6f, Quaternion.identity);
        var camera = new GameObject("Projectile Model Preview").AddComponent<Camera>();
        var light = new GameObject("Projectile Preview Light").AddComponent<Light>();
        var texture = new RenderTexture(1280, 720, 24);
        Texture2D image = null;
        var previous = RenderTexture.active;
        try
        {
            light.type = LightType.Directional; light.intensity = 1f;
            light.transform.rotation = Quaternion.Euler(35f, -35f, 0f);
            camera.transform.position = center + new Vector3(4f, 2.5f, -5f);
            camera.transform.LookAt(center);
            camera.orthographic = true; camera.orthographicSize = 1.35f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.12f, 0.14f, 0.17f);
            camera.targetTexture = texture;
            RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = texture });
            RenderTexture.active = texture;
            image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "Gam3333-projectile-models.png"), image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous; camera.targetTexture = null; texture.Release();
            Object.DestroyImmediate(texture); if (image != null) Object.DestroyImmediate(image);
            Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(light.gameObject);
            Object.DestroyImmediate(bolt); Object.DestroyImmediate(axe);
        }
    }

    static void Fly(MechProjectile shot, float dt) => typeof(MechProjectile).GetMethod("Step", BindingFlags.Instance | BindingFlags.NonPublic)
        .Invoke(shot, new object[] { dt });
    static void Model(MechProjectile shot, string meshName, Action<bool, string> check)
    {
        check(shot != null && shot.IsLive, meshName + " is a live projectile");
        var mesh = shot.GetComponentInChildren<MeshFilter>();
        check(mesh != null && mesh.sharedMesh.name == meshName && mesh.GetComponent<Renderer>().sharedMaterial.mainTexture != null,
            meshName + " uses the authored textured mesh");
        check(shot.GetComponentsInChildren<Collider>(true).Length == 0 && shot.GetComponentsInChildren<Rigidbody>(true).Length == 0,
            meshName + " retains swept collision without duplicate physics bodies");
    }

    public static void Run(Action<bool, string> check)
    {
        var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TestMech.PlayerPath),
            new Vector3(56000f, 2f, 0f), Quaternion.identity);
        var view = new GameObject("Projectile Model Aim").AddComponent<Camera>();
        var cleanup = new List<GameObject>();
        try
        {
            var equipment = player.GetComponent<EquipManager>();
            var combat = player.GetComponent<MechCombat>(); TestMech.StopPlayerInput(combat); combat.BindCamera(view);
            foreach (var slot in new[] { PartCategory.ShoulderR, PartCategory.ArmR })
            {
                var part = player.GetComponent<MechCombat>().GetPart(slot);
                var muzzle = part.muzzle.position;
                view.transform.SetPositionAndRotation(muzzle, Quaternion.identity);
                var target = GameObject.CreatePrimitive(PrimitiveType.Cube); cleanup.Add(target);
                target.transform.position = muzzle + Vector3.forward * 6f;
                var health = target.AddComponent<CombatHealth>(); Physics.SyncTransforms();
                var shot = combat.TryAttack(slot).Projectile;
                Model(shot, slot == PartCategory.ShoulderR ? "CrossbowBoltMesh" : "ThrowingAxeMesh", check);
                check(Vector3.Distance(shot.transform.position, muzzle) < 0.001f && Vector3.Dot(shot.transform.forward, shot.Direction) > 0.999f,
                    slot + " launches from its authored muzzle toward aim");
                var loaded = part.GetComponentsInChildren<MeshFilter>().Single(m => m.sharedMesh.name == (slot == PartCategory.ShoulderR ? "CrossbowBoltMesh" : "ThrowingAxeMesh")).GetComponent<Renderer>();
                check(!loaded.enabled, slot + " hides the loaded model as its projectile leaves");
                var visual = shot.transform.Find("Projectile Visual"); var rotation = visual.localRotation; var direction = shot.Direction;
                Fly(shot, 0.01f);
                check(shot.Direction == direction && (slot == PartCategory.ArmR
                    ? Quaternion.Angle(rotation, visual.localRotation) > 1f : Quaternion.Angle(rotation, visual.localRotation) < 0.01f),
                    slot + " has the correct bolt/spinning-axe flight without homing");
                Fly(shot, 0.1f);
                check(!shot.IsLive && health.CurrentHealth == health.MaximumHealth - combat.GetPart(slot).Stats.GetStat(StatType.Damage),
                    slot + " model projectile still applies existing damage once");
                float after = health.CurrentHealth; Fly(shot, 0.1f);
                check(health.CurrentHealth == after, slot + " spent projectile cannot damage twice");
                Object.DestroyImmediate(shot.gameObject); Object.DestroyImmediate(target);
            }

            int shoulderAmmo = combat.AmmoRemaining(PartCategory.ShoulderR);
            var original = equipment.GetEquipped(PartCategory.ArmR);
            var right = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/arm_crossbow.prefab")).GetComponent<Part>();
            check(equipment.TryEquip(right, PartCategory.ArmR), "Arm crossbow equips normally"); cleanup.Add(original.gameObject);
            // The just-dropped axe is a real world blocker. Move it out of this
            // isolated firing lane before checking the newly equipped weapon.
            original.transform.position += Vector3.right * 100f; Physics.SyncTransforms();
            view.transform.SetPositionAndRotation(right.muzzle.position, Quaternion.identity);
            var bolt = combat.TryAttack(PartCategory.ArmR).Projectile; Model(bolt, "CrossbowBoltMesh", check);
            int crossbowAmmo = (int)right.Stats.GetStat(StatType.Ammo);
            float nextShot = right.Stats.MagazineSize == 1 ? Mathf.Max(right.Stats.ShotInterval, right.Stats.GetStat(StatType.Reload)) : right.Stats.ShotInterval;
            check(bolt.Source == right && right.RemainingAmmo == crossbowAmmo - 1 && combat.ReloadRemaining(PartCategory.ArmR) > nextShot - 0.1f,
                "RMB arm crossbow uses its ammo, fire rate and reload stats");
            check(combat.TryAttack(PartCategory.ArmR).Projectile == null && combat.AmmoRemaining(PartCategory.ShoulderR) == shoulderAmmo,
                "Arm reload rejects duplicate shots without consuming shoulder ammo");
            Object.DestroyImmediate(bolt.gameObject);
            var left = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/arm_crossbow.prefab")).GetComponent<Part>();
            original = equipment.GetEquipped(PartCategory.ArmL);
            check(equipment.TryEquip(left, PartCategory.ArmL), "Second arm crossbow equips into the left socket"); cleanup.Add(original.gameObject);
            original.transform.position += Vector3.right * 100f; Physics.SyncTransforms();
            view.transform.SetPositionAndRotation(left.muzzle.position, Quaternion.identity);
            MechProjectile leftShot = null;
            Action<MechProjectile> fired = shot => leftShot = shot;
            combat.ShotFired += fired;
            check(combat.TryAttack(PartCategory.ArmL).Started, "LMB fires the actual left arm crossbow while the right reloads");
            combat.ShotFired -= fired; Model(leftShot, "CrossbowBoltMesh", check);
            check(left.RemainingAmmo == crossbowAmmo - 1 && right.RemainingAmmo == crossbowAmmo - 1 && combat.AmmoRemaining(PartCategory.ShoulderR) == shoulderAmmo,
                "Both arm weapons own independent ammo and keep shoulder state intact");
            Object.DestroyImmediate(leftShot.gameObject);
            equipment.Unequip(PartCategory.ArmR); cleanup.Add(right.gameObject);
            check(combat.TryAttack(PartCategory.ArmR).Projectile == null, "Empty right arm cannot secretly fire the left arm's crossbow");
            check(combat.TryAttack((PartCategory)99).Projectile == null, "Unknown socket is safely rejected");
        }
        finally
        {
            foreach (var go in cleanup) if (go != null) Object.DestroyImmediate(go);
            Object.DestroyImmediate(player); Object.DestroyImmediate(view.gameObject);
        }
    }
}
