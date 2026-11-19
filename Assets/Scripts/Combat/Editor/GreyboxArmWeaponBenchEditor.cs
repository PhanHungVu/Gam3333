using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GreyboxArmWeaponBench))]
public sealed class GreyboxArmWeaponBenchEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var bench = (GreyboxArmWeaponBench)target;
        EditorGUILayout.HelpBox("Play EnemyBehaviorPlaytest. Shoulder Mode selects the nine shoulder weapons; off keeps arms 5-13. [ / ] selects, comma/period equips left/right, backslash drops an exact-stat pickup. Q/E activates shoulders: press for one Katyusha salvo, hold for Tesla pulses. F5-F12 enemy encounters are unchanged.", MessageType.Info);
        using (new EditorGUI.DisabledScope(!Application.isPlaying || bench.CurrentWeapons == null))
        {
            if (bench.CurrentWeapons != null)
                for (int i = 0; i < bench.CurrentWeapons.Length; i++)
                {
                    var part = bench.CurrentWeapons[i]; if (part == null) continue;
                    EditorGUILayout.BeginHorizontal(); EditorGUILayout.LabelField((i + (bench.shoulderMode ? 1 : 5)) + " " + part.data.partName, GUILayout.MinWidth(110));
                    if (GUILayout.Button("Left")) { bench.Select(i); bench.EquipLeft(); }
                    if (GUILayout.Button("Right")) { bench.Select(i); bench.EquipRight(); }
                    if (GUILayout.Button("Drop")) { bench.Select(i); bench.SpawnSelected(); }
                    EditorGUILayout.EndHorizontal();
                }
            if (GUILayout.Button("Clear Loose Bench Weapons")) bench.ClearLooseWeapons();
            if (bench.shoulderMode)
            {
                EditorGUILayout.HelpBox("Static targets have 10,000 HP. Giant Rock uses ring positions plus one safe-center control in group tests. Other weapons use one tanky target, five clustered targets, or fifteen spread targets. Clear active AI encounters using the existing encounter controls first.", MessageType.Info);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("1 Tanky Target")) bench.SpawnShoulderTargets(1);
                if (GUILayout.Button("5 Grouped")) bench.SpawnShoulderTargets(5);
                if (GUILayout.Button("15 Spread")) bench.SpawnShoulderTargets(15);
                EditorGUILayout.EndHorizontal();
                if (GUILayout.Button("Clear Shoulder Targets")) bench.ClearShoulderTargets();
            }
        }
    }
}
