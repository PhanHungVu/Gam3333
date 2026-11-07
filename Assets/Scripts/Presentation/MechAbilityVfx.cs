using System.Collections.Generic;
using UnityEngine;

// Shows each ability's effect on the mech: listens to MechAbilities (gameplay announces, presentation shows,
// like CombatFeedback does for the hurt VFX). The part's PartData says which effect (Ability Vfx).
// Timed abilities (Damage Amp, Heal) keep their effect playing until they end; instant ones (Jump) play it for
// Instant Seconds. Then the effect stops emitting, its last particles fade, and it is removed.
// Put it on the player, next to MechAbilities.
[DisallowMultipleComponent]
public sealed class MechAbilityVfx : MonoBehaviour
{
    [Tooltip("How long an instant ability's effect (e.g. Jump) plays before fading out.")]
    [SerializeField, Min(0f)] float instantSeconds = 1f;
    [Tooltip("Where effects sit, relative to the mech's root.")]
    [SerializeField] Vector3 offset = Vector3.zero;

    readonly Dictionary<PartCategory, GameObject> active = new Dictionary<PartCategory, GameObject>();
    MechAbilities abilities;

    public int ActiveCount => active.Count;

    void Awake() => abilities = GetComponent<MechAbilities>();
    void OnEnable()
    {
        if (abilities == null) return;
        abilities.AbilityStarted += OnStarted;
        abilities.AbilityEnded += OnEnded;
    }
    void OnDisable()
    {
        if (abilities != null) { abilities.AbilityStarted -= OnStarted; abilities.AbilityEnded -= OnEnded; }
        foreach (var effect in active.Values) StopAndClear(effect);
        active.Clear();
    }

    void OnStarted(PartCategory socket, Part part, float duration)
    {
        var prefab = part != null && part.Stats != null ? part.Stats.abilityVfx : null;
        if (prefab == null) return;
        if (active.TryGetValue(socket, out var previous)) { StopAndClear(previous); active.Remove(socket); }
        var effect = Instantiate(prefab, transform);
        effect.transform.localPosition = offset;
        effect.transform.localRotation = Quaternion.identity;
        foreach (var system in effect.GetComponentsInChildren<ParticleSystem>(true)) system.Play(true);
        if (duration > 0f) active[socket] = effect;
        else StartCoroutine(StopAfter(effect, instantSeconds));
    }

    void OnEnded(PartCategory socket, Part part)
    {
        if (!active.TryGetValue(socket, out var effect)) return; // instant effects end on their own timer
        active.Remove(socket);
        StopAndClear(effect);
    }

    System.Collections.IEnumerator StopAfter(GameObject effect, float seconds)
    {
        yield return new WaitForSeconds(seconds);
        StopAndClear(effect);
    }

    // Stops emitting, lets the particles already out finish, then removes the effect.
    static void StopAndClear(GameObject effect)
    {
        if (effect == null) return;
        float linger = 0f;
        foreach (var system in effect.GetComponentsInChildren<ParticleSystem>(true))
        {
            system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            linger = Mathf.Max(linger, system.main.startLifetime.constantMax);
        }
        Destroy(effect, linger + 0.1f);
    }
}
