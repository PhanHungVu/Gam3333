using UnityEngine;

// Plans one drop per life before combat, shows its marker, then spawns the promised loose part
// on death. Listens to CombatHealth.Died the same way
// EnemyCombatFeedback does, so the combat scripts don't need any changes.
// Right-click the component -> "Fill From Carried Weapons" to fill the list with one entry per
// equipped weapon part on this enemy (editor only - a running game can't trace a part back to its
// prefab). Two of the same weapon give two entries, so drops follow what the enemy carries.
[DisallowMultipleComponent]
[RequireComponent(typeof(CombatHealth))]
public sealed class EnemyPartDrop : MonoBehaviour
{
    [Header("Drop")]
    [Tooltip("Loose part prefabs; one is picked at random. Current cannon: Mech/Specialist/shoulder_cannon_specialist. Each Part must have Is Equipped unchecked.")]
    [SerializeField] Part[] partPrefabs = new Part[0];
    [Tooltip("Chance that this enemy drops anything at all when it dies.")]
    [SerializeField, Range(0f, 100f)] float dropChancePercent = 30f;

    [Header("Loot carrier glint")]
    [SerializeField] bool showDropMarker = true;
    [Tooltip("Optional override; otherwise uses VFX/Pickups/Resources/EnemyDropHighlight.prefab.")]
    [SerializeField] PickupHighlight dropMarkerPrefab;

    [Header("Spawn feel")]
    [Tooltip("Height above the enemy's pivot where the part appears.")]
    [SerializeField] float spawnHeight = 1f;
    [Tooltip("Small upward pop so the drop reads as an event. 0 = just falls.")]
    [SerializeField] float popVelocity = 3f;

    CombatHealth health;
    bool deathResolved;
    bool planPrepared;
    float plannedChance;
    Part[] plannedEntries = new Part[0];
    Part plannedPart;
    PickupHighlight dropMarker;
    static PickupHighlight markerTemplate;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetMarkerCache() => markerTemplate = null;

    public int PartCount => partPrefabs != null ? partPrefabs.Length : 0;
    public Part LastDrop { get; private set; }
    public Part DroppedCarriedPart { get; private set; }
    public Part PlannedDrop
    {
        get { if (isActiveAndEnabled && !deathResolved) PrepareDropPlan(); return plannedPart; }
    }
    public bool WillDropPart
    {
        get
        {
            if (!isActiveAndEnabled || deathResolved || health == null || health.IsDead) return false;
            PrepareDropPlan();
            return plannedPart != null;
        }
    }

    void Awake() => health = GetComponent<CombatHealth>();
    void OnEnable()
    {
        deathResolved = planPrepared = false; LastDrop = DroppedCarriedPart = null;
        health.Died += OnDied;
        PrepareDropPlan(); RefreshMarker();
    }
    void OnDisable() { health.Died -= OnDied; HideMarker(); }
    void LateUpdate() => RefreshMarker();

    void PrepareDropPlan()
    {
        if (deathResolved) return;
        int count = partPrefabs != null ? partPrefabs.Length : 0;
        bool unchanged = planPrepared && plannedChance == dropChancePercent && plannedEntries.Length == count;
        for (int i = 0; unchanged && i < count; i++) unchanged = plannedEntries[i] == partPrefabs[i];
        if (unchanged) return;

        // Inspector/configuration edits deliberately refresh the plan. Ordinary
        // frames, damage and visibility changes never reroll a carrier's promise.
        plannedChance = dropChancePercent; planPrepared = true; plannedPart = null;
        plannedEntries = count > 0 ? (Part[])partPrefabs.Clone() : new Part[0];
        if (count == 0 || dropChancePercent <= 0 || float.IsNaN(dropChancePercent)) return;
        float roll = Random.value * 100f;
        if (dropChancePercent < 100f && roll >= dropChancePercent) return;
        plannedPart = partPrefabs[Random.Range(0, count)];
    }

    void RefreshMarker()
    {
        if (!showDropMarker || !WillDropPart) { HideMarker(); return; }
        if (dropMarker == null)
        {
            if (markerTemplate == null) markerTemplate = Resources.Load<PickupHighlight>("EnemyDropHighlight");
            var prefab = dropMarkerPrefab != null ? dropMarkerPrefab : markerTemplate;
            if (prefab == null) return;
            dropMarker = Instantiate(prefab, transform, false);
            dropMarker.name = "Enemy Loot Glint"; dropMarker.Bind(transform);
        }
        if (!dropMarker.gameObject.activeSelf) dropMarker.gameObject.SetActive(true);
    }

    void HideMarker() { if (dropMarker != null) dropMarker.gameObject.SetActive(false); }

    void OnDied() => ResolveDeathDrop();

    // Called before death presentation too; always one roll/spawn per life, whatever the listener order.
    public Part ResolveDeathDrop()
    {
        if (deathResolved || !isActiveAndEnabled || health == null || !health.IsDead) return LastDrop;
        PrepareDropPlan();
        deathResolved = true;
        HideMarker(); // Hide before the death snapshot can copy its renderers.
        Part partPrefab = plannedPart;
        if (partPrefab == null) return null;

        Vector3 position = transform.position + Vector3.up * spawnHeight;
        Quaternion rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        // No parent on purpose: CombatHealth deactivates this enemy right after Died,
        // and a child would vanish with it.
        LastDrop = Instantiate(partPrefab, position, rotation);
        // Roll before a pickup can equip the new item in this same frame.
        // The carried weapon/loot definition stays unchanged for AI and corpse matching.
        LastDrop.RollLootStats();
        foreach (var carried in GetComponentsInChildren<Part>(true))
        {
            if (!carried.isEquipped || carried.Stats == null || partPrefab.Stats == null) continue;
            if (carried.Stats == partPrefab.Stats || !string.IsNullOrEmpty(carried.Stats.partId)
                && string.Equals(carried.Stats.partId, partPrefab.Stats.partId, System.StringComparison.Ordinal))
            { DroppedCarriedPart = carried; break; }
        }

        // Part.Awake has already run inside Instantiate and added the Rigidbody.
        var body = LastDrop.GetComponent<Rigidbody>();
        if (body != null && !body.isKinematic)
        { body.linearVelocity = Vector3.up * Mathf.Max(0, popVelocity); body.angularVelocity = Vector3.zero; }
        return LastDrop;
    }

#if UNITY_EDITOR
    // Fills Part Prefabs with the prefab of every equipped weapon part on this enemy.
    [ContextMenu("Fill From Carried Weapons")]
    void FillFromCarriedWeapons()
    {
        var found = new System.Collections.Generic.List<Part>();
        foreach (var part in GetComponentsInChildren<Part>(true))
        {
            if (!part.isEquipped || part.Stats == null || part.Stats.subtype == PartSubtype.None) continue;
            // Carried cannon components can be embedded in an enemy prefab. Loot must use
            // the unequipped Specialist pickup, rather than an equipped enemy child or the archive.
            var cannon = part.Stats.partId == "backweaponA3"
                ? UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Specialist/shoulder_cannon_specialist.prefab") : null;
            var source = cannon != null ? cannon.GetComponent<Part>() : UnityEditor.PrefabUtility.GetCorrespondingObjectFromOriginalSource(part);
            if (source != null) found.Add(source);
        }
        if (found.Count == 0) { Debug.LogWarning("No equipped weapon parts found on this enemy.", this); return; }
        UnityEditor.Undo.RecordObject(this, "Fill From Carried Weapons");
        partPrefabs = found.ToArray();
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}
