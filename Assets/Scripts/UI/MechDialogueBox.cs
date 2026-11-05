using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// In-game dialogue box: reads a JSON dialogue file and plays it into the face sprite, the speaker
// label and the TMP body. Each line prints one character at a time, holds for a set time after the
// last character has appeared, then the next line starts by itself, so the conversation plays
// through without input. The box hides after the last line and can be replayed with Play().
// Nothing here changes gameplay: dialogue is presentation only.
[DisallowMultipleComponent]
[DefaultExecutionOrder(370)]
public sealed class MechDialogueBox : MonoBehaviour
{
    [Header("Content")]
    [Tooltip("JSON dialogue file. Root: { id, lines: [ { speaker, portrait, text, seconds } ] }.")]
    [SerializeField] TextAsset dialogueFile;
    [Tooltip("Portraits matched by name: a line's portrait picks the sprite with the same name.")]
    [SerializeField] Sprite[] portraits;
    [Header("Playback")]
    [SerializeField] bool playOnStart = true;
    [Tooltip("Typing speed of the body text, in characters per second.")]
    [SerializeField, Min(1f)] float charactersPerSecond = 40f;
    [Tooltip("Seconds a line stays on screen after its last character is printed.")]
    [SerializeField, Min(0.1f)] float holdSeconds = 2f;
    [Header("Authored references")]
    [SerializeField] CanvasGroup group;
    [SerializeField] TextMeshProUGUI speakerLabel;
    [SerializeField] TextMeshProUGUI bodyLabel;
    [SerializeField] Image portrait;

    DialogueScript script;
    Color portraitColour = Color.white;
    int index = -1;
    bool revealing;
    int revealCount;
    float revealed;
    float hold;
    float timer;

    public bool IsVisible => group != null && group.alpha > 0f;
    public bool IsRevealing => revealing;
    public int LineIndex => index;
    public int LineCount => script != null && script.lines != null ? script.lines.Length : 0;
    public string CurrentSpeaker => speakerLabel != null ? speakerLabel.text : string.Empty;
    public string CurrentText => bodyLabel != null ? bodyLabel.text : string.Empty;
    public Sprite CurrentPortrait => portrait != null ? portrait.sprite : null;
    // Typing speed is also settable at runtime, so a cutscene can speed up or skip the reveal.
    public float CharactersPerSecond
    {
        get => charactersPerSecond;
        set => charactersPerSecond = Mathf.Max(1f, value);
    }

    void Awake()
    {
        if (group == null) group = GetComponent<CanvasGroup>();
        if (portrait != null) portraitColour = portrait.color;
        Load();
        Hide();
    }

    void Start()
    {
        if (playOnStart) Play();
    }

    // Typing the current line, then counting down to the next one. The countdown starts only once
    // the last character has been printed, so the reader gets the full hold time with the full line.
    void Update()
    {
        if (!IsVisible) return;
        if (revealing)
        {
            Reveal();
            return;
        }
        if (timer <= 0f) return;
        timer -= Time.unscaledDeltaTime;
        if (timer <= 0f) Advance();
    }

    void Reveal()
    {
        revealed += charactersPerSecond * Time.unscaledDeltaTime;
        int shown = Mathf.Min(Mathf.FloorToInt(revealed), revealCount);
        if (bodyLabel != null) bodyLabel.maxVisibleCharacters = shown;
        if (shown < revealCount) return;
        revealing = false;
        timer = hold;
    }

    // Reads the JSON file. Called on Awake, and again whenever the designer swaps the file at runtime.
    public void Load()
    {
        script = null;
        if (dialogueFile == null)
        {
            Debug.LogWarning("Dialogue box has no JSON file assigned.", this);
            return;
        }
        try
        {
            script = JsonUtility.FromJson<DialogueScript>(dialogueFile.text);
        }
        catch (Exception error)
        {
            Debug.LogError($"Dialogue file {dialogueFile.name}.json could not be read: {error.Message}", this);
        }
        if (script == null || script.lines == null || script.lines.Length == 0)
        {
            Debug.LogWarning($"Dialogue file {dialogueFile.name} has no lines.", this);
            script = null;
        }
    }

    // Starts from the first line and shows the box.
    public void Play()
    {
        if (LineCount == 0) return;
        index = -1;
        Show();
        Advance();
    }

    // Next line; on the last line it closes the box.
    public void Advance()
    {
        if (index + 1 >= LineCount)
        {
            Hide();
            return;
        }
        index++;
        ShowLine(script.lines[index]);
        Show();
    }

    public void Show()
    {
        if (group != null) group.alpha = 1f;
    }

    public void Hide()
    {
        if (group != null) group.alpha = 0f;
        revealing = false;
        timer = 0f;
    }

    void ShowLine(DialogueLine line)
    {
        if (speakerLabel != null) speakerLabel.text = line.speaker ?? string.Empty;
        if (portrait != null)
        {
            var sprite = PortraitFor(line.portrait);
            portrait.sprite = sprite;
            // Without art the face keeps the faint placeholder the prefab was authored with.
            portrait.color = sprite != null ? Color.white : portraitColour;
        }

        hold = line.seconds > 0f ? line.seconds : holdSeconds;
        revealed = 0f;
        revealCount = 0;
        revealing = false;
        timer = 0f;
        if (bodyLabel != null)
        {
            // Hide the characters before the text is set, so the whole line can never show for a
            // frame, then measure it: the reveal is driven by TMP's own visible-character limit,
            // which keeps the layout and wrapping identical from the first letter to the last.
            bodyLabel.maxVisibleCharacters = 0;
            bodyLabel.text = line.text ?? string.Empty;
            bodyLabel.ForceMeshUpdate();
            revealCount = bodyLabel.textInfo.characterCount;
            if (revealCount > 0)
            {
                revealing = true;
                return;
            }
            bodyLabel.maxVisibleCharacters = int.MaxValue;
        }
        // Empty line, or no body label at all: hold it straight away.
        timer = hold;
    }

    // Portraits follow the name in the JSON line; the sprites themselves are listed on the prefab.
    public Sprite PortraitFor(string name)
    {
        if (string.IsNullOrEmpty(name) || portraits == null) return null;
        string wanted = name.Trim();
        foreach (var candidate in portraits)
            if (candidate != null && string.Equals(candidate.name.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
                return candidate;
        return null;
    }
}
