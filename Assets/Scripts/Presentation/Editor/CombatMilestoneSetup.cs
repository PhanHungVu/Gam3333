using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class CombatMilestoneSetup
{
    public const string ResultsPath = "Assets/Prefabs/UI/Mission Results.prefab";
    [MenuItem("Tools/Mech/Combat/Apply HUD Results and Ragdoll Integration")]
    public static void Apply()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before updating combat assets.");
        var existingResults = AssetDatabase.LoadAssetAtPath<GameObject>(ResultsPath);
        var results = existingResults != null ? existingResults.GetComponent<MissionResultsView>() : null;
        if (results == null)
        {
            var view = MissionResultsView.Create(null);
            try { PrefabUtility.SaveAsPrefabAsset(view.gameObject,ResultsPath); }
            finally { UnityEngine.Object.DestroyImmediate(view.gameObject); }
            results = AssetDatabase.LoadAssetAtPath<GameObject>(ResultsPath).GetComponent<MissionResultsView>();
        }
        var abilityRoot=PrefabUtility.LoadPrefabContents("Assets/Prefabs/UI/AbilitiesHUDPodPrefab.prefab");
        try
        {
            var icons=AssetDatabase.FindAssets("t:Sprite",new[]{"Assets/Sprites/PartIcons"})
                .Select(guid=>AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(guid))).Where(icon=>icon!=null).ToArray();
            var fields=new SerializedObject(abilityRoot.GetComponent<AbilitiesHudPod>()); var list=fields.FindProperty("partIcons");
            list.arraySize=icons.Length; for(int i=0;i<icons.Length;i++) list.GetArrayElementAtIndex(i).objectReferenceValue=icons[i];
            fields.FindProperty("headIcon").objectReferenceValue=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/AbilityIcons/HealIcon.png");
            fields.FindProperty("torsoIcon").objectReferenceValue=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/AbilityIcons/DamageAmpIcon.png");
            fields.FindProperty("legsIcon").objectReferenceValue=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/AbilityIcons/JumpIcon.png");
            fields.ApplyModifiedPropertiesWithoutUndo();
            abilityRoot.GetComponent<AbilitiesHudPod>().ConfigureCooldownLine();
            abilityRoot.GetComponent<AbilitiesHudPod>().ConfigureReadyVfx();
            PrefabUtility.SaveAsPrefabAsset(abilityRoot,"Assets/Prefabs/UI/AbilitiesHUDPodPrefab.prefab");
        }
        finally { PrefabUtility.UnloadPrefabContents(abilityRoot); }
        var weaponRoot=PrefabUtility.LoadPrefabContents("Assets/Prefabs/UI/WeaponsHUDPodPrefab.prefab");
        try
        {
            weaponRoot.GetComponent<WeaponsHudPod>().ConfigureCooldownLine();
            weaponRoot.GetComponent<WeaponsHudPod>().ConfigureReadyVfx();
            PrefabUtility.SaveAsPrefabAsset(weaponRoot,"Assets/Prefabs/UI/WeaponsHUDPodPrefab.prefab");
        }
        finally { PrefabUtility.UnloadPrefabContents(weaponRoot); }
        foreach(var path in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/Prefabs/Enemies"})
            .Select(AssetDatabase.GUIDToAssetPath).Concat(new[]{TestPlayerPath}))
        {
            if(path.Contains("/Old/")) continue;
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                if(root.GetComponent<CombatHealth>()==null) continue;
                if(root.GetComponent<CombatDeathRagdoll>()==null) root.AddComponent<CombatDeathRagdoll>();
                if(root.TryGetComponent<MechPlayerRig>(out var rig))
                {
                    var participant=root.GetComponent<MissionParticipant>() ?? root.AddComponent<MissionParticipant>();
                    var fields=new SerializedObject(participant); fields.FindProperty("resultsPrefab").objectReferenceValue=results; fields.ApplyModifiedPropertiesWithoutUndo();
                    fields=new SerializedObject(rig); fields.FindProperty("combatHudPrefab").objectReferenceValue=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/CombatHUD.prefab");
                    fields.ApplyModifiedPropertiesWithoutUndo();
                }
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets(); Debug.Log("COMBAT MILESTONES ASSETS READY: existing HUD icons, scene-local results, bounded physics ragdolls.");
    }
    const string TestPlayerPath="Assets/Prefabs/Mech/Base/FullPlayer_mech_base.prefab";
}
