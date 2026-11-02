using System;
using System.Linq;
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
        var equipment=player.GetComponent<EquipManager>(); var combat=player.GetComponent<MechCombat>();
        var bridge=player.GetComponent<MechCombat>();var rig=player.GetComponent<MechPlayerRig>();
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=new Vector3(22000,-0.25f,0);floor.transform.localScale=new Vector3(100,0.5f,100);
        check(player.GetComponent<Rigidbody>()==null && player.GetComponent<CharacterController>()!=null,
            "Merged player uses only the existing CharacterController movement");
        check(rig.GameplayCamera!=null && rig.GameplayCamera.GetComponent<CameraControlThirdPerson>().Target==player.transform,
            "Merged player creates/binds the existing Cinemachine camera before gameplay initialization");
        for(int i=0;i<=6;i++) check(equipment.GetEquipped((PartCategory)i)!=null,"Starting part registered: "+(PartCategory)i);
        check(combat.SubtypeOf(PartCategory.ArmL)==PartSubtype.Melee && combat.SubtypeOf(PartCategory.ArmR)==PartSubtype.Projectile
            && combat.SubtypeOf(PartCategory.ShoulderL)==PartSubtype.Splash && combat.SubtypeOf(PartCategory.ShoulderR)==PartSubtype.Splash,"All four existing weapon assets drive equipped combat");
        var cannonStats=combat.GetPart(PartCategory.ShoulderL).Stats;
        int cannonAmmo=(int)cannonStats.GetStat(StatType.Ammo);
        check(cannonStats.GetStat(StatType.Damage)>0 && cannonStats.GetStat(StatType.SplashRadius)>0 && cannonStats.GetStat(StatType.SplashDamage)>0
            && cannonStats.GetStat(StatType.Range)>0 && cannonStats.ShotInterval>0,"Cannon part data carries damage, splash radius and damage, range and fire rate");
        check(combat.AmmoRemaining(PartCategory.ArmR)==(int)combat.GetPart(PartCategory.ArmR).Stats.GetStat(StatType.Ammo)
            && combat.AmmoRemaining(PartCategory.ShoulderR)==(int)combat.GetPart(PartCategory.ShoulderR).Stats.GetStat(StatType.Ammo),"Axe and crossbow start with their part data's ammunition");
        foreach(var part in player.GetComponentsInChildren<Part>())
            foreach(var collider in part.GetComponents<Collider>()) check(!collider.enabled,"Equipped physical collider disabled");
        var cannon=bridge.GetPart(PartCategory.ShoulderL);
        var shot=combat.TryAttack(PartCategory.ShoulderL).Projectile;
        check(shot!=null && combat.AmmoRemaining(PartCategory.ShoulderL)==cannonAmmo-1 && combat.TryAttack(PartCategory.ShoulderL).Projectile==null,"Equipped cannon fires once and observes its reload");
        if(shot!=null)Object.DestroyImmediate(shot.gameObject);
        equipment.Unequip(PartCategory.ShoulderL);
        check(!cannon.isEquipped && cannon.GetComponent<Rigidbody>()!=null && combat.SubtypeOf(PartCategory.ShoulderL)==PartSubtype.None
            && combat.TryAttack(PartCategory.ShoulderL).Projectile==null,"Dropping a weapon disables its fire slot and restores physical interaction");
        cannon.transform.position+=Vector3.right*20;
        var replacement=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/Old/shoulder_base_l.prefab")).GetComponent<Part>();
        check(equipment.TryEquip(replacement) && combat.AmmoRemaining(PartCategory.ShoulderL)==(int)replacement.Stats.GetStat(StatType.Ammo),"Replacement cannon has independent ammunition");
        check(!equipment.TryEquip(replacement),"Already equipped parts cannot be equipped twice");
        check(equipment.TryEquip(cannon) && combat.AmmoRemaining(PartCategory.ShoulderL)==cannonAmmo-1 && combat.ReloadRemaining(PartCategory.ShoulderL)>0,"Re-equipping the original cannon preserves ammunition and reload deadline");
        Object.DestroyImmediate(replacement.gameObject);
        var oldTorso=equipment.GetEquipped(PartCategory.Torso);
        var torso=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/Old/torso_base.prefab")).GetComponent<Part>();
        check(equipment.TryEquip(torso) && bridge.GetPart(PartCategory.ShoulderL)==cannon && cannon.transform.IsChildOf(torso.transform)
            && combat.AmmoRemaining(PartCategory.ShoulderL)==cannonAmmo-1,"Torso replacement transfers socket occupants without losing weapon state");
        Object.DestroyImmediate(oldTorso.gameObject);
        bool targetMode=player.GetComponent<MechTargetLockController>().IsFreeAim;
        // Firing follows the socket: Q/E fire whatever sits in the left/right shoulder.
        const string partsPath="Assets/Prefabs/Mech/Base/Parts/";
        var crossbowPart=equipment.GetEquipped(PartCategory.ShoulderR);
        var spareCannon=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(partsPath+"shoulder_cannon.prefab")).GetComponent<Part>();
        equipment.Unequip(PartCategory.ShoulderL); cannon.transform.position+=Vector3.right*20;
        equipment.Unequip(PartCategory.ShoulderR);
        check(equipment.TryEquip(crossbowPart,PartCategory.ShoulderL) && equipment.TryEquip(spareCannon,PartCategory.ShoulderR),"Shoulder weapons equip on either side");
        var leftShot=combat.TryAttack(PartCategory.ShoulderL).Projectile; var rightShot=combat.TryAttack(PartCategory.ShoulderR).Projectile;
        check(leftShot!=null && leftShot.Source==crossbowPart && rightShot!=null && rightShot.Source==spareCannon,
            "Q/E fire the left/right shoulder by socket, not by weapon type");
        if(leftShot!=null)Object.DestroyImmediate(leftShot.gameObject); if(rightShot!=null)Object.DestroyImmediate(rightShot.gameObject);
        var secondCannon=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(partsPath+"shoulder_cannon.prefab")).GetComponent<Part>();
        check(equipment.TryEquip(secondCannon,PartCategory.ShoulderL),"A second cannon fits the other shoulder");
        var duplicateShot=combat.TryAttack(PartCategory.ShoulderL).Projectile;
        check(duplicateShot!=null && duplicateShot.Source==secondCannon,"Two cannons fire independently from their own sockets");
        if(duplicateShot!=null)Object.DestroyImmediate(duplicateShot.gameObject);
        // Hold-F swap: the pressed button's socket takes the selected floor part, if it fits.
        crossbowPart.transform.position+=Vector3.right*20;
        equipment.SetSelectedPart(crossbowPart);
        check(!equipment.TrySwapSelectedInto(PartCategory.ArmL) && !crossbowPart.isEquipped,"An arm socket refuses a shoulder part");
        check(equipment.TrySwapSelectedInto(PartCategory.ShoulderL) && equipment.GetEquipped(PartCategory.ShoulderL)==crossbowPart,
            "The existing selected-part API equips the requested socket");
        var detector=player.GetComponentInChildren<PickupDetector>(true);
        check(detector.HighlightColour(PartType.Arm)!=detector.HighlightColour(PartType.Shoulder),"Arm and shoulder pickups glow in different colours");
        Object.DestroyImmediate(secondCannon.gameObject);
        check(player.GetComponent<MechTargetLockController>().IsFreeAim==targetMode,"Equipment swaps preserve targeting mode");
        var actions=AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>("Assets/InputSystem_Actions.inputactions");
        check(actions.FindAction("Player/SwapLeft").bindings.Any(b=>b.path=="<Keyboard>/1")
            &&actions.FindAction("Player/SwapRight").bindings.Any(b=>b.path=="<Keyboard>/2")
            &&actions.FindAction("Player/SwapPart",false)==null,"Pickup uses 1/2 independently of F, mouse and Q/E attacks");
        Object.DestroyImmediate(player);Object.DestroyImmediate(floor);
    }
}
