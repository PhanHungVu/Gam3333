using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public sealed class StarterTorsoVisual
{
    public PartData torso;
    [Tooltip("Visual model only. This is not instantiated as gameplay equipment.")]
    public GameObject model;
    public Vector3 rotation;
    [Tooltip("The player prefab used in missions when this torso is chosen (e.g. the golem or heavy player variant).")]
    public GameObject playerPrefab;
}

[CreateAssetMenu(fileName = "QuestBoardCatalogue", menuName = "Mech/Pre-Mission/Board Catalogue")]
public sealed class QuestBoardCatalogue : ScriptableObject
{
    [Min(1)] public int smallOfferCount = 3;
    [Min(1)] public int smallCompletionsToUnlockBig = 3;
    [Tooltip("Only these mission types can be offered by this board. An empty list offers no missions.")]
    public List<QuestMissionType> offeredMissionTypes = new List<QuestMissionType>
        { QuestMissionType.KingOfTheHill, QuestMissionType.Escort, QuestMissionType.Elimination };
    public List<QuestDefinition> quests = new List<QuestDefinition>();
    [Tooltip("Available starting torsos, using the existing gameplay PartData assets.")]
    public List<PartData> starterTorsos = new List<PartData>();
    public List<StarterTorsoVisual> starterTorsoVisuals = new List<StarterTorsoVisual>();

    public bool AllowsMissionType(QuestMissionType type) => offeredMissionTypes != null && offeredMissionTypes.Contains(type);

    public StarterTorsoVisual FindTorsoVisual(PartData torso)
    {
        if (torso == null || starterTorsoVisuals == null) return null;
        foreach (var visual in starterTorsoVisuals)
            if (visual != null && visual.torso == torso) return visual;
        return null;
    }

    // The mission player prefab for a starting torso, or null if none is set.
    public GameObject PlayerPrefabFor(PartData torso) => FindTorsoVisual(torso)?.playerPrefab;
}
