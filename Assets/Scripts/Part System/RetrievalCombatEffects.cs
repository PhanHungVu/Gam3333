using System.Collections.Generic;
using UnityEngine;

// Combat listener, separate from targeting/travel and ordinary equip/attack cooldowns.
[DisallowMultipleComponent, RequireComponent(typeof(RemotePartRetrieval), typeof(RetrievalBonusCooldowns))]
[RequireComponent(typeof(RetrievalSpeedBuff))]
public sealed class RetrievalCombatEffects : MonoBehaviour
{
    [SerializeField] RetrievalEffectCatalogue catalogue;
    RemotePartRetrieval retrieval;
    RetrievalBonusCooldowns cooldowns;
    MechCombat combat;
    RetrievalSpeedBuff speedBuff;
    Part active;
    RetrievalEffectKind kind;
    RetrievalWeaponFamily family;
    object token;
    bool pathActive;
    RetrievalEffectCatalogue.Entry settings;
    float damage, radius;
    Vector3 boltOrigin;
    readonly HashSet<RetrievalPiercingBolt> bolts = new HashSet<RetrievalPiercingBolt>();
    readonly CombatBlast blast = new CombatBlast();
    readonly HashSet<CombatHealth> history = new HashSet<CombatHealth>();
    public RetrievalEffectCatalogue Catalogue => catalogue;
    public bool HasActivePath => active != null && pathActive;
    public int LastPathHits { get; private set; }
    public int LastBlastHits { get; private set; }
    public int BonusBlastCount { get; private set; }
    public int LastBarrageAxes { get; private set; }
    public int BonusBarrageCount { get; private set; }
    public int BonusBoltCount { get; private set; }
    public event System.Action<RetrievalPiercingBolt> BoltCreated;
    public event System.Action<MechProjectile, Vector3> BarrageProjectileCreated;
    void Awake()
    {
        retrieval = GetComponent<RemotePartRetrieval>(); cooldowns = GetComponent<RetrievalBonusCooldowns>(); combat = GetComponent<MechCombat>();
        speedBuff = GetComponent<RetrievalSpeedBuff>();
    }
    void OnEnable()
    {
        retrieval.Started += Begin; retrieval.Departed += Launch; retrieval.Travelled += Sweep;
        retrieval.Arrived += Arrive; retrieval.Cancelled += End;
    }
    void OnDisable()
    {
        retrieval.Started -= Begin; retrieval.Departed -= Launch; retrieval.Travelled -= Sweep;
        retrieval.Arrived -= Arrive; retrieval.Cancelled -= End; Clear();
        if (speedBuff != null) speedBuff.Clear();
        foreach (var bolt in new List<RetrievalPiercingBolt>(bolts)) if (bolt != null) bolt.Stop();
        bolts.Clear();
    }
    void Begin(Part part, PartCategory slot)
    {
        Clear(); LastPathHits = LastBlastHits = LastBarrageAxes = 0;
        if (catalogue == null || part == null || !catalogue.TryGet(part.Stats, out var entry) || entry.effect == RetrievalEffectKind.None) return;
        float value = Mathf.Max(0, part.Stats.GetStat(StatType.Damage)) * Mathf.Max(0, entry.damageMultiplier) * (combat != null ? combat.DamageModifiers.Total : 1);
        if (entry.effect == RetrievalEffectKind.ArrivalSpeedBuff)
        {
            if (!CanBuff(entry)) return;
        }
        else if (value <= 0) return;
        var reservation = new object();
        if (!cooldowns.TryReserve(entry.family, reservation)) return;
        active = part; token = reservation; family = entry.family; kind = entry.effect;
        settings = entry;
        damage = value; radius = Mathf.Max(.01f, entry.hitRadius);
        if (kind == RetrievalEffectKind.PiercingReturnBolt)
            boltOrigin = RetrievalPiercingBolt.GroundOrigin(part.PickupPoint, transform, entry.spawnHeight);
    }
    void Launch(Part part, PartCategory slot)
    {
        if (active != part) return;
        if (kind == RetrievalEffectKind.PiercingReturnBolt)
        {
            Vector3 target = RetrievalPiercingBolt.PlayerPoint(transform, settings.spawnHeight);
            if ((target - boltOrigin).sqrMagnitude < .0001f || !Commit()) return;
            Physics.SyncTransforms();
            LayerMask layers = combat != null ? combat.HitLayers : Physics.DefaultRaycastLayers;
            RetrievalPiercingBolt.Fire(part, transform, boltOrigin, target, damage, layers,
                combat != null ? combat.MetresPerGolem : 4f, settings, bolt =>
                {
                    BonusBoltCount++; bolts.Add(bolt);
                    bolt.Finished += _ => bolts.Remove(bolt);
                    BoltCreated?.Invoke(bolt);
                });
            return;
        }
        if (kind != RetrievalEffectKind.ReturnPath || !Commit()) return;
        // The lift/hover is anticipation only; the attack starts at fast departure.
        pathActive = true;
        Sweep(part, retrieval.ActivePickupPoint, retrieval.ActivePickupPoint);
    }
    void Sweep(Part part, Vector3 from, Vector3 to)
    {
        if (part == null || active != part || !pathActive || part.RetrievalOwner != retrieval) return;
        Physics.SyncTransforms();
        LayerMask layers = combat != null ? combat.HitLayers : Physics.DefaultRaycastLayers;
        layers &= Part.MovementQueryMask;
        var result = blast.ResolveCapsule(from, to, radius, damage, transform, layers, history, part);
        LastPathHits += result.Hits;
    }
    bool Commit()
    {
        if (token == null || !cooldowns.TryCommit(family, token)) return false;
        token = null; return true;
    }
    bool CanBuff(RetrievalEffectCatalogue.Entry entry) => speedBuff != null
        && speedBuff.CanApply(entry.buffDuration, entry.movementMultiplier, entry.attackCadenceMultiplier);
    void Arrive(Part part, PartCategory slot)
    {
        bool trigger = isActiveAndEnabled && part != null && active == part
            && (kind == RetrievalEffectKind.ArrivalBlast || kind == RetrievalEffectKind.ArrivalBarrage || kind == RetrievalEffectKind.ArrivalSpeedBuff && CanBuff(settings))
            && part.isEquipped && part.EquippedBy == retrieval.Equipment && Commit();
        var effect = kind; var profile = settings;
        float blastDamage = damage, blastRadius = radius;
        Clear();
        if (!trigger) return;
        if (effect == RetrievalEffectKind.ArrivalSpeedBuff)
        {
            speedBuff.Apply(profile.buffDuration, profile.movementMultiplier, profile.attackCadenceMultiplier);
            return;
        }
        Physics.SyncTransforms();
        LayerMask layers = combat != null ? combat.HitLayers : Physics.DefaultRaycastLayers;
        layers &= Part.MovementQueryMask;
        if (effect == RetrievalEffectKind.ArrivalBarrage)
        {
            LastBarrageAxes = RetrievalAxeBarrage.Fire(part, transform, blastDamage, layers, combat != null ? combat.MetresPerGolem : 4f,
                profile, (shot, direction) => BarrageProjectileCreated?.Invoke(shot, direction));
            if (LastBarrageAxes > 0) BonusBarrageCount++;
            return;
        }
        Vector3 origin = transform.position + Vector3.up * .5f;
        var result = blast.Resolve(origin, blastRadius, 0, blastDamage, transform, layers);
        LastBlastHits = result.Hits; BonusBlastCount++;
        CombatVisualEvents.ReportRetrievalBlast(part, transform, origin, blastRadius, result);
    }
    void End(Part part, PartCategory slot) => Clear();
    void Clear()
    {
        if (token != null) cooldowns.Release(family, token);
        token = null; active = null; pathActive = false; history.Clear();
    }
}
