using UnityEngine;
using UnityEngine.Rendering;

// Turns a TerrainMap into a low-poly, flat-shaded mesh: every triangle has its own vertices, so
// each face is lit as one flat colour. Submesh 0 is walkable ground, submesh 1 is cliff (wall) cells.
public static class TerrainMeshBuilder
{
    public static Mesh Build(TerrainMap map)
    {
        int cells = map.Cols * map.Rows;
        var vertices = new Vector3[cells * 6];
        var ground = new System.Collections.Generic.List<int>(cells * 6);
        var cliffs = new System.Collections.Generic.List<int>();
        int v = 0;
        for (int row = 0; row < map.Rows; row++)
            for (int col = 0; col < map.Cols; col++)
            {
                Vector3 c00 = Corner(map, col, row), c10 = Corner(map, col + 1, row);
                Vector3 c01 = Corner(map, col, row + 1), c11 = Corner(map, col + 1, row + 1);
                var target = map.IsWalkable(col, row) ? ground : cliffs;
                // Two triangles, clockwise seen from above so they face up.
                foreach (var p in new[] { c00, c01, c11, c00, c11, c10 })
                {
                    vertices[v] = p;
                    target.Add(v++);
                }
            }

        var mesh = new Mesh { name = "Generated Terrain", indexFormat = IndexFormat.UInt32 };
        mesh.vertices = vertices;
        mesh.subMeshCount = 2;
        mesh.SetTriangles(ground, 0);
        mesh.SetTriangles(cliffs, 1);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static Vector3 Corner(TerrainMap map, int x, int z) =>
        new Vector3(map.MinX + x * map.CellSize, map.Height(x, z), map.MinZ + z * map.CellSize);
}
