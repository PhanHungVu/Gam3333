using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine;

public class PickupDetector : MonoBehaviour
{
    EquipManager manager;

    // Bookkeeping: which colliders are already counted (prevents re-adding every frame)
    readonly Dictionary<Collider, Part> tracked = new Dictionary<Collider, Part>();
    // Public view: one entry per tracked collider, so duplicates of the same Part are allowed
    readonly List<Part> nearbyParts = new List<Part>();

    public IReadOnlyList<Part> NearbyParts => nearbyParts;
    public GameObject partSwapHudPopupParent;
    [Tooltip("Existing comparison panel, shared by nearby swapping and remote retrieval.")]
    public PartSwapWorldHudPresenter worldSpaceHud;
    GameObject popup1;
    GameObject popup2;
    public GameObject highlightElementPrefab;
    [Header("Highlight colour by part type")]
    public Color armHighlight = new Color(0.147f, 0.81f, 0.819f, 1f);   // original cyan
    public Color shoulderHighlight = new Color(0.25f, 0.9f, 0.3f, 1f);
    public Color headHighlight = new Color(1f, 0.85f, 0.2f, 1f);
    public Color legsHighlight = new Color(0.65f, 0.35f, 1f, 1f);
    public Color torsoHighlight = Color.white;
    GameObject highlightElement;
    PartType? highlightType;
    int selectedIndex = -1;
    bool cycleRequested;
    Collider[] sensors;
    RemotePartRetrieval retrieval;
    Part lastAimed;
    bool nearbyOverride;
    public RemotePartRetrieval Retrieval => retrieval;

    // Input revalidates the actual trigger overlap instead of trusting a stale selected item.
    public bool IsNearby(Part part)
    {
        if (!isActiveAndEnabled || part == null || part.isEquipped || part.IsRetrieving || !part.gameObject.activeInHierarchy || sensors == null) return false;
        foreach (var pair in tracked)
        {
            var other = pair.Key;
            if (pair.Value != part || other == null || !other.enabled || !other.gameObject.activeInHierarchy) continue;
            foreach (var sensor in sensors)
                if (sensor != null && sensor.enabled && sensor.isTrigger && sensor.gameObject.activeInHierarchy
                    && Physics.ComputePenetration(sensor, sensor.transform.position, sensor.transform.rotation,
                        other, other.transform.position, other.transform.rotation, out _, out _)) return true;
        }
        return false;
    }

    // Called by player input (CyclePart): move the highlight to the next nearby part.
    public void CycleSelection() => cycleRequested = true;

    void Awake()
    {
        sensors = GetComponents<Collider>();
        manager = GetComponentInParent<EquipManager>();
        retrieval = manager != null ? manager.GetComponent<RemotePartRetrieval>() : null;
        if (partSwapHudPopupParent == null) partSwapHudPopupParent = GameObject.Find("Canvas/PartSwapPopup");
        if (partSwapHudPopupParent != null)
        {
            popup1 = partSwapHudPopupParent.transform.Find("Popup1")?.gameObject;
            popup2 = partSwapHudPopupParent.transform.Find("Popup2")?.gameObject;
        }
        ShowPopups(false, false);
    }
    readonly List<Part> uniqueParts = new List<Part>();
    // Presentation uses the same deduplicated list as active-target cycling.
    public int DistinctNearbyPartCount => uniqueParts.Count;
    void RebuildUnique()
    {
        uniqueParts.Clear();
        foreach (var p in nearbyParts)
            if (p != null && !p.isEquipped && !p.IsRetrieving && p.isActiveAndEnabled && !uniqueParts.Contains(p))
                uniqueParts.Add(p);
    }
    private void Update() => RefreshSelection();
    public void RefreshSelection()
    {
        bool cycle = cycleRequested;
        cycleRequested = false;
        if (manager == null) return;
        if (retrieval == null) retrieval = manager.GetComponent<RemotePartRetrieval>();
        Part aimed = retrieval != null && retrieval.isActiveAndEnabled ? retrieval.Target : null;
        if (aimed != lastAimed) nearbyOverride = false;
        lastAimed = aimed;
        RebuildUnique();
        int count = uniqueParts.Count;
        if (cycle && count > 0) nearbyOverride = true;
        var remote = retrieval != null && retrieval.isActiveAndEnabled ? retrieval.ActivePart : null;
        if (remote == null && !nearbyOverride) remote = aimed;
        if (remote != null)
        {
            ShowPopups(false, false); SelectPart(remote); return;
        }
        if (count == 0)
        {
            selectedIndex = -1;
            ShowPopups(false, false);
            manager.ClearSelectedPart(manager.GetSelectedPart());
            ClearHighlight();
            return;
        }

        if (count == 1)
        {
            selectedIndex = 0;
            ShowPopups(true, false);
        }
        else
        {
            ShowPopups(false, true);

            // also guards against a stale index after parts leave the range
            if (selectedIndex < 0 || selectedIndex >= count) selectedIndex = 0;

            if (cycle)
                selectedIndex = (selectedIndex + 1) % count;
        }

        SelectPart(uniqueParts[selectedIndex]);
    }

    void ShowPopups(bool single, bool multiple)
    {
        if(worldSpaceHud!=null && worldSpaceHud.isActiveAndEnabled && worldSpaceHud.HasView) single=multiple=false;
        if (popup1 != null) popup1.SetActive(single);
        if (popup2 != null) popup2.SetActive(multiple);
    }

    void OnTriggerEnter(Collider other) => OnTriggerStay(other);

    void OnTriggerStay(Collider other)
    {
        if (manager == null) return;

        var part = other.GetComponentInParent<Part>();

        // Not a part, or currently equipped: make sure it's NOT in the list
        if (part == null || part.isEquipped)
        {
            RemoveCollider(other);
            return;
        }

        // Valid and unequipped: add once per collider
        if (tracked.ContainsKey(other)) return;

        tracked[other] = part;
        nearbyParts.Add(part);
    }

    void OnTriggerExit(Collider other) => RemoveCollider(other);

    void RemoveCollider(Collider col)
    {
        if (!tracked.TryGetValue(col, out var part)) return;

        tracked.Remove(col);
        nearbyParts.Remove(part); // removes one entry only

        // Only clear in the manager when no colliders of this part remain
        if (manager != null && !nearbyParts.Contains(part))
        {
            manager.ClearSelectedPart(part);
            ClearHighlight();
        }
    }

    void OnDisable()
    {
        if (manager != null)
            foreach (var part in nearbyParts)
                manager.ClearSelectedPart(part);

        tracked.Clear();
        nearbyParts.Clear();
        selectedIndex = -1;
        nearbyOverride = false; lastAimed = null;
        if (manager != null) manager.ClearSelectedPart(manager.GetSelectedPart());
        ShowPopups(false, false);
        ClearHighlight();
    }

    void SelectPart(Part part)
    {
        if (part.isEquipped || (part.IsRetrieving && part.RetrievalOwner != retrieval)) return;
        SetHighlight(part);
        manager.SetSelectedPart(part);
    }

    void SetHighlight(Part part)
    {
        if (highlightElementPrefab == null) return;
        if (highlightElement == null)
        {
            highlightElement = Instantiate(highlightElementPrefab);
            highlightType = null;
        }
        highlightElement.transform.position = part.PickupPoint;
        if (highlightType != part.partType) Recolour(part.partType);
    }

    public Color HighlightColour(PartType type) => type switch
    {
        PartType.Arm => armHighlight,
        PartType.Shoulder => shoulderHighlight,
        PartType.Head => headHighlight,
        PartType.Legs => legsHighlight,
        _ => torsoHighlight,
    };

    // Restart the effect in the new colour so particles of the previous type don't linger.
    void Recolour(PartType type)
    {
        highlightType = type;
        foreach (var system in highlightElement.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = system.main;
            main.startColor = HighlightColour(type);
            system.Clear(true);
            system.Play(true);
        }
    }

    void ClearHighlight()
    {
        if (highlightElement != null) Destroy(highlightElement);
        highlightElement = null;
        highlightType = null;
    }

    public void PartEquipped(Part part)
    {
        var colliders = new List<Collider>();
        foreach (var pair in tracked) if (pair.Value == part) colliders.Add(pair.Key);
        foreach (var collider in colliders) RemoveCollider(collider);
        RebuildUnique();
        if (uniqueParts.Count != 0) { selectedIndex = 0; SelectPart(uniqueParts[0]); }
        else
        {
            selectedIndex = -1;
            ClearHighlight();
        }
    }
}
