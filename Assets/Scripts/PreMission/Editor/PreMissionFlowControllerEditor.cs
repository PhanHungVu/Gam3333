using UnityEditor;
using UnityEngine;

// Temporary Inspector tool for the P1–P4 presentation review; excluded from player builds.
[CustomEditor(typeof(PreMissionFlowController))]
public sealed class PreMissionFlowControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var flow=(PreMissionFlowController)target;
        EditorGUILayout.LabelField("Temporary Quest Testing",EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Play Mode only. Completes the first offered small quest through a simulated successful result. Remove after P4 review.",MessageType.Info);
        using(new EditorGUI.DisabledScope(!QuestProgressionTools.CanCompleteSmallQuest(flow)))
            if(GUILayout.Button("Complete Small Quest (Test)"))
            {
                QuestProgressionTools.CompleteSmallQuest(flow); Repaint();
            }
        if(Application.isPlaying && flow.Session!=null)
        {
            var run=flow.Session.Progression;
            EditorGUILayout.LabelField("Small successes",run.SuccessfulSmallCompletions+" / "+run.RequiredSmallCompletions);
            if(run.ActiveAttempt!=null) EditorGUILayout.LabelField("A mission attempt is already active.");
            else if(flow.IsPosting || flow.IsTransitioning) EditorGUILayout.LabelField("Wait for the board animation to finish.");
            else if(flow.Session.View!=PreMissionView.Board) EditorGUILayout.LabelField("Return to the Quest Board to use this test.");
        }
        EditorGUILayout.Space(); DrawDefaultInspector();
    }
}
