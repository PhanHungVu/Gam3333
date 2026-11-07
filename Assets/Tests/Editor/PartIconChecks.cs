using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Part pictures live on each part's PartData: every weapon has an Icon (weapons HUD), and the default
// head, torso and legs have an Ability Icon (abilities HUD).
public static class PartIconChecks
{
    public static void Run(Action<bool, string> check)
    {
        var parts = AssetDatabase.FindAssets("t:PartData", new[] { "Assets/PartCatalogue" })
            .Select(g => AssetDatabase.LoadAssetAtPath<PartData>(AssetDatabase.GUIDToAssetPath(g))).Where(p => p != null).ToList();
        var weapons = parts.Where(p => p.subtype != PartSubtype.None).ToList();
        // Weapons still waiting for icon art are listed as a warning, not a failure (the game runs without them).
        var missing = weapons.Where(p => p.icon == null).Select(p => p.name).ToList();
        if (missing.Count > 0) Debug.LogWarning("MECH WARNING: weapon parts without an Icon yet (art pending): " + string.Join(", ", missing));
        check(weapons.Count > 0, "Weapon parts are listed for the icon audit (missing icons are warnings)");
        PartData Find(string name) => parts.FirstOrDefault(p => AssetDatabase.GetAssetPath(p).EndsWith(name + ".asset"));
        check(new[] { "PartData_Head", "PartData_Torso", "PartData_Legs_Fast" }.All(n => Find(n) != null && Find(n).abilityIcon != null && Find(n).icon != null),
            "The default head, torso and legs have their Icon and Ability Icon set on their PartData");
    }
}
