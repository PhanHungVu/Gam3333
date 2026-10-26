using UnityEngine;

// Tuning for sub-level generation, kept in an asset so design can adjust it without code.
// Step 1 covers the terrain; chunk placement, roads and POIs add their settings later.
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
    [Range(0f, 1f)] public float terraceSharpness = 0.85f;

    [Header("Walls")]
    [Tooltip("Cells steeper than this (degrees) are cliffs: walls nobody can climb.")]
    [Range(1f, 89f)] public float maxWalkableSlope = 35f;

    public TerrainSettings ToTerrainSettings() => new TerrainSettings
    {
        mapSize = mapSize, cellSize = cellSize, hillSize = hillSize, noiseLayers = noiseLayers,
        layerStrength = layerStrength, layerDetail = layerDetail, heightSpread = heightSpread, terraceLevels = terraceLevels,
        levelHeight = levelHeight, terraceSharpness = terraceSharpness, maxWalkableSlope = maxWalkableSlope,
    };
}
