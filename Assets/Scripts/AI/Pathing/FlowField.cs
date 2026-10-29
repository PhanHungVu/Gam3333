using System.Collections.Generic;
using UnityEngine;

// Port of the fish project's flow_field.gd - this is the actual equivalent, not
// A* (see the earlier correction: the fish never used A*, it used exactly this).
// One shared field, built from a single goal outward, read by every enemy with a
// single lookup. Sits on top of NavGrid: reads its blocked/open cells, never
// writes to it, and knows nothing about EnemyAgent - the same one-way dependency
// direction as the rest of Scripts/AI/.
//
// Three layers, same as the plain-language breakdown earlier:
//   cost[]        - how hard each open cell is to enter (higher near walls)
//   integration[] - flood-filled distance-to-goal from the player's cell, in cost units
//   direction[]   - for each cell, which neighbour has the lower integration value
//
// This is Phase 5 only: build + arrow gizmos. Nothing reads from this yet - that's
// Phase 6, when ChaseState swaps from seeking the player directly to seeking
// whatever direction is sampled here.
[RequireComponent(typeof(NavGrid))]
public sealed class FlowField : MonoBehaviour
{
    [Header("Goal")]
    [Tooltip("Leave empty to find the object tagged Player.")]
    [SerializeField] Transform goal;

    [Header("Rebuild trigger")]
    [Tooltip("Rebuild only once the goal has moved this far from where the field was last built - not every frame.")]
    [SerializeField] float rebuildDistance = 3f;
    [Tooltip("Never rebuild more often than this many seconds, even when the goal moves fast. 0 = no cap.")]
    [SerializeField, Min(0f)] float minRebuildInterval = 0f;

    [Header("Flood")]
    [Tooltip("Only work out directions within this many metres of the goal; enemies further out chase directly (ChaseState's fallback). Keeps each rebuild the same cost however big the map is. 0 = the whole grid.")]
    [SerializeField, Min(0f)] float floodRadius = 0f;

    [Header("Cost field")]
    [Tooltip("Extra cost for a cell directly touching a blocked cell - this is the 'mud near walls' effect, it gives obstacles some clearance instead of hugging them.")]
    [SerializeField] float wallAdjacencyCost = 4f;

    [Header("Gizmos")]
    [SerializeField] bool drawArrows = true;
    [SerializeField] Color arrowColor = new Color(0.3f, 0.5f, 0.95f, 0.9f);
    [Tooltip("Skip cells to avoid a solid wall of arrows - 1 draws every cell, 2 draws every other, etc.")]
    [SerializeField, Min(1)] int gizmoStride = 2;

    NavGrid grid;
    float[] cost;
    float[] integration;
    sbyte[] dirX, dirZ; // -1/0/1 per cell - which neighbour to step toward
    Vector3 lastGoalPos;
    float lastBuildTime;
    bool built;
    bool costBuilt;
    // The cells the previous build touched - cleared before the next build, so a
    // limited flood never has to reset the whole grid.
    int boxMinC, boxMinR, boxMaxC, boxMaxR;

    // One shared field for the whole horde - same idea as the fish project, where
    // every fish read the same flow_field singleton rather than each holding a
    // reference to it.
    public static FlowField Instance { get; private set; }

    void Awake()
    {
        grid = GetComponent<NavGrid>();
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        if (goal == null)
        {
            var player = GameObject.FindWithTag("Player");
            if (player != null) goal = player.transform;
        }
        TryBuild(force: true);
    }

    void Update()
    {
        if (goal == null) return;
        TryBuild(force: false);
    }

    // Rebuilds everything from the current NavGrid, e.g. after it re-bakes for newly generated terrain.
    public void Rebuild() => TryBuild(force: true);

    void TryBuild(bool force)
    {
        if (!grid.Baked) return; // NavGrid hasn't baked yet this frame - wait
        if (goal == null) return;

        if (!force && built && Vector3.Distance(goal.position, lastGoalPos) < rebuildDistance) return;
        if (!force && built && Time.time - lastBuildTime < minRebuildInterval) return;

        Build(goal.position, force);
        lastGoalPos = goal.position;
        lastBuildTime = Time.time;
        built = true;
        Debug.Log($"[FlowField] Built from goal at {goal.position}. Grid is {grid.Cols}x{grid.Rows}.");
    }

