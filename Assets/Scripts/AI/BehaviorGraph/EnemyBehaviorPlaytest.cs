using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Arena controls via Inspector or Input System shortcuts; no combat HUD.
public sealed partial class EnemyBehaviorPlaytest : MonoBehaviour
{
    [SerializeField] Transform player;
    [SerializeField] SpawnDirector director;
    [SerializeField] RoamingSpawner spawnPoint;
    [SerializeField] WaveData roaming, survival;
    [SerializeField] GameObject[] roster;
    [SerializeField] EnemyBehaviorRole selectedRole;
    [SerializeField, Min(3)] float spawnDistance = 16;
    [Header("Sword horde pressure test")]
    [SerializeField] EnemyHordeCoordinator coordinator;
    [SerializeField] EnemySpecialCoordinator specialCoordinator;
    [SerializeField, Range(1, 24)] int swarmCount = 10;
    [SerializeField] int encounterSeed = 4711;
    [SerializeField, Range(30, 360)] float spawnArc = 210;
    [Tooltip("Returns the player to the pose captured when Play Mode began. Player stats and movement tuning are unchanged.")]
    [SerializeField] bool resetPlayerPosition = true;
    [SerializeField] bool useCoordination = true;
    [Header("Arena keyboard shortcuts")]
    [Tooltip("F5–F10 retain their tests. Page Up/Down selects a preset; F11 starts it; F12 replays it. Home compares melee movement; End logs combat state. Click the Game view first.")]
    [SerializeField] bool enableKeyboardShortcuts = true;
    readonly List<Vector3> swarmSpawnPositions = new List<Vector3>();
    readonly List<GameObject> swarmActors = new List<GameObject>();
    Vector3 playerStart;
    Quaternion playerStartRotation;
    bool comparisonRunning, appliedCoordination;
    public IReadOnlyList<Vector3> SwarmSpawnPositions => swarmSpawnPositions;
    public EnemyHordeCoordinator Coordinator => coordinator;
    public EnemySpecialCoordinator SpecialCoordinator => specialCoordinator;
    public bool ComparisonRunning => comparisonRunning;
    void Awake()
    {
        if (player != null) { playerStart = player.position; playerStartRotation = player.rotation; }
        if (coordinator == null) coordinator = GetComponent<EnemyHordeCoordinator>();
        if (coordinator == null) coordinator = gameObject.AddComponent<EnemyHordeCoordinator>();
        if (specialCoordinator == null) specialCoordinator = GetComponent<EnemySpecialCoordinator>();
        if (specialCoordinator == null) specialCoordinator = gameObject.AddComponent<EnemySpecialCoordinator>();
    }
    void Update()
    {
        if (comparisonRunning && useCoordination != appliedCoordination) SetSwarmCoordination(useCoordination);
        var keyboard = Keyboard.current;
        if (!enableKeyboardShortcuts || keyboard == null) return;
        if (keyboard.f9Key.wasPressedThisFrame) ClearEnemies();
        else if (keyboard.homeKey.wasPressedThisFrame) TogglePressureMovement();
        else if (keyboard.endKey.wasPressedThisFrame) CaptureCombatReview();
        else if (keyboard.f12Key.wasPressedThisFrame) ReplayLastEncounter();
        else if (keyboard.f11Key.wasPressedThisFrame) StartSelectedEncounter();
        else if (keyboard.pageDownKey.wasPressedThisFrame) CycleEncounter(1);
        else if (keyboard.pageUpKey.wasPressedThisFrame) CycleEncounter(-1);
        else if (keyboard.f10Key.wasPressedThisFrame) StartSpecialEncounter();
        else if (keyboard.f8Key.wasPressedThisFrame) StartMixedEncounter();
        else if (keyboard.f7Key.wasPressedThisFrame) StartRangedEncounter();
        else if (keyboard.f6Key.wasPressedThisFrame) StartPressureSwarm();
        else if (keyboard.f5Key.wasPressedThisFrame) SpawnSelected();
    }
    public void SetSwarmCoordination(bool enabled)
    {
        useCoordination = appliedCoordination = enabled;
        if (coordinator != null) coordinator.enabled = enabled;
    }
    [ContextMenu("M1/Start Baseline Sword Swarm")] public void StartBaselineSwarm() => StartSwordSwarm(false);
    [ContextMenu("M1/Start Positioning Only Sword Swarm")]
    public void StartPositioningSwarm()
    { if (!Application.isPlaying || coordinator == null) return; coordinator.MeleeStyle = EnemyMeleeStyle.ReservedPositions; coordinator.UseAttackPermissions = false; StartSwordSwarm(true); }
    [ContextMenu("M2/Start Coordinated Sword Swarm")]
    public void StartCoordinatedSwarm()
    { if (!Application.isPlaying || coordinator == null) return; coordinator.MeleeStyle = EnemyMeleeStyle.ReservedPositions; coordinator.UseAttackPermissions = true; StartSwordSwarm(true); }
    [ContextMenu("Start Pressure Sword Swarm")]
    public void StartPressureSwarm()
    { if (!Application.isPlaying || coordinator == null) return; coordinator.MeleeStyle = EnemyMeleeStyle.ContinuousPressure; coordinator.UseAttackPermissions = false; StartSwordSwarm(true); }
    public void StartSwordSwarm(bool coordinated)
        => StartEncounter(coordinated, Enumerable.Repeat(EnemyBehaviorRole.Sword, swarmCount).ToArray());

