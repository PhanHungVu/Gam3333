using System.Collections.Generic;
using UnityEngine;

// The abilities HUD beside the weapons HUD: one small pod per ability the mech carries. Head,
// torso and legs each own one ability, so the pods follow the equipped parts and are rebuilt
// whenever the equipment changes. Pods read left to right as Head, Torso, Legs next to the
// weapons HUD. Ability code calls Activate(slot) when an ability fires; the pod then dims its
// sprite and counts that part's Ability Cooldown stat down.
[DisallowMultipleComponent]
[DefaultExecutionOrder(360)]
public sealed class MechAbilitiesHud : MonoBehaviour
{
    // Only these slots own abilities; arms and shoulders are weapons and belong to the weapons HUD.
    static readonly PartCategory[] AbilitySlots = { PartCategory.Head, PartCategory.Torso, PartCategory.Legs };

    [Header("Row prefab")]
    [SerializeField] AbilitiesHudPod podPrefab;
    [SerializeField] Vector2 podSize = new Vector2(56f, 56f);
    [SerializeField, Min(0f)] float podSpacing = 8f;
    [Header("Player binding")]
    [Tooltip("Seconds between searches while no player is in the scene yet.")]
    [SerializeField, Min(0.1f)] float playerSearchInterval = 0.5f;

    readonly List<AbilitiesHudPod> pods = new List<AbilitiesHudPod>();
    RectTransform layout;
    EquipManager equipment;
    float nextPlayerSearch;

    public IReadOnlyList<AbilitiesHudPod> Pods => pods;
    public EquipManager Equipment => equipment;

    void Awake() => layout = (RectTransform)transform;

    void OnEnable()
    {
        // Cells are built from live equipment, so nothing is created while editing the prefab.
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

    void Rebuild()
    {
        Clear();
        if (equipment == null || podPrefab == null) return;
        foreach (var slot in AbilitySlots)
        {
            var part = equipment.GetEquipped(slot);
            if (part == null || part.Stats == null) continue;
            var pod = Instantiate(podPrefab, transform, false);
            pod.Bind(part);
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
    }

    // Fires the ability in a slot: the pod dims its sprite and counts the cooldown down. Returns
    // false when that slot has no ability pod, so an ability system can ignore mounts without one.
    public bool Activate(PartCategory slot)
    {
        return ReportAcceptedActivation(slot);
    }

    // Compatibility entry point for gameplay. Never wire this to a raw key press/request.
    public bool ReportAcceptedActivation(PartCategory slot)
    {
        var part = equipment != null ? equipment.GetEquipped(slot) : null;
        if (part == null || part.Stats == null || (slot != PartCategory.Head && slot != PartCategory.Torso && slot != PartCategory.Legs)) return false;
        return PartAbilityCooldown.For(part).ConfirmActivation(Mathf.Max(0, part.Stats.GetStat(StatType.AbilityCooldown)));
    }

    // Pods sit next to the weapons HUD and grow leftwards, so the row keeps its right edge and
    // reads Head, Torso, Legs from left to right whichever parts are equipped.
    void Layout()
    {
        if (layout == null) return;
        for (int i = 0; i < pods.Count; i++)
        {
            var row = (RectTransform)pods[i].transform;
            row.anchorMin = row.anchorMax = new Vector2(1f, 0.5f);
            row.pivot = row.anchorMin;
            row.sizeDelta = podSize;
            row.anchoredPosition = new Vector2(-(pods.Count - 1 - i) * (podSize.x + podSpacing), 0f);
        }
        float width = pods.Count > 0 ? pods.Count * podSize.x + (pods.Count - 1) * podSpacing : 0f;
        layout.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
    }
}
