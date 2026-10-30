using UnityEngine;

// Only the visual parent hops; navigation, collisions and ground following stay with EnemyAgent.
[DefaultExecutionOrder(280)]
public sealed class EnemyHopPresentation : MonoBehaviour
{
    [SerializeField] Transform visualRoot;
    [SerializeField, Min(0)] float hopHeight = 0.32f;
    [SerializeField, Min(0.1f)] float hopFrequency = 2.5f;
    EnemyAgent agent;
    EnemyGraphAttack attack;
    Vector3 rest;
    void Awake() { agent = GetComponent<EnemyAgent>(); attack = GetComponent<EnemyGraphAttack>(); if (visualRoot != null) rest = visualRoot.localPosition; }
    void LateUpdate()
    {
        if (visualRoot == null || agent == null) return;
        float offset = agent.Velocity.sqrMagnitude > 0.1f && (attack == null || !attack.IsExecuting)
            ? Mathf.Abs(Mathf.Sin(Time.time * hopFrequency * Mathf.PI)) * hopHeight : 0;
        visualRoot.localPosition = rest + Vector3.up * offset;
    }
    void OnDisable() { if (visualRoot != null) visualRoot.localPosition = rest; }
}
