using UnityEngine;

// GAMEPLAY HANDOFF: intentionally empty. See PreMission/README.md.
// Replace this component with a subclass and assign it to the flow controller,
// or implement this method when mission gameplay is ready.
public class MissionStartHook : MonoBehaviour
{
    public virtual void StartMission(QuestDefinition selectedQuest, PartData startingTorso)
    {
    }
}
