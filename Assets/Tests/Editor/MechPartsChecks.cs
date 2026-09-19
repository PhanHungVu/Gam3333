using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
public static class MechPartsChecks
{
    public static void Run(Action<bool,string> check)
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/FullPlayer_mech_base.prefab");
        var player=Object.Instantiate(prefab,new Vector3(22000,1.6f,0),Quaternion.identity);
        var equipment=player.GetComponent<EquipManager>(); var combat=player.GetComponent<MechCombatController>();
        var bridge=player.GetComponent<MechEquipmentCombat>();var rig=player.GetComponent<MechPlayerRig>();
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=new Vector3(22000,-0.25f,0);floor.transform.localScale=new Vector3(100,0.5f,100);
        check(player.GetComponent<SimpleMove>()==null && player.GetComponent<Rigidbody>()==null && player.GetComponent<CharacterController>()!=null,
            "Merged player uses only the existing CharacterController movement");
        check(rig.GameplayCamera!=null && rig.GameplayCamera.GetComponent<CameraControlThirdPerson>().Target==player.transform,
            "Merged player creates/binds the existing Cinemachine camera before gameplay initialization");
        for(int i=0;i<=6;i++) check(equipment.GetEquipped((PartCategory)i)!=null,"Starting part registered: "+(PartCategory)i);
        check(combat.HasSword && combat.HasWeapon(MechWeaponSlot.ThrowingAxe) && combat.HasWeapon(MechWeaponSlot.Cannon) && combat.HasWeapon(MechWeaponSlot.Crossbow),"All four existing weapon assets drive equipped combat");
        check(combat.GetWeapon(MechWeaponSlot.Cannon).damage==100 && combat.GetWeapon(MechWeaponSlot.Cannon).effectiveRange==20
            && combat.GetWeapon(MechWeaponSlot.Cannon).maximumRange==30 && combat.GetWeapon(MechWeaponSlot.Cannon).explosionRadius==6,"Part stats preserve cannon damage, effective/max range and AoE");
        check(combat.GetWeapon(MechWeaponSlot.ThrowingAxe).effectiveRange==12 && combat.GetWeapon(MechWeaponSlot.Crossbow).effectiveRange==40
            && combat.AmmoRemaining(MechWeaponSlot.ThrowingAxe)==30 && combat.AmmoRemaining(MechWeaponSlot.Crossbow)==50,"Axe and crossbow retain their original ranges and ammunition");
        foreach(var part in player.GetComponentsInChildren<Part>())
            foreach(var collider in part.GetComponents<Collider>()) check(!collider.enabled,"Equipped physical collider disabled");
        var cannon=bridge.GetPart(MechWeaponSlot.Cannon);
        var shot=combat.TryFire(MechWeaponSlot.Cannon);
        check(shot!=null && combat.AmmoRemaining(MechWeaponSlot.Cannon)==19 && combat.TryFire(MechWeaponSlot.Cannon)==null,"Equipped cannon fires once and observes its reload");
        if(shot!=null)Object.DestroyImmediate(shot.gameObject);
        equipment.Unequip(PartCategory.ShoulderL);
        check(!cannon.isEquipped && cannon.GetComponent<Rigidbody>()!=null && !combat.HasWeapon(MechWeaponSlot.Cannon)
            && combat.TryFire(MechWeaponSlot.Cannon)==null,"Dropping a weapon disables its fire slot and restores physical interaction");
        cannon.transform.position+=Vector3.right*20;
        var replacement=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/shoulder_base_l.prefab")).GetComponent<Part>();
        check(equipment.TryEquip(replacement) && combat.AmmoRemaining(MechWeaponSlot.Cannon)==20,"Replacement cannon has independent ammunition");
        check(!equipment.TryEquip(replacement),"Already equipped parts cannot be equipped twice");
        check(equipment.TryEquip(cannon) && combat.AmmoRemaining(MechWeaponSlot.Cannon)==19 && combat.ReloadRemaining(MechWeaponSlot.Cannon)>0,"Re-equipping the original cannon preserves ammunition and reload deadline");
        Object.DestroyImmediate(replacement.gameObject);
        var oldTorso=equipment.GetEquipped(PartCategory.Torso);
        var torso=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/torso_base.prefab")).GetComponent<Part>();
        check(equipment.TryEquip(torso) && bridge.GetPart(MechWeaponSlot.Cannon)==cannon && cannon.transform.IsChildOf(torso.transform)
            && combat.AmmoRemaining(MechWeaponSlot.Cannon)==19,"Torso replacement transfers socket occupants without losing weapon state");
        Object.DestroyImmediate(oldTorso.gameObject);
        bool targetMode=player.GetComponent<MechTargetLockController>().IsFreeAim;
        foreach(string name in new[]{"ResonancePulse","ZigzagBarrage","BloomMissile"})
        {
            var module=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/Abilities/"+name+" Part.prefab"));
            var part=module.GetComponent<Part>(); var ability=module.GetComponent<MechPartAbility>();
            check(equipment.TryEquip(part) && bridge.GetAbility(MechWeaponSlot.Cannon)!=null,"Equip independent "+name+" module through the existing shoulder socket");
            cannon.transform.position+=Vector3.right*20;
            combat.TryFire(MechWeaponSlot.Cannon);
            check(ability.IsActive && !combat.HasWeapon(MechWeaponSlot.Cannon),"Socket activation runs "+name+" instead of a hidden duplicate cannon");
            Physics.SyncTransforms();
            var motor=player.GetComponent<MechMovementController>();
            var step=typeof(MechFoundationChecks).GetMethod("Step",BindingFlags.Static|BindingFlags.NonPublic);
            for(int settle=0;settle<40;settle++) step.Invoke(null,new object[]{motor,rig.GameplayCamera,Vector2.zero,false,false,false,0.02f,false});
            step.Invoke(null,new object[]{motor,rig.GameplayCamera,Vector2.right,false,false,true,0.02f,false});
            ability.Tick(0.02f);
            check(ability.IsActive && motor.IsQuickBoosting,"Equipped "+name+" remains active during actual movement boost");
            player.SetActive(false);check(!ability.IsActive,"Player disable cancels equipped ability damage and releases its projectiles");player.SetActive(true);
            equipment.Unequip(PartCategory.ShoulderL);
            check(!module.GetComponent<MechAbilityDriver>().TryActivate(),"Dropped ability cannot attack using its former owner");
            Object.DestroyImmediate(module);
            equipment.TryEquip(cannon);
        }
        check(player.GetComponent<MechTargetLockController>().IsFreeAim==targetMode,"Equipment swaps preserve targeting mode");
        check(equipment.interactKey==KeyCode.F,"Pickup does not conflict with E cannon activation");
        Object.DestroyImmediate(player);Object.DestroyImmediate(floor);
    }
}
