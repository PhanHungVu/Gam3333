using UnityEditor;

[CustomEditor(typeof(EnemyEncounterPreset))]
public sealed class EnemyEncounterPresetEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        bool explicitLayout = serializedObject.FindProperty("layout").enumValueIndex == (int)EnemyEncounterLayout.ExplicitPositions;
        if (explicitLayout) DrawPropertiesExcluding(serializedObject, "seed", "distance", "arc", "groups");
        else DrawPropertiesExcluding(serializedObject, "explicitSpawns");
        serializedObject.ApplyModifiedProperties();
        var preset = (EnemyEncounterPreset)target;
        EditorGUILayout.HelpBox(preset.RequestedCount + " requested actors. Presets use the existing arena roster. Maximum "
            + EnemyEncounterPreset.MaximumActors + " per test. Duplicate an asset to keep an alternative test layout.", MessageType.Info);
    }
}
