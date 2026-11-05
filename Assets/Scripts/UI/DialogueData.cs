using System;
using UnityEngine;

// One line of an in-game dialogue. MechDialogueBox fills these from a JSON file, so the field names
// must match the keys exactly: {"speaker": "...", "portrait": "...", "text": "...", "seconds": 0}.
[Serializable]
public sealed class DialogueLine
{
    [Tooltip("Name shown above the line.")]
    public string speaker;
    [Tooltip("Portrait sprite name; the dialogue box matches it against its portrait list.")]
    public string portrait;
    [TextArea] public string text;
    [Tooltip("Seconds this line stays before the next one. 0 uses the dialogue box's hold time.")]
    public float seconds;
}

// The JSON root: {"id": "...", "lines": [ { ... } ]}.
[Serializable]
public sealed class DialogueScript
{
    [Tooltip("Identifier for the dialogue, for logs and future save data.")]
    public string id;
    public DialogueLine[] lines;
}
