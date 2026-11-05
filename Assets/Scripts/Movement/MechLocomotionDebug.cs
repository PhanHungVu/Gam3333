using UnityEngine;
using UnityEngine.InputSystem;

// Optional development harness. F1-F4 keep number keys free for real pickups.
[RequireComponent(typeof(MechEquipmentLocomotion))]
[DisallowMultipleComponent]
public sealed class MechLocomotionDebug : MonoBehaviour
{
    [SerializeField] Part[] modulePrefabs = new Part[0];
    [SerializeField] bool showOverlay;
    [SerializeField] Key toggleKey = Key.Backquote;
    [SerializeField] bool enableSwapShortcuts = true;
    EquipManager equipment;
    MechMovementController movement;
    Part[] cached;
    GameObject storage;
    GUIStyle labelStyle;
    static readonly Rect ToggleArea = new Rect(12, 12, 225, 28);
    public bool IsOverlayVisible => showOverlay;
    public void ToggleOverlay() => showOverlay = !showOverlay;
    public void SetOverlayVisible(bool visible) => showOverlay = visible;
    // A debug-button click must not be consumed by the normal click-to-capture cursor rule.
    public bool PointerOverToggle
    {
        get
        {
            if (!isActiveAndEnabled || Mouse.current == null || Cursor.lockState == CursorLockMode.Locked) return false;
            Vector2 pointer = Mouse.current.position.ReadValue();
            return ToggleArea.Contains(new Vector2(pointer.x, Screen.height - pointer.y));
        }
    }

    void Awake()
    {
        equipment = GetComponent<EquipManager>();
        movement = GetComponent<MechMovementController>();
        cached = new Part[modulePrefabs.Length];
        if (!Application.isEditor && !Debug.isDebugBuild) enabled = false;
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null || Time.timeScale <= 0f) return;
        if (toggleKey != Key.None && keyboard[toggleKey].wasPressedThisFrame) ToggleOverlay();
        if (!enableSwapShortcuts) return;
        if (keyboard.f1Key.wasPressedThisFrame) TrySwap(0);
        else if (keyboard.f2Key.wasPressedThisFrame) TrySwap(1);
        else if (keyboard.f3Key.wasPressedThisFrame) TrySwap(2);
        else if (keyboard.f4Key.wasPressedThisFrame) TrySwap(3);
    }

    public bool TrySwap(int index)
    {
        if (!isActiveAndEnabled || index < 0 || index >= cached.Length) return false;
        Part prefab = modulePrefabs[index];
        if (prefab == null || prefab.data == null || prefab.partType != PartType.Legs || prefab.GetComponent<LocomotionModule>() == null)
            return false;
        Part old = equipment.GetEquipped(PartCategory.Legs);
        // Identity belongs to the part definition, not its locomotion family.
        if (old != null && old.data == prefab.data) return true;
        if (storage == null)
        {
            storage = new GameObject("Locomotion Debug Stored Legs");
            storage.SetActive(false);
        }
        if (cached[index] == null) cached[index] = Instantiate(prefab, storage.transform);
        Part next = cached[index];
        if (next.isEquipped) return false;
        next.transform.SetPositionAndRotation(transform.position, transform.rotation);
        next.transform.SetParent(null, true);
        next.gameObject.SetActive(true);
        if (!equipment.TryEquip(next))
        {
            next.gameObject.SetActive(false);
            next.transform.SetParent(storage.transform, true);
            return false;
        }
        if (old != null)
        {
            int oldIndex = System.Array.FindIndex(modulePrefabs, p => p != null && p.data == old.data);
            if (oldIndex >= 0)
            {
                if (cached[oldIndex] != null && cached[oldIndex] != old
                    && cached[oldIndex].transform.IsChildOf(storage.transform)) Destroy(cached[oldIndex].gameObject);
                cached[oldIndex] = old;
                old.gameObject.SetActive(false);
                old.transform.SetParent(storage.transform, true);
            }
        }
        return true;
    }

    void OnDestroy()
    {
        if (storage != null) Destroy(storage);
    }

    void OnGUI()
    {
        if (movement == null) return;
        string shortcut = toggleKey == Key.None ? "" : " [" + (toggleKey == Key.Backquote ? "`" : toggleKey.ToString()) + "]";
        if (GUI.Button(ToggleArea, (showOverlay ? "Hide Locomotion Debug" : "Locomotion Debug") + shortcut)) ToggleOverlay();
        if (!showOverlay) return;
        if (labelStyle == null) labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 14 };
        var data = movement.AnimationData;
        Rect area = new Rect(12, 48, 350, 294);
        GUI.Box(area, "LOCOMOTION DEBUG");
        GUILayout.BeginArea(new Rect(area.x + 12, area.y + 25, area.width - 24, area.height - 30));
        var legs = equipment.GetEquipped(PartCategory.Legs);
        string moduleName = legs != null && legs.data != null ? legs.data.partName : data.LocomotionType.ToString();
        string shortcuts = "";
        for (int i = 0; i < Mathf.Min(4, modulePrefabs.Length); i++)
            if (modulePrefabs[i] != null && modulePrefabs[i].data != null)
                shortcuts += $"F{i + 1} {modulePrefabs[i].data.partName}" + (i % 2 == 0 ? "   " : "\n");
        GUILayout.Label($"Module: {moduleName}\nState: {data.State}\n"
            + $"Speed: {data.Speed:F2} m/s\nForward: {data.ForwardSpeed:F2}   Side: {data.SideSpeed:F2}\n"
            + $"Vertical: {data.VerticalSpeed:F2} m/s\nGrounded: {data.IsGrounded}   Boosting: {data.IsBoosting}\n"
            + $"Turning: {data.IsTurning}   Yaw: {data.TurnRate:F1} deg/s\nAim: {data.AimDirection.ToString("F2")}\n\n"
            + shortcuts + "Space: jump / hover lift", labelStyle);
        GUILayout.EndArea();
    }
}
