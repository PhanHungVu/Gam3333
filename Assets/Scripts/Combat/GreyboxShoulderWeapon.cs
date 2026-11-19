using System.Collections.Generic;
using UnityEngine;

// Per-part mechanics only. Equipment, input admission, ammo, cooldowns, health,
// projectiles and all damage queries remain in the existing shared systems.
[DisallowMultipleComponent, RequireComponent(typeof(Part))]
[DefaultExecutionOrder(301)]
public sealed class GreyboxShoulderWeapon : MonoBehaviour, ICombatDamageBlocker
{
    public enum Weapon { Shield = 3, GiantRock, FishingLine, Mortar, Katyusha, Tentacles, TeslaCoil }
    public Weapon weapon;
    public bool keepAuthoredStats;
    [Header("Shield")]
    [Min(.01f)] public float blockDuration = .5f;
    public bool blockAllDirections = true;
    [Range(1, 180)] public float frontalHalfAngle = 90;
    [Header("Rock ring (Range is outer radius; AoE is band thickness)")]
    [Min(.1f)] public float ringRangeScale = 1;
    [Min(.1f)] public float ringThicknessScale = 1;
    [Min(.05f)] public float ringSweepSeconds = .8f;
    [Min(.1f)] public float ringHalfHeight = 1.5f;
    [Header("Mortar (horizontal speed is the PartData Projectile Speed)")]
    [Min(.1f)] public float mortarGravity = 25;
    [Min(.05f)] public float mortarMinimumFlight = .55f;
    [Header("Tentacle")]
    [Min(.1f)] public float tentacleWidth = 1.25f;
    [Min(.1f)] public float tentacleHeight = 3;
    [Min(.05f)] public float tentacleSeconds = .25f;
    [Header("Katyusha salvo")]
    [Tooltip("Full horizontal fan angle. Intrinsic salvo spread is separate from head Accuracy.")]
    [Range(0, 45)] public float salvoFanAngle = 8;
    [Tooltip("Metres between the two-row rack's launch points. Zero stacks their origins for diagnostics.")]
    [Range(0, 1)] public float salvoLaunchSpacing = .3f;
    [Header("Tesla")]
    [Min(.1f)] public float chainDistance = 4;
    [Range(1, 32)] public int maximumChainTargets = 5;
    [Min(.01f)] public float lightningSeconds = .15f;
    [Header("Primitive presentation")]
    public Material debugMaterial;
    public Transform shieldVisual, rockVisual, tentacleVisual;
    public LineRenderer rope;

    Part part;
    MechCombat owner;
    CombatHealth defender;
    Vector3 shieldRestPosition;
    Quaternion shieldRestRotation;
    float remaining, duration, shieldUntil, damage, swept;
    Vector3 tentacleCenter, tentacleSize;
    Quaternion tentacleRotation;
    LineRenderer innerRing, outerRing;
    readonly LineRenderer[] lightning = new LineRenderer[32];
    readonly Vector3[] nodes = new Vector3[33];
    readonly HashSet<CombatHealth> victims = new HashSet<CombatHealth>();
    public bool HandlesAttack => weapon == Weapon.Shield || weapon == Weapon.GiantRock || weapon == Weapon.Tentacles || weapon == Weapon.TeslaCoil;
    public bool IsActive => remaining > 0 && Equipped;
    bool Equipped => part != null && part.isEquipped && part.EquippedBy != null && owner != null
        && part.EquippedBy.gameObject == owner.gameObject && part.transform.IsChildOf(owner.transform) && owner.isActiveAndEnabled;
    public int LastHitCount { get; private set; }

