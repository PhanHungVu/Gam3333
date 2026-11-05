using UnityEngine;

// Which player a mission starts with, handed over by whatever launched it (e.g. the quest board, from the
// chosen starting torso). Null means the scene's own player is used. Cleared when Play starts.
public static class MissionLoadout
{
    public static GameObject PlayerPrefab { get; set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => PlayerPrefab = null;
}

// Swaps the scene's player (tagged Player) for MissionLoadout's prefab before anything else wakes up, so the
// generator, camera and HUD all set themselves up on the chosen player. Without a loadout the scene's own
// player stays (opening a test scene directly); with no player in the scene, Default Player is spawned.
// Lives on the SubLevel prefab.
[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public sealed class MissionPlayerSpawner : MonoBehaviour
{
    [Tooltip("Spawned when the scene has no player and no loadout was handed over.")]
    [SerializeField] GameObject defaultPlayer;

    void Awake()
    {
        var existing = GameObject.FindWithTag("Player");
        var prefab = MissionLoadout.PlayerPrefab != null ? MissionLoadout.PlayerPrefab : existing == null ? defaultPlayer : null;
        if (prefab == null) return; // keep the scene's own player
        Vector3 position = existing != null ? existing.transform.position : transform.position;
        Quaternion rotation = existing != null ? existing.transform.rotation : Quaternion.identity;
        if (existing != null) DestroyImmediate(existing);
        var player = Instantiate(prefab, position, rotation);
        player.name = prefab.name;
    }
}
