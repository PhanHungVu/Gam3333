using System.Collections.Generic;
using UnityEngine;

public enum ChunkShape { Box, Circle }

// A chunk's label sheet: what the level generator will need to place and connect it. Holds no
// gameplay; the objectives inside the chunk do that. Put it on the chunk prefab's root.
// Path connectors are the points on the chunk's edge where generated roads will attach.
[DisallowMultipleComponent]
public sealed class ChunkDefinition : MonoBehaviour
{
    const string ConnectorPrefix = "PathConnector";

    [Tooltip("Box: a rectangle Size X by Size Y. Circle: a circle Size X across (Size Y is ignored).")]
    [SerializeField] ChunkShape shape = ChunkShape.Box;
    [Tooltip("Footprint in golem lengths, centred on this object: X by Z for a box, X = diameter for a circle.")]
    [SerializeField] Vector2 sizeInGolems = new Vector2(10f, 10f);
    [Tooltip("Gives metres per golem. Left empty, 4 metres per golem is used, as in MechCombat.")]
    [SerializeField] MechRulesConfig rulesConfig;
    [Tooltip("Where roads join the chunk. Empty slots are ignored; with none assigned, children whose name starts with 'PathConnector' are used.")]
    [SerializeField] Transform[] pathConnectors;
    [Tooltip("Whether this chunk can serve as a sub-level's start or extraction area.")]
    [SerializeField] bool canBeStartOrEnd;
    [Tooltip("Roaming spawns pause while the player is inside this chunk. Start/end chunks never pause roaming.")]
    [SerializeField] bool pausesRoaming = true;

    [Header("Scene view only")]
    [Tooltip("Width of the lane drawn from each connector to the chunk centre - the space to keep clear of cover.")]
    [SerializeField, Min(0.1f)] float laneWidthInGolems = 2f;

    public Vector2 SizeInGolems => sizeInGolems;
    public Vector2 SizeMetres => IsCircle ? new Vector2(sizeInGolems.x, sizeInGolems.x) * MetresPerGolem : sizeInGolems * MetresPerGolem;
    public bool IsCircle => shape == ChunkShape.Circle;
    public float RadiusMetres => SizeMetres.x * 0.5f;
    public bool CanBeStartOrEnd => canBeStartOrEnd;
    public bool PausesRoaming => pausesRoaming && !canBeStartOrEnd;
    float MetresPerGolem => rulesConfig != null ? rulesConfig.metresPerGolem : 4f;

    public IReadOnlyList<Transform> PathConnectors
    {
        get
        {
            var found = new List<Transform>();
            if (pathConnectors != null)
                foreach (var connector in pathConnectors)
                    if (connector != null) found.Add(connector);
            if (found.Count > 0) return found;

            foreach (var child in GetComponentsInChildren<Transform>(true))
                if (child != transform && child.name.StartsWith(ConnectorPrefix)) found.Add(child);
            return found;
        }
    }

    void OnValidate() => sizeInGolems = Vector2.Max(sizeInGolems, Vector2.one);

    void OnEnable() { if (PausesRoaming) RoamingBlockers.Register(this, Contains); }
    void OnDisable() => RoamingBlockers.Unregister(this);

    // Inside the footprint (box or circle), in the chunk's own orientation. Height is ignored.
    public bool Contains(Vector3 position)
    {
        Vector3 local = transform.InverseTransformPoint(position);
        if (IsCircle) return local.x * local.x + local.z * local.z <= RadiusMetres * RadiusMetres;
        Vector2 half = SizeMetres * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.y;
    }

    // Footprint box, connector markers, and a lane from each connector to the centre.
    // Always drawn, so the chunk's bounds and lanes are visible while placing cover.
    void OnDrawGizmos()
    {
        Vector2 size = SizeMetres;
        Gizmos.color = Color.cyan;
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        if (IsCircle)
        {
            const int segments = 64;
            float r = RadiusMetres;
            Vector3 previous = new Vector3(r, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                Vector3 next = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
        else Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, 0.1f, size.y));
        Gizmos.matrix = Matrix4x4.identity;

        float halfLane = laneWidthInGolems * MetresPerGolem * 0.5f;
        Vector3 centre = transform.position;
        Gizmos.color = Color.magenta;
        foreach (var connector in PathConnectors)
        {
            Vector3 start = connector.position;
            start.y = centre.y + 0.1f;
            Vector3 end = new Vector3(centre.x, start.y, centre.z);

            Gizmos.DrawSphere(start, 0.5f);
            Gizmos.DrawLine(start, start + connector.forward * 2f);

            Vector3 along = end - start;
            if (along.sqrMagnitude < 0.01f) continue;
            Vector3 side = Vector3.Cross(Vector3.up, along.normalized) * halfLane;
            Gizmos.DrawLine(start, end);
            Gizmos.DrawLine(start + side, end + side);
            Gizmos.DrawLine(start - side, end - side);
            Gizmos.DrawLine(start + side, start - side);
        }
    }
}
