using System;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RetrievalBonusCooldowns))]
public sealed class RetrievalBonusCooldownsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var gate = (RetrievalBonusCooldowns)target;
        if (gate.bypassCooldownForTesting)
            EditorGUILayout.HelpBox("Temporary retrieval bonus cooldown bypass is ON. Every configured pull can trigger damage. Normal weapon attacks and reloads are unchanged. Uncheck the bypass to restore family timers.", MessageType.Info);
        if (!Application.isPlaying) return;
        EditorGUILayout.Space(); EditorGUILayout.LabelField("Live bonus timers (this player)", EditorStyles.boldLabel);
        foreach (RetrievalWeaponFamily family in Enum.GetValues(typeof(RetrievalWeaponFamily)))
        {
            float remaining = gate.Remaining(family);
            EditorGUILayout.LabelField(family.ToString(), gate.IsReserved(family) ? "Reserved" : remaining > 0 ? remaining.ToString("0.0") + "s" : "Ready");
        }
        EditorGUILayout.HelpBox("A ready gate grants a bonus only when that weapon has a configured retrieval effect. Normal pickup is always available.", MessageType.Info);
    }
    public override bool RequiresConstantRepaint() => Application.isPlaying;
}