    void Build(Vector3 goalWorldPos, bool force)
    {
        int cols = grid.Cols, rows = grid.Rows;
        int count = cols * rows;

        if (cost == null || cost.Length != count)
        {
            cost = new float[count];
            integration = new float[count];
            dirX = new sbyte[count];
            dirZ = new sbyte[count];
            for (int i = 0; i < count; i++) integration[i] = float.PositiveInfinity;
            boxMinC = 0; boxMinR = 0; boxMaxC = cols - 1; boxMaxR = rows - 1;
            costBuilt = false;
        }

        // The cost field only depends on NavGrid's blocked cells, which don't move,
        // so it's worked out once (and again on a forced build, e.g. at Start).
        if (!costBuilt || force)
        {
            BuildCostField(cols, rows);
            costBuilt = true;
        }

        ClearBox(cols);

        grid.WorldToCell(goalWorldPos, out int gc, out int gr);
        gc = Mathf.Clamp(gc, 0, cols - 1);
        gr = Mathf.Clamp(gr, 0, rows - 1);

        int radiusCells = floodRadius > 0f ? Mathf.CeilToInt(floodRadius / grid.CellSize) : int.MaxValue;
        if (radiusCells == int.MaxValue)
        {
            boxMinC = 0; boxMinR = 0; boxMaxC = cols - 1; boxMaxR = rows - 1;
        }
        else
        {
            boxMinC = Mathf.Max(0, gc - radiusCells); boxMaxC = Mathf.Min(cols - 1, gc + radiusCells);
            boxMinR = Mathf.Max(0, gr - radiusCells); boxMaxR = Mathf.Min(rows - 1, gr + radiusCells);
        }

        BuildIntegrationField(cols, gc, gr, radiusCells);
        BuildDirectionField(cols, rows);
    }

    // Resets every cell the previous build touched back to "unreached, no direction".
    void ClearBox(int cols)
    {
        for (int r = boxMinR; r <= boxMaxR; r++)
        {
            for (int c = boxMinC; c <= boxMaxC; c++)
            {
                int i = r * cols + c;
                integration[i] = float.PositiveInfinity;
                dirX[i] = 0;
                dirZ[i] = 0;
            }
        }
    }