    void Awake()
    {
        part = GetComponent<Part>();
        if (keepAuthoredStats && Application.isPlaying) part.KeepAuthoredStats();
        if (shieldVisual != null) { shieldRestPosition = shieldVisual.localPosition; shieldRestRotation = shieldVisual.localRotation; }
    }
    public bool TryAttack(MechCombat combat)
    {
        if (!HandlesAttack || part == null || part.Stats == null || part.ReadyAt > Time.time
            || remaining > 0 && weapon != Weapon.TeslaCoil) return false;
        owner = combat;
        if (!Equipped || CombatHitStop.BlocksNewActions(owner.transform)) return false;
        bool ammo = weapon == Weapon.TeslaCoil;
        if (ammo && part.RemainingAmmo <= 0) return false;
        var preferred = owner.Targeting != null && owner.Targeting.CurrentTarget != null
            ? owner.Targeting.CurrentTarget.GetComponentInParent<CombatHealth>() : null;
        Collider target = null;
        if (weapon == Weapon.Tentacles || weapon == Weapon.TeslaCoil)
        {
            target = owner.Blast.FindEnemy(Origin, owner.RangeMetresOf(part), owner.transform, owner.HitLayers, preferred);
            if (target == null) return false;
        }
        part.Consume(part.Stats.ShotInterval, ammo, part.Stats.GetStat(StatType.Reload), part.Stats.MagazineSize);
        damage = Mathf.Max(0, part.Stats.GetStat(StatType.Damage)) * owner.DamageModifiers.Total;
        victims.Clear(); LastHitCount = 0;
        owner.RaiseAttackStarted(part);
        switch (weapon)
        {
            case Weapon.Shield:
                defender = owner.GetComponent<CombatHealth>();
                duration = remaining = Mathf.Max(.01f, blockDuration); shieldUntil = Time.time + remaining;
                if (defender != null) defender.AddDamageBlocker(this);
                UpdateShield(); break;
            case Weapon.GiantRock:
                duration = remaining = Mathf.Max(.05f, ringSweepSeconds); swept = 0;
                DrawRing(); break;
            case Weapon.Tentacles: StrikeTentacle(target); break;
            case Weapon.TeslaCoil: Chain(target); break;
        }
        return true;
    }
    Vector3 Origin => owner.transform.position + Vector3.up * .5f;
    public Vector3 SalvoMuzzle(Vector3 muzzle, Vector3 center, int index, int count)
    {
        int columns = Mathf.Max(1, (count + 1) / 2), rows = count > 1 ? 2 : 1;
        var offset = new Vector3(index % columns - (columns - 1) * .5f, index / columns - (rows - 1) * .5f, 0);
        Vector3 direction = center - muzzle;
        return muzzle + Quaternion.LookRotation(direction.sqrMagnitude > .0001f ? direction : transform.forward)
            * (offset * Mathf.Max(0, salvoLaunchSpacing));
    }
    public Vector3 SalvoAimPoint(Vector3 muzzle, Vector3 center, int index, int count)
    {
        Vector3 direction = center - muzzle;
        if (direction.sqrMagnitude < .0001f) direction = transform.forward;
        float angle = count > 1 ? (index / (float)(count - 1) - .5f) * Mathf.Clamp(salvoFanAngle, 0, 45) : 0;
        return muzzle + Quaternion.AngleAxis(angle, Vector3.up) * direction;
    }
    public bool BlocksDamage(float amount, Vector3? sourcePosition)
    {
        if (weapon != Weapon.Shield || !IsActive || defender == null || defender.IsDead || Time.time >= shieldUntil) return false;
        if (blockAllDirections) return true;
        if (!sourcePosition.HasValue) return false;
        var incoming = Vector3.ProjectOnPlane(sourcePosition.Value - owner.transform.position, Vector3.up);
        return incoming.sqrMagnitude < .0001f || Vector3.Angle(owner.transform.forward, incoming) <= frontalHalfAngle;
    }
    void Update() => Tick(Time.deltaTime);
    void Tick(float dt)
    {
        if (remaining <= 0) return;
        if (!Equipped || MissionOutcomeController.BlocksGameplay(gameObject.scene)) { Cancel(); return; }
        if (dt <= 0 || weapon != Weapon.Shield && CombatHitStop.IsFrozen(owner.transform)) return;
        float step = Mathf.Min(remaining, dt);
        if (weapon == Weapon.GiantRock)
        {
            float degrees = 360 * step / duration;
            float outer = owner.RangeMetresOf(part) * Mathf.Max(.1f, ringRangeScale);
            float width = Mathf.Clamp(part.Stats.GetStat(StatType.SplashRadius) * ringThicknessScale, .01f, outer);
            var result = owner.Blast.ResolveMeleeRing(Origin, outer, width, ringHalfHeight, swept, degrees, damage,
                owner.transform, owner.HitLayers, victims, part);
            LastHitCount += result.Hits; swept += degrees;
        }
        remaining = Mathf.Max(0, remaining - step);
        if (remaining < .00001f) remaining = 0;
        if (weapon == Weapon.Shield) UpdateShield();
        else if (weapon == Weapon.GiantRock) DrawRing();
        else if (weapon == Weapon.Tentacles) DrawTentacle();
        if (remaining <= 0) Cancel();
    }
    void UpdateShield()
    {
        if (shieldVisual != null && owner != null)
            shieldVisual.SetPositionAndRotation(owner.transform.position + owner.transform.forward * 2 + Vector3.up * 1.2f, owner.transform.rotation);
    }
    void StrikeTentacle(Collider target)
    {
        Vector3 direction = target.bounds.center - Origin;
        float length = Mathf.Min(owner.RangeMetresOf(part), direction.magnitude + .5f);
        if (direction.sqrMagnitude < .0001f) direction = owner.transform.forward;
        tentacleRotation = Quaternion.LookRotation(direction.normalized);
        tentacleCenter = Origin + direction.normalized * (length * .5f);
        tentacleSize = new Vector3(tentacleWidth, tentacleHeight, Mathf.Max(.1f, length));
        LastHitCount = owner.Blast.ResolveBox(tentacleCenter, tentacleSize * .5f, tentacleRotation, damage,
            owner.transform, owner.HitLayers, Origin, victims, part).Hits;
        duration = remaining = Mathf.Max(.05f, tentacleSeconds); DrawTentacle();
    }
    void DrawTentacle()
    {
        if (tentacleVisual == null) return;
        tentacleVisual.gameObject.SetActive(true);
        float progress = Mathf.Clamp01(1 - remaining / duration);
        float extension = Mathf.Max(.05f, progress < .4f ? progress / .4f : (1 - progress) / .6f);
        Vector3 back = tentacleCenter - tentacleRotation * Vector3.forward * tentacleSize.z * .5f;
        tentacleVisual.SetPositionAndRotation(back + tentacleRotation * Vector3.forward * tentacleSize.z * extension * .5f, tentacleRotation);
        SetWorldScale(tentacleVisual, new Vector3(.25f, .25f, tentacleSize.z * extension));
    }
    void Chain(Collider target)
    {
        // Visual duration must not throttle gameplay. Reuse lines for the latest
        // pulse and hide any extra links left by a longer preceding chain.
        foreach (var link in lightning) if (link != null) link.enabled = false;
        nodes[0] = part.muzzle != null ? part.muzzle.position : Origin;
        int count = 0;
        while (target != null && count < Mathf.Clamp(maximumChainTargets, 1, 32))
        {
            var health = target.GetComponentInParent<CombatHealth>();
            if (health == null || !victims.Add(health)) break;
            Vector3 point = target.bounds.center;
            var result = owner.Blast.Resolve(point, 0, damage, 0, owner.transform, owner.HitLayers, target, nodes[count]);
            if (result.Hits == 0) break; // No visual arc to an undamaged/blocked target.
            LastHitCount += result.Hits;
            nodes[++count] = point;
            target = owner.Blast.FindEnemy(point, chainDistance, owner.transform, owner.HitLayers,
                excluded: victims, requireCenterWithinRange: true);
        }
        for (int i = 0; i < count; i++)
        {
            if (lightning[i] == null) lightning[i] = Line("Tesla link " + (i + 1), new Color(.6f, .85f, 1), .06f);
            lightning[i].positionCount = 2; lightning[i].SetPosition(0, nodes[i]); lightning[i].SetPosition(1, nodes[i + 1]); lightning[i].enabled = true;
        }
        duration = remaining = Mathf.Max(.01f, lightningSeconds);
    }
    LineRenderer Line(string label, Color color, float width)
    {
        var line = new GameObject(label).AddComponent<LineRenderer>(); line.transform.SetParent(transform, false);
        line.useWorldSpace = true; line.sharedMaterial = debugMaterial;
        line.startWidth = line.endWidth = width; line.startColor = line.endColor = color;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false; return line;
    }
    void DrawRing()
    {
        float outer = owner.RangeMetresOf(part) * Mathf.Max(.1f, ringRangeScale);
        float width = Mathf.Clamp(part.Stats.GetStat(StatType.SplashRadius) * ringThicknessScale, .01f, outer);
        if (innerRing == null) innerRing = Line("Ring inner edge", new Color(1,.55f,.15f), .04f);
        if (outerRing == null) outerRing = Line("Ring outer edge", new Color(1,.55f,.15f), .04f);
        Circle(innerRing, outer - width); Circle(outerRing, outer);
        Vector3 radial = new Vector3(Mathf.Sin(swept * Mathf.Deg2Rad), 0, Mathf.Cos(swept * Mathf.Deg2Rad));
        Vector3 point = Origin + radial * (outer - width * .5f);
        if (rockVisual != null) { rockVisual.gameObject.SetActive(true); rockVisual.position = point; }
        if (rope != null) { rope.enabled = true; rope.positionCount = 2; rope.SetPosition(0, part.muzzle != null ? part.muzzle.position : Origin); rope.SetPosition(1, point); }
    }
    void Circle(LineRenderer line, float radius)
    {
        line.enabled = true; line.loop = true; line.positionCount = 64;
        for (int i = 0; i < 64; i++) { float angle = i * Mathf.PI * 2 / 64; line.SetPosition(i, Origin + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * radius); }
    }
    static void SetWorldScale(Transform target, Vector3 scale)
    {
        Vector3 parent = target.parent != null ? target.parent.lossyScale : Vector3.one;
        target.localScale = new Vector3(scale.x / parent.x, scale.y / parent.y, scale.z / parent.z);
    }
    void Cancel()
    {
        remaining = 0;
        if (defender != null) defender.RemoveDamageBlocker(this); defender = null;
        if (shieldVisual != null) { shieldVisual.localPosition = shieldRestPosition; shieldVisual.localRotation = shieldRestRotation; }
        if (rockVisual != null) rockVisual.gameObject.SetActive(false);
        if (tentacleVisual != null) tentacleVisual.gameObject.SetActive(false);
        if (rope != null) rope.enabled = false;
        if (innerRing != null) innerRing.enabled = false;
        if (outerRing != null) outerRing.enabled = false;
        foreach (var link in lightning) if (link != null) link.enabled = false;
    }
    void OnDisable() => Cancel();
    void OnTransformParentChanged() { if (remaining > 0 && !Equipped) Cancel(); }
}
