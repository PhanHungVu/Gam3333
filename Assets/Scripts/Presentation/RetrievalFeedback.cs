using UnityEngine;

// All weapon families share arrival feedback, independently of whether their combat bonus is ready.
[DisallowMultipleComponent, RequireComponent(typeof(RemotePartRetrieval))]
public sealed class RetrievalFeedback : MonoBehaviour
{
    [SerializeField, Range(0, .2f)] float arrivalShakeStrength = .12f;
    [SerializeField, Range(.05f, .5f)] float arrivalShakeDuration = .16f;
    RemotePartRetrieval retrieval;
    CombatFeedback feedback;
    public int ArrivalImpulseCount { get; private set; }
    void Awake() { retrieval = GetComponent<RemotePartRetrieval>(); feedback = GetComponent<CombatFeedback>(); }
    void OnEnable() => retrieval.Arrived += Arrived;
    void OnDisable() => retrieval.Arrived -= Arrived;
    void Arrived(Part part, PartCategory slot)
    {
        if (RemotePartRetrieval.IsWeapon(part) && feedback != null
            && feedback.PlayMovementImpulse(Vector3.down, arrivalShakeStrength, arrivalShakeDuration)) ArrivalImpulseCount++;
    }
}
