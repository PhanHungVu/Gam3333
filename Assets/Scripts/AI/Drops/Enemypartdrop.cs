using UnityEngine;

// Rolls a drop chance when this enemy dies and, on success, spawns one loose part the player can
// pick up, chosen at random from Part Prefabs. Listens to CombatHealth.Died the same way
// EnemyCombatFeedback does, so the combat scripts don't need any changes.
// Right-click the component -> "Fill From Carried Weapons" to fill the list from the weapon parts
// in EnemyAttack's Weapons (editor only - a running game can't trace a part back to its prefab).
[DisallowMultipleComponent]
[RequireComponent(typeof(CombatHealth))]
public sealed class EnemyPartDrop : MonoBehaviour
{
    [Header("Drop")]
    [Tooltip("Loose part prefabs from Prefabs/Mech/Base/Parts; one is picked at random. Each Part must have Is Equipped unchecked.")]
    [SerializeField] Part[] partPrefabs = new Part[0];
    [Tooltip("Chance that this enemy drops anything at all when it dies.")]
    [SerializeField, Range(0f, 100f)] float dropChancePercent = 30f;

    [Header("Spawn feel")]
    [Tooltip("Height above the enemy's pivot where the part appears.")]
    [SerializeField] float spawnHeight = 1f;
    [Tooltip("Small upward pop so the drop reads as an event. 0 = just falls.")]
    [SerializeField] float popVelocity = 3f;

    CombatHealth health;

    public int PartCount => partPrefabs != null ? partPrefabs.Length : 0;

    void Awake() => health = GetComponent<CombatHealth>();
    void OnEnable() => health.Died += OnDied;
    void OnDisable() => health.Died -= OnDied;

    void OnDied()
    {
        if (partPrefabs == null || partPrefabs.Length == 0) return;
        if (Random.value * 100f >= dropChancePercent) return;
        Part partPrefab = partPrefabs[Random.Range(0, partPrefabs.Length)];
        if (partPrefab == null) return;

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

#if UNITY_EDITOR
    // Fills Part Prefabs with the prefab of each weapon part this enemy carries, once each.
    [ContextMenu("Fill From Carried Weapons")]
    void FillFromCarriedWeapons()
    {
        var attack = GetComponent<EnemyAttack>();
        if (attack == null) { Debug.LogWarning("No EnemyAttack on this enemy.", this); return; }
        var found = new System.Collections.Generic.List<Part>();
        foreach (var weapon in attack.Weapons)
        {
            var part = weapon != null ? weapon.GetComponent<Part>() : null;
            var source = part != null ? UnityEditor.PrefabUtility.GetCorrespondingObjectFromOriginalSource(part) : null;
            if (source != null && !found.Contains(source)) found.Add(source);
        }
        if (found.Count == 0) { Debug.LogWarning("No weapon part prefabs found in EnemyAttack's Weapons.", this); return; }
        UnityEditor.Undo.RecordObject(this, "Fill From Carried Weapons");
        partPrefabs = found.ToArray();
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
