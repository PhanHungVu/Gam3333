using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GreyboxArmWeaponBench))]
public sealed class GreyboxArmWeaponBenchEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var bench = (GreyboxArmWeaponBench)target;
        EditorGUILayout.HelpBox("Play the existing EnemyBehaviorPlaytest scene. [ / ] selects a weapon; comma equips left; period equips right; backslash spawns an exact-stat pickup. Fire with normal LMB/RMB. Existing F5-F12 enemy encounter controls are unchanged.", MessageType.Info);
        using (new EditorGUI.DisabledScope(!Application.isPlaying || bench.catalogue == null))
        {
            if (bench.catalogue != null)
                for (int i = 0; i < bench.catalogue.weapons.Length; i++)
                {
                    var part = bench.catalogue.weapons[i]; if (part == null) continue;
                    EditorGUILayout.BeginHorizontal(); EditorGUILayout.LabelField((i + 5) + " " + part.data.partName, GUILayout.MinWidth(110));
                    if (GUILayout.Button("Left")) { bench.Select(i); bench.EquipLeft(); }
                    if (GUILayout.Button("Right")) { bench.Select(i); bench.EquipRight(); }
                    if (GUILayout.Button("Drop")) { bench.Select(i); bench.SpawnSelected(); }
                    EditorGUILayout.EndHorizontal();
                }
            if (GUILayout.Button("Clear Loose Bench Weapons")) bench.ClearLooseWeapons();
        }
    }
}
