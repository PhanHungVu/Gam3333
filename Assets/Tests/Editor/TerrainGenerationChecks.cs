using System;
using System.Linq;
using UnityEngine;

// Sub-level terrain: seeded, terraced, with steep cells marked as walls.
public static class TerrainGenerationChecks
{
    public static void Run(Action<bool, string> check)
    {
        var settings = new TerrainSettings { mapSize = 200f };
        var first = TerrainGenerator.Generate(42, settings);
        var again = TerrainGenerator.Generate(42, settings);
        var other = TerrainGenerator.Generate(43, settings);
        check(first.Heights.SequenceEqual(again.Heights) && first.Walkable.SequenceEqual(again.Walkable),
            "The same seed always builds the same terrain");
        check(!first.Heights.SequenceEqual(other.Heights), "A different seed builds different terrain");
        check(first.Cols == 50 && first.Rows == 50 && Mathf.Approximately(first.MinX, -100f),
            "Terrain covers the map size in cells, centred on the origin");
        float top = (settings.terraceLevels - 1) * settings.levelHeight;
        check(first.Heights.All(h => h >= 0f && h <= top + 0.001f), "Heights stay between the lowest and highest terrace levels");

        check(Mathf.Approximately(TerrainGenerator.Terrace(0f, 4, 4f, 0.6f), 0f)
            && Mathf.Approximately(TerrainGenerator.Terrace(1f, 4, 4f, 0.6f), 12f)
            && Mathf.Approximately(TerrainGenerator.Terrace(1f / 3f + 0.05f, 4, 4f, 0.6f), 4f),
            "Terracing settles heights onto the levels");
        check(TerrainGenerator.IsWalkable(0f, 0f, 0f, 0f, 4f, 35f)
            && TerrainGenerator.IsWalkable(0f, 1f, 0f, 1f, 4f, 35f)
            && !TerrainGenerator.IsWalkable(0f, 4f, 0f, 4f, 4f, 35f),
            "Cells steeper than Max Walkable Slope are walls");
        var full = TerrainGenerator.Generate(42, new TerrainSettings());
        int levelsUsed = full.Heights.Select(h => Mathf.RoundToInt(h / 6f)).Distinct().Count();
        check(levelsUsed >= 3, "Height Spread lets the map reach most terrace levels");
        var sharp = TerrainGenerator.Generate(42, new TerrainSettings { terraceSharpness = 1f });
        check(sharp.WalkableCount > 0 && sharp.WalkableCount < sharp.Cols * sharp.Rows,
            "Sharp terraces give walkable levels separated by cliffs");

        var mesh = TerrainMeshBuilder.Build(first);
        try
        {
            check(mesh.vertexCount == first.Cols * first.Rows * 6 && mesh.subMeshCount == 3
                && mesh.GetTriangles(1).Length == (first.Cols * first.Rows - first.WalkableCount) * 6,
                "The terrain mesh is flat-shaded, with cliffs and roads in their own parts");
            check(mesh.normals.All(n => n.y > 0f), "Every terrain face points upward");
        }
        finally { UnityEngine.Object.DestroyImmediate(mesh); }
    }
}
