using System;
using System.Collections.Generic;

public enum EscortState { Idle, Advancing, Contested, Retreating, Arrived }

// The escort payload's movement along its route as a plain class (no Unity types), so it ports as-is.
// Player in range, no enemies: advance. Player and enemies: stop. Enemies only: roll back, never past the
// last checkpoint reached. Nobody: stop. It starts at Start (the first checkpoint); passing a checkpoint makes
// it the floor (and the respawn point); the last checkpoint is the end, where the escort has arrived.
public sealed class EscortRules
{
    readonly float length;
    readonly List<float> checkpoints;

    public float Distance { get; private set; }
    public float Floor { get; private set; }            // the last checkpoint reached (respawn point)
    public int CheckpointsPassed { get; private set; }
    public int CheckpointCount => checkpoints.Count;
    public EscortState State { get; private set; } = EscortState.Idle;
    public bool Arrived => State == EscortState.Arrived;
    public float Length => length;

    // start and end are distances along the route; checkpoints are the ones after the start (the end included).
    public EscortRules(float start, float end, IEnumerable<float> checkpointDistances)
    {
        length = Math.Max(start, end);
        Distance = Floor = Math.Max(0f, start);
        checkpoints = new List<float>();
        foreach (float d in checkpointDistances) if (d > start + 0.0001f && d <= length + 0.0001f) checkpoints.Add(d);
        checkpoints.Sort();
        if (checkpoints.Count == 0 || checkpoints[checkpoints.Count - 1] < length - 0.0001f) checkpoints.Add(length);
    }

    // Distance to the next checkpoint (or the end), or the end itself when all are passed.
    public float NextCheckpoint => CheckpointsPassed < checkpoints.Count ? checkpoints[CheckpointsPassed] : length;

    // Returns how many checkpoints were passed during this step.
    public int Step(float deltaTime, bool playerInRange, bool enemiesInRange, float speed, float reverseSpeed)
    {
        if (Arrived) return 0;
        if (playerInRange && !enemiesInRange) { State = EscortState.Advancing; Distance += Math.Max(0f, speed) * deltaTime; }
        else if (playerInRange) State = EscortState.Contested;
        else if (enemiesInRange) { State = EscortState.Retreating; Distance = Math.Max(Floor, Distance - Math.Max(0f, reverseSpeed) * deltaTime); }
        else State = EscortState.Idle;

        int passed = 0;
        while (CheckpointsPassed < checkpoints.Count && Distance >= checkpoints[CheckpointsPassed] - 0.0001f)
        {
            Floor = checkpoints[CheckpointsPassed];
            CheckpointsPassed++; passed++;
        }
        if (Distance >= length - 0.0001f) { Distance = length; State = EscortState.Arrived; }
        return passed;
    }

    // After the payload is destroyed: back to the last checkpoint reached.
    public void ReturnToCheckpoint() { Distance = Floor; State = EscortState.Idle; }
}
