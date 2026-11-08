using System;
using System.Linq;
using UnityEditor;

// The map border: a seeded band of trees inside Width of every edge, never on roads, on every mission map.
public static class BorderChecks
{
    public static void Run(Action<bool, string> check)
    {
        var terrain = new TerrainSettings();
        var map = TerrainGenerator.Generate(23, terrain);
        var s = new BorderSettings { width = 16f, spacing = 4f, weights = new[] { 1f, 0.5f } };
        var trees = BorderPlanner.Plan(map, 23, s);
        float w = map.Cols * map.CellSize;
        int expectedRough = (int)(4f * w * s.width / (s.spacing * s.spacing) * 0.8f);
        check(trees.Count > expectedRough && trees.All(t => BorderPlanner.InBand(map, t.X, t.Z, s.width + 0.01f)),
            "The border fills a band of trees within its width of every map edge, and nowhere else (" + trees.Count + " trees)");
        check(trees.All(t => !map.IsRoad(Math.Min(map.Cols - 1, (int)((t.X - map.MinX) / map.CellSize)), Math.Min(map.Rows - 1, (int)((t.Z - map.MinZ) / map.CellSize))))
            && trees.All(t => t.Scale >= s.minScale - 0.001f && t.Scale <= s.maxScale + 0.001f && (t.Prefab == 0 || t.Prefab == 1)),
            "Border trees never stand on a road and use the border's tree types and scale range");
        var again = BorderPlanner.Plan(map, 23, s);
        check(again.Count == trees.Count && again.Zip(trees, (a, b) => a.X == b.X && a.Z == b.Z && a.Prefab == b.Prefab).All(x => x),
            "The same seed always gives the same border");

        float mid = map.MinX + w * 0.5f, edgeZ = map.MinZ + 6f, insideZ = map.MinZ + 40f;
        check(BorderPlanner.IsBorderWall(map, mid, edgeZ, 16f) && !BorderPlanner.IsBorderWall(map, mid, insideZ, 16f)
            && !BorderPlanner.IsBorderWall(map, mid, edgeZ, 0f),
            "Enemy pathing and spawning treat the border band as a wall, and the playable area inside it as open");

        bool allMaps = new[] { "KOTH", "Escort", "Elimination" }.All(n =>
        {
            var g = AssetDatabase.LoadAssetAtPath<SubLevelGenSettings>("Assets/LevelCatalogue/SubLevelGen_" + n + ".asset");
            return g != null && g.borderWidth > 0f && g.borderWidth < g.edgeMargin && g.borderTrees.Length > 0 && g.borderTrees.All(t => t.prefab != null) && g.borderWalls;
        });
        check(allMaps, "KOTH, Escort and Elimination maps all have a tree border (narrower than the chunks' edge margin) and edge walls");
    }
}