    [ContextMenu("M3/Start Ranged Positioning Encounter")]
    public void StartRangedEncounter() => StartEncounter(true, new[] { EnemyBehaviorRole.Crossbow, EnemyBehaviorRole.Crossbow,
        EnemyBehaviorRole.HeavyRanged, EnemyBehaviorRole.Crossbow, EnemyBehaviorRole.Crossbow });

    [ContextMenu("M3/Start Mixed Pressure Encounter")]
    public void StartMixedEncounter()
    {
        if (!Application.isPlaying || coordinator == null) return;
        coordinator.MeleeStyle = EnemyMeleeStyle.ContinuousPressure; coordinator.UseAttackPermissions = false;
        StartEncounter(true, new[] { EnemyBehaviorRole.Sword, EnemyBehaviorRole.Crossbow, EnemyBehaviorRole.Sword,
            EnemyBehaviorRole.Sword, EnemyBehaviorRole.Crossbow, EnemyBehaviorRole.HeavyRanged,
            EnemyBehaviorRole.Sword, EnemyBehaviorRole.Crossbow, EnemyBehaviorRole.Sword, EnemyBehaviorRole.Sword });
    }
    [ContextMenu("M4/Start Specials With Horde")]
    public void StartSpecialEncounter()
    {
        if (!Application.isPlaying || coordinator == null) return;
        coordinator.MeleeStyle = EnemyMeleeStyle.ContinuousPressure; coordinator.UseAttackPermissions = false;
        StartEncounter(true, new[] { EnemyBehaviorRole.Sword, EnemyBehaviorRole.Spinner, EnemyBehaviorRole.Sword,
            EnemyBehaviorRole.Crossbow, EnemyBehaviorRole.Sword, EnemyBehaviorRole.RockRopePull, EnemyBehaviorRole.Spinner,
            EnemyBehaviorRole.Sword, EnemyBehaviorRole.RockRope, EnemyBehaviorRole.Sword, EnemyBehaviorRole.Crossbow,
            EnemyBehaviorRole.Spinner, EnemyBehaviorRole.Sword });
    }
    void StartEncounter(bool coordinated, EnemyBehaviorRole[] roles)
    {
        if (!Application.isPlaying || player == null || !player.gameObject.activeInHierarchy || roster == null || roster.Length == 0 || roster[0] == null) return;
        ClearEnemies();
        if (resetPlayerPosition)
        {
            var capsule = player.GetComponent<CharacterController>(); bool wasEnabled = capsule != null && capsule.enabled;
            if (wasEnabled) capsule.enabled = false;
            player.SetPositionAndRotation(playerStart, playerStartRotation);
            if (wasEnabled) capsule.enabled = true;
        }
        SetSwarmCoordination(coordinated); comparisonRunning = true; swarmSpawnPositions.Clear();
        var random = new System.Random(encounterSeed); var grid = FindAnyObjectByType<NavGrid>();
        for (int i = 0; i < roles.Length; i++)
        {
            int role = (int)roles[i];
            if (role < 0 || role >= roster.Length || roster[role] == null) continue;
            float angle = (-spawnArc * 0.5f + spawnArc * (i + 0.5f) / roles.Length + (float)(random.NextDouble() - 0.5) * 3) * Mathf.Deg2Rad;
            float radius = spawnDistance + (float)(random.NextDouble() - 0.5) * 1.2f;
            Vector3 position = player.position + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * radius;
            position.y = EnemyAgent.SpawnGroundY(position, 0) + 1;
            if (grid != null && grid.Baked && grid.IsBlockedWorld(position))
            { Debug.LogWarning("Arena encounter skipped a blocked spawn. Adjust the seed, distance or arc.", this); continue; }
            var actor = Instantiate(roster[role], position, Quaternion.LookRotation(Steering.Flat(player.position - position)));
            actor.name = "Swarm " + roles[i] + " " + (i + 1);
            if (roles[i] <= EnemyBehaviorRole.ThrowingAxe) actor.AddComponent<EnemyCoordinationMember>().Bind(coordinator);
            actor.GetComponent<EnemyGraphController>().BindSpecialCoordinator(specialCoordinator);
            swarmActors.Add(actor);
            swarmSpawnPositions.Add(position);
        }
    }
    [ContextMenu("Spawn Selected Enemy")]
    public void SpawnSelected()
    {
        if (!Application.isPlaying || player == null || !player.gameObject.activeInHierarchy || roster == null
            || (int)selectedRole < 0 || (int)selectedRole >= roster.Length || roster[(int)selectedRole] == null) return;
        Vector3 position = player.position + player.forward * spawnDistance;
        position.y = EnemyAgent.SpawnGroundY(position, 0) + 1;
        var actor = Instantiate(roster[(int)selectedRole], position, Quaternion.LookRotation(Steering.Flat(player.position-position)));
        actor.GetComponent<EnemyGraphController>().BindSpecialCoordinator(specialCoordinator); swarmActors.Add(actor);
    }
    [ContextMenu("Start Roaming Wave")] public void StartRoaming() => StartWave(roaming);
    [ContextMenu("Start Survival Wave")] public void StartSurvival() => StartWave(survival);
    void StartWave(WaveData data) { if (Application.isPlaying && director != null) { director.StopWave(this); director.RunWave(data, new SpawnPoint[] { spawnPoint }, this); } }
    [ContextMenu("Stop Wave")] public void StopWave() { if (Application.isPlaying && director != null) director.StopWave(this); }
    [ContextMenu("Clear Arena Enemies")]
    public void ClearEnemies()
    {
        if (!Application.isPlaying) return;
        StopWave(); comparisonRunning = false;
        if (coordinator != null) coordinator.ResetPressureDiagnostics();
        foreach (var shot in FindObjectsByType<MechProjectile>())
            if (shot.gameObject.scene == gameObject.scene && shot.OwnerTeam == CombatTeam.Enemy)
            { shot.gameObject.SetActive(false); Destroy(shot.gameObject); }
        foreach (var actor in EnemyAgent.All.ToArray()) if (actor != null && actor.gameObject.scene == gameObject.scene)
        { actor.gameObject.SetActive(false); Destroy(actor.gameObject); }
        // Dead enemies are inactive and no longer in EnemyAgent.All.
        foreach (var actor in swarmActors) if (actor != null) Destroy(actor);
        swarmActors.Clear();
    }
}
