using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public enum StatType
{
    Health, MoveSpeed,
    Damage, MagSize, Ammo, CooldownReload, Range, SingleShot, AutoAim
}

[System.Serializable]
public struct StatEntry
{
    public StatType type;
    public float value;
}

[CreateAssetMenu(fileName = "PartData", menuName = "Mech/Part")]
public class PartData : ScriptableObject
{
    public string partId;
    public string partName;
    [TextArea] public string description;
    public List<StatEntry> stats;

    public float GetStat(StatType type)
    {
        foreach (var s in stats)
            if (s.type == type) return s.value;
        return 0f;
    }

    public bool GetBool(StatType type) => GetStat(type) > 0.5f;

#if UNITY_EDITOR
    void OnValidate()
    {
        if (string.IsNullOrEmpty(partId)) return;

        string[] guids = AssetDatabase.FindAssets("t:PartData");
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var other = AssetDatabase.LoadAssetAtPath<PartData>(path);
            if (other != null && other != this && other.partId == partId)
            {
                Debug.LogWarning($"Duplicate partId '{partId}' found on {other.name} and {name}", this);
            }
        }
    }
#endif
}