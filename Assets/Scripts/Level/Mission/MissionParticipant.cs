using UnityEngine;

// Local default registration. A future session/roster owner can register remote participants explicitly.
[DisallowMultipleComponent, RequireComponent(typeof(CombatHealth))]
public sealed class MissionParticipant : MonoBehaviour
{
    [SerializeField] MissionResultsView resultsPrefab;
    [SerializeField] bool createLocalMission = true;
    public string ParticipantId => "local-" + GetEntityId();
    void Start()
    {
        var mission = MissionOutcomeController.Find(gameObject.scene);
        if (mission == null && createLocalMission) mission = MissionOutcomeController.Ensure(gameObject.scene, resultsPrefab);
        if (mission != null) mission.RegisterParticipant(ParticipantId, GetComponent<CombatHealth>(), true);
    }
}
