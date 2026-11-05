using UnityEditor;
using UnityEngine;

// Inspector debug controls for the abilities HUD: fire an ability and watch its pod dim and count
// the cooldown down, exactly as an ability system will drive it. Pods are built in play mode from
// the equipped head, torso and legs, so the buttons are only live while playing.
[CustomEditor(typeof(MechAbilitiesHud))]
public sealed class MechAbilitiesHudEditor : Editor
{
    static readonly PartCategory[] Slots = { PartCategory.Head, PartCategory.Torso, PartCategory.Legs };
    double nextRepaint;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var hud = (MechAbilitiesHud)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);
        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "Ability pods are built in play mode. Enter play mode to use these buttons.",
                MessageType.Info);
            return;
        }
        if (!hud.gameObject.scene.IsValid())
        {
            EditorGUILayout.HelpBox(
                "This is the prefab asset. Select the AbilitiesHUD inside the scene's CombatHUD to test it.",
                MessageType.Info);
            return;
        }
        if (hud.Pods.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "No ability pods yet: the HUD has not bound a player with head, torso or legs parts.",
                MessageType.Warning);
            return;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            foreach (var slot in Slots)
                if (GUILayout.Button("Use " + slot)) hud.Activate(slot);
        }
        if (GUILayout.Button("Use All")) hud.ActivateAll();

        EditorGUILayout.Space();
        foreach (var pod in hud.Pods)
            EditorGUILayout.LabelField(pod.Slot.HasValue ? pod.Slot.Value.ToString() : "?",
                pod.IsCoolingDown ? "cooldown " + pod.Remaining.ToString("0.0") + "s" : "ready");
    }

    void OnEnable() => EditorApplication.update += RepaintWhilePlaying;

    void OnDisable() => EditorApplication.update -= RepaintWhilePlaying;

    // The cooldown read-out is live, so the inspector needs a gentle repaint while it counts down.
    void RepaintWhilePlaying()
    {
        if (!Application.isPlaying || target == null) return;
        if (EditorApplication.timeSinceStartup < nextRepaint) return;
        nextRepaint = EditorApplication.timeSinceStartup + 0.1;
        Repaint();
    }
}
