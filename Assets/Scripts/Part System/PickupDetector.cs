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
        if (partSwapHudPopupParent == null) partSwapHudPopupParent = GameObject.Find("Canvas/PartSwapPopup");
        if (partSwapHudPopupParent != null)
        {
            popup1 = partSwapHudPopupParent.transform.Find("Popup1")?.gameObject;
            popup2 = partSwapHudPopupParent.transform.Find("Popup2")?.gameObject;
        }
        ShowPopups(false, false);
    }
    readonly List<Part> uniqueParts = new List<Part>();
    void RebuildUnique()
    {
        uniqueParts.Clear();
        foreach (var p in nearbyParts)
            if (p != null && !p.isEquipped && p.gameObject.activeInHierarchy && !uniqueParts.Contains(p))
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

            if (Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame)
                selectedIndex = (selectedIndex + 1) % count;
        }

        SelectPart(uniqueParts[selectedIndex]);
    }

    void ShowPopups(bool single, bool multiple)
    {
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
        ShowPopups(false, false);
        ClearHighlight();
    }

    void SelectPart(Part part)
    {
        if (part.isEquipped) return;
        SetHighlight(part);
        manager.SetSelectedPart(part);
    }

    void SetHighlight(Part part)
    {
        if (highlightElementPrefab == null) return;
        if (highlightElement == null)
            highlightElement = Instantiate(highlightElementPrefab);
        highlightElement.transform.position = part.transform.position;
    }

    void ClearHighlight()
    {
        if (highlightElement != null) Destroy(highlightElement);
        highlightElement = null;
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
