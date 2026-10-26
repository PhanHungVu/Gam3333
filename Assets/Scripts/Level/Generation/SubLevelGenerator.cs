using UnityEngine;

// Generates a sub-level's terrain from a seed and the settings asset. Step 1: terrain only -
// a low-poly mesh with a collider, cliffs (cells steeper than Max Walkable Slope) as walls.
// Generates on Play, or from the component's menu (Generate / Randomise Seed) in the editor.
[DisallowMultipleComponent]
public sealed class SubLevelGenerator : MonoBehaviour
{
    [SerializeField] SubLevelGenSettings settings;
    [Tooltip("The same seed always builds the same map.")]
    [SerializeField] int seed = 12345;
    [Tooltip("Pick a new seed each time Play starts.")]
    [SerializeField] bool randomSeedOnPlay;
    [SerializeField] bool generateOnStart = true;

    [Header("Look")]
    [Tooltip("Optional. Left empty, a plain green material is made.")]
    [SerializeField] Material groundMaterial;
    [Tooltip("Optional. Left empty, a plain grey-brown material is made.")]
    [SerializeField] Material cliffMaterial;
    [Tooltip("Scene view: mark wall (cliff) cells in red while this object is selected.")]
    [SerializeField] bool drawWalls = true;

    [Header("Debug (read-only)")]
    [SerializeField] int lastSeed;
    [SerializeField] float walkablePercent;

    const string TerrainName = "Generated Terrain";
    TerrainMap map;

    public TerrainMap Map => map;
    public int Seed => seed;

    void Start()
    {
        if (!generateOnStart) return;
        if (randomSeedOnPlay) seed = Random.Range(int.MinValue, int.MaxValue);
        Generate();
    }

    [ContextMenu("Generate")]
    public void Generate()
    {
        if (settings == null) { Debug.LogWarning("SubLevelGenerator needs a SubLevelGenSettings asset.", this); return; }
        map = TerrainGenerator.Generate(seed, settings.ToTerrainSettings());
        lastSeed = seed;
        walkablePercent = 100f * map.WalkableCount / Mathf.Max(1, map.Cols * map.Rows);

        var old = transform.Find(TerrainName);
        if (old != null) { if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject); }
        var terrain = new GameObject(TerrainName);
        // Rebuilt from the seed every time, so it is never saved into the scene.
        if (!Application.isPlaying) terrain.hideFlags = HideFlags.DontSave;
        terrain.transform.SetParent(transform, false);
        var mesh = TerrainMeshBuilder.Build(map);
        terrain.AddComponent<MeshFilter>().sharedMesh = mesh;
        terrain.AddComponent<MeshRenderer>().sharedMaterials = new[]
        {
            groundMaterial != null ? groundMaterial : MakeMaterial(new Color(0.42f, 0.62f, 0.32f)),
            cliffMaterial != null ? cliffMaterial : MakeMaterial(new Color(0.45f, 0.38f, 0.32f)),
        };
        terrain.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    [ContextMenu("Randomise Seed")]
    void RandomiseSeed()
    {
        seed = Random.Range(int.MinValue, int.MaxValue);
        Generate();
    }

    static Material MakeMaterial(Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var material = new Material(shader) { color = color };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        return material;
    }

    void OnDrawGizmosSelected()
    {
        if (!drawWalls || map == null) return;
        Gizmos.color = new Color(1f, 0.15f, 0.1f, 0.6f);
        var size = new Vector3(map.CellSize * 0.8f, 0.2f, map.CellSize * 0.8f);
        for (int row = 0; row < map.Rows; row++)
            for (int col = 0; col < map.Cols; col++)
            {
                if (map.IsWalkable(col, row)) continue;
                float h = Mathf.Max(Mathf.Max(map.Height(col, row), map.Height(col + 1, row)),
                    Mathf.Max(map.Height(col, row + 1), map.Height(col + 1, row + 1)));
                Gizmos.DrawCube(transform.TransformPoint(new Vector3(map.MinX + (col + 0.5f) * map.CellSize, h + 0.1f,
                    map.MinZ + (row + 0.5f) * map.CellSize)), size);
            }
    }
}
