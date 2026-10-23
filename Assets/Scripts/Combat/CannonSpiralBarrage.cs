using System;
using UnityEngine;

// A bounded pool of delayed gameplay sequences, independent of projectile and VFX lifetime.
[DisallowMultipleComponent]
[DefaultExecutionOrder(350)]
public sealed class CannonSpiralBarrage : MonoBehaviour
{
    [Serializable]
    public struct Settings
    {
        [Range(1, 24)] public int nodeCount;
        [Range(-360, 360)] public float angularIncrement;
        [Min(0.1f)] public float startRadius;
        [Min(0.01f)] public float spacing;
        [Min(0.08f)] public float initialDelay;
        [Min(0.08f)] public float nodeDelay;
        [Min(0.1f)] public float blastRadius;
        [Range(0, 1)] public float damageMultiplier;
        [Tooltip("Total actual health damage allowed across all secondary detonations; zero disables secondary damage.")]
        [Min(0)] public float maximumSecondaryDamage;
        [Tooltip("Distinct health components that may take secondary damage across one barrage.")]
        [Range(0, 32)] public int maximumSecondaryTargets;

        public static Settings Default => new Settings { nodeCount = 8, angularIncrement = 60f,
            startRadius = 6, spacing = 0.8f, initialDelay = 0.25f, nodeDelay = 0.18f,
            blastRadius = 2, damageMultiplier = 0.35f, maximumSecondaryDamage = 200, maximumSecondaryTargets = 5 };

        public Settings Validated()
        {
            var s = this;
            s.nodeCount = Mathf.Clamp(s.nodeCount, 1, 24);
            s.angularIncrement = Mathf.Clamp(s.angularIncrement, -360, 360);
            s.startRadius = Mathf.Max(0.1f, s.startRadius);
            s.spacing = Mathf.Max(0.01f, s.spacing);
            s.initialDelay = Mathf.Max(0.08f, s.initialDelay);
            s.nodeDelay = Mathf.Max(0.08f, s.nodeDelay);
            s.blastRadius = Mathf.Max(0.1f, s.blastRadius);
            s.damageMultiplier = Mathf.Clamp01(s.damageMultiplier);
            s.maximumSecondaryDamage = Mathf.Max(0, s.maximumSecondaryDamage);
            s.maximumSecondaryTargets = Mathf.Clamp(s.maximumSecondaryTargets, 0, 32);
            return s;
        }
    }

    sealed class Sequence
    {
        public bool Active;
        public Settings Settings;
        public Transform Owner;
        public Part Source;
        public Vector3 Origin, Heading;
        public LayerMask Layers;
        public float Damage, NextTime;
        public int NextNode, Hits, Kills;
        public readonly CombatBlast.Budget Budget = new CombatBlast.Budget();
    }

    const int Capacity = 8;
    readonly Sequence[] sequences = new Sequence[Capacity];
    readonly CombatBlast blast = new CombatBlast();
    MechCombat combat;
    public event Action<MechProjectileImpact> ImpactResolved;
    public int ActiveCount { get; private set; }
    public int PoolSize { get; private set; }

    void Awake() => combat = GetComponent<MechCombat>();

    public static Vector3 NodePosition(Vector3 origin, Vector3 heading, Settings settings, int index)
    {
        heading = Vector3.ProjectOnPlane(heading, Vector3.up).normalized;
        if (heading.sqrMagnitude < 0.001f) heading = Vector3.forward;
        return origin + Quaternion.AngleAxis(index * settings.angularIncrement, Vector3.up) * heading
            * (settings.startRadius + index * settings.spacing);
    }

    public bool TryBegin(MechProjectileImpact initial, float impactDamage, Vector3 heading,
        Transform owner, LayerMask layers, Settings settings)
    {
        if (!isActiveAndEnabled || owner == null || !owner.gameObject.activeInHierarchy
            || combat != null && !combat.isActiveAndEnabled) return false;
        Sequence sequence = null;
        for (int i = 0; i < Capacity; i++)
        {
            if (sequences[i] == null) { sequences[i] = new Sequence(); PoolSize++; }
            if (!sequences[i].Active) { sequence = sequences[i]; break; }
        }
        // Saturation falls back to the unchanged normal impact, never unbounded work.
        if (sequence == null) return false;
        sequence.Settings = settings.Validated();
        sequence.Owner = owner;
        sequence.Source = initial.Source;
        sequence.Origin = initial.Position;
        sequence.Heading = heading;
        sequence.Layers = layers;
        sequence.Damage = impactDamage * sequence.Settings.damageMultiplier;
        sequence.NextTime = Time.time + sequence.Settings.initialDelay;
        sequence.NextNode = 0;
        sequence.Hits = initial.Hits;
        sequence.Kills = initial.Kills;
        sequence.Budget.Reset(sequence.Settings.maximumSecondaryDamage, sequence.Settings.maximumSecondaryTargets);
        sequence.Active = true;
        ActiveCount++;
        return true;
    }

    void LateUpdate() => Tick(Time.time);

    void Tick(float now)
    {
        for (int i = 0; i < PoolSize; i++)
        {
            Sequence sequence = sequences[i];
            if (!sequence.Active) continue;
            if (sequence.Owner == null || !sequence.Owner.gameObject.activeInHierarchy) { Complete(sequence); continue; }
            if (now < sequence.NextTime) continue;
            Vector3 position = NodePosition(sequence.Origin, sequence.Heading, sequence.Settings, sequence.NextNode++);
            // One node per sequence per frame. A hitch stretches the schedule rather
            // than dumping overdue damage/flashes at once; normal intervals are fixed.
            sequence.NextTime = now + sequence.Settings.nodeDelay;
            var result = blast.Resolve(position, sequence.Settings.blastRadius,
                sequence.Damage, sequence.Owner, sequence.Layers, budget: sequence.Budget);
            sequence.Hits += result.Hits;
            sequence.Kills += result.Kills;
            try
            {
                ImpactResolved?.Invoke(new MechProjectileImpact(sequence.Source, PartSubtype.Explosive, position, sequence.Settings.blastRadius,
                    result.Hits, result.Kills, deferConfirmation: true, secondary: true));
            }
            finally
            {
                // A failing presentation listener must never leave extra damage scheduled.
                if (sequence.Active && sequence.NextNode >= sequence.Settings.nodeCount) Complete(sequence);
            }
        }
    }

    void Complete(Sequence sequence)
    {
        var result = new MechProjectileImpact(sequence.Source, PartSubtype.Explosive, sequence.Origin, 0,
            sequence.Hits, sequence.Kills, confirmationOnly: true);
        sequence.Active = false;
        sequence.Owner = null;
        sequence.Source = null;
        sequence.Budget.Targets.Clear();
        ActiveCount--;
        ImpactResolved?.Invoke(result);
    }

    public void CancelAll()
    {
        // Confirm only already-resolved kills. No damage remains scheduled after disable.
        for (int i = 0; i < PoolSize; i++) if (sequences[i].Active) Complete(sequences[i]);
    }
    void OnDisable() => CancelAll();
}
