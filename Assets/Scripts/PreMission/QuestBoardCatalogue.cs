using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public sealed class StarterTorsoVisual
{
    public PartData torso;
    [Tooltip("Visual model only. This is not instantiated as gameplay equipment.")]
    public GameObject model;
    public Vector3 rotation;
}

[CreateAssetMenu(fileName = "QuestBoardCatalogue", menuName = "Mech/Pre-Mission/Board Catalogue")]
public sealed class QuestBoardCatalogue : ScriptableObject
{
    [Min(1)] public int minimumOffers = 4;
    [Min(1)] public int maximumOffers = 6;
    [Tooltip("Include a main quest when one is available. This never locks side quests.")]
    public bool includeMainQuestWhenAvailable = true;
    public List<QuestDefinition> quests = new List<QuestDefinition>();
    [Tooltip("Available starting torsos, using the existing gameplay PartData assets.")]
    public List<PartData> starterTorsos = new List<PartData>();
    public List<StarterTorsoVisual> starterTorsoVisuals = new List<StarterTorsoVisual>();

    public StarterTorsoVisual FindTorsoVisual(PartData torso)
    {
        if (torso == null || starterTorsoVisuals == null) return null;
        foreach (var visual in starterTorsoVisuals)
            if (visual != null && visual.torso == torso) return visual;
        return null;
    }
}
