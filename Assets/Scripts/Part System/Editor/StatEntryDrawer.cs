using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(StatEntry))]
public class StatEntryDrawer : PropertyDrawer
{
    static readonly StatType[] BoolStats = { StatType.SingleShot };

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        var typeProp = property.FindPropertyRelative("type");
        var valueProp = property.FindPropertyRelative("value");

        float lineHeight = EditorGUIUtility.singleLineHeight;
        float spacing = EditorGUIUtility.standardVerticalSpacing;

        Rect typeRect = new Rect(position.x, position.y, position.width, lineHeight);
        Rect valueRect = new Rect(position.x, position.y + lineHeight + spacing, position.width, lineHeight);

        EditorGUI.PropertyField(typeRect, typeProp);

        var selectedType = (StatType)typeProp.enumValueIndex;
        bool isBool = System.Array.IndexOf(BoolStats, selectedType) >= 0;

        if (isBool)
        {
            bool current = valueProp.floatValue > 0.5f;
            bool newVal = EditorGUI.Toggle(valueRect, "Value", current);
            valueProp.floatValue = newVal ? 1f : 0f;
        }
        else
        {
            EditorGUI.PropertyField(valueRect, valueProp, new GUIContent("Value"));
        }

        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float lineHeight = EditorGUIUtility.singleLineHeight;
        float spacing = EditorGUIUtility.standardVerticalSpacing;
        return lineHeight * 2 + spacing;
    }
}