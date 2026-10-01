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
    GameObject popup1;
    GameObject popup2;
    public GameObject highlightElementPrefab;
    GameObject highlightElement;
    int selectedIndex = -1;

    void Awake()
    {
        manager = GetComponentInParent<EquipManager>();
        partSwapHudPopupParent = GameObject.Find("Canvas/PartSwapPopup");
        if (partSwapHudPopupParent == null)
        {
            Debug.LogError("PickupDetector: Could not find PartSwapPopup parent object in the scene.");
            return;
        }
        popup1 = partSwapHudPopupParent.transform.Find("Popup1").gameObject;
        popup2 = partSwapHudPopupParent.transform.Find("Popup2").gameObject;
        popup1.SetActive(false);
        popup2.SetActive(false);
    }
    readonly List<Part> uniqueParts = new List<Part>();
    void RebuildUnique()
    {
        uniqueParts.Clear();
        foreach (var p in nearbyParts)
            if (p != null && !uniqueParts.Contains(p))
                uniqueParts.Add(p);
    }
    private void Update()
    {
        if (manager == null) return;
        RebuildUnique();
        int count = uniqueParts.Count;
        if (count == 0)
        {
            selectedIndex = -1;
            popup1.SetActive(false);
            popup2.SetActive(false);
            ClearHighlight();
            return;
        }

        if (count == 1)
        {
            selectedIndex = 0;
            popup1.SetActive(true);
            popup2.SetActive(false);
        }
        else
        {
            popup1.SetActive(false);
            popup2.SetActive(true);

            // also guards against a stale index after parts leave the range
            if (selectedIndex < 0 || selectedIndex >= count) selectedIndex = 0;

            if (Keyboard.current.vKey.wasPressedThisFrame)
                selectedIndex = (selectedIndex + 1) % count;
        }

        SelectPart(uniqueParts[selectedIndex]);
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
    }

    void SelectPart(Part part)
    {
        //if (manager.GetSelectedPart() == part) return;
        SetHighlight(part);
        manager.SetSelectedPart(part);
        Debug.Log($"Selected part: {part.name}");
    }

    void SetHighlight(Part part)
    {
        if (highlightElement == null)
            highlightElement = Instantiate(highlightElementPrefab);
        highlightElement.transform.position = part.transform.position;
    }

    void ClearHighlight()
    {
        if (highlightElement != null) Destroy(highlightElement);
        highlightElement = null;
    }
}