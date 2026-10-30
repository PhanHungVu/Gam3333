using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(EnemyBehaviorPlaytest))]
public sealed class EnemyBehaviorPlaytestEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            var arena = (EnemyBehaviorPlaytest)target;
            if (GUILayout.Button("Spawn Selected Enemy")) arena.SpawnSelected();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Roaming Wave")) arena.StartRoaming();
            if (GUILayout.Button("Survival Wave")) arena.StartSurvival();
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Stop Wave")) arena.StopWave();
            if (GUILayout.Button("Clear Arena Enemies")) arena.ClearEnemies();
        }
    }
}
