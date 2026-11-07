using System;
using System.Collections.Generic;
using UnityEngine;

// Player-owned bonus gate. Equipment changes and component enable/disable never reset these timers.
[DisallowMultipleComponent]
public sealed class RetrievalBonusCooldowns : MonoBehaviour
{
    [Min(0)] public float cooldownDuration = 30f;
    [Header("Temporary damage testing")]
    [Tooltip("Allow every retrieval bonus without waiting. Reservations and normal weapon firing/reload cooldowns remain intact. Disable to restore the stored family timers.")]
    public bool bypassCooldownForTesting;
    sealed class State { public double readyAt; public object reservation; }
    readonly Dictionary<RetrievalWeaponFamily, State> states = new Dictionary<RetrievalWeaponFamily, State>();
    static bool Valid(RetrievalWeaponFamily family) => family >= RetrievalWeaponFamily.Sword && family <= RetrievalWeaponFamily.LargeCrossbow;
    public float Remaining(RetrievalWeaponFamily family) => !bypassCooldownForTesting && states.TryGetValue(family, out var state) ? (float)Math.Max(0, state.readyAt - Time.timeAsDouble) : 0;
    public bool IsReserved(RetrievalWeaponFamily family) => states.TryGetValue(family, out var state) && state.reservation != null;
    public bool IsReady(RetrievalWeaponFamily family) => Valid(family) && Remaining(family) <= 0 && !IsReserved(family);
    public bool TryReserve(RetrievalWeaponFamily family, object token)
    {
        if (token == null || !IsReady(family)) return false;
        if (!states.TryGetValue(family, out var state)) states[family] = state = new State();
        state.reservation = token; return true;
    }
    public bool TryCommit(RetrievalWeaponFamily family, object token)
    {
        if (token == null || !states.TryGetValue(family, out var state) || !ReferenceEquals(state.reservation, token)) return false;
        state.readyAt = Time.timeAsDouble + Math.Max(0, cooldownDuration); state.reservation = null; return true;
    }
    public void Release(RetrievalWeaponFamily family, object token)
    {
        if (token != null && states.TryGetValue(family, out var state) && ReferenceEquals(state.reservation, token)) state.reservation = null;
    }
}
