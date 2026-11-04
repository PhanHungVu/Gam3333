using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(EnemyBehaviorPlaytest))]
public sealed class EnemyBehaviorPlaytestEditor : Editor
{
    bool showComparisons;
    bool showSpawnIssues;
    bool showCombatReview;
    public override void OnInspectorGUI()
    {
        serializedObject.Update(); DrawPropertiesExcluding(serializedObject, "selectedEncounter"); serializedObject.ApplyModifiedProperties();
        var arena = (EnemyBehaviorPlaytest)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("M5 controlled encounters", EditorStyles.boldLabel);
        var presets = arena.EncounterPresets;
        if (presets != null)
        {
            var names = new string[presets.Count];
            for (int i = 0; i < names.Length; i++) names[i] = presets[i] != null ? presets[i].DisplayName : "Missing preset";
            EditorGUI.BeginChangeCheck();
            int selected = EditorGUILayout.Popup("Selected preset", arena.SelectedEncounterIndex, names);
            if (EditorGUI.EndChangeCheck())
            { Undo.RecordObject(arena, "Select enemy encounter"); arena.SelectEncounter(selected); EditorUtility.SetDirty(arena); }
        }
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("Start Selected Preset (F11)")) arena.StartSelectedEncounter();
            using (new EditorGUI.DisabledScope(!arena.CanReplayEncounter))
                if (GUILayout.Button("Replay Last Preset (F12)")) arena.ReplayLastEncounter();
            EditorGUILayout.HelpBox("Page Up/Down selects a preset; F11 starts it; F12 replays its saved composition and placement. F9 clears. Edit preset assets for counts/layouts; green Gizmos preview their positions.", MessageType.Info);
            EditorGUILayout.LabelField(arena.LastEncounterReport, EditorStyles.wordWrappedLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Toggle Melee Movement (Home)")) arena.TogglePressureMovement();
            if (GUILayout.Button("Combat Snapshot (End)")) { arena.CaptureCombatReview(); showCombatReview = true; }
            EditorGUILayout.EndHorizontal();
            showCombatReview = EditorGUILayout.Foldout(showCombatReview, "M6 combat review", true);
            if (showCombatReview) EditorGUILayout.SelectableLabel(arena.CombatReviewReport, EditorStyles.textArea, GUILayout.Height(220));
            if (arena.SpawnIssues.Count > 0)
            {
                showSpawnIssues = EditorGUILayout.Foldout(showSpawnIssues, "Skipped spawns / configuration", true);
                if (showSpawnIssues) foreach (string issue in arena.SpawnIssues) EditorGUILayout.LabelField(issue, EditorStyles.wordWrappedLabel);
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Continuous melee pressure", EditorStyles.boldLabel);
            if (GUILayout.Button("Start Pressure Swarm")) arena.StartPressureSwarm();
            if (GUILayout.Button("Start Ranged Positioning Encounter")) arena.StartRangedEncounter();
            if (GUILayout.Button("Start Mixed Pressure Encounter")) arena.StartMixedEncounter();
            if (GUILayout.Button("Start Specials With Horde")) arena.StartSpecialEncounter();
            EditorGUILayout.HelpBox("Game view shortcuts: F5 selected enemy; F6 melee pressure; F7 ranged; F8 mixed; F9 clear; F10 specials with horde. Encounter starts replace the previous group.", MessageType.Info);
            if (arena.SpecialCoordinator != null)
                EditorGUILayout.LabelField($"Specials: {arena.SpecialCoordinator.ActiveSpinnerAttacks} spinner, {arena.SpecialCoordinator.ActiveOrbitAttacks} orbit, {arena.SpecialCoordinator.PendingAttacks} seeking an opening");
            EditorGUILayout.HelpBox("Enemies close in and attack whenever range, facing and weapon cooldown allow. Scene Gizmos show local approach goals.", MessageType.Info);
            showComparisons = EditorGUILayout.Foldout(showComparisons, "Earlier behavior comparisons", true);
            if (showComparisons)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Baseline")) arena.StartBaselineSwarm();
                if (GUILayout.Button("Reserved Positions")) arena.StartPositioningSwarm();
                EditorGUILayout.EndHorizontal();
                if (GUILayout.Button("Reserved Positions + Attack Budget")) arena.StartCoordinatedSwarm();
                if (arena.Coordinator != null)
                    EditorGUILayout.LabelField($"Reservations: {arena.Coordinator.CommittedMeleeAttacks} committed; {arena.Coordinator.PendingMeleeRequests} queued");
            }
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
