using System.Linq;
using UnityEngine;

// Inspector-driven arena controls; no debug UI is added to the player's HUD.
public sealed class EnemyBehaviorPlaytest : MonoBehaviour
{
    [SerializeField] Transform player;
    [SerializeField] SpawnDirector director;
    [SerializeField] RoamingSpawner spawnPoint;
    [SerializeField] WaveData roaming, survival;
    [SerializeField] GameObject[] roster;
    [SerializeField] EnemyBehaviorRole selectedRole;
    [SerializeField, Min(3)] float spawnDistance = 16;
    [ContextMenu("Spawn Selected Enemy")]
    public void SpawnSelected()
    {
        if (!Application.isPlaying || player == null || roster == null || (int)selectedRole >= roster.Length) return;
        Vector3 position = player.position + player.forward * spawnDistance;
        position.y = EnemyAgent.SpawnGroundY(position, 0) + 1;
        Instantiate(roster[(int)selectedRole], position, Quaternion.LookRotation(Steering.Flat(player.position-position)));
    }
    [ContextMenu("Start Roaming Wave")] public void StartRoaming() => StartWave(roaming);
    [ContextMenu("Start Survival Wave")] public void StartSurvival() => StartWave(survival);
    void StartWave(WaveData data) { if (Application.isPlaying && director != null) { director.StopWave(this); director.RunWave(data, new SpawnPoint[] { spawnPoint }, this); } }
    [ContextMenu("Stop Wave")] public void StopWave() { if (Application.isPlaying && director != null) director.StopWave(this); }
    [ContextMenu("Clear Arena Enemies")]
    public void ClearEnemies()
    {
        if (!Application.isPlaying) return;
        StopWave(); foreach (var actor in EnemyAgent.All.ToArray()) if (actor != null && actor.gameObject.scene == gameObject.scene) Destroy(actor.gameObject);
    }
}
