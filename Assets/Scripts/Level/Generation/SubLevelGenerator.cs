using System.Collections.Generic;
using UnityEngine;

// Generates a sub-level from a seed and the settings asset: the terrain (a low-poly mesh with a
// collider, cliffs steeper than Max Walkable Slope as walls), then the start chunk near the middle
// and the mission chunks spread across the map, each on flattened ground, joined by roads from path
// connector to path connector (ramps carved through cliffs where needed), then decor such as trees in
// woods and clearings, kept off roads, chunks and cliffs. On Play the player is
// moved to the start chunk's PlayerStart. Generates in Awake on Play (before NavGrid bakes, so enemy
// pathing sees the terrain), or from the component's menu in the editor. During Play it registers the
// terrain with NavTerrain (cliffs as walls, ground heights) and sets the player's climbable slope to
// Max Walkable Slope, so player and enemies obey the same limit.
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
    [Tooltip("Optional. Left empty, a plain sandy material is made.")]
    [SerializeField] Material roadMaterial;
    [Tooltip("Scene view: mark wall (cliff) cells in red while this object is selected.")]
    [SerializeField] bool drawWalls = true;
    [Tooltip("On Play the player is placed this far above the start chunk's PlayerStart and drops onto it.")]
    [SerializeField, Min(0f)] float playerDropHeight = 2f;

    [Header("Debug (read-only)")]
    [SerializeField] int lastSeed;
    [SerializeField] float walkablePercent;
    [SerializeField] int missionChunksPlaced;
    [SerializeField] int roadsBuilt;
    [SerializeField] int roadsCarved;
    [SerializeField] bool allConnected;
    [SerializeField] int decorPlaced;

    const string TerrainName = "Generated Terrain";
    const string ChunksName = "Generated Chunks";
    const string DecorName = "Generated Decor";
    const string ObstacleTag = "Obstacle";
    TerrainMap map;
    List<ChunkPlacement> placements = new List<ChunkPlacement>();
    List<Road> roads = new List<Road>();
    Transform startInstance;

    public TerrainMap Map => map;
    public IReadOnlyList<ChunkPlacement> Placements => placements;
    public IReadOnlyList<Road> Roads => roads;
    public int Seed => seed;

    void Awake()
    {
        if (!generateOnStart) return;
        if (randomSeedOnPlay) seed = Random.Range(int.MinValue, int.MaxValue);
        Generate();
    }

    void Start()
    {
        MovePlayerToStart();
        ApplyPlayerSlope();
    }

    void OnDestroy() => NavTerrain.Unregister(this);

    // Enemy pathing reads the terrain through NavTerrain; regenerating during Play re-bakes it.
    void ShareTerrainWithPathing()
    {
        if (!Application.isPlaying || map == null) return;
        NavTerrain.Register(this, IsWallAt, GroundAt);
        var grid = FindAnyObjectByType<NavGrid>();
        if (grid != null && grid.Baked)
        {
            grid.Bake();
            if (FlowField.Instance != null) FlowField.Instance.Rebuild();
        }
    }

    bool IsWallAt(Vector3 world)
    {
        Vector3 local = transform.InverseTransformPoint(world);
        return map.WallAt(local.x, local.z);
    }

    float GroundAt(Vector3 world)
    {
        Vector3 local = transform.InverseTransformPoint(world);
        return transform.TransformPoint(new Vector3(local.x, DecorScatter.HeightAt(map, local.x, local.z), local.z)).y;
    }

    // The player can climb exactly what enemies can: Max Walkable Slope.
    void ApplyPlayerSlope()
    {
        if (settings == null) return;
        var player = GameObject.FindWithTag("Player");
        var controller = player != null ? player.GetComponent<CharacterController>() : null;
        if (controller != null) controller.slopeLimit = settings.maxWalkableSlope;
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
        roads = RoadBuilder.Build(map, placements, footprints, Connectors(footprints.Count), settings.ToRoadSettings());
        roadsBuilt = roads.Count;
        roadsCarved = 0;
        foreach (var r in roads) if (r.Carved) roadsCarved++;
        if (roadsCarved > 0)
            Debug.LogWarning($"SubLevelGenerator: {roadsCarved} road(s) on seed {seed} had no way round and carve through a cliff.", this);
        allConnected = RoadBuilder.AllConnected(placements.Count, roads);
        if (!allConnected)
            Debug.LogWarning($"SubLevelGenerator: on seed {seed} not every chunk is joined to the start by road.", this);
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
            roadMaterial != null ? roadMaterial : MakeMaterial(new Color(0.76f, 0.66f, 0.47f)),
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

        PlaceDecor(footprints);
        ShareTerrainWithPathing();
    }

    // Each decor group, seeded separately so adding a group never moves another group's items.
    void PlaceDecor(List<ChunkFootprint> footprints)
    {
        var decor = Rebuild(DecorName).transform;
        decorPlaced = 0;
        if (settings.decorGroups == null) return;
        for (int g = 0; g < settings.decorGroups.Length; g++)
        {
            var group = settings.decorGroups[g];
            if (group == null || group.prefabs == null || group.prefabs.Length == 0) continue;
            var parent = new GameObject(string.IsNullOrEmpty(group.name) ? "Decor " + g : group.name).transform;
            parent.SetParent(decor, false);
            if (!Application.isPlaying) parent.gameObject.hideFlags = HideFlags.DontSave;
            var items = DecorScatter.Scatter(map, seed ^ (0x7f4a7c15 * (g + 1)), group.ToSettings(), placements, footprints,
                settings.startChunk != null);
            foreach (var item in items)
            {
                var prefab = group.prefabs[item.Prefab] != null ? group.prefabs[item.Prefab].prefab : null;
                if (prefab == null) continue;
                var placedItem = Instantiate(prefab, transform.TransformPoint(new Vector3(item.X, item.Height, item.Z)),
                    transform.rotation * Quaternion.Euler(0f, item.Yaw, 0f), parent);
                placedItem.transform.localScale = prefab.transform.localScale * item.Scale;
                foreach (var part in placedItem.GetComponentsInChildren<Transform>(true))
                {
                    // NavGrid counts objects tagged Obstacle that carry a collider themselves.
                    if (group.blocksPathing && part.GetComponent<Collider>() != null) part.gameObject.tag = ObstacleTag;
                    if (!Application.isPlaying) part.gameObject.hideFlags = HideFlags.DontSave;
                }
                decorPlaced++;
            }
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

    // Path connectors per footprint, in the same order as ChunkFootprints, in each chunk's own space.
    List<IReadOnlyList<ChunkConnector>> Connectors(int footprintCount)
    {
        var list = new List<IReadOnlyList<ChunkConnector>>();
        if (settings.startChunk != null) list.Add(ConnectorsOf(settings.startChunk));
        var mission = settings.missionChunk != null ? ConnectorsOf(settings.missionChunk) : new List<ChunkConnector>();
        while (list.Count < footprintCount) list.Add(mission);
        return list;
    }

    static List<ChunkConnector> ConnectorsOf(ChunkDefinition chunk)
    {
        var list = new List<ChunkConnector>();
        var root = chunk.transform;
        foreach (var connector in chunk.PathConnectors)
        {
            Vector3 local = root.InverseTransformPoint(connector.position);
            Vector3 forward = root.InverseTransformDirection(connector.forward);
            list.Add(new ChunkConnector(local.x, local.z, forward.x, forward.z));
        }
        return list;
    }

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
