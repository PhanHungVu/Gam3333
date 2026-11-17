using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Opt-in arena tool. Does not add weapons to production waves or drop tables.
public sealed class GreyboxArmWeaponBench : MonoBehaviour
{
    public GreyboxArmWeaponCatalogue catalogue;
    public EquipManager player;
    [Range(0, 8)] public int selected;
    public bool keyboardShortcuts = true;
    readonly List<Part> created = new List<Part>();
    public Part Selected => catalogue != null && catalogue.weapons != null && selected >= 0 && selected < catalogue.weapons.Length ? catalogue.weapons[selected] : null;
    public void Select(int index)
    {
        if (catalogue == null || catalogue.weapons == null || catalogue.weapons.Length == 0) return;
        selected = (index % catalogue.weapons.Length + catalogue.weapons.Length) % catalogue.weapons.Length;
        if (Selected != null) Debug.Log("Greybox arm " + (selected + 5) + ": " + Selected.data.partName + " | , left | . right | \\ spawn", this);
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
        if (slot != PartCategory.ArmL && slot != PartCategory.ArmR) return false;
        var part = SpawnSelected();
        if (part == null) return false;
        if (player.TryEquip(part, slot)) return true;
        created.Remove(part); Destroy(part.gameObject); return false;
    }
    [ContextMenu("Equip Selected Left Arm (,)")] public void EquipLeft() => Equip(PartCategory.ArmL);
    [ContextMenu("Equip Selected Right Arm (.)")] public void EquipRight() => Equip(PartCategory.ArmR);
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
}
