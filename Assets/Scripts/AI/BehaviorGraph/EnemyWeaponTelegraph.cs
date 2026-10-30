using UnityEngine;

[RequireComponent(typeof(EnemyAttack), typeof(EnemyThreatMarker))]
[DefaultExecutionOrder(240)]
public sealed class EnemyWeaponTelegraph : MonoBehaviour
{
    EnemyAttack attack;
    EnemyThreatMarker marker;
    void Awake() { attack = GetComponent<EnemyAttack>(); marker = GetComponent<EnemyThreatMarker>(); }
    void LateUpdate()
    {
        if (!attack.isActiveAndEnabled || !attack.IsTelegraphing) { marker.Hide(); return; }
        var start = transform.position;
        var end = attack.CurrentAttackIsMelee ? start + transform.forward * 3 : attack.CommittedAim;
        marker.Lane(start, end, attack.CurrentAttackIsMelee ? 2 : 0.55f, attack.TelegraphProgress);
    }
    void OnDisable() { if (marker != null) marker.Hide(); }
}
