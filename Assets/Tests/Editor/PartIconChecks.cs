using System;
using System.Linq;
using UnityEditor;

// Part pictures live on each part's PartData: every weapon has an Icon (weapons HUD), and the default
// head, torso and legs have an Ability Icon (abilities HUD).
public static class PartIconChecks
{
    public static void Run(Action<bool, string> check)
    {
        var parts = AssetDatabase.FindAssets("t:PartData", new[] { "Assets/PartCatalogue" })
            .Select(g => AssetDatabase.LoadAssetAtPath<PartData>(AssetDatabase.GUIDToAssetPath(g))).Where(p => p != null).ToList();
        var weapons = parts.Where(p => p.subtype != PartSubtype.None).ToList();
        check(weapons.Count > 0 && weapons.All(p => p.icon != null), "Every weapon part has its Icon set on its PartData");
        PartData Find(string name) => parts.FirstOrDefault(p => AssetDatabase.GetAssetPath(p).EndsWith(name + ".asset"));
        check(new[] { "PartData_Head", "PartData_Torso", "PartData_Legs_Fast" }.All(n => Find(n) != null && Find(n).abilityIcon != null && Find(n).icon != null),
            "The default head, torso and legs have their Icon and Ability Icon set on their PartData");
    }
}
