using UnityEngine;

// One outpost slot type: which pieces can go there (with chances) and how many.
[System.Serializable]
public sealed class OutpostSlotPieces
{
    public DecorPrefab[] pieces = new DecorPrefab[0];
    [Min(0)] public int min;
    [Min(0)] public int max;

    public OutpostSlotSettings ToSettings()
    {
        var weights = new float[pieces.Length];
        for (int i = 0; i < pieces.Length; i++) weights[i] = pieces[i] != null && pieces[i].prefab != null ? pieces[i].weight : 0f;
        return new OutpostSlotSettings { weights = weights, min = min, max = max };
    }
}

// The outpost the generator builds inside this (circular) chunk, on its generated interior.
// Centre: one piece in the middle of the hold area. Camp: on the flat hold area in the middle (e.g. the
// elimination camp's tents, which are spawn points). Wall Edge: next to walls (extra cover such as rocks).
// High: on top of blocks (vantage, such as platforms). Flat: open ground (tents, trees).
// Entry lanes stay clear. Put it on the chunk prefab's root, next to ChunkDefinition.
[DisallowMultipleComponent]
[RequireComponent(typeof(ChunkDefinition))]
public sealed class ChunkOutpost : MonoBehaviour
{
    public OutpostSlotPieces centre = new OutpostSlotPieces { min = 1, max = 1 };
    public OutpostSlotPieces wallEdge = new OutpostSlotPieces { min = 4, max = 8 };
    public OutpostSlotPieces high = new OutpostSlotPieces { min = 1, max = 3 };
    public OutpostSlotPieces flat = new OutpostSlotPieces { min = 3, max = 6 };
    public OutpostSlotPieces camp = new OutpostSlotPieces { min = 0, max = 0 };
    [Tooltip("Metres between outpost pieces.")]
    [Min(0.5f)] public float spacing = 6f;
    [Tooltip("Metres kept clear either side of each entry lane.")]
    [Min(0f)] public float laneClearance = 2f;
    [Min(0.01f)] public float minScale = 0.9f;
    [Min(0.01f)] public float maxScale = 1.2f;
    [Tooltip("Tag pieces with colliders Obstacle so enemy pathing goes round them.")]
    public bool blocksPathing = true;

    public OutpostSettings ToSettings() => new OutpostSettings
    {
        centre = centre.ToSettings(), flat = flat.ToSettings(), wallEdge = wallEdge.ToSettings(), high = high.ToSettings(), camp = camp.ToSettings(),
        spacing = spacing, laneClearance = laneClearance, minScale = Mathf.Min(minScale, maxScale), maxScale = Mathf.Max(minScale, maxScale),
    };

    public OutpostSlotPieces Slot(OutpostSlot slot) => slot == OutpostSlot.Centre ? centre : slot == OutpostSlot.Camp ? camp
        : slot == OutpostSlot.WallEdge ? wallEdge : slot == OutpostSlot.High ? high : flat;
}
