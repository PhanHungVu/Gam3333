using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class AbilityReadyVfxChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
    static IEnumerator Frames(int count = 2) { int end = Time.frameCount + count; while (Time.frameCount < end) yield return null; }
    static IEnumerator Delay(float seconds) { float end = Time.unscaledTime + seconds; while (Time.unscaledTime < end) yield return null; }
    static AbilitiesHudPod Pod(Transform parent, Part part, int index)
    {
        var pod = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/Combat HUD/AbilitiesHUDPodPrefab.prefab"), parent).GetComponent<AbilitiesHudPod>();
        var rect = (RectTransform)pod.transform; rect.anchorMin = rect.anchorMax = new Vector2(.5f,0); rect.pivot = new Vector2(.5f,.5f);
        rect.anchoredPosition = new Vector2((index - 1)*150,100); pod.Bind(part); return pod;
    }
    public static IEnumerator Run(Action<bool, string> check)
    {
        var root = new GameObject("Ready VFX fixture"); root.SetActive(false);
        var head = AssetDatabase.LoadAssetAtPath<PartData>("Assets/PartCatalogue/PartData_Head.asset");
        var torso = AssetDatabase.LoadAssetAtPath<PartData>("Assets/PartCatalogue/PartData_Torso.asset");
        var legs = AssetDatabase.LoadAssetAtPath<PartData>("Assets/PartCatalogue/PartData_Legs.asset");
        var parts = TestMech.Equip(root,(PartCategory.Head,head),(PartCategory.Torso,torso),(PartCategory.Legs,legs)); root.SetActive(true);
        var ui = new GameObject("Ready VFX Canvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
        var canvas = ui.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = ui.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920,1080);
        var legacyRoot=new GameObject("Older ability pod",typeof(RectTransform)); legacyRoot.transform.SetParent(ui.transform,false); legacyRoot.SetActive(false);
        var legacy=Pod(legacyRoot.transform,parts[0],0); Object.DestroyImmediate(legacy.GetComponentInChildren<AbilityReadyVfx>(true).gameObject); legacyRoot.SetActive(true);
        check(legacy.GetComponentsInChildren<AbilityReadyVfx>(true).Length==0 && legacy.GetComponentsInChildren<AbilityReadyBurstGraphic>(true).Length==0,
            "A HUD without the authored burst stays effect-free; the old runtime fallback is removed");
        Object.DestroyImmediate(legacyRoot);
        var pods = new[] { Pod(ui.transform,parts[0],0),Pod(ui.transform,parts[1],1),Pod(ui.transform,parts[2],2) };
        var effects = new[] { pods[0].GetComponentInChildren<AbilityReadyVfx>(true),pods[1].GetComponentInChildren<AbilityReadyVfx>(true),pods[2].GetComponentInChildren<AbilityReadyVfx>(true) };
        yield return Frames(3);
        check(effects[0]!=null && effects[0].PlayCount==0 && effects[1].PlayCount==0 && effects[2].PlayCount==0,
            "Ready HUD initialization creates no completion burst");
        check(ui.GetComponentsInChildren<AbilityReadyBurstGraphic>(true).Length==3
            && !effects[0].Graphic.enabled && !effects[1].Graphic.enabled && !effects[2].Graphic.enabled,
            "Each pod starts with exactly one authored, hidden prefab column");
        for(int i=0;i<3;i++) PartAbilityCooldown.For(parts[i]).ConfirmActivation(.25f+i*.1f);
        yield return Delay(.5f);
        check(effects[0].PlayCount==1 && effects[1].PlayCount==1 && effects[2].PlayCount==1,"Three actual cooldown expiries independently trigger exactly one upward burst per icon");
        check(Get<Image>(pods[0],"icon").color==Color.white && Get<TextMeshProUGUI>(pods[0],"cooldownLabel").text==string.Empty
            && !Get<Image>(pods[0],"cooldownLine").enabled,"Ready cue restores normal icon brightness and removes countdown/sweep");
        foreach(var effect in effects)
            check(effect.IsPlaying && effect.Graphic.ParticleCount>=35 && !effect.Graphic.raycastTarget && !effect.Graphic.maskable
                && effect.Graphic.transform.parent==canvas.transform,"Each dense non-interactive particle column renders above the existing Canvas without pod clipping");
        yield return Capture(canvas,pods,effects,1920,1080,"ready-columns-1920");
        yield return Capture(canvas,pods,effects,1280,720,"ready-columns-1280");
        var icon = Get<Image>(pods[2],"icon");
        canvas.renderMode = RenderMode.WorldSpace; canvas.transform.localScale = Vector3.one*.01f; effects[2].Tick(0);
        check(Vector3.Distance(icon.rectTransform.TransformPoint(icon.rectTransform.rect.center),effects[2].Graphic.transform.position)<.001f,
            "Particle origin also follows its icon on a world-space Canvas");
        canvas.transform.localScale = Vector3.one; canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        yield return Delay(.7f);
        check(!effects[0].IsPlaying && !effects[1].IsPlaying && !effects[2].IsPlaying && !effects[0].Graphic.enabled
            && Get<Image>(pods[0],"icon").rectTransform.localScale==Vector3.one,"Burst fades out, disables its cached mesh and restores the exact icon scale");
        int plays = effects[0].PlayCount; pods[0].Bind(parts[0]); yield return Frames();
        var duplicate = Pod(ui.transform,parts[0],0);
        check(effects[0].PlayCount==plays && duplicate.GetComponentInChildren<AbilityReadyVfx>(true).PlayCount==0,"Refreshing or rebuilding an already-ready icon does not replay the cue");
        var state = PartAbilityCooldown.For(parts[0]);
        for(int i=0;i<3;i++) { state.Synchronize(.02f,10); yield return Delay(.06f); }
        check(effects[0].PlayCount==plays && duplicate.GetComponentInChildren<AbilityReadyVfx>(true).PlayCount==0,"Tiny authoritative remaining-time corrections cannot repeatedly retrigger a ready cue");
        var renderer = effects[0].Graphic;
        state.ConfirmActivation(.2f); yield return Delay(.3f);
        check(effects[0].PlayCount+duplicate.GetComponentInChildren<AbilityReadyVfx>(true).PlayCount==plays+1,"Duplicate HUD views cannot consume the same physical part's completion twice");
        check(effects[0].Graphic==renderer,"Subsequent completions reuse the existing UI renderer");
        Object.DestroyImmediate(duplicate.gameObject);
        state.ConfirmActivation(.25f); yield return Frames();
        pods[0].gameObject.SetActive(false); Object.DestroyImmediate(pods[0].gameObject); yield return Frames();
        pods[0]=Pod(ui.transform,parts[0],0); effects[0]=pods[0].GetComponentInChildren<AbilityReadyVfx>(true);
        check(effects[0].PlayCount==0,"Rebuilding during a real cooldown does not fabricate an early ready burst");
        yield return Delay(.3f);
        check(effects[0].PlayCount==1,"Rebuilt HUD observes the existing physical cooldown finishing once");
        var randomBefore = UnityEngine.Random.state; effects[2].Play();
        check(UnityEngine.Random.state.Equals(randomBefore),"Visual particles do not alter gameplay's global random sequence");
        var button = new GameObject("Unblocked button",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image),typeof(Button)); button.transform.SetParent(ui.transform,false);
        var buttonRect=(RectTransform)button.transform; buttonRect.anchorMin=Vector2.zero; buttonRect.anchorMax=Vector2.one; buttonRect.sizeDelta=Vector2.zero; button.transform.SetAsFirstSibling();
        bool ownsEvents = EventSystem.current==null;
        var events = ownsEvents ? new GameObject("Ready VFX events",typeof(EventSystem)).GetComponent<EventSystem>() : EventSystem.current;
        scaler.enabled=false; scaler.enabled=true;
        yield return Frames(2); // Publish the new button's Canvas depth before raycasting it.
        Canvas.ForceUpdateCanvases(); effects[2].Tick(0);
        var pointer = new PointerEventData(events) { position = RectTransformUtility.WorldToScreenPoint(null,Get<Image>(pods[2],"icon").rectTransform.position) };
        var hits = new List<RaycastResult>(); ui.GetComponent<GraphicRaycaster>().Raycast(pointer,hits);
        bool foundButton=false, hitBurst=false; foreach(var hit in hits) { foundButton |= hit.gameObject==button; hitBurst |= hit.gameObject.GetComponent<AbilityReadyBurstGraphic>()!=null; }
        check(foundButton && !hitBurst,"UI raycasts still reach buttons behind the cosmetic column");
        effects[2].Tick(.08f);
        check(Get<Image>(pods[2],"icon").rectTransform.localScale.x>1,"Ready icon has a subtle positive scale pulse");
        pods[2].gameObject.SetActive(false);
        check(!effects[2].IsPlaying && !effects[2].Graphic.enabled && Get<Image>(pods[2],"icon").rectTransform.localScale==Vector3.one,
            "Disabling a pod immediately clears its external visual and restores its scale");
        Object.DestroyImmediate(ui); Object.DestroyImmediate(root); if(ownsEvents) Object.DestroyImmediate(events.gameObject); yield return Frames();
        check(Object.FindObjectsByType<AbilityReadyBurstGraphic>(FindObjectsInactive.Include).Length==0,"HUD destruction leaves no orphan ready VFX renderers on the Canvas");
    }

    static IEnumerator Capture(Canvas canvas, AbilitiesHudPod[] pods, AbilityReadyVfx[] effects,int width,int height,string name)
    {
        if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;
        var camera = new GameObject("Ready VFX capture").AddComponent<Camera>(); camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.035f,.045f,.06f);
        var target = new RenderTexture(width,height,24); camera.targetTexture=target;
        canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=1;
        var scaler=canvas.GetComponent<CanvasScaler>(); scaler.enabled=false; scaler.enabled=true; Canvas.ForceUpdateCanvases();
        for(int i=0;i<effects.Length;i++) { effects[i].Tick(0); effects[i].Graphic.SetAge(.12f); }
        Canvas.ForceUpdateCanvases(); camera.Render();
        var previous=RenderTexture.active; RenderTexture.active=target;
        var picture=new Texture2D(width,height,TextureFormat.RGB24,false); picture.ReadPixels(new Rect(0,0,width,height),0,0); picture.Apply();
        string folder=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"AbilityReadyCaptures"); Directory.CreateDirectory(folder); File.WriteAllBytes(Path.Combine(folder,name+".png"),picture.EncodeToPNG());
        RenderTexture.active=previous; canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.worldCamera=null; camera.targetTexture=null; target.Release();
        Object.DestroyImmediate(picture); Object.DestroyImmediate(target); Object.DestroyImmediate(camera.gameObject);
    }
}
