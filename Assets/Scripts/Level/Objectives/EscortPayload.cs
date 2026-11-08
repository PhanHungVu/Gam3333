using System;
using UnityEngine;

// The thing you escort: a floating object that runs the generated escort route from the first checkpoint to
// the last (EscortRules decide when it moves). It faces its direction of travel, draws the route ahead to the
// next checkpoint, and shows its push radius as a ring coloured by state. Its Body (a child with CombatHealth,
// on the player's team) can be destroyed: after Respawn Delay it comes back at the last checkpoint reached.
// Once first pushed it runs its own wave (Escort Wave) from a ring around it; those enemies go for the payload,
// following its own flow field, and each checkpoint passed makes the wave bigger. Reaching the last checkpoint
// completes the escort (a mission objective). Structure: this root (always on) with a child Body that has the
// collider, renderer and CombatHealth. The generator places it at the first checkpoint.
[DisallowMultipleComponent]
public sealed class EscortPayload : MonoBehaviour, IMissionObjective, IMissionStatus, IWaveAnchor
{
    [Tooltip("Metres per second while the player pushes it.")]
    [SerializeField, Min(0f)] float speed = 3f;
    [Tooltip("Metres per second it rolls back when only enemies are near.")]
    [SerializeField, Min(0f)] float reverseSpeed = 1.5f;
    [Tooltip("The player (or enemies) must be within this many metres to move or stop it.")]
    [SerializeField, Min(0.5f)] float radius = 12f;
    [Min(1f)] [SerializeField] float maxHealth = 500f;
    [Tooltip("While it moves with no enemies near (green ring), it heals this % of its max health per second. 0 = off.")]
    [SerializeField, Min(0f)] float advancingHeal = 2f;
    [Tooltip("Seconds after being destroyed before it returns at the last checkpoint.")]
    [SerializeField, Min(0f)] float respawnDelay = 5f;
    [Tooltip("How high it floats above the route.")]
    [SerializeField] float hoverHeight = 2.5f;
    [Tooltip("The guide line drawn from it along the route to the next checkpoint.")]
    [SerializeField] Color guideColor = new Color(1f, 0.85f, 0.1f);

    [Header("Push radius ring")]
    [SerializeField] Color idleColor = new Color(1f, 0.85f, 0.1f);
    [SerializeField] Color advancingColor = new Color(0.3f, 0.9f, 0.35f);
    [SerializeField] Color contestedColor = new Color(1f, 0.55f, 0.1f);
    [SerializeField] Color retreatingColor = new Color(1f, 0.2f, 0.15f);

    [Header("Escort wave")]
    [Tooltip("The wave its enemies come from once the escort starts (e.g. WaveData_Escort). Its enemies go for the payload.")]
    [SerializeField] WaveData escortWave;
    [Tooltip("Escort enemies appear on a ring this many metres from the payload.")]
    [SerializeField, Min(1f)] float spawnRingRadius = 40f;
    [Tooltip("How far the payload's own flow field reaches. Keep it larger than the spawn ring so every escort enemy finds its way.")]
    [SerializeField, Min(1f)] float flowFieldRadius = 60f;
    [Tooltip("How much bigger the escort wave gets for each checkpoint passed (0.25 = 25% more each time).")]
    [SerializeField, Min(0f)] float waveIncreasePerCheckpoint = 0.25f;

    [Header("Debug (read-only)")]
    [SerializeField] EscortState state;
    [SerializeField] float distance;
    [SerializeField] int checkpointsPassed;
    [SerializeField] float respawnIn;

    EscortRoute route;
    Transform space;     // the generator: the route's points are in its space
    EscortRules rules;
    CombatHealth body;
    LineRenderer guide, ring;
    Transform player;
    float respawnAt = -1f;
    bool waveStarted;
    ChunkRingSpawner spawnRing;
    SpawnDirector director;

    public EscortRules Rules => rules;
    public bool IsComplete => rules != null && rules.Arrived;
    public CombatHealth Body => body;
    // Its escort wave stays near it: enemies beyond the wave's Leash Distance are removed and replaced.
    public Vector3 WaveAnchor => transform.position;
    public float Radius => radius;
    public event Action<int> CheckpointReached; // the number of checkpoints passed so far

