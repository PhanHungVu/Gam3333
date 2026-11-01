using UnityEngine;

// The way out: a hold zone on the start chunk that stays closed (hidden, not timing) until the mission
// opens it once every objective is complete. Holding inside it for its time completes the extraction.
// Enemies keep coming meanwhile (the start chunk never pauses roaming). Put it on a child of Chunk_Start.
[DisallowMultipleComponent]
public sealed class ExtractZone : HoldZone
{
    [Tooltip("Open from the start (for testing). Normally the mission opens it.")]
    [SerializeField] bool startOpen;

    bool open;
    public override bool IsOpen => open;

    protected override void Awake()
    {
        base.Awake();
        open = startOpen;
    }

    public void Open() => open = true;

    // Sensible defaults when the component is first added: a 10 second extraction.
    void Reset() { holdSeconds = 10f; graceSeconds = 5f; failedSeconds = 2f; }
}
