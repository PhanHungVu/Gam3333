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

    public bool ConfirmActivation(float cooldownSeconds)
    {
        if (IsCoolingDown || !Valid(cooldownSeconds)) return false;
        Synchronize(cooldownSeconds, cooldownSeconds);
        return true;
    }

    // Optional authoritative update for a future gameplay/network ability implementation.
    public void Synchronize(float remainingSeconds, float durationSeconds)
    {
        if (!Valid(remainingSeconds) || !Valid(durationSeconds)) return;
        Duration = Mathf.Max(remainingSeconds, durationSeconds);
        readyAt = Time.time + remainingSeconds;
    }

    static bool Valid(float seconds) => seconds >= 0 && !float.IsNaN(seconds) && !float.IsInfinity(seconds);
    public static PartAbilityCooldown For(Part part)
    {
        if (part == null) return null;
        return part.TryGetComponent<PartAbilityCooldown>(out var state) ? state : part.gameObject.AddComponent<PartAbilityCooldown>();
    }
}
