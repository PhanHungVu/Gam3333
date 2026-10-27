using System.Collections.Generic;
using UnityEngine;

// Generates a sub-level from a seed and the settings asset: the terrain (a low-poly mesh with a
// collider, cliffs steeper than Max Walkable Slope as walls), then the start chunk near the middle
// and the mission chunks spread across the map, each on flattened ground. On Play the player is
// moved to the start chunk's PlayerStart. Generates on Play, or from the component's menu in the editor.
// Everything generated is rebuilt from the seed, so none of it is saved into the scene.
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
    [Tooltip("On Play the player is placed this far above the start chunk's PlayerStart and drops onto it.")]
    [SerializeField, Min(0f)] float playerDropHeight = 2f;

    [Header("Debug (read-only)")]
    [SerializeField] int lastSeed;
    [SerializeField] float walkablePercent;
    [SerializeField] int missionChunksPlaced;

    const string TerrainName = "Generated Terrain";
    const string ChunksName = "Generated Chunks";
    TerrainMap map;
    List<ChunkPlacement> placements = new List<ChunkPlacement>();
    Transform startInstance;

    public TerrainMap Map => map;
    public IReadOnlyList<ChunkPlacement> Placements => placements;
    public int Seed => seed;

    void Start()
    {
        if (!generateOnStart) return;
        if (randomSeedOnPlay) seed = Random.Range(int.MinValue, int.MaxValue);
        Generate();
        MovePlayerToStart();
    }

    [ContextMenu("Generate")]
    public void Generate()
    {
        if (settings == null) { Debug.LogWarning("SubLevelGenerator needs a SubLevelGenSettings asset.", this); return; }
        map = TerrainGenerator.Generate(seed, settings.ToTerrainSettings());
        lastSeed = seed;
        var footprints = ChunkFootprints(out int requested);
        var placement = settings.ToPlacementSettings();
        placements = ChunkPlacer.Place(map, seed, footprints, placement);
        ChunkPlacer.Flatten(map, placements, footprints, placement);
        walkablePercent = 100f * map.WalkableCount / Mathf.Max(1, map.Cols * map.Rows);
        missionChunksPlaced = 0;
        foreach (var p in placements) if (!(settings.startChunk != null && p.Footprint == 0)) missionChunksPlaced++;
        if (missionChunksPlaced < requested)
            Debug.LogWarning($"SubLevelGenerator: only {missionChunksPlaced} of {requested} mission chunks fit on seed {seed}.", this);

        var terrain = Rebuild(TerrainName);
        var mesh = TerrainMeshBuilder.Build(map);
        terrain.AddComponent<MeshFilter>().sharedMesh = mesh;
        terrain.AddComponent<MeshRenderer>().sharedMaterials = new[]
        {
            groundMaterial != null ? groundMaterial : MakeMaterial(new Color(0.42f, 0.62f, 0.32f)),
            cliffMaterial != null ? cliffMaterial : MakeMaterial(new Color(0.45f, 0.38f, 0.32f)),
        };
        terrain.AddComponent<MeshCollider>().sharedMesh = mesh;

        var chunks = Rebuild(ChunksName).transform;
        startInstance = null;
        foreach (var p in placements)
        {
            bool isStart = settings.startChunk != null && p.Footprint == 0;
            var prefab = isStart ? settings.startChunk : settings.missionChunk;
            if (prefab == null) continue;
            var chunk = Instantiate(prefab.gameObject, transform.TransformPoint(new Vector3(p.X, p.Height, p.Z)), transform.rotation, chunks);
            chunk.name = prefab.name;
            if (!Application.isPlaying)
                foreach (var part in chunk.GetComponentsInChildren<Transform>(true)) part.gameObject.hideFlags = HideFlags.DontSave;
            if (isStart) startInstance = chunk.transform;
        }
    }

    // The start's footprint first (if there is a start chunk), then one per mission chunk; the
    // mission count is picked from the seed within the settings' range.
    List<ChunkFootprint> ChunkFootprints(out int missionCount)
    {
        var list = new List<ChunkFootprint>();
        if (settings.startChunk != null) list.Add(FootprintOf(settings.startChunk));
        int lo = Mathf.Min(settings.missionChunkMin, settings.missionChunkMax), hi = Mathf.Max(settings.missionChunkMin, settings.missionChunkMax);
        missionCount = settings.missionChunk != null ? new System.Random(seed ^ 0x6a09e667).Next(lo, hi + 1) : 0;
        for (int i = 0; i < missionCount; i++) list.Add(FootprintOf(settings.missionChunk));
        return list;
    }

    static ChunkFootprint FootprintOf(ChunkDefinition chunk) => new ChunkFootprint(chunk.SizeMetres.x, chunk.SizeMetres.y);

    // Replaces a generated child; in the editor it is marked never to be saved into the scene.
    GameObject Rebuild(string childName)
    {
        var old = transform.Find(childName);
        if (old != null) { if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject); }
        var child = new GameObject(childName);
        if (!Application.isPlaying) child.hideFlags = HideFlags.DontSave;
        child.transform.SetParent(transform, false);
        return child;
    }

    // Puts the player (tagged Player) at the start chunk's PlayerStart, or its centre if it has none.
    void MovePlayerToStart()
    {
        if (startInstance == null) return;
        var player = GameObject.FindWithTag("Player");
        if (player == null) return;
        Transform marker = FindChild(startInstance, "PlayerStart") ?? startInstance;
        var controller = player.GetComponent<CharacterController>();
        if (controller != null) controller.enabled = false;
        player.transform.position = marker.position + Vector3.up * playerDropHeight;
        if (controller != null) controller.enabled = true;
    }

    static Transform FindChild(Transform root, string childName)
    {
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == childName) return child;
        return null;
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
