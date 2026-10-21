using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Explicit editor command, never an automatic asset migrator. Batch use is restricted to the isolated validation copy.
public static class MechPartsIntegration
{
    const string Full = "Assets/Prefabs/Mech/Base/FullPlayer_mech_base.prefab";
    static void Ref(Object obj, string field, Object value)
    { var so = new SerializedObject(obj); so.FindProperty(field).objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }
    static Component Copy(Component source, GameObject destination)
    { var c = destination.GetComponent(source.GetType()); if(c==null) c=destination.AddComponent(source.GetType()); EditorUtility.CopySerialized(source, c); return c; }
    public static void UpdateMuzzlesBatch()
    {
        ConfigurePartPrefabs(); FixVisualRoot(); AssetDatabase.SaveAssets(); EditorApplication.Exit(0);
    }
    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use this migration in the isolated validation copy.");
        ConfigureData(); ConfigurePartPrefabs(); ConfigureAbilityParts(); BuildPlayer(); FixVisualRoot();
        IntegrateScene("Assets/Scenes/A33.unity"); IntegrateScene("Assets/Scenes/Merge Test.unity");
        AssetDatabase.SaveAssets(); Debug.Log("PARTS INTEGRATION ASSETS COMPLETE"); EditorApplication.Exit(0);
    }
    static void FixVisualRoot()
    {
        var root=PrefabUtility.LoadPrefabContents(Full);
        var visual=root.transform.Find("VisualMech");
        if(visual!=null && visual.Find("Model Orientation")==null)
        {
            var pose=new GameObject("Model Orientation").transform;pose.SetParent(visual,false);
            var children=new System.Collections.Generic.List<Transform>();foreach(Transform child in visual)if(child!=pose)children.Add(child);
            foreach(var child in children)child.SetParent(pose,false);
            pose.localRotation=visual.localRotation;pose.localScale=visual.localScale;
            visual.localRotation=Quaternion.identity;visual.localScale=Vector3.one;
            PrefabUtility.SaveAsPrefabAsset(root,Full);
        }
        PrefabUtility.UnloadPrefabContents(root);
    }
    static void ConfigureData()
    {
        var names = new[]{"A1_Sword", "A2_ThrowingAxe", "A3_Cannon", "A4_LargeCrossbow"};
        for (int i=0;i<names.Length;i++)
        {
            var data=AssetDatabase.LoadAssetAtPath<PartData>("Assets/PartCatalogue/PartData_"+names[i]+".asset");
            if(data==null) throw new Exception("Missing existing part asset: "+names[i]);
            data.weapon=(MechPartWeapon)(i+1); data.metresPerGolem=4; data.maximumRangeMultiplier=1.5f; data.cannonExplosionRadius=6;
            EditorUtility.SetDirty(data);
        }
    }
    static void ConfigurePartPrefabs()
    {
        foreach(string guid in AssetDatabase.FindAssets("t:Prefab", new[]{"Assets/Prefabs/Mech/Base/Parts"}))
        {
            string path=AssetDatabase.GUIDToAssetPath(guid); var root=PrefabUtility.LoadPrefabContents(path);
            var part=root.GetComponent<Part>();
            if(part!=null)
            {
                part.authoredScale=root.transform.localScale;
                if(part.data!=null && part.data.weapon>=MechPartWeapon.ThrowingAxe && part.muzzle==null)
                {
                    part.muzzle=new GameObject("Muzzle").transform;part.muzzle.SetParent(root.transform,false);
                    part.muzzle.localPosition=new Vector3(0.35f,0.2f,0);
                }
                if(part.data!=null && part.data.weapon==MechPartWeapon.Cannon && part.recoilVisual==null)
                {
                    var renderer=root.transform.Find("shoulder_base_l_collider");
                    var visual=new GameObject("Cannon Recoil Visual"); visual.transform.SetParent(renderer,false);
                    Copy(renderer.GetComponent<MeshFilter>(),visual); Copy(renderer.GetComponent<MeshRenderer>(),visual);
                    Object.DestroyImmediate(renderer.GetComponent<MeshRenderer>()); Object.DestroyImmediate(renderer.GetComponent<MeshFilter>());
                    part.recoilVisual=visual.transform;
                }
            }
            else // Pickup volume is a trigger, not visible geometry.
                foreach(var r in root.GetComponentsInChildren<Renderer>()) r.enabled=false;
            PrefabUtility.SaveAsPrefabAsset(root,path); PrefabUtility.UnloadPrefabContents(root);
        }
    }
    static void ConfigureAbilityParts()
    {
        foreach(var name in new[]{"ResonancePulse","ZigzagBarrage","BloomMissile"})
        {
            string path="Assets/Prefabs/Abilities/"+name+" Part.prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            var part=root.GetComponent<Part>(); if(part==null) part=root.AddComponent<Part>();
            string dataPath="Assets/PartCatalogue/PartData_"+name+".asset";
            var data=AssetDatabase.LoadAssetAtPath<PartData>(dataPath);
            if(data==null) {data=ScriptableObject.CreateInstance<PartData>();data.partId="ability"+name;data.partName=name;AssetDatabase.CreateAsset(data,dataPath);}
            part.data=data; part.partType=PartType.Shoulder; part.isStartingPart=true; part.authoredScale=Vector3.one;
            var model=GameObject.CreatePrimitive(PrimitiveType.Cube);model.name="Ability Module";model.transform.SetParent(root.transform,false);
            model.transform.localPosition=new Vector3(-0.15f,0.2f,0);model.transform.localScale=new Vector3(0.3f,0.7f,0.3f);
            model.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Combat/Prototype Combat.mat");
            PrefabUtility.SaveAsPrefabAsset(root,path);PrefabUtility.UnloadPrefabContents(root);
        }
    }
    static void BuildPlayer()
    {
        // Preserve the original green cursor asset and use it as a fallback in scenes without a Canvas.
        EditorSceneManager.OpenScene("Assets/Scenes/A33.unity");
        var cursor=Object.FindAnyObjectByType<LockOnCursor>();
        if(cursor!=null)
        {
            var canvas=cursor.GetComponentInParent<Canvas>();
            var clone=Object.Instantiate(canvas.gameObject); clone.name="Mech Cursor Canvas";
            foreach(var hud in clone.GetComponentsInChildren<MechEnergyHud>(true)) Ref(hud,"energy",null);
            var binding=clone.GetComponentInChildren<LockOnCursor>();
            Ref(binding,"targetLockController",null); Ref(binding,"gameplayCamera",null);
            PrefabUtility.SaveAsPrefabAsset(clone,"Assets/Prefabs/Mech Cursor Canvas.prefab"); Object.DestroyImmediate(clone);
        }
        var root=PrefabUtility.LoadPrefabContents(Full);
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech.prefab");
        if(root.GetComponent<SimpleMove>()!=null) Object.DestroyImmediate(root.GetComponent<SimpleMove>());
        if(root.GetComponent<Rigidbody>()!=null) Object.DestroyImmediate(root.GetComponent<Rigidbody>());
        root.tag="Player";
        var visual=new GameObject("VisualMech").transform; visual.SetParent(root.transform,false);
        var parts=root.GetComponentsInChildren<Part>(true);
        foreach(var part in parts) part.transform.SetParent(visual,true);
        var torsoSocket=new GameObject("Torso Socket").AddComponent<Socket>();
        torsoSocket.socketType=PartCategory.Torso; torsoSocket.transform.SetParent(visual,false);
        Part torso=null; foreach(var part in parts) if(part.partType==PartType.Torso) torso=part;
        torso.transform.SetParent(torsoSocket.transform,true); torso.transform.localPosition=Vector3.zero;
        foreach(var part in parts)
        {
            if(part==torso) continue;
            foreach(var socket in torso.GetComponentsInChildren<Socket>())
                if(PartSlots.Fits(socket.socketType,part.partType) && socket.GetComponentInChildren<Part>()==null)
                {part.transform.SetParent(socket.transform,true);part.transform.SetPositionAndRotation(socket.transform.position,socket.transform.rotation);break;}
        }
        visual.localRotation=Quaternion.Euler(0,-90,0); visual.localScale=Vector3.one*1.5f;
        Bounds bounds=default;bool first=true;
        foreach(var r in visual.GetComponentsInChildren<Renderer>()) {if(first){bounds=r.bounds;first=false;}else bounds.Encapsulate(r.bounds);}
        visual.localPosition=Vector3.up*(root.transform.position.y-bounds.center.y);
        foreach(var c in source.GetComponents<Component>()) if(!(c is Transform)) Copy(c,root);
        Ref(root.GetComponent<MechMovementController>(),"energy",root.GetComponent<MechEnergyController>());
        Ref(root.GetComponent<MechBoostPresentation>(),"movement",root.GetComponent<MechMovementController>());
        Ref(root.GetComponent<MechBoostPresentation>(),"visualRoot",visual);
        Ref(root.GetComponent<CombatFeedback>(),"recoilVisual",null);
        Ref(root.GetComponent<CombatFeedback>(),"cameraControl",null);
        var equip=root.GetComponent<EquipManager>(); equip.interactKey=KeyCode.F; equip.partScale=1.5f;
        root.AddComponent<MechEquipmentCombat>();

        var rig=root.AddComponent<MechPlayerRig>();
        Ref(rig,"cameraPrefab",AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Main Camera.prefab"));
        Ref(rig,"cursorPrefab",AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech Cursor Canvas.prefab"));
        PrefabUtility.SaveAsPrefabAsset(root,Full); PrefabUtility.UnloadPrefabContents(root);
    }
    static void IntegrateScene(string path)
    {
        if(!System.IO.File.Exists(path))return;
        var scene=EditorSceneManager.OpenScene(path);
        var full=AssetDatabase.LoadAssetAtPath<GameObject>(Full);
        var players=Object.FindObjectsByType<MechCombatController>();
        var existing=Object.FindAnyObjectByType<MechPlayerRig>();
        GameObject player=existing!=null?existing.gameObject:null;
        if(player==null)
        {
            player=(GameObject)PrefabUtility.InstantiatePrefab(full,scene);
            if(players.Length>0) player.transform.SetPositionAndRotation(players[0].transform.position,players[0].transform.rotation);
        }
        foreach(var old in players) if(old.gameObject!=player) Object.DestroyImmediate(old.gameObject);
        foreach(var camera in Object.FindObjectsByType<CameraControlThirdPerson>())
            Ref(camera,"target",player.transform);
        foreach(var binding in Object.FindObjectsByType<LockOnCursor>())
            Ref(binding,"targetLockController",player.GetComponent<MechTargetLockController>());
        // The new player owns its energy HUD. Remove only the old scene component, preserving the cursor Canvas.
        foreach(var hud in Object.FindObjectsByType<MechEnergyHud>())
            Ref(hud,"energy",player.GetComponent<MechEnergyController>());
        EditorSceneManager.SaveScene(scene);
    }
}
