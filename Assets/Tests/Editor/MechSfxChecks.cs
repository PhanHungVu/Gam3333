using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class MechSfxChecks
{
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static AudioSource AttackSource(MechWeaponAnimator driver) => driver.transform.Find("Weapon Attack Audio")?.GetComponent<AudioSource>();
    static void MovementTick(MechMovementAudio audio) => typeof(MechMovementAudio).GetMethod("LateUpdate", Private).Invoke(audio, null);

    public static void Run(Action<bool, string> check)
    {
        foreach (string name in MechSfxSetup.Sounds.Concat(new[] { "Golem1Legs_Move" }))
        {
            var clip = MechSfxSetup.Clip(name);
            check(clip != null && clip.length > 0f && clip.channels == 1 && clip.LoadAudioData(), name + " is imported as a ready spatial audio clip");
            var samples = new float[clip.samples * clip.channels];
            check(clip.GetData(samples, 0) && samples.Any(s => Mathf.Abs(s) > 0.01f), name + " contains playable, non-silent audio");
        }
        var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(MechAnimatedLegsSetup.PlayerPath), new Vector3(60000f, 1.6f, 0f), Quaternion.identity);
        var plane = GameObject.CreatePrimitive(PrimitiveType.Plane); plane.transform.position = new Vector3(60000f, 0f, 0f);
        plane.transform.localScale = Vector3.one * 10f;
        var camera = new GameObject("SFX Aim").AddComponent<Camera>();
        var dropped = new List<GameObject>();
        try
        {
            var equipment = player.GetComponent<EquipManager>();
            var combat = player.GetComponent<MechCombat>(); TestMech.StopPlayerInput(combat); combat.BindCamera(camera);
            var movement = player.GetComponent<MechMovementController>(); TestMech.StopPlayerInput(movement);
            string[] parts = { "arm_sword", "arm_throwingaxe", "shoulder_cannon", "shoulder_crossbow", "arm_mace", "arm_crossbow" };
            PartCategory[] slots = { PartCategory.ArmL, PartCategory.ArmR, PartCategory.ShoulderL, PartCategory.ShoulderR, PartCategory.ArmL, PartCategory.ArmR };
            for (int i = 0; i < parts.Length; i++)
            {
                var part = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/" + parts[i] + ".prefab")).GetComponent<Part>();
                var driver = part.GetComponent<MechWeaponAnimator>();
                check(!driver.PlayAttack(1f) && AttackSource(driver) == null, parts[i] + " ground pickup never creates or plays attack audio");
                var old = equipment.GetEquipped(slots[i]);
                check(equipment.TryEquip(part, slots[i]), parts[i] + " equips for sound check");
                dropped.Add(old.gameObject); old.transform.position += Vector3.right * 200f;
                camera.transform.SetPositionAndRotation(part.muzzle != null ? part.muzzle.position : player.transform.position, Quaternion.identity);
                Physics.SyncTransforms();
                MechProjectile shot = null;
                if (i == 0 || i == 4) check(combat.TryAttack(PartCategory.ArmL).Started, parts[i] + " admits its melee/preview attack");
                else shot = i == 2 ? combat.TryAttack(PartCategory.ShoulderL).Projectile : i == 3 ? combat.TryAttack(PartCategory.ShoulderR).Projectile : combat.TryAttack(PartCategory.ArmR).Projectile;
                var source = AttackSource(driver);
                check(source != null && source.clip == MechSfxSetup.Clip(MechSfxSetup.Sounds[i]) && source.isPlaying,
                    parts[i] + " accepted attack plays exactly its assigned sound");
                check(source.volume == driver.Animations.attackVolume && !source.playOnAwake && source.spatialBlend > 0f,
                    parts[i] + " attack has configured volume and spatial playback");
                source.time = source.clip.length * 0.3f; int audioFrame = source.timeSamples;
                if (i == 0 || i == 4) check(!combat.TryAttack(PartCategory.ArmL).Started, parts[i] + " cooldown rejects another attack");
                else check((i == 2 ? combat.TryAttack(PartCategory.ShoulderL).Projectile : i == 3 ? combat.TryAttack(PartCategory.ShoulderR).Projectile : combat.TryAttack(PartCategory.ArmR).Projectile) == null,
                    parts[i] + " cooldown rejects another shot");
                check(source.timeSamples == audioFrame && driver.GetComponentsInChildren<AudioSource>().Length == 1,
                    parts[i] + " rejected attack does not restart audio or create more voices");
                if (shot != null) Object.DestroyImmediate(shot.gameObject);
                equipment.Unequip(slots[i]); dropped.Add(part.gameObject);
                check(!source.isPlaying, parts[i] + " drop immediately stops its attack sound");
                check(equipment.TryEquip(part, slots[i]) && !source.isPlaying, parts[i] + " re-equip stays silent until another attack");
                dropped.Remove(part.gameObject);
            }
            check(!new SerializedObject(player.GetComponent<CombatFeedback>()).FindProperty("fireAudio").boolValue,
                "Cannon uses one authored attack voice rather than duplicate feedback audio");
            var audio = player.GetComponent<MechMovementAudio>(); Physics.SyncTransforms();
            for (int i = 0; i < 60; i++) movement.Tick(default, camera.transform, 0.02f);
            MovementTick(audio);
            check(audio.transform.Find("Leg Movement Audio") == null, "Idle player has no movement audio voice");
            for (int i = 0; i < 30; i++) movement.Tick(new MechLocomotionInput { move = Vector2.up }, camera.transform, 0.02f);
            MovementTick(audio); var walking = audio.transform.Find("Leg Movement Audio").GetComponent<AudioSource>();
            check(walking.isPlaying && walking.loop && walking.clip == MechSfxSetup.Clip("Golem1Legs_Move"), "Resolved grounded walking starts the leg movement loop");
            float walkPitch = walking.pitch;
            for (int i = 0; i < 30; i++) movement.Tick(new MechLocomotionInput { move = Vector2.up, sprint = true }, camera.transform, 0.02f);
            MovementTick(audio);
            check(Mathf.Abs(walking.pitch - walkPitch) < 0.01f && audio.GetComponentsInChildren<AudioSource>().Count(s => s.loop) == 1,
                "Removed sprint does not increase walking sound cadence or add voices");
            movement.Tick(new MechLocomotionInput { move = Vector2.up, quickBoost = true }, camera.transform, 0.02f); MovementTick(audio);
            check(!walking.isPlaying, "Boost stops walking audio");
            for (int i = 0; i < 60; i++) movement.Tick(default, camera.transform, 0.02f);
            movement.Tick(new MechLocomotionInput { jump = true, jumpHeld = true }, camera.transform, 0.02f); MovementTick(audio);
            check(!walking.isPlaying, "Airborne player has no walking audio");
            audio.enabled = false; check(!walking.isPlaying, "Disabling movement audio stops its loop");
            foreach (string family in new[] { "mace", "crossbow" })
                check(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Enemy_" + family + ".prefab").GetComponent<MechMovementAudio>() != null,
                    family + " enemy also has spatial movement audio");
        }
        finally
        {
            foreach (var part in dropped) if (part != null) Object.DestroyImmediate(part);
            Object.DestroyImmediate(player); Object.DestroyImmediate(plane); Object.DestroyImmediate(camera.gameObject);
        }
    }
}
