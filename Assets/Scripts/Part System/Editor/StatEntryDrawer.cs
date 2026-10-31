using UnityEditor;
using UnityEngine;

// Shows a part stat as its type above its value.
[CustomPropertyDrawer(typeof(StatEntry))]
public class StatEntryDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        float lineHeight = EditorGUIUtility.singleLineHeight;
        float spacing = EditorGUIUtility.standardVerticalSpacing;
        EditorGUI.PropertyField(new Rect(position.x, position.y, position.width, lineHeight), property.FindPropertyRelative("type"));
        EditorGUI.PropertyField(new Rect(position.x, position.y + lineHeight + spacing, position.width, lineHeight),
            property.FindPropertyRelative("value"), new GUIContent("Value"));
        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
        EditorGUIUtility.singleLineHeight * 2 + EditorGUIUtility.standardVerticalSpacing;
}
