using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

public static class GreyboxArmWeaponArenaChecks
{
    const string Pending = "GreyboxArmWeaponArenaChecks.Pending";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Stack<IEnumerator> work = new Stack<IEnumerator>();
    static int checks, frame = -1;
    static bool running;
    static double deadline;
    static Keyboard keyboard;
    static Mouse mouse;
    static KeyboardState keyState;
    static MouseState mouseState;
    static bool keysPending, mousePending;
    static void Check(bool valid, string message) { if (!valid) throw new Exception(message); checks++; Debug.Log("GREYBOX ARENA PASS: " + message); }
    [InitializeOnLoadMethod] static void Register() => EditorApplication.update += Tick;
    public static void RunBatch()
    {
        if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).Contains(".quest-review")) throw new Exception("Disposable project only.");
        EditorSceneManager.OpenScene(GreyboxArmWeaponSetup.Arena); SessionState.SetBool(Pending,true); EditorApplication.EnterPlaymode();
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Pending,false) || !EditorApplication.isPlaying || EditorApplication.isCompiling || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (!running) { running=true; deadline=EditorApplication.timeSinceStartup+120; PreMissionReliabilityChecks.BeginLogAudit(); work.Push(Run()); }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if (EditorApplication.timeSinceStartup>deadline) throw new Exception("Greybox arena checks timed out.");
            while(work.Count>0){var step=work.Peek();if(!step.MoveNext()){work.Pop();continue;}if(step.Current is IEnumerator child){work.Push(child);continue;}return;}
            Finish(null);
        }
        catch(Exception error){Finish(error);}
    }
    static void Finish(Exception error)
    {
        SessionState.SetBool(Pending,false); PreMissionReliabilityChecks.EndLogAudit(); Time.captureFramerate=0;
        InputSystem.onBeforeUpdate -= FeedInput;
        if(error!=null) Debug.LogException(error);
        Debug.Log("GREYBOX ARENA CHECKS " + (error==null?"COMPLETE: ":"FAILED: ")+checks); EditorApplication.Exit(error==null?0:1);
    }
    static IEnumerator Frames(int count) { int until=Time.frameCount+count; while(Time.frameCount<until) yield return null; }
    static void FeedInput()
    {
        if(InputState.currentUpdateType!=InputUpdateType.Dynamic)return;
        if(keysPending){InputSystem.QueueStateEvent(keyboard,keyState);keysPending=false;}
        if(mousePending){InputSystem.QueueStateEvent(mouse,mouseState);mousePending=false;}
    }
    static IEnumerator Press(GreyboxArmWeaponBench bench, Key key)
    {
        bench.keyboardShortcuts=true; keyState=new KeyboardState(key); keysPending=true;
        yield return Frames(2);
        keyState=new KeyboardState();keysPending=true;yield return Frames(2);bench.keyboardShortcuts=false;
    }
    static IEnumerator Run()
    {
        Application.runInBackground=true; Time.captureFramerate=60; AudioListener.volume=0;
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        var bench=Object.FindAnyObjectByType<GreyboxArmWeaponBench>(); Check(bench!=null && bench.catalogue!=null && bench.catalogue.weapons.Length==9,"Existing combat arena contains the connected nine-weapon bench"); bench.keyboardShortcuts=false;
        var input=Object.FindAnyObjectByType<MechPlayerInput>(); Check(input!=null,"Arena retains its existing player and normal input component"); input.enabled=false;
        yield return Frames(8);
        var player=input.gameObject; var combat=player.GetComponent<MechCombat>(); bench.player=player.GetComponent<EquipManager>();
        var feedback=player.GetComponent<CombatFeedback>(); if(feedback!=null) feedback.enabled=false;
        Check(Object.FindObjectsByType<MechWeaponsHud>().Length==1 && Object.FindObjectsByType<LockOnCursor>().Length==1,"Arena still uses one shared weapon HUD and cursor");
        for(int i=0;i<9;i++)
        {
            bench.selected=i; Check(bench.Equip(PartCategory.ArmL),"Arena equips "+GreyboxArmWeaponSetup.Names[i]+" through existing arm slots");
            yield return Frames(2);
            var part=bench.player.GetEquipped(PartCategory.ArmL);
            var pod=Object.FindObjectsByType<WeaponsHudPod>().FirstOrDefault(p=>p.Weapon==part);
            Check(pod!=null && pod.CurrentIcon==part.Stats.icon && !part.HasRolledStats,"Shared HUD binds the exact-stat "+part.Stats.partName+" and its greybox icon");
            Check(combat.TryAttack(PartCategory.ArmL).Started,"Normal combat entry attacks with "+part.Stats.partName);
            yield return Frames(1);
            Check(pod.IsCoolingDown && pod.CooldownProgress<1,"Existing cooldown UI follows "+part.Stats.partName);
        }
        bench.ClearLooseWeapons(); yield return Frames(3);
        keyboard=InputSystem.AddDevice<Keyboard>(); InputSystem.onBeforeUpdate+=FeedInput; bench.selected=0;
        yield return Press(bench,Key.RightBracket); Check(bench.selected==1,"Right bracket selects the Hook through the real Input System; selected="+bench.selected+", pending="+keysPending+", time="+Time.timeScale);
        yield return Press(bench,Key.Period); Check(bench.player.GetEquipped(PartCategory.ArmR).data==bench.Selected.data,"Period equips the selected weapon in the right arm");
        yield return Press(bench,Key.Backslash); yield return Frames(2);
        Check(Object.FindObjectsByType<Part>().Any(p=>p.data==bench.Selected.data&&!p.isEquipped&&!p.HasRolledStats),"Backslash spawns an exact-stat physical pickup for ordinary swapping");
        bench.selected=0; yield return Press(bench,Key.Comma); var minigun=bench.player.GetEquipped(PartCategory.ArmL);
        Check(minigun.data==bench.Selected.data,"Comma equips the Minigun in the left arm");
        bench.ClearLooseWeapons(); yield return Frames(3);
        mouse=InputSystem.AddDevice<Mouse>(); mouseState=new MouseState();mousePending=true;yield return Frames(2);
        input.AssumeCursorCaptured=true; input.enabled=true; yield return Frames(3);
        int before=minigun.RemainingAmmo;
        mouseState=new MouseState().WithButton(MouseButton.Left);mousePending=true;
        yield return Frames(45);
        mouseState=new MouseState();mousePending=true; yield return Frames(2);
        int after=minigun.RemainingAmmo;
        Check(before-after>=6 && before-after<=9,"Holding normal LMB continuously fires the Minigun at its real 10/s cadence");
        yield return Frames(15); Check(minigun.RemainingAmmo==after,"Releasing LMB stops sustained Minigun fire"); input.enabled=false;
        Capture(player); InputSystem.RemoveDevice(mouse); InputSystem.RemoveDevice(keyboard);
    }
    static void Capture(GameObject player)
    {
        if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)return;
        // Keep unrelated dropped starter parts and their comparison UI out of the
        // attachment review. This cleanup affects this disposable fixture only.
        foreach(var part in Object.FindObjectsByType<Part>()) if(!part.isEquipped && part.GetComponentInParent<CombatHealth>()==null) Object.DestroyImmediate(part.gameObject);
        foreach(var panel in Object.FindObjectsByType<PartSwapWorldView>()) panel.gameObject.SetActive(false);
        var preview=new GameObject("Greybox equipped preview").AddComponent<Camera>(); preview.transform.position=player.transform.position+new Vector3(7,4,-8); preview.transform.LookAt(player.transform.position+Vector3.up);
        preview.clearFlags=CameraClearFlags.SolidColor; preview.backgroundColor=new Color(.1f,.12f,.14f);
        var texture=new RenderTexture(960,540,24); preview.targetTexture=texture; preview.Render();
        var previous=RenderTexture.active; RenderTexture.active=texture;
        var pixels=new Texture2D(960,540,TextureFormat.RGB24,false); pixels.ReadPixels(new Rect(0,0,960,540),0,0); pixels.Apply(); RenderTexture.active=previous;
        File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../greybox-arms-equipped.png")),pixels.EncodeToPNG());
        preview.targetTexture=null; texture.Release(); Object.DestroyImmediate(texture); Object.DestroyImmediate(pixels); Object.DestroyImmediate(preview.gameObject);
    }
}
