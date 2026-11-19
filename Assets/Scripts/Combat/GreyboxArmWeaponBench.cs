using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Opt-in arena tool. Does not add weapons to production waves or drop tables.
public sealed class GreyboxArmWeaponBench : MonoBehaviour
{
    public GreyboxArmWeaponCatalogue catalogue;
    public GreyboxShoulderWeaponCatalogue shoulderCatalogue;
    public bool shoulderMode;
    public EquipManager player;
    [Range(0, 8)] public int selected;
    public bool keyboardShortcuts = true;
    readonly List<Part> created = new List<Part>();
    readonly List<CombatHealth> shoulderTestTargets = new List<CombatHealth>();
    public IReadOnlyList<CombatHealth> ShoulderTestTargets => shoulderTestTargets;
    public Part[] CurrentWeapons => shoulderMode ? shoulderCatalogue != null ? shoulderCatalogue.weapons : null : catalogue != null ? catalogue.weapons : null;
    public Part Selected => CurrentWeapons != null && selected >= 0 && selected < CurrentWeapons.Length ? CurrentWeapons[selected] : null;
    public void Select(int index)
    {
        if (CurrentWeapons == null || CurrentWeapons.Length == 0) return;
        selected = (index % CurrentWeapons.Length + CurrentWeapons.Length) % CurrentWeapons.Length;
        if (Selected != null) Debug.Log((shoulderMode ? "Shoulder " : "Arm ") + (selected + (shoulderMode ? 1 : 5)) + ": " + Selected.data.partName + " | , left | . right | \\ spawn", this);
    }
    bool FindPlayer()
    {
        if (player != null && player.isActiveAndEnabled) return true;
        var input = FindAnyObjectByType<MechPlayerInput>();
        if (input != null) player = input.GetComponent<EquipManager>();
        return player != null && player.isActiveAndEnabled;
    }
    void Update()
    {
        var keys = Keyboard.current;
        if (!keyboardShortcuts || keys == null || Time.timeScale <= 0) return;
        if (keys.leftBracketKey.wasPressedThisFrame) Select(selected - 1);
        else if (keys.rightBracketKey.wasPressedThisFrame) Select(selected + 1);
        else if (keys.commaKey.wasPressedThisFrame) EquipLeft();
        else if (keys.periodKey.wasPressedThisFrame) EquipRight();
        else if (keys.backslashKey.wasPressedThisFrame) SpawnSelected();
    }
    public Part SpawnSelected()
    {
        if (!Application.isPlaying || !FindPlayer() || Selected == null) return null;
        var part = Instantiate(Selected, player.transform.position + player.transform.forward * 3 + Vector3.up, Quaternion.identity);
        part.KeepAuthoredStats(); created.Add(part); return part;
    }
    public bool Equip(PartCategory slot)
    {
        if (shoulderMode ? slot != PartCategory.ShoulderL && slot != PartCategory.ShoulderR : slot != PartCategory.ArmL && slot != PartCategory.ArmR) return false;
        var part = SpawnSelected();
        if (part == null) return false;
        if (player.TryEquip(part, slot)) return true;
        created.Remove(part); Destroy(part.gameObject); return false;
    }
    [ContextMenu("Equip Selected Left (,)")] public void EquipLeft() => Equip(shoulderMode ? PartCategory.ShoulderL : PartCategory.ArmL);
    [ContextMenu("Equip Selected Right (.)")] public void EquipRight() => Equip(shoulderMode ? PartCategory.ShoulderR : PartCategory.ArmR);
    [ContextMenu("Spawn Selected Pickup (Backslash)")] void SpawnPickup() => SpawnSelected();
    [ContextMenu("Clear Loose Bench Weapons")]
    public void ClearLooseWeapons()
    {
        for (int i = created.Count - 1; i >= 0; i--)
        {
            var part = created[i];
            if (part != null && part.isEquipped) continue;
            if (part != null) Destroy(part.gameObject);
            created.RemoveAt(i);
        }
    }
    // Development-only static opponents for repeatable balance comparisons.
    // Production enemy AI, waves and loot tables are not involved.
    public void SpawnShoulderTargets(int count)
    {
        if (!Application.isPlaying || !shoulderMode || !FindPlayer() || Selected == null || count != 1 && count != 5 && count != 15) return;
        ClearShoulderTargets();
        var prototype = Selected.GetComponent<GreyboxShoulderWeapon>();
        bool ring = prototype != null && prototype.weapon == GreyboxShoulderWeapon.Weapon.GiantRock;
        var forward = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < .001f) forward = Vector3.forward;
        var right = Vector3.Cross(Vector3.up, forward);
        int columns = count == 5 ? 3 : 5;
        float spacing = count == 5 ? 1.8f : 5f;
        float ringRadius = ring ? player.GetComponent<MechCombat>().RangeMetresOf(Selected) * prototype.ringRangeScale
            - Selected.Stats.GetStat(StatType.SplashRadius) * prototype.ringThicknessScale * .5f : 0;
        for (int i = 0; i < count; i++)
        {
            bool centerControl = ring && count > 1 && i == count - 1;
            Vector3 offset = count == 1 ? forward * 18
                : forward * (18 + i / columns * spacing) + right * ((i % columns - (columns - 1) * .5f) * spacing);
            if (ring)
            {
                float angle = i * Mathf.PI * 2 / (count == 1 ? 1 : count - 1);
                offset = centerControl ? forward * 8 : (forward * Mathf.Cos(angle) + right * Mathf.Sin(angle)) * ringRadius;
            }
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = centerControl ? "Shoulder test - safe ring center" : "Shoulder test target " + (i + 1);
            cube.transform.position = player.transform.position + offset + Vector3.up;
            cube.transform.localScale = count == 1 ? new Vector3(4, 4, 4) : new Vector3(1.2f, 3, 1.2f);
            var health = cube.AddComponent<CombatHealth>(); health.Team = CombatTeam.Enemy; health.SetMaximum(10000, true);
            cube.AddComponent<MechTargetable>(); shoulderTestTargets.Add(health);
        }
        Physics.SyncTransforms();
    }
    [ContextMenu("Clear Shoulder Test Targets")]
    public void ClearShoulderTargets()
    {
        foreach (var health in shoulderTestTargets) if (health != null) { health.gameObject.SetActive(false); Destroy(health.gameObject); }
        shoulderTestTargets.Clear();
    }
}
