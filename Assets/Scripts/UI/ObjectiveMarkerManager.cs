using System;
using System.Collections.Generic;
using UnityEngine;

// The one and only objective marker manager. It asks the mission how many objectives the level has
// (MissionObjectives.All, so any IMissionObjective counts: KOTH zones, elimination zones, escort
// payloads, anything new), then gives each open objective its own ObjectiveMarkerScript instance.
// A marker lives until its objective is complete; the end chunk only gets its exit marker once the
// extraction opens. There is never more than one manager: a duplicate destroys itself on Awake.
[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
public sealed class ObjectiveMarkerManager : MonoBehaviour
{
    [Header("Marker")]
    [Tooltip("Marker prefab, instanced once per objective. It needs an ObjectiveMarkerScript.")]
    [SerializeField] RectTransform markerPrefab;
    [Tooltip("Optional separate prefab for the exit marker. Empty: the marker prefab with the exit icon.")]
    [SerializeField] RectTransform exitMarkerPrefab;
    [Header("Objectives")]
    [Tooltip("Seconds to keep looking for objectives while the level is still being generated.")]
    [SerializeField, Min(0f)] float searchSeconds = 8f;
    [Header("Icons")]
    [Tooltip("Marker sprites picked by objective name: KOTH uses the sprite named objectiveMarkerKOTH.")]
    [SerializeField] Sprite[] icons;
    [SerializeField] string iconPrefix = "objectiveMarker";
    [Tooltip("Chunk names carry the quest type: Chunk_KOTH gives KOTH. Empty strings are ignored.")]
    [SerializeField] string chunkPrefix = "Chunk_";
    [Header("Camera")]
    [SerializeField] Camera gameplayCamera;

    sealed class Slot
    {
        public IMissionObjective objective;   // null for the exit marker
        public ObjectiveMarkerScript marker;
        public bool exit;
    }

    // One manager: a second one removes itself instead of doubling every marker on screen.
    static ObjectiveMarkerManager instance;

    readonly List<Slot> slots = new List<Slot>();
    Canvas canvas;
    Transform player;
    MissionTracker tracker;
    float searchUntil;
    float nextSync;
    float nextTrackerSearch;
    bool removed;

    public bool HasMarkers => slots.Count > 0;
    public int MarkerCount => slots.Count;
    public bool HasExitMarker
    {
        get
        {
            foreach (var slot in slots) if (slot.exit) return true;
            return false;
        }
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning($"ObjectiveMarkerManager: '{name}' is a duplicate and was removed. " +
                             "Only one manager is allowed, so check the prefabs for a second copy.", this);
            removed = true;
            gameObject.SetActive(false);
            Destroy(gameObject);
            return;
        }
        instance = this;

        canvas = GetComponentInParent<Canvas>();
        // Authored inside the cursor prefab, this object must not inherit the cursor's fade or tag
        // along with the cursor every frame, so it moves up to the canvas root before spawning.
        if (canvas != null && transform.parent != canvas.transform)
            transform.SetParent(canvas.transform, false);
        if (gameplayCamera == null) gameplayCamera = Camera.main;
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void Start()
    {
        if (removed) return;
        searchUntil = Time.unscaledTime + searchSeconds;
        Sync();
    }

    void LateUpdate()
    {
        if (removed) return;
        if (gameplayCamera == null) gameplayCamera = Camera.main;
        // Objectives register as the level builds, so keep asking for a while instead of once.
        if (Time.unscaledTime < searchUntil && Time.unscaledTime >= nextSync)
        {
            nextSync = Time.unscaledTime + 0.5f;
            Sync();
        }
        PruneCompleted();
        ResolveMission();
    }

    // One marker per objective that is still open. Safe to call as often as you like.
    public void Sync()
    {
        if (markerPrefab == null) return;
        var objectives = MissionObjectives.All;
        for (int i = 0; i < objectives.Count; i++)
        {
            var objective = objectives[i];
            if (objective == null || objective.IsComplete) continue;
            if (HasSlot(objective)) continue;
            Spawn(objective);
        }
    }

    bool HasSlot(IMissionObjective objective)
    {
        foreach (var slot in slots)
            if (!slot.exit && slot.objective == objective) return true;
        return false;
    }

    void Spawn(IMissionObjective objective)
    {
        if (objective is not Component component) return;
        var zone = ZoneOf(objective);
        string type = TypeName(zone, component);
        var rect = Instantiate(markerPrefab, transform, false);
        rect.name = "Objective Marker " + type;
        var marker = rect.GetComponent<ObjectiveMarkerScript>();
        if (marker == null) marker = rect.gameObject.AddComponent<ObjectiveMarkerScript>();
        marker.Assign(component.transform, zone, gameplayCamera, canvas, Player, IconFor(type));
        slots.Add(new Slot { objective = objective, marker = marker });
    }

    // The chunk an objective belongs to: a zone knows its own chunk, anything else sits under one.
    static ChunkDefinition ZoneOf(IMissionObjective objective)
    {
        if (objective is HoldZone hold) return hold.Chunk;
        if (objective is Component component) return component.GetComponentInParent<ChunkDefinition>();
        return null;
    }

    // "Chunk_KOTH" gives KOTH; a loose objective falls back to its type name, so EscortPayload gives
    // Escort and KOTH_Zone gives KOTH. Any new objective kind names its own icon this way.
    string TypeName(ChunkDefinition zone, Component objective)
    {
        if (zone != null) return StripChunk(zone.name);
        string name = objective.GetType().Name;
        if (name.EndsWith("Payload", StringComparison.Ordinal)) name = name.Substring(0, name.Length - "Payload".Length);
        if (name.EndsWith("Zone", StringComparison.Ordinal)) name = name.Substring(0, name.Length - "Zone".Length);
        return name.Trim('_');
    }

    string StripChunk(string objectName)
    {
        string name = TrimClone(objectName);
        if (!string.IsNullOrEmpty(chunkPrefix) && name.StartsWith(chunkPrefix, StringComparison.OrdinalIgnoreCase))
            name = name.Substring(chunkPrefix.Length);
        return name;
    }

    static string TrimClone(string objectName)
    {
        string name = (objectName ?? string.Empty).Trim();
        const string clone = "(Clone)";
        if (name.EndsWith(clone, StringComparison.Ordinal))
            name = name.Substring(0, name.Length - clone.Length).TrimEnd();
        return name;
    }

    // A marker lives until its objective is done.
    void PruneCompleted()
    {
        for (int i = slots.Count - 1; i >= 0; i--)
        {
            var slot = slots[i];
            if (slot.exit) continue;
            if (slot.objective != null && !slot.objective.IsComplete) continue;
            DestroyMarker(slot);
            slots.RemoveAt(i);
        }
    }

    void DestroyMarker(Slot slot)
    {
        if (slot.marker != null) Destroy(slot.marker.gameObject);
    }

    // The end chunk only gets a marker once the extraction opens, and every marker goes when the
    // mission is over.
    void ResolveMission()
    {
        if (tracker == null && Time.unscaledTime >= nextTrackerSearch)
        {
            nextTrackerSearch = Time.unscaledTime + 1f;
            tracker = FindAnyObjectByType<MissionTracker>();
        }
        if (tracker == null) return;

        if (tracker.State == MissionState.Complete)
        {
            Clear();
            return;
        }
        if (tracker.State == MissionState.ExtractOpen && !HasExitMarker) SpawnExitMarker();
    }

    void SpawnExitMarker()
    {
        var prefab = exitMarkerPrefab != null ? exitMarkerPrefab : markerPrefab;
        if (prefab == null) return;
        var extract = tracker.Extract;
        if (extract == null) return;

        var rect = Instantiate(prefab, transform, false);
        rect.name = "Objective Marker Exit";
        var marker = rect.GetComponent<ObjectiveMarkerScript>();
        if (marker == null) marker = rect.gameObject.AddComponent<ObjectiveMarkerScript>();
        var zone = extract.GetComponentInParent<ChunkDefinition>();
        marker.Assign(extract.transform, zone, gameplayCamera, canvas, Player, IconFor("Exit"));
        slots.Add(new Slot { marker = marker, exit = true });
    }

    public void Clear()
    {
        foreach (var slot in slots) DestroyMarker(slot);
        slots.Clear();
    }

    // Marker art is named after what it marks: KOTH uses "objectiveMarkerKOTH", the exit "objectiveMarkerExit".
    public Sprite IconFor(string type)
    {
        if (string.IsNullOrEmpty(type) || icons == null) return null;
        string wanted = iconPrefix + type;
        foreach (var icon in icons)
            if (icon != null && string.Equals(TrimClone(icon.name), wanted, StringComparison.OrdinalIgnoreCase))
                return icon;
        return null;
    }

    Transform Player
    {
        get
        {
            if (player == null)
            {
                var rig = CombatHudBinding.FindPlayer();
                if (rig != null) player = rig.transform;
            }
            return player;
        }
    }
}
