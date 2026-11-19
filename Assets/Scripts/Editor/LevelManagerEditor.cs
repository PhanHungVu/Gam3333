using UnityEditor;

[CustomEditor(typeof(LevelManager))]
public sealed class LevelManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.HelpBox("Observation only. Future tuning values are stored but are not applied to drops, waves, damage or HP.", MessageType.Info);
        DrawPropertiesExcluding(serializedObject, "observed", "enemies");
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("observed"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("enemies"), true);
        }
        serializedObject.ApplyModifiedProperties();
        if (EditorApplication.isPlaying) Repaint();
    }
}
