using UnityEngine;

// Tuning for sub-level generation, kept in an asset so design can adjust it without code.
// Terrain, chunk placement and roads; POIs add their settings later.
[CreateAssetMenu(fileName = "SubLevelGenSettings", menuName = "Mech/Sub-level Generation")]
public sealed class SubLevelGenSettings : ScriptableObject
{
    [Header("Map")]
    [Tooltip("Width and depth of the map in metres, centred on the origin. Match the NavGrid's area.")]
    [Min(20f)] public float mapSize = 500f;
    [Tooltip("Size of one terrain cell in metres (4 = one golem). Smaller is finer but heavier.")]
    [Min(0.5f)] public float cellSize = 4f;

    [Header("Hills (Perlin noise)")]
    [Tooltip("Roughly how many metres across a typical hill is.")]
    [Min(1f)] public float hillSize = 160f;
    [Tooltip("Layers of noise added together: more layers add smaller bumps on the big hills.")]
    [Range(1, 8)] public int noiseLayers = 4;
    [Tooltip("How much each smaller layer adds (0.5 = half as strong as the one before).")]
    [Range(0f, 1f)] public float layerStrength = 0.5f;
    [Tooltip("How much smaller each layer's bumps are (2 = half the size).")]
    [Range(1f, 4f)] public float layerDetail = 2f;
    [Tooltip("Stretches the hills so the map reaches every terrace level. 1 = raw noise (mostly the middle levels).")]
    [Range(0.5f, 4f)] public float heightSpread = 2.2f;

    [Header("Terraces")]
    [Tooltip("Number of height levels (the lowest is 0 m).")]
    [Range(2, 10)] public int terraceLevels = 4;
    [Tooltip("Metres between one level and the next.")]
    [Min(0.1f)] public float levelHeight = 6f;
    [Tooltip("0 = smooth rolling hills, 1 = sharp steps. In between, gentle rises become ramps and steep ones cliffs.")]
    [Range(0f, 1f)] public float terraceSharpness = 0.75f;

    [Header("Variety")]
    [Tooltip("Roughly how much of the map is terraced with cliffs. The rest is smooth rolling hills. 1 = cliffs everywhere, 0 = all smooth.")]
    [Range(0f, 1f)] public float cliffCoverage = 0.75f;
    [Tooltip("Metres across a cliffy or a smooth region. Bigger gives whole areas of each rather than patches.")]
    [Min(10f)] public float variationSize = 350f;
    [Tooltip("How gradually cliffy areas soften into smooth ones. Small = abrupt edge, large = cliffs fade into hills.")]
    [Range(0.02f, 0.5f)] public float regionBlend = 0.25f;
    [Tooltip("How much terracing smooth areas keep, so they read as a softer version of the cliff areas. 0 = pure rolling hills.")]
    [Range(0f, 1f)] public float smoothAreaTerracing = 0.4f;

    [Header("Walls")]
    [Tooltip("Cells steeper than this (degrees) are cliffs: walls nobody can climb.")]
    [Range(1f, 89f)] public float maxWalkableSlope = 35f;

    [Header("Chunks")]
    [Tooltip("Start/extract chunk, placed near the middle of the map. The player starts at its PlayerStart child.")]
    public ChunkDefinition startChunk;
    [Tooltip("The mission's chunk (e.g. Chunk_Survival), repeated across the map.")]
    public ChunkDefinition missionChunk;
    [Tooltip("How many mission chunks to place; the seed picks a number in this range.")]
    [Min(0)] public int missionChunkMin = 3;
    [Min(0)] public int missionChunkMax = 4;
    [Tooltip("Metres of gap between chunk footprints.")]
    [Min(0f)] public float minSpacing = 20f;
    [Tooltip("Metres kept clear of chunks at the map border.")]
    [Min(0f)] public float edgeMargin = 20f;
    [Tooltip("Metres over which the flattened ground under a chunk fades into the hills (wider = gentler ramps up to it).")]
    [Min(0.1f)] public float blendDistance = 24f;
    [Tooltip("Random spots tried when placing chunks. More finds better spots but is slower.")]
    [Range(20, 2000)] public int placementCandidates = 300;
    [Tooltip("How much uneven ground counts against a spot, compared with spreading chunks out.")]
    [Min(0f)] public float flatnessWeight = 10f;

    [Header("Roads")]
    [Tooltip("Extra road links beyond the shortest network joining every chunk, so there are loops and route choices.")]
    [Range(0, 6)] public int extraLinks = 1;
    [Tooltip("Road width in metres (12 = three golems).")]
    [Min(1f)] public float roadWidth = 12f;
    [Tooltip("Roads are evened out to stay under this slope (degrees), carving ramps through cliffs where needed.")]
    [Range(1f, 45f)] public float maxRoadSlope = 20f;
    [Tooltip("Metres over which a road's edges fade into the terrain.")]
    [Min(0.1f)] public float roadBlend = 8f;
    [Tooltip("How strongly a road's route avoids slopes.")]
    [Min(0f)] public float roadSlopeCost = 4f;
    [Tooltip("Roads go round cliffs. When there is no way round at all, carve a ramp through for that road (with a Console warning) so every chunk stays reachable.")]
    public bool carveIfNoWayRound = true;
    [Tooltip("Only for carved roads: how strongly they still avoid crossing cliffs.")]
    [Min(1f)] public float roadCliffCost = 10f;
    [Tooltip("Cells either side averaged when evening out a road's height. Higher = smoother, flatter roads.")]
    [Range(0, 10)] public int roadSmoothing = 3;

    public RoadSettings ToRoadSettings() => new RoadSettings
    {
        extraLinks = extraLinks, roadWidth = roadWidth, maxRoadSlope = maxRoadSlope, roadBlend = roadBlend,
        slopeCost = roadSlopeCost, cliffCost = roadCliffCost, smoothing = roadSmoothing, maxWalkableSlope = maxWalkableSlope,
        carveIfNoWayRound = carveIfNoWayRound,
    };

    public ChunkPlacementSettings ToPlacementSettings() => new ChunkPlacementSettings
    {
        minSpacing = minSpacing, edgeMargin = edgeMargin, blendDistance = blendDistance, candidates = placementCandidates,
        flatnessWeight = flatnessWeight, levelHeight = levelHeight, terraceLevels = terraceLevels, maxWalkableSlope = maxWalkableSlope,
    };

    public TerrainSettings ToTerrainSettings() => new TerrainSettings
    {
        mapSize = mapSize, cellSize = cellSize, hillSize = hillSize, noiseLayers = noiseLayers,
        layerStrength = layerStrength, layerDetail = layerDetail, heightSpread = heightSpread, terraceLevels = terraceLevels,
        levelHeight = levelHeight, terraceSharpness = terraceSharpness, maxWalkableSlope = maxWalkableSlope,
        cliffCoverage = cliffCoverage, variationSize = variationSize, regionBlend = regionBlend, smoothAreaTerracing = smoothAreaTerracing,
    };
}
