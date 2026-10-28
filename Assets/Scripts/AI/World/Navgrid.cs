using System.Collections.Generic;
using UnityEngine;

// New for the enemy AI - the fish project never needed this, since its cavern was
// generated as a grid from the start (see grid.gd / cavern_generator.gd). This is
// the Unity equivalent: it reads a scene that was NOT built as a grid and turns it
// into one, once, by checking which cells overlap something tagged Obstacle.
//
// This is Phase 4 only: bake + gizmos. Nothing reads from this yet - that's Pathing,
// next phase. The whole point right now is to look at the gizmos in the Scene view
// and confirm the blocked cells actually match the real obstacles before anything
// gets built on top of this data.
public sealed class NavGrid : MonoBehaviour
{
    [Header("Area covered")]
    [Tooltip("Centered on this point - matches the Plane, which sits at world origin.")]
    [SerializeField] Vector3 origin = Vector3.zero;
    [SerializeField] float width = 200f;   // Plane scale 20 -> a 10x10 default mesh -> 200 world units
    [SerializeField] float depth = 200f;

    [Header("Cell size")]
    [Tooltip("Confirmed against the corridor gap (7m) and the slalom gaps (~10.4m) - both clear this with room to spare.")]
    [SerializeField] float cellSize = 2f;

    [Header("What counts as an obstacle")]
    [SerializeField] string obstacleTag = "Obstacle";
    [Tooltip("Inflates each obstacle's footprint so a cell right at the edge doesn't read as open when an enemy's body wouldn't actually fit.")]
    [SerializeField] float bodyRadiusPadding = 0.6f; // matches EnemyAgent's default Body Radius

    [Header("Height band")]
    [Tooltip("Below this, an obstacle is climbable and ignored entirely (curbs, small debris). Placeholder from the Mech's Step Offset (0.3).")]
    [SerializeField] float stepHeight = 0.3f;
    [Tooltip("Above this, still checked, but nothing here needs to reach further - shoulder height on a 2m enemy body. Untested guess.")]
    [SerializeField] float shoulderHeight = 1.6f;

    [Header("Gizmos")]
    [SerializeField] bool drawBlockedCells = true;
    [SerializeField] bool drawOpenCells = true;
    [SerializeField] Color blockedColor = new Color(0.85f, 0.25f, 0.2f, 0.5f);
    [SerializeField] Color openColor = new Color(0.2f, 0.8f, 0.3f, 0.15f);

    int cols, rows;
    bool[] blocked;
    bool baked;

    void Start() => Bake();

    [ContextMenu("Bake Now")]
    public void Bake()
    {
        cols = Mathf.Max(1, Mathf.CeilToInt(width / cellSize));
        rows = Mathf.Max(1, Mathf.CeilToInt(depth / cellSize));
        blocked = new bool[cols * rows];

        // Gather obstacle footprints once, height-checked, in XZ. Bounds intersection
        // rather than a physics probe: deterministic, doesn't depend on a Physics
        // layer (obstacles are TAGGED, not layered, on purpose - see the Default-layer
        // note from a few phases back, this avoids touching that system at all), and
        // Collider.bounds is already an axis-aligned box even for a rotated cube,
        // which is a safe (slightly conservative) footprint to block against.
        // With generated terrain (NavTerrain), the height band is measured from the ground under
        // each obstacle, so cover and trees on a plateau count just like on low ground.
        var footprints = new List<Rect>();
        foreach (var go in GameObject.FindGameObjectsWithTag(obstacleTag))
        {
            var col = go.GetComponent<Collider>();
            if (col == null) continue;

            Bounds b = col.bounds;
            float floor = NavTerrain.GroundHeight(b.center, origin.y);
            float bandMin = floor + stepHeight;
            float bandMax = floor + shoulderHeight;
            bool inBand = b.max.y >= bandMin && b.min.y <= bandMax;
            if (!inBand) continue; // shorter than step height (or floating above the band) - climbable/irrelevant, ignored

            float pad = bodyRadiusPadding;
            footprints.Add(Rect.MinMaxRect(b.min.x - pad, b.min.z - pad, b.max.x + pad, b.max.z + pad));
        }

        float minX = origin.x - width * 0.5f;
        float minZ = origin.z - depth * 0.5f;

        // Each footprint marks only the cells it covers (same result as testing every cell against
        // every footprint, but fast enough for hundreds of trees).
        foreach (var f in footprints)
        {
            int c0 = Mathf.Max(0, Mathf.FloorToInt((f.xMin - minX) / cellSize));
            int c1 = Mathf.Min(cols - 1, Mathf.CeilToInt((f.xMax - minX) / cellSize) - 1);
            int r0 = Mathf.Max(0, Mathf.FloorToInt((f.yMin - minZ) / cellSize));
            int r1 = Mathf.Min(rows - 1, Mathf.CeilToInt((f.yMax - minZ) / cellSize) - 1);
            for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++)
                    if (f.Overlaps(new Rect(minX + c * cellSize, minZ + r * cellSize, cellSize, cellSize))) blocked[r * cols + c] = true;
        }

