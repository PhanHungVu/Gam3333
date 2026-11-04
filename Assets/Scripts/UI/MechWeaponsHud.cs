using System.Collections.Generic;
using UnityEngine;

// The weapons HUD in the screen's right corner: one pod per equipped shoulder and arm weapon.
// Which sockets exist belongs to the equipped torso, so the rows are rebuilt from the equipment
// every time it changes. Rows are grouped by side and weapon type: the left shoulder upper-left,
// the right shoulder upper-right, the left arm lower-left, the right arm lower-right. Each cell
// stacks its rows away from the screen edge and the HUD grows to fit, so a torso with more than
// one weapon per side still reads correctly.
[DisallowMultipleComponent]
[DefaultExecutionOrder(360)]
public sealed class MechWeaponsHud : MonoBehaviour
{
    [Header("Row prefab")]
    [SerializeField] WeaponsHudPod podPrefab;
    [SerializeField] Vector2 podSize = new Vector2(180f, 86f);
    [SerializeField, Min(0f)] float podSpacing = 6f;
    [Header("Cells (one per corner of the layout)")]
    [SerializeField] RectTransform shoulderLeftCell;
    [SerializeField] RectTransform shoulderRightCell;
    [SerializeField] RectTransform armLeftCell;
    [SerializeField] RectTransform armRightCell;
    [Header("Player binding")]
    [Tooltip("Seconds between searches while no player is in the scene yet.")]
    [SerializeField, Min(0.1f)] float playerSearchInterval = 0.5f;

    // One corner of the layout: its rows and which way they grow away from the screen edge.
    sealed class Cell
    {
        public readonly RectTransform rect;
        public readonly bool stackDown;
        public Cell(RectTransform rect, bool stackDown) { this.rect = rect; this.stackDown = stackDown; }
    }

    readonly List<WeaponsHudPod> pods = new List<WeaponsHudPod>();
    readonly List<Part> shown = new List<Part>();
    Cell[] cells;
    RectTransform layout;
    EquipManager equipment;
    float nextPlayerSearch;

    public IReadOnlyList<WeaponsHudPod> Pods => pods;
    public EquipManager Equipment => equipment;

    void Awake()
    {
        layout = (RectTransform)transform;
        cells = new[]
        {
            new Cell(shoulderLeftCell, true), new Cell(shoulderRightCell, true),
            new Cell(armLeftCell, false), new Cell(armRightCell, false),
        };
    }

    void OnEnable()
    {
        // Rows are built from live equipment, so nothing is created while editing the prefab.
        if (!Application.isPlaying) return;
        BindPlayer(CombatHudBinding.FindEquipment());
        Rebuild();
    }

    void OnDisable()
    {
        if (!Application.isPlaying) return;
        Clear();
        BindPlayer(null);
    }

    void LateUpdate()
    {
        // The scene player may be spawned after the HUD, so keep looking until it is found.
        if (equipment != null || Time.unscaledTime < nextPlayerSearch) return;
        nextPlayerSearch = Time.unscaledTime + playerSearchInterval;
        BindPlayer(CombatHudBinding.FindEquipment());
    }

    void BindPlayer(EquipManager manager)
    {
        if (manager == equipment) return;
        if (equipment != null) equipment.EquipmentChanged -= Rebuild;
        equipment = manager;
        if (equipment != null) equipment.EquipmentChanged += Rebuild;
        Rebuild();
    }

    // Rebuilds the rows from the equipped sockets: dynamic because the torso decides which
    // shoulder and arm sockets exist, and any of them may be empty.
    void Rebuild()
    {
        Clear();
        if (equipment == null) return;
        foreach (var socket in equipment.GetComponentsInChildren<Socket>(true))
        {
            if (socket == null) continue;
            var weapon = equipment.GetEquipped(socket.socketType);
            if (weapon == null || weapon.Stats == null || shown.Contains(weapon)) continue;
            var cell = CellFor(socket.socketType);
            if (cell == null || podPrefab == null) continue;
            shown.Add(weapon);
            var pod = Instantiate(podPrefab, cell.rect, false);
            pod.Bind(weapon);
            pods.Add(pod);
        }
        Layout();
    }

    void Clear()
    {
        foreach (var pod in pods)
        {
            if (pod == null) continue;
            // Hide at once: Destroy removes the object at the end of the frame, after this rebuild.
            pod.gameObject.SetActive(false);
            Destroy(pod.gameObject);
        }
        pods.Clear();
        shown.Clear();
    }

    Cell CellFor(PartCategory slot)
    {
        RectTransform rect = slot switch
        {
            PartCategory.ShoulderL => shoulderLeftCell,
            PartCategory.ShoulderR => shoulderRightCell,
            PartCategory.ArmL => armLeftCell,
            PartCategory.ArmR => armRightCell,
            _ => null,
        };
        if (rect == null) return null;
        foreach (var cell in cells) if (cell.rect == rect) return cell;
        return null;
    }

    // Rows anchor to the cell corner they belong to and stack away from the screen edge. The two
    // rows then size the HUD, so its top edge stays put and nothing overlaps.
    void Layout()
    {
        float top = 0f, bottom = 0f;
        foreach (var cell in cells)
        {
            float used = Layout(cell);
            if (cell.stackDown) top = Mathf.Max(top, used);
            else bottom = Mathf.Max(bottom, used);
        }
        if (layout == null) return;
        float height = top + bottom + (top > 0f && bottom > 0f ? podSpacing : 0f);
        layout.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
    }

    float Layout(Cell cell)
    {
        if (cell.rect == null) return 0f;
        float used = 0f;
        for (int i = 0; i < cell.rect.childCount; i++)
        {
            var row = cell.rect.GetChild(i) as RectTransform;
            if (row == null || !row.gameObject.activeSelf) continue;
            row.anchorMin = row.anchorMax = cell.rect.pivot;
            row.pivot = row.anchorMin;
            row.sizeDelta = podSize;
            row.anchoredPosition = new Vector2(0f, cell.stackDown ? -used : used);
            used += podSize.y + podSpacing;
        }
        used = Mathf.Max(0f, used - podSpacing);
        cell.rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, used);
        cell.rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, podSize.x);
        return used;
    }
}
