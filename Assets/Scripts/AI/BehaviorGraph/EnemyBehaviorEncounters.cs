using System.Collections.Generic;
using UnityEngine;

public sealed partial class EnemyBehaviorPlaytest
{
    [Header("M5 encounter presets")]
    [SerializeField] EnemyEncounterPreset[] encounterPresets;
    [SerializeField, Min(0)] int selectedEncounter;
    [Tooltip("Controlled preset actors do not drop loot. Does not edit enemy prefabs or production waves.")]
    [SerializeField] bool disablePresetDrops = true;
    [SerializeField] bool drawPresetSpawns = true;
    readonly List<EnemyEncounterSpawn> presetPlan = new List<EnemyEncounterSpawn>();
    readonly List<EnemyEncounterSpawn> replayPlan = new List<EnemyEncounterSpawn>();
    readonly List<string> spawnIssues = new List<string>();
    readonly List<float> plannedRadii = new List<float>();
    readonly RaycastHit[] spawnGroundHits = new RaycastHit[32];
    readonly Collider[] spawnOverlaps = new Collider[64];
    Vector3 replayOrigin;
    Quaternion replayRotation;
    string replayLabel;
    long replayRequested;
    int replaySeed;
    public IReadOnlyList<EnemyEncounterPreset> EncounterPresets => encounterPresets;
    public EnemyEncounterPreset SelectedEncounter => encounterPresets != null && selectedEncounter >= 0
        && selectedEncounter < encounterPresets.Length ? encounterPresets[selectedEncounter] : null;
    public int SelectedEncounterIndex => selectedEncounter;
    public string LastEncounterReport { get; private set; } = "No M5 preset started.";
    public IReadOnlyList<string> SpawnIssues => spawnIssues;
    public bool CanReplayEncounter => replayLabel != null;
    public void SelectEncounter(int index)
    {
        if (encounterPresets == null || encounterPresets.Length == 0) return;
        selectedEncounter = ((index % encounterPresets.Length) + encounterPresets.Length) % encounterPresets.Length;
    }
    public void CycleEncounter(int direction)
    {
        SelectEncounter(selectedEncounter + direction);
        if (SelectedEncounter != null) Debug.Log("Selected encounter: " + SelectedEncounter.DisplayName + ". F11 starts it; F12 replays the last preset.", this);
    }
    [ContextMenu("M5/Start Selected Preset")]
    public void StartSelectedEncounter() => StartPreset(SelectedEncounter);
    public void StartPreset(EnemyEncounterPreset preset)
    {
        if (!Application.isPlaying || preset == null || player == null || !player.gameObject.activeInHierarchy) return;
        preset.BuildSpawnPlan(presetPlan);
        replayPlan.Clear(); replayPlan.AddRange(presetPlan); replayLabel = preset.DisplayName;
        replayRequested = preset.RequestedCount; replaySeed = preset.Seed;
        replayOrigin = resetPlayerPosition ? playerStart : player.position;
        replayRotation = resetPlayerPosition ? playerStartRotation : player.rotation;
        RunPresetPlan(false);
    }
    [ContextMenu("M5/Replay Last Preset")]
    public void ReplayLastEncounter()
    {
        if (!Application.isPlaying || !CanReplayEncounter || player == null || !player.gameObject.activeInHierarchy) return;
        RunPresetPlan(true);
    }
    void RunPresetPlan(bool replay)
    {
        ClearEnemies();
        if (resetPlayerPosition || replay)
        {
            var capsule = player.GetComponent<CharacterController>(); bool enabled = capsule != null && capsule.enabled;
            if (enabled) capsule.enabled = false;
            player.SetPositionAndRotation(replayOrigin, replayRotation);
            if (enabled) capsule.enabled = true;
        }
        Physics.SyncTransforms();
        coordinator.MeleeStyle = EnemyMeleeStyle.ContinuousPressure; coordinator.UseAttackPermissions = false;
        SetSwarmCoordination(true); comparisonRunning = true; swarmSpawnPositions.Clear(); plannedRadii.Clear(); spawnIssues.Clear();
        if (replayRequested > EnemyEncounterPreset.MaximumActors)
            spawnIssues.Add("Requested " + replayRequested + " actors; this test tool limits a preset to " + EnemyEncounterPreset.MaximumActors + ".");
        var grid = FindAnyObjectByType<NavGrid>();
        for (int i = 0; i < replayPlan.Count; i++)
        {
            var spawn = replayPlan[i]; int role = (int)spawn.role;
            if (roster == null || role < 0 || role >= roster.Length || roster[role] == null)
            { spawnIssues.Add("Spawn " + (i + 1) + ": missing roster prefab for " + spawn.role + "."); continue; }
            var prefabAgent = roster[role].GetComponent<EnemyAgent>();
            var prefabBrain = roster[role].GetComponent<EnemyGraphController>();
            if (prefabAgent == null || prefabBrain == null || prefabBrain.Role != spawn.role)
            { spawnIssues.Add("Spawn " + (i + 1) + ": prefab role/agent does not match " + spawn.role + "."); continue; }
            Vector3 point = replayOrigin + replayRotation * new Vector3(spawn.offset.x, 0, spawn.offset.y);
            if (!ValidPresetSpawn(point, prefabAgent, grid, out point, out var reason))
            { spawnIssues.Add("Spawn " + (i + 1) + " (" + spawn.role + "): " + reason); continue; }
            var heading = Steering.Flat(replayOrigin - point);
            var actor = Instantiate(roster[role], point, Quaternion.LookRotation(heading));
            actor.GetComponent<EnemyAgent>().SetSpawnGroundFallback(point.y - prefabAgent.PivotHeight);
            actor.name = replayLabel + " / " + spawn.role + " " + (i + 1);
            if (spawn.role <= EnemyBehaviorRole.ThrowingAxe)
            {
                if (!actor.TryGetComponent<EnemyCoordinationMember>(out var member)) member = actor.AddComponent<EnemyCoordinationMember>();
                member.Bind(coordinator);
            }
            actor.GetComponent<EnemyGraphController>().BindSpecialCoordinator(specialCoordinator);
            if (disablePresetDrops && actor.TryGetComponent<EnemyPartDrop>(out var drops)) drops.enabled = false;
            swarmActors.Add(actor); swarmSpawnPositions.Add(point); plannedRadii.Add(prefabAgent.BodyRadius);
        }
        LastEncounterReport = replayLabel + ": " + swarmSpawnPositions.Count + "/" + replayRequested + " spawned; seed " + replaySeed
            + (spawnIssues.Count > 0 ? "; " + spawnIssues.Count + " placement/configuration issue(s)." : ".");
        if (spawnIssues.Count > 0) Debug.LogWarning(LastEncounterReport + " See the arena Inspector for skipped-spawn details.", this);
        else Debug.Log(LastEncounterReport, this);
    }
    bool ValidPresetSpawn(Vector3 candidate, EnemyAgent agent, NavGrid grid, out Vector3 point, out string reason)
    {
        point = default; reason = "";
        if (!float.IsFinite(candidate.x) || !float.IsFinite(candidate.z)) { reason = "invalid coordinates."; return false; }
        if (grid == null || !grid.Baked) { reason = "navigation grid is not baked."; return false; }
        if (grid.IsBlockedWorld(candidate) || NavTerrain.IsWall(candidate)) { reason = "blocked or outside the navigation grid."; return false; }
        var body = player.GetComponent<Collider>();
        float feet = body != null ? body.bounds.min.y : replayOrigin.y - 1.6f;
        float ground = EnemyAgent.SpawnGroundY(candidate, feet);
        int count = Physics.RaycastNonAlloc(new Vector3(candidate.x, ground + 4, candidate.z), Vector3.down,
            spawnGroundHits, 8, ~0, QueryTriggerInteraction.Ignore);
        if (count == spawnGroundHits.Length) { reason = "ground query is crowded."; return false; }
        float nearest = float.PositiveInfinity; bool found = false;
        for (int i = 0; i < count; i++)
        {
            var hit = spawnGroundHits[i];
            if (hit.collider.GetComponentInParent<CombatHealth>() != null || hit.collider.GetComponentInParent<MechProjectile>() != null
                || hit.normal.y < .7f || hit.distance >= nearest) continue;
            point = hit.point + Vector3.up * agent.PivotHeight; nearest = hit.distance; found = true;
        }
        if (!found) { reason = "no supported ground."; return false; }
        float playerRadius = player.TryGetComponent<CharacterController>(out var capsule) ? capsule.radius : .6f;
        if (Steering.Flat(point - replayOrigin).sqrMagnitude < Mathf.Pow(playerRadius + agent.BodyRadius + .25f, 2))
        { reason = "overlaps the player start."; return false; }
        for (int i = 0; i < swarmSpawnPositions.Count; i++)
            if (Steering.Flat(point - swarmSpawnPositions[i]).sqrMagnitude < Mathf.Pow(plannedRadii[i] + agent.BodyRadius + .25f, 2))
            { reason = "overlaps another planned enemy."; return false; }
        count = Physics.OverlapSphereNonAlloc(point + Vector3.up * agent.ProbeHeight, agent.BodyRadius, spawnOverlaps, ~0, QueryTriggerInteraction.Ignore);
        if (count == spawnOverlaps.Length) { reason = "body query is crowded."; return false; }
        for (int i = 0; i < count; i++)
            if (spawnOverlaps[i].GetComponentInParent<CombatHealth>() == null && spawnOverlaps[i].GetComponentInParent<MechProjectile>() == null)
            { reason = "body clearance is blocked."; return false; }
        return true;
    }
    void OnDrawGizmosSelected()
    {
        if (!drawPresetSpawns || player == null || SelectedEncounter == null) return;
        SelectedEncounter.BuildSpawnPlan(presetPlan);
        Vector3 origin = Application.isPlaying && resetPlayerPosition ? playerStart : player.position;
        Quaternion rotation = Application.isPlaying && resetPlayerPosition ? playerStartRotation : player.rotation;
        Gizmos.color = new Color(.3f, 1, .65f);
        foreach (var spawn in presetPlan)
        {
            Vector3 point = origin + rotation * new Vector3(spawn.offset.x, 0, spawn.offset.y);
            point.y = EnemyAgent.SpawnGroundY(point, origin.y - 1.6f) + .1f;
            Gizmos.DrawWireSphere(point, .6f);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(point + Vector3.up, spawn.role.ToString());
#endif
        }
    }
}
