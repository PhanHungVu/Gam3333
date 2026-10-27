using UnityEngine;

// What spawning this enemy costs from a wave's resource. Put it on each enemy prefab:
// small 5, elite 10, boss 20 (bosses never come from waves, but keep a cost for completeness).
[DisallowMultipleComponent]
public sealed class EnemySpawnCost : MonoBehaviour
{
    [SerializeField, Min(1)] int cost = 5;
    public int Cost => cost;

    // A prefab's cost; 0 when it has no EnemySpawnCost, which waves treat as unspawnable.
    public static int Of(GameObject prefab)
    {
        var spawnCost = prefab != null ? prefab.GetComponent<EnemySpawnCost>() : null;
        return spawnCost != null ? spawnCost.Cost : 0;
    }
}
