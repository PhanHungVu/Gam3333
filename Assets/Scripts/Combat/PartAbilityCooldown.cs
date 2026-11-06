using UnityEngine;

// State belongs to the physical part, so rebuilding a HUD or swapping the part cannot reset it.
// Gameplay calls ConfirmActivation only AFTER accepting/executing an ability; input requests do not.
[DisallowMultipleComponent, RequireComponent(typeof(Part))]
public sealed class PartAbilityCooldown : MonoBehaviour
{
    float readyAt;
    public float Duration { get; private set; }
    public float Remaining => Mathf.Max(0, readyAt - Time.time);
    public bool IsCoolingDown => Remaining > 0;
    // Presentation bookkeeping only: survives a HUD rebuild and never changes cooldown admission/timing.
    public uint ReadyCueCycle { get; private set; }
    uint consumedReadyCue;

    public bool ConfirmActivation(float cooldownSeconds)
    {
        if (IsCoolingDown || !Valid(cooldownSeconds)) return false;
        SetCooldown(cooldownSeconds, cooldownSeconds, true);
        return true;
    }

    // Optional authoritative update for a future gameplay/network ability implementation.
    public void Synchronize(float remainingSeconds, float durationSeconds)
    {
        if (!Valid(remainingSeconds) || !Valid(durationSeconds)) return;
        SetCooldown(remainingSeconds, durationSeconds, false);
    }

    void SetCooldown(float remainingSeconds, float durationSeconds, bool acceptedActivation)
    {
        // A confirmed activation always starts a cue cycle. Tiny late authoritative corrections
        // after reaching zero keep the same cue, rather than making the ready burst flicker/repeat.
        if (remainingSeconds > 0 && (acceptedActivation || !IsCoolingDown && remainingSeconds > .1f)) ReadyCueCycle++;
        Duration = Mathf.Max(remainingSeconds, durationSeconds);
        readyAt = Time.time + remainingSeconds;
    }

    public bool TryConsumeReadyCue(uint observedCycle)
    {
        if (IsCoolingDown || observedCycle == 0 || observedCycle != ReadyCueCycle || consumedReadyCue == observedCycle) return false;
        consumedReadyCue = observedCycle;
        return true;
    }

    static bool Valid(float seconds) => seconds >= 0 && !float.IsNaN(seconds) && !float.IsInfinity(seconds);
    public static PartAbilityCooldown For(Part part)
    {
        if (part == null) return null;
        return part.TryGetComponent<PartAbilityCooldown>(out var state) ? state : part.gameObject.AddComponent<PartAbilityCooldown>();
    }
}
