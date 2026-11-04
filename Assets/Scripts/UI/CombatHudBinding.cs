using UnityEngine;

// The combat HUD binds to the one local player (MechPlayerRig), never to an enemy, so every view
// agrees on whose parts and health it shows. Views re-search while no player is in the scene yet.
public static class CombatHudBinding
{
    public static MechPlayerRig FindPlayer() => UnityEngine.Object.FindAnyObjectByType<MechPlayerRig>();

    public static EquipManager FindEquipment()
    {
        var player = FindPlayer();
        return player != null ? player.GetComponent<EquipManager>() : null;
    }
}