    public bool ShowStatus => rules != null && !rules.Arrived;
    public string Status
    {
        get
        {
            if (rules == null) return "Escort";
            if (respawnAt >= 0f) return $"Payload destroyed: back at the last checkpoint in {Mathf.Max(0f, respawnAt - Time.time):0.0}s";
            if (!waveStarted && rules.State == EscortState.Idle) return "Reach the payload at the first checkpoint";
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

    // Called by the generator once the route exists. lastIsExtract: the route's last chunk is the end
    // (extraction) chunk, not a checkpoint. The escort runs from the first checkpoint to the last.
    public void Configure(EscortRoute escortRoute, Transform routeSpace, bool lastIsExtract)
    {
        route = escortRoute; space = routeSpace;
        var points = route.CheckpointDistances;
        int count = points.Count - (lastIsExtract ? 1 : 0);
        if (count < 2) Debug.LogWarning("EscortPayload: the escort needs at least 2 checkpoints (a start and an end).", this);
        float start = count > 0 ? points[0] : 0f, end = count > 0 ? points[count - 1] : 0f;
        var between = new System.Collections.Generic.List<float>();
        for (int i = 1; i < count; i++) between.Add(points[i]);
        rules = new EscortRules(start, end, between);
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
        ring = new GameObject("Push Radius").AddComponent<LineRenderer>();
        ring.transform.SetParent(transform, false);
        ring.useWorldSpace = true;
        ring.loop = true;
        ring.widthMultiplier = 0.25f;
        ring.material = guide.material;
        ring.positionCount = 64;

        // The payload's own flow field, made while switched off so it wakes up already pointing here.
        var fieldObject = new GameObject("Flow Field");
        fieldObject.SetActive(false);
        fieldObject.transform.SetParent(transform, false);
        fieldObject.AddComponent<FlowField>().Configure(transform, flowFieldRadius, 2f, 0.2f, false);
        fieldObject.SetActive(true);

        spawnRing = new GameObject("Spawn Ring").AddComponent<ChunkRingSpawner>();
        spawnRing.transform.SetParent(transform, false);
        spawnRing.Configure(spawnRingRadius);
    }

    void OnEnable() => MissionObjectives.Register(this);
    void OnDisable()
    {
        MissionObjectives.Unregister(this);
        StopWave();
    }
    void OnDestroy() { if (body != null) body.Died -= OnDestroyed; }

    // The escort wave starts the first time the payload is pushed, and stops when it arrives.
    void StartWave()
    {
        waveStarted = true;
        director = SpawnDirector.Instance;
        if (escortWave == null || director == null) { Debug.LogWarning("EscortPayload: no Escort Wave or no Spawn Director, so no escort enemies come.", this); return; }
        director.Spawned += OnSpawned;
        director.RunWave(escortWave, new SpawnPoint[] { spawnRing }, this);
    }

    void StopWave()
    {
        if (director == null) return;
        director.Spawned -= OnSpawned;
        director.StopWave(this);
        director = null;
    }

    void OnSpawned(UnityEngine.Object owner, GameObject enemy)
    {
        if (owner != this || body == null) return;
        var agent = enemy.GetComponent<EnemyAgent>();
        if (agent != null) agent.PreferredTarget = body.transform;
    }

    void OnDestroyed() => respawnAt = Time.time + respawnDelay;

    void Update()
    {
        if (rules == null || body == null) return;
        if (respawnAt >= 0f)
        {
            if (Time.time >= respawnAt) Respawn();
            else { respawnIn = respawnAt - Time.time; guide.enabled = false; ring.enabled = false; return; }
        }
        bool playerNear = PlayerNear(), enemiesNear = EnemiesNear();
        int passed = rules.Step(Time.deltaTime, playerNear, enemiesNear, speed, reverseSpeed);
        if (!waveStarted && rules.State == EscortState.Advancing) StartWave();
        if (passed > 0)
        {
            CheckpointReached?.Invoke(rules.CheckpointsPassed);
            if (director != null) director.ScaleWave(this, 1f + waveIncreasePerCheckpoint * rules.CheckpointsPassed);
        }
        if (rules.Arrived) StopWave();
        float heal = AdvancingHeal(rules.State, body.MaximumHealth, advancingHeal, Time.deltaTime);
        if (heal > 0f) body.Heal(heal);
        Place();
        DrawGuide();
        DrawRing();
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

    // How much the payload heals this frame: only while advancing (green ring: player near, no enemies).
    public static float AdvancingHeal(EscortState state, float maximumHealth, float percentPerSecond, float deltaTime) =>
        state == EscortState.Advancing ? Mathf.Max(0f, maximumHealth) * Mathf.Max(0f, percentPerSecond) / 100f * Mathf.Max(0f, deltaTime) : 0f;

    // The push radius on the ground around the payload, coloured by what it's doing.
    void DrawRing()
    {
        ring.enabled = !rules.Arrived;
        if (!ring.enabled) return;
        Color color = rules.State switch
        {
            EscortState.Advancing => advancingColor,
            EscortState.Contested => contestedColor,
            EscortState.Retreating => retreatingColor,
            _ => idleColor,
        };
        ring.startColor = ring.endColor = color;
        Vector3 centre = transform.position - Vector3.up * (hoverHeight - 0.3f);
        for (int i = 0; i < ring.positionCount; i++)
        {
            float a = i * Mathf.PI * 2f / ring.positionCount;
            ring.SetPosition(i, centre + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
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
