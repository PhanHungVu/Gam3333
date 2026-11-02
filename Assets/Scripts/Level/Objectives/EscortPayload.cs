using System;
using UnityEngine;

// The thing you escort: a floating object that follows the generated escort route (EscortRules decide when
// it moves). It faces its direction of travel and draws the route ahead to the next checkpoint as a guide.
// Its Body (a child with CombatHealth, on the player's team) can be destroyed: after Respawn Delay it comes
// back at the last checkpoint reached, at full health. Reaching the end completes the escort, so it counts as
// a mission objective. The generator places it at the start of the route. Structure: this root (always on)
// with a child Body that has the collider, renderer and CombatHealth.
[DisallowMultipleComponent]
public sealed class EscortPayload : MonoBehaviour, IMissionObjective, IMissionStatus
{
    [Tooltip("Metres per second while the player pushes it.")]
    [SerializeField, Min(0f)] float speed = 3f;
    [Tooltip("Metres per second it rolls back when only enemies are near.")]
    [SerializeField, Min(0f)] float reverseSpeed = 1.5f;
    [Tooltip("The player (or enemies) must be within this many metres to move or stop it.")]
    [SerializeField, Min(0.5f)] float radius = 12f;
    [Min(1f)] [SerializeField] float maxHealth = 500f;
    [Tooltip("Seconds after being destroyed before it returns at the last checkpoint.")]
    [SerializeField, Min(0f)] float respawnDelay = 5f;
    [Tooltip("How high it floats above the route.")]
    [SerializeField] float hoverHeight = 2.5f;
    [Tooltip("The guide line drawn from it along the route to the next checkpoint.")]
    [SerializeField] Color guideColor = new Color(1f, 0.85f, 0.1f);

    [Header("Debug (read-only)")]
    [SerializeField] EscortState state;
    [SerializeField] float distance;
    [SerializeField] int checkpointsPassed;
    [SerializeField] float respawnIn;

    EscortRoute route;
    Transform space;     // the generator: the route's points are in its space
    EscortRules rules;
    CombatHealth body;
    LineRenderer guide;
    Transform player;
    float respawnAt = -1f;

    public EscortRules Rules => rules;
    public bool IsComplete => rules != null && rules.Arrived;
    public CombatHealth Body => body;
    public float Radius => radius;
    public event Action<int> CheckpointReached; // the number of checkpoints passed so far

    public string Status
    {
        get
        {
            if (rules == null) return "Escort";
            if (respawnAt >= 0f) return $"Payload destroyed: back at the last checkpoint in {Mathf.Max(0f, respawnAt - Time.time):0.0}s";
            int total = rules.CheckpointCount;
            string where = $"Checkpoint {Mathf.Min(rules.CheckpointsPassed + 1, total)} / {total}";
            return rules.State switch
            {
                EscortState.Advancing => $"Escorting: {where}",
                EscortState.Contested => $"Payload contested: {where}",
                EscortState.Retreating => $"Payload being pushed back! {where}",
                _ => $"Stay near the payload to move it: {where}",
            };
        }
    }

    // Called by the generator once the route exists. Places the payload at the start.
    public void Configure(EscortRoute escortRoute, Transform routeSpace)
    {
        route = escortRoute; space = routeSpace;
        rules = new EscortRules(route.Length, route.CheckpointDistances);
        Place();
    }

    void Awake()
    {
        body = GetComponentInChildren<CombatHealth>(true);
        if (body == null) { Debug.LogWarning("EscortPayload needs a child Body with CombatHealth.", this); return; }
        body.Team = CombatTeam.Player;
        body.SetMaximum(maxHealth, refill: true);
        body.Died += OnDestroyed;
        guide = new GameObject("Route Guide").AddComponent<LineRenderer>();
        guide.transform.SetParent(transform, false);
        guide.useWorldSpace = true;
        guide.widthMultiplier = 0.4f;
        guide.material = new Material(Shader.Find("Sprites/Default"));
        guide.startColor = guide.endColor = guideColor;
    }

    void OnEnable() => MissionObjectives.Register(this);
    void OnDisable() => MissionObjectives.Unregister(this);
    void OnDestroy() { if (body != null) body.Died -= OnDestroyed; }

    void OnDestroyed() => respawnAt = Time.time + respawnDelay;

    void Update()
    {
        if (rules == null || body == null) return;
        if (respawnAt >= 0f)
        {
            if (Time.time >= respawnAt) Respawn();
            else { respawnIn = respawnAt - Time.time; guide.enabled = false; return; }
        }
        bool playerNear = PlayerNear(), enemiesNear = EnemiesNear();
        int passed = rules.Step(Time.deltaTime, playerNear, enemiesNear, speed, reverseSpeed);
        if (passed > 0) CheckpointReached?.Invoke(rules.CheckpointsPassed);
        Place();
        DrawGuide();
        state = rules.State; distance = rules.Distance; checkpointsPassed = rules.CheckpointsPassed; respawnIn = 0f;
    }

    void Respawn()
    {
        respawnAt = -1f;
        rules.ReturnToCheckpoint();
        body.gameObject.SetActive(true);
        body.SetMaximum(maxHealth, refill: true);
        Place();
    }

    void Place()
    {
        route.Sample(rules.Distance, out float x, out float y, out float z, out float dx, out float dz);
        transform.position = space.TransformPoint(new Vector3(x, y + hoverHeight, z));
        var forward = space.TransformDirection(new Vector3(dx, 0f, dz));
        if (forward.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
    }

    // The route from here to the next checkpoint, sampled every few metres.
    void DrawGuide()
    {
        guide.enabled = !rules.Arrived;
        if (!guide.enabled) return;
        float from = rules.Distance, to = rules.NextCheckpoint;
        int count = Mathf.Clamp(Mathf.CeilToInt((to - from) / 3f) + 1, 2, 400);
        guide.positionCount = count;
        for (int i = 0; i < count; i++)
        {
            route.Sample(Mathf.Lerp(from, to, i / (count - 1f)), out float x, out float y, out float z, out _, out _);
            guide.SetPosition(i, space.TransformPoint(new Vector3(x, y + 0.3f, z)));
        }
    }

    bool PlayerNear()
    {
        if (player == null) { var found = GameObject.FindWithTag("Player"); if (found != null) player = found.transform; }
        return player != null && player.gameObject.activeInHierarchy && FlatDistance(player.position) <= radius;
    }

    bool EnemiesNear()
    {
        foreach (var enemy in EnemyAgent.All)
            if (enemy != null && enemy.isActiveAndEnabled && FlatDistance(enemy.transform.position) <= radius) return true;
        return false;
    }

    float FlatDistance(Vector3 point)
    {
        var offset = point - transform.position; offset.y = 0f;
        return offset.magnitude;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = guideColor;
        const int segments = 48;
        Vector3 c = transform.position, previous = c + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            Vector3 next = c + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            Gizmos.DrawLine(previous, next); previous = next;
        }
    }
}