    // 1 for a plain open cell; higher right next to a blocked cell, so the flood
    // naturally prefers a route with breathing room over one that hugs a wall.
    void BuildCostField(int cols, int rows)
    {
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int i = r * cols + c;
                if (grid.IsBlocked(c, r)) { cost[i] = float.PositiveInfinity; continue; }

                bool nearWall = grid.IsBlocked(c - 1, r) || grid.IsBlocked(c + 1, r) ||
                                 grid.IsBlocked(c, r - 1) || grid.IsBlocked(c, r + 1);
                cost[i] = nearWall ? 1f + wallAdjacencyCost : 1f;
            }
        }
    }

    static readonly int[] Nx = { 1, -1, 0, 0, 1, 1, -1, -1 };
    static readonly int[] Nz = { 0, 0, 1, -1, 1, -1, 1, -1 };
    static readonly float[] NCost = { 1f, 1f, 1f, 1f, 1.41421356f, 1.41421356f, 1.41421356f, 1.41421356f }; // diagonal steps cost more

    // Dijkstra flood outward from the goal's cell - not a plain unweighted BFS,
    // since the cost field means some neighbours are cheaper to enter than others.
    // Stops at radiusCells (straight-line, in cells) from the goal; int.MaxValue floods everything.
    void BuildIntegrationField(int cols, int gc, int gr, int radiusCells)
    {
        int rows = grid.Rows;
        if (grid.IsBlocked(gc, gr)) return; // goal standing on a blocked cell - nothing to flood from

        int start = gr * cols + gc;
        integration[start] = 0f;

        long radiusSq = radiusCells == int.MaxValue ? long.MaxValue : (long)radiusCells * radiusCells;
        var frontier = new PriorityQueue((boxMaxC - boxMinC + 1) * (boxMaxR - boxMinR + 1));
        frontier.Push(start, 0f);

        while (frontier.Count > 0)
        {
            frontier.Pop(out int i, out float d);
            if (d > integration[i]) continue; // a cheaper path to this cell was already processed

            int c = i % cols, r = i / cols;
            for (int n = 0; n < 8; n++)
            {
                int nc = c + Nx[n], nr = r + Nz[n];
                if (nc < 0 || nc >= cols || nr < 0 || nr >= rows) continue;
                if (grid.IsBlocked(nc, nr)) continue;
                long dc = nc - gc, dr = nr - gr;
                if (dc * dc + dr * dr > radiusSq) continue; // outside the flood radius

                int ni = nr * cols + nc;
                float nd = d + NCost[n] * cost[ni];
                if (nd < integration[ni])
                {
                    integration[ni] = nd;
                    frontier.Push(ni, nd);
                }
            }
        }
    }

    // For every reachable cell in this build's box, point toward whichever neighbour
    // has the lowest integration value - the actual "signpost" every enemy will read from.
    void BuildDirectionField(int cols, int rows)
    {
        for (int r = boxMinR; r <= boxMaxR; r++)
        {
            for (int c = boxMinC; c <= boxMaxC; c++)
            {
                int i = r * cols + c;
                if (float.IsPositiveInfinity(integration[i])) { dirX[i] = 0; dirZ[i] = 0; continue; }

                float best = integration[i];
                int bestX = 0, bestZ = 0;
                for (int n = 0; n < 8; n++)
                {
                    int nc = c + Nx[n], nr = r + Nz[n];
                    if (nc < 0 || nc >= cols || nr < 0 || nr >= rows) continue;
                    int ni = nr * cols + nc;
                    if (integration[ni] < best) { best = integration[ni]; bestX = Nx[n]; bestZ = Nz[n]; }
                }
                dirX[i] = (sbyte)bestX;
                dirZ[i] = (sbyte)bestZ;
            }
        }
    }

    // ---- public query API - unused today, this is what ChaseState reads from in Phase 6 ----

    public bool HasDirection => built;

    // Flat (XZ) unit vector to steer toward from worldPos, or Vector3.zero if
    // unreachable or off the grid entirely.
    public Vector3 SampleDirection(Vector3 worldPos)
    {
        if (!built || dirX == null) return Vector3.zero;
        grid.WorldToCell(worldPos, out int c, out int r);
        if (c < 0 || c >= grid.Cols || r < 0 || r >= grid.Rows) return Vector3.zero;

        int i = r * grid.Cols + c;
        if (dirX[i] == 0 && dirZ[i] == 0) return Vector3.zero;
        return new Vector3(dirX[i], 0f, dirZ[i]).normalized;
    }

    void OnDrawGizmos()
    {
        if (!drawArrows || !built || grid == null) return;

        Gizmos.color = arrowColor;
        for (int r = 0; r < grid.Rows; r += gizmoStride)
        {
            for (int c = 0; c < grid.Cols; c += gizmoStride)
            {
                int i = r * grid.Cols + c;
                if (dirX[i] == 0 && dirZ[i] == 0) continue; // blocked or unreachable - nothing to draw

                Vector3 origin = grid.CellToWorldCenter(c, r) + Vector3.up * 0.15f;
                Vector3 dir = new Vector3(dirX[i], 0f, dirZ[i]).normalized * (grid.CellSize * 0.4f);
                Vector3 tip = origin + dir;

                Gizmos.DrawLine(origin, tip);
                Vector3 back = -dir.normalized * (grid.CellSize * 0.12f);
                Vector3 side = Vector3.Cross(dir.normalized, Vector3.up) * (grid.CellSize * 0.08f);
                Gizmos.DrawLine(tip, tip + back + side);
                Gizmos.DrawLine(tip, tip + back - side);
            }
        }
    }

    // Tiny binary-heap priority queue - avoids pulling in a package for one file.
    // Not exposed outside this class; nothing else needs it.
    sealed class PriorityQueue
    {
        readonly List<(int i, float d)> heap;
        public int Count => heap.Count;

        public PriorityQueue(int capacityHint) => heap = new List<(int, float)>(capacityHint);

        public void Push(int i, float d)
        {
            heap.Add((i, d));
            int c = heap.Count - 1;
            while (c > 0)
            {
                int p = (c - 1) / 2;
                if (heap[p].d <= heap[c].d) break;
                (heap[p], heap[c]) = (heap[c], heap[p]);
                c = p;
            }
        }

        public void Pop(out int i, out float d)
        {
            (i, d) = heap[0];
            int last = heap.Count - 1;
            heap[0] = heap[last];
            heap.RemoveAt(last);

            int p = 0;
            while (true)
            {
                int l = p * 2 + 1, r = p * 2 + 2, smallest = p;
                if (l < heap.Count && heap[l].d < heap[smallest].d) smallest = l;
                if (r < heap.Count && heap[r].d < heap[smallest].d) smallest = r;
                if (smallest == p) break;
                (heap[p], heap[smallest]) = (heap[smallest], heap[p]);
                p = smallest;
            }
        }
    }
}