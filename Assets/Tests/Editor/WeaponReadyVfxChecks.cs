using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class WeaponReadyVfxChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Get<T>(object target,string name) => (T)target.GetType().GetField(name,Private).GetValue(target);
    static IEnumerator Frames(int count=2) { int end=Time.frameCount+count; while(Time.frameCount<end) yield return null; }
    static IEnumerator Delay(float seconds) { float end=Time.time+seconds; while(Time.time<end) yield return null; }
    static WeaponsHudPod Pod(Transform parent,Part part,int index)
    {
        var pod=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/WeaponsHUDPodPrefab.prefab"),parent).GetComponent<WeaponsHudPod>();
        var rect=(RectTransform)pod.transform; rect.anchorMin=rect.anchorMax=new Vector2(.5f,0); rect.pivot=new Vector2(.5f,.5f);
        rect.anchoredPosition=new Vector2((index-1)*210,90); pod.Bind(part); return pod;
    }
    public static IEnumerator Run(Action<bool,string> check)
    {
        var fixture=new GameObject("Weapon ready fixture"); fixture.SetActive(false); fixture.transform.position=new Vector3(300,20,300);
        var combat=fixture.AddComponent<MechCombat>();
        var camera=new GameObject("Weapon fixture camera").AddComponent<Camera>(); camera.transform.position=fixture.transform.position-Vector3.forward*10;
        combat.BindCamera(camera);
        var ranged=TestMech.WithMagazine(TestMech.Ranged("Cannon",PartType.Shoulder,PartSubtype.Projectile,1,4,.18f,2,10,.1f),2,.35f);
        var sword=TestMech.Sword(1,.25f);
        var arm=TestMech.WithMagazine(TestMech.Ranged("Throwing axe",PartType.Arm,PartSubtype.Projectile,1,4,.24f,2,10,.1f),1,.3f);
        var parts=TestMech.Equip(fixture,(PartCategory.ShoulderL,ranged),(PartCategory.ArmL,sword),(PartCategory.ArmR,arm)); fixture.SetActive(true);
        var ui=new GameObject("Weapon ready Canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
        var canvas=ui.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceOverlay;
        var scaler=ui.GetComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution=new Vector2(1920,1080);
        var pods=new[]{Pod(ui.transform,parts[0],0),Pod(ui.transform,parts[1],1),Pod(ui.transform,parts[2],2)};
        var effects=new[]{pods[0].GetComponent<AbilityReadyVfx>(),pods[1].GetComponent<AbilityReadyVfx>(),pods[2].GetComponent<AbilityReadyVfx>()};
        yield return Frames();
        check(effects[0]!=null && effects[0].PlayCount==0 && effects[1].PlayCount==0 && effects[2].PlayCount==0,"Already-ready weapons initialize without ready bursts");
        check(combat.TryAttack(PartCategory.ShoulderL).Started && combat.TryAttack(PartCategory.ArmL).Started && combat.TryAttack(PartCategory.ArmR).Started,
            "Real shoulder fire, melee swing and arm auto-reload start independent cooldowns");
        check(!combat.TryAttack(PartCategory.ShoulderL).Started,"Rejected repeat fire cannot create an extra ready cycle");
        yield return Delay(.4f);
        check(effects[0].PlayCount==1 && effects[1].PlayCount==1 && effects[2].PlayCount==1,"Shot interval, melee cooldown and automatic reload each emit one matching icon burst");
        check(Get<Image>(pods[0],"icon").color==Color.white && !Get<Image>(pods[0],"cooldownLine").enabled,"Weapon ready burst restores artwork brightness and hides its completed sweep");
        yield return Capture(canvas,effects);
        yield return Delay(.6f);
        check(effects[0].PlayCount==1 && effects[1].PlayCount==1 && effects[2].PlayCount==1 && !effects[0].IsPlaying,
            "Idle ready weapons do not repeatedly replay the effect");
        check(combat.TryReload(PartCategory.ShoulderL) && !combat.TryReload(PartCategory.ShoulderL),"Only the accepted partially empty reload starts a new weapon cue cycle");
        yield return Delay(.45f);
        check(effects[0].PlayCount==2,"Manual reload completion uses the same white upward ready burst");

        // Drive HUD refresh manually to reproduce held fire starting a new cooldown before LateUpdate sees the ready gap.
        pods[0].enabled=false;
        check(combat.TryAttack(PartCategory.ShoulderL).Started,"Held-fire fixture starts a real ammunition-consuming shot");
        yield return Delay(.24f);
        int before=effects[0].PlayCount;
        check(combat.TryAttack(PartCategory.ShoulderL).Started && parts[0].CooldownRemaining>0,"Next held shot can immediately start a new cooldown at readiness");
        typeof(WeaponsHudPod).GetMethod("Refresh",Private).Invoke(pods[0],null);
        check(effects[0].PlayCount==before+1 && pods[0].IsCoolingDown,"Ready completion is preserved when a held weapon fires again before its HUD refresh");
        yield return Delay(.25f); before=effects[0].PlayCount;
        typeof(WeaponsHudPod).GetMethod("Refresh",Private).Invoke(pods[0],null);
        check(parts[0].RemainingAmmo==1 && effects[0].PlayCount==before,"A still-reloading weapon does not report readiness prematurely");
        yield return Delay(.2f); typeof(WeaponsHudPod).GetMethod("Refresh",Private).Invoke(pods[0],null);
        check(effects[0].PlayCount==before+1,"The next automatic reload still reports its own independent completion");
        check(combat.TryAttack(PartCategory.ShoulderL).Started && parts[0].RemainingAmmo==0,"Final live round is consumed normally");
        before=effects[0].PlayCount; yield return Delay(.25f); typeof(WeaponsHudPod).GetMethod("Refresh",Private).Invoke(pods[0],null);
        check(effects[0].PlayCount==before && Get<Image>(pods[0],"icon").color.r<1,"Exhausted ammunition remains dim without a false ready burst after the final cooldown");
        pods[0].enabled=true;

        check(combat.TryAttack(PartCategory.ArmL).Started,"Melee weapon starts another actual cooldown before re-equipping");
        var equipment=fixture.GetComponent<EquipManager>(); var loose=equipment.Unequip(PartCategory.ArmL); check(equipment.TryEquip(loose,PartCategory.ArmL),"Weapon can be removed and reattached during its cooldown");
        pods[1].gameObject.SetActive(false); Object.Destroy(pods[1].gameObject);
        pods[1]=Pod(ui.transform,parts[1],1); effects[1]=pods[1].GetComponent<AbilityReadyVfx>();
        var duplicate=Pod(ui.transform,parts[1],2);
        check(effects[1].PlayCount==0 && duplicate.GetComponent<AbilityReadyVfx>().PlayCount==0,"Rebuilt cooling weapon rows do not emit an early cue");
        yield return Delay(.35f);
        check(effects[1].PlayCount+duplicate.GetComponent<AbilityReadyVfx>().PlayCount==1,"One physical weapon completion is emitted once across duplicate or rebuilt HUD views");
        Object.Destroy(duplicate.gameObject);
        pods[1].Bind(parts[1]); yield return Frames();
        check(effects[1].PlayCount<=1,"Rebinding a ready weapon does not replay its completed cue");
        foreach(var shot in Object.FindObjectsByType<MechProjectile>())
            if(shot.Source==parts[0] || shot.Source==parts[1] || shot.Source==parts[2]) Object.Destroy(shot.gameObject);
        Object.Destroy(ui); Object.Destroy(fixture); Object.Destroy(camera.gameObject); Object.Destroy(ranged); Object.Destroy(sword); Object.Destroy(arm); yield return Frames(3);
        check(Object.FindObjectsByType<AbilityReadyBurstGraphic>(FindObjectsInactive.Include).Length==0,"Weapon HUD cleanup leaves no external ready-burst graphics behind");
    }
    static IEnumerator Capture(Canvas canvas,AbilityReadyVfx[] effects)
    {
        if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;
        var camera=new GameObject("Weapon ready capture").AddComponent<Camera>(); camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.035f,.045f,.06f);
        var target=new RenderTexture(1280,720,24); camera.targetTexture=target;
        canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=1;
        var scaler=canvas.GetComponent<CanvasScaler>(); scaler.enabled=false; scaler.enabled=true; Canvas.ForceUpdateCanvases();
        foreach(var effect in effects) {effect.Tick(0); effect.Graphic.SetAge(.12f);} Canvas.ForceUpdateCanvases(); camera.Render();
        var previous=RenderTexture.active; RenderTexture.active=target;
        var picture=new Texture2D(1280,720,TextureFormat.RGB24,false); picture.ReadPixels(new Rect(0,0,1280,720),0,0); picture.Apply();
        var folder=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"WeaponReadyCaptures"); Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder,"weapon-ready-bursts.png"),picture.EncodeToPNG());
        RenderTexture.active=previous; canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.worldCamera=null; camera.targetTexture=null; target.Release();
        Object.Destroy(picture); Object.Destroy(target); Object.Destroy(camera.gameObject);
    }
}