        // Cliffs and off-map ground from generated terrain are walls too.
        int wallCells = 0;
        if (NavTerrain.Active)
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    var centre = new Vector3(minX + (c + 0.5f) * cellSize, origin.y, minZ + (r + 0.5f) * cellSize);
                    if (!NavTerrain.IsWall(centre)) continue;
                    if (!blocked[r * cols + c]) wallCells++;
                    blocked[r * cols + c] = true;
                }

        baked = true;
        int blockedCount = 0;
        for (int i = 0; i < blocked.Length; i++) if (blocked[i]) blockedCount++;
        Debug.Log($"[NavGrid] Baked {cols}x{rows} cells. Found {footprints.Count} obstacle(s) tagged '{obstacleTag}'. {blockedCount} cells blocked" +
            (NavTerrain.Active ? $" ({wallCells} by terrain walls)." : "."));
    }

    // ---- public query API - unused today, this is what Pathing reads from next phase ----

    public int Cols => cols;
    public int Rows => rows;
    public float CellSize => cellSize;
    public bool Baked => baked;

    public bool IsBlocked(int col, int row)
    {
        if (col < 0 || col >= cols || row < 0 || row >= rows) return true; // outside the grid counts as blocked
        return blocked[row * cols + col];
    }

    public bool IsBlockedWorld(Vector3 worldPos)
    {
        if (!baked) return false;
        WorldToCell(worldPos, out int c, out int r);
        if (c < 0 || c >= cols || r < 0 || r >= rows) return true; // outside the grid counts as blocked
        return blocked[r * cols + c];
    }

    public void WorldToCell(Vector3 worldPos, out int col, out int row)
    {
        col = Mathf.FloorToInt((worldPos.x - (origin.x - width * 0.5f)) / cellSize);
        row = Mathf.FloorToInt((worldPos.z - (origin.z - depth * 0.5f)) / cellSize);
    }

    public Vector3 CellToWorldCenter(int col, int row)
    {
        float x = origin.x - width * 0.5f + (col + 0.5f) * cellSize;
        float z = origin.z - depth * 0.5f + (row + 0.5f) * cellSize;
        return new Vector3(x, origin.y, z);
    }

    void OnDrawGizmos()
    {
        if (!baked || blocked == null) return;
        Vector3 size = new Vector3(cellSize * 0.92f, 0.05f, cellSize * 0.92f); // slight gap between cells so the grid reads clearly

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                bool b = blocked[r * cols + c];
                if (b && !drawBlockedCells) continue;
                if (!b && !drawOpenCells) continue;

                Gizmos.color = b ? blockedColor : openColor;
                Gizmos.DrawCube(CellToWorldCenter(c, r), size);
            }
        }
    }
}