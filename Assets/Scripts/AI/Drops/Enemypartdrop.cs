using UnityEngine;

// Rolls a drop chance when this enemy dies and spawns a loose part the player can pick up.
// Listens to CombatHealth.Died the same way EnemyCombatFeedback does, so the combat
// scripts don't need any changes.
[DisallowMultipleComponent]
[RequireComponent(typeof(CombatHealth))]
public sealed class EnemyPartDrop : MonoBehaviour
{
    [Header("Drop")]
    [Tooltip("Loose part prefab from Prefabs/Mech/Base/Parts. Its Part must have Is Equipped unchecked.")]
    [SerializeField] Part partPrefab;
    [SerializeField, Range(0f, 100f)] float dropChancePercent = 30f;

    [Header("Spawn feel")]
    [Tooltip("Height above the enemy's pivot where the part appears.")]
    [SerializeField] float spawnHeight = 1f;
    [Tooltip("Small upward pop so the drop reads as an event. 0 = just falls.")]
    [SerializeField] float popVelocity = 3f;

    CombatHealth health;

    void Awake() => health = GetComponent<CombatHealth>();
    void OnEnable() => health.Died += OnDied;
    void OnDisable() => health.Died -= OnDied;

    void OnDied()
    {
        if (partPrefab == null) return;
        if (Random.value * 100f >= dropChancePercent) return;

        Vector3 position = transform.position + Vector3.up * spawnHeight;
        Quaternion rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        // No parent on purpose: CombatHealth deactivates this enemy right after Died,
        // and a child would vanish with it.
        Part drop = Instantiate(partPrefab, position, rotation);

        // Part.Awake has already run inside Instantiate and added the Rigidbody.
        var body = drop.GetComponent<Rigidbody>();
        if (body != null && popVelocity > 0f)
            body.AddForce(Vector3.up * popVelocity, ForceMode.VelocityChange);
    }
}