using System;
using UnityEngine;

public enum KOTH_ZoneState { Idle, Capturing, Blocked, Complete }

// King of the Hill rules as a plain class (no Unity types), so they port to the custom engine as-is.
// Progress fills while the player is inside alone, pauses when they step out, and freezes while an
// enemy is also inside. Once complete it stays complete.
public sealed class KOTH_HoldZoneRules
{
    public float CaptureSeconds { get; }
    public float Elapsed { get; private set; }
    public KOTH_ZoneState State { get; private set; } = KOTH_ZoneState.Idle;
    public float Progress => Math.Min(1f, Elapsed / CaptureSeconds);

    public KOTH_HoldZoneRules(float captureSeconds) => CaptureSeconds = Math.Max(0.1f, captureSeconds);

    // Advances one frame. Returns true when the state changed.
    public bool Tick(float deltaTime, bool playerInside, bool enemyInside)
    {
        if (State == KOTH_ZoneState.Complete) return false;

        KOTH_ZoneState next = !playerInside ? KOTH_ZoneState.Idle
            : enemyInside ? KOTH_ZoneState.Blocked
            : KOTH_ZoneState.Capturing;

        if (next == KOTH_ZoneState.Capturing)
        {
            Elapsed = Math.Min(CaptureSeconds, Elapsed + Math.Max(0f, deltaTime));
            if (Elapsed >= CaptureSeconds) next = KOTH_ZoneState.Complete;
        }

        bool changed = next != State;
        State = next;
        return changed;
    }
}

// Measures who is inside the zone each frame and feeds it to the rules. Finds the player by the
// "Player" tag and enemies through EnemyAgent's list, so it needs no colliders or scene wiring.
// Inside means horizontal distance only (the map is flat for now). Visuals listen to the events.
[DisallowMultipleComponent]
public sealed class KOTH_HoldZone : MonoBehaviour
{
    [Tooltip("Seconds the player must stand in the zone, with no enemy inside, to capture it.")]
    [SerializeField, Min(0.1f)] float captureSeconds = 30f;
    [Tooltip("Zone radius in golem lengths.")]
    [SerializeField, Min(0.1f)] float radiusInGolems = 3f;
    [Tooltip("Gives metres per golem. Left empty, 4 metres per golem is used, as in MechCombat.")]
    [SerializeField] MechRulesConfig rulesConfig;

    KOTH_HoldZoneRules rules;
    Transform player;
    float nextPlayerSearch;

    public KOTH_ZoneState State => rules != null ? rules.State : KOTH_ZoneState.Idle;
    public float Progress => rules != null ? rules.Progress : 0f;
    public bool IsComplete => State == KOTH_ZoneState.Complete;
    public float RadiusMetres => radiusInGolems * MetresPerGolem;
    float MetresPerGolem => rulesConfig != null ? rulesConfig.metresPerGolem : 4f;

    public event Action<KOTH_ZoneState> StateChanged;
    public event Action Completed;

    void Awake() => rules = new KOTH_HoldZoneRules(captureSeconds);

    void Update()
    {
        if (rules.State == KOTH_ZoneState.Complete) return;
        FindPlayerIfNeeded();

        bool playerInside = player != null && player.gameObject.activeInHierarchy && Inside(player.position);
        bool enemyInside = playerInside && AnyEnemyInside();

        if (!rules.Tick(Time.deltaTime, playerInside, enemyInside)) return;
        StateChanged?.Invoke(rules.State);
        if (rules.State == KOTH_ZoneState.Complete) Completed?.Invoke();
    }

    void FindPlayerIfNeeded()
    {
        if (player != null || Time.unscaledTime < nextPlayerSearch) return;
        nextPlayerSearch = Time.unscaledTime + 0.5f;
        var found = GameObject.FindWithTag("Player");
        if (found != null) player = found.transform;
    }

    bool AnyEnemyInside()
    {
        foreach (var enemy in EnemyAgent.All)
            if (enemy != null && Inside(enemy.Position)) return true;
        return false;
    }

    bool Inside(Vector3 position)
    {
        Vector3 offset = position - transform.position;
        offset.y = 0f;
        float radius = RadiusMetres;
        return offset.sqrMagnitude <= radius * radius;
    }

    // Always drawn, so the zone can be placed without selecting it.
    void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        const int segments = 48;
        float radius = RadiusMetres;
        Vector3 centre = transform.position;
        Vector3 previous = centre + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 next = centre + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }
}
