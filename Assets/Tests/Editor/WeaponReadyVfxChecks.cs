using System;
using System.Collections;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// The ready burst belongs to abilities only. Weapon pods must not own or add an AbilityReadyVfx,
// so their cooldown completion never emits the burst; the ability side is covered by
// AbilityReadyVfxChecks.
public static class WeaponReadyVfxChecks
{
    public static IEnumerator Run(Action<bool, string> check)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/UI/Combat HUD/WeaponsHUDPodPrefab.prefab");
        check(prefab != null, "Weapon pod prefab loads from its authored path");
        if (prefab == null) yield break;

        var pod = Object.Instantiate(prefab).GetComponent<WeaponsHudPod>();
        yield return null;
        check(pod != null, "Weapon pod prefab carries its HUD script");
        if (pod != null)
        {
            check(pod.GetComponents<AbilityReadyVfx>().Length == 0,
                "Weapon pods neither author nor add a ready burst effect");
            check(pod.GetComponentInChildren<AbilityReadyVfx>(true) == null,
                "Weapon pods have no nested ready burst effect");
            Object.DestroyImmediate(pod.gameObject);
        }
    }
}
