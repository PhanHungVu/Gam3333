using System.Collections.Generic;
using UnityEngine;

public enum QuestCategory { Side, Main }
public enum QuestMissionType { Retrieval, Elimination, KingOfTheHill, Escort, Defense }

// Authored information only. Browsing a board never changes this shared asset.
[CreateAssetMenu(fileName = "Quest", menuName = "Mech/Pre-Mission/Quest")]
public sealed class QuestDefinition : ScriptableObject
{
    public string questId;
    public string title;
    [TextArea(3, 10)] public string description;
    public QuestCategory category;
    public QuestMissionType missionType = QuestMissionType.Elimination;
    [Min(1)] public int estimatedMinutes = 10;
    [Min(0)] public int gniReward;
    public bool available = true;
    public List<PartData> availableParts = new List<PartData>();
    public List<GameObject> availableEnemies = new List<GameObject>();
    [Tooltip("Optional data for the gameplay developer. The UI does not execute mission settings.")]
    public ScriptableObject missionSettings;
}
