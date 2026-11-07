using UnityEngine;

public class Part : MonoBehaviour
{
    // Loose parts settle on the world, but never obstruct characters or the camera.
    public const string LoosePhysicsLayerName = "MechPart";
    public static int MovementQueryMask
    {
        get
        {
            int layer = LayerMask.NameToLayer(LoosePhysicsLayerName);
            return layer < 0 ? Physics.DefaultRaycastLayers : Physics.DefaultRaycastLayers & ~(1 << layer);
        }
    }
    public static void ConfigureCharacterCollisions(GameObject actor)
    {
        int layer = LayerMask.NameToLayer(LoosePhysicsLayerName);
        if (layer < 0) return;
        foreach (var collider in actor.GetComponentsInChildren<Collider>(true))
        {
            // Pickup triggers must still detect parts. Enemy limb colliders can
            // remain solid, so include them but preserve their settings for a drop.
            if (collider.isTrigger) continue;
            var part = collider.GetComponentInParent<Part>();
            if (part != null) part.CachePhysics();
            collider.excludeLayers |= 1 << layer;
        }
    }
    public Transform recoilVisual;
    [Tooltip("Direction the recoil visual kicks, in its local space.")]
    public Vector3 recoilAxis = Vector3.left;
    public Transform muzzle;
    Transform overriddenMuzzle;
    Pose authoredMuzzlePose;
    // Attachment-specific marker corrections never change the shared weapon prefab or enemy copies.
    internal void ApplyMuzzlePose(bool apply, Pose pose)
    {
        if (overriddenMuzzle != null && (!apply || overriddenMuzzle != muzzle))
        {
            overriddenMuzzle.SetLocalPositionAndRotation(authoredMuzzlePose.position, authoredMuzzlePose.rotation);
            overriddenMuzzle = null;
        }
        if (!apply || muzzle == null) return;
        if (overriddenMuzzle == null)
        { overriddenMuzzle = muzzle; authoredMuzzlePose = new Pose(muzzle.localPosition, muzzle.localRotation); }
        muzzle.SetLocalPositionAndRotation(pose.position, pose.rotation);
    }
    public Vector3 authoredScale = Vector3.one;
    public PartData data;    
    // What this part is, read from its data (the single source of truth). Parts have no side:
    // which arm/shoulder socket it uses is decided when it is equipped.
    public PartType partType => data != null ? data.partType : default;
    // This part's stats. Systems read stats only through here, so per-part stat rolls can plug in later.
    public PartData Stats => data;
    // Slot this part currently occupies; null while it is loose.
    public PartCategory? Slot { get; internal set; }
    public bool isStartingPart = true;
    public bool isEquipped = false;
    public EquipManager EquippedBy { get; internal set; }
    public int RemainingAmmo { get; private set; }   // total shots left; at 0 the weapon is empty
    public int MagazineRounds { get; private set; }  // shots left before the next reload
    public float ReadyAt { get; private set; }       // next shot or swing allowed (fire rate and reload)
    public bool IsReloading => reloadEndsAt > Time.time;
    float readyStartedAt;
    public float CooldownRemaining => Mathf.Max(0,ReadyAt-Time.time);
    public float CooldownDuration => Mathf.Max(0,ReadyAt-readyStartedAt);
    public float CooldownProgress => CooldownRemaining<=0 || CooldownDuration<=0 ? 1 : Mathf.Clamp01(1-CooldownRemaining/CooldownDuration);
    // Presentation only. Remember an expired wait even if held fire immediately starts the next shot.
    public uint WeaponCooldownCycle { get; private set; }
    uint completedWeaponCue, consumedWeaponCue;
    bool cooldownUsesAmmo;
    float reloadEndsAt;
    bool initialized;
    Collider[] colliders;
    bool[] colliderStates;
    int[] colliderLayers;
    int[] colliderExclusions;
    PickupHighlight pickupHighlight;
    void Awake() { if (EquippedBy == null && !isEquipped) SetEquippedPhysics(false); }

    // State belongs to this physical part, not the shared PartData asset.
    internal void InitializeAmmo(int ammunition, int magazine)
    {
        if (initialized) return;
        RemainingAmmo = ammunition;
        MagazineRounds = magazine > 0 ? Mathf.Min(magazine, ammunition) : ammunition;
        initialized = true;
    }

    // One shot or swing: the next is allowed after interval (1 / Fire Rate). A shot also uses a round;
    // when that empties the magazine and ammo remains, the reload starts by itself, and the next shot
    // waits for both the interval and the reload. A magazine of 0 never reloads.
    internal void Consume(float interval, bool ammunition, float reload = 0f, int magazine = 0)
    {
        RecordCompletedWeaponCooldown();
        WeaponCooldownCycle++;
        cooldownUsesAmmo = ammunition;
        readyStartedAt = Time.time;
        ReadyAt = Time.time + interval;
        if (!ammunition) return;
        RemainingAmmo = Mathf.Max(0, RemainingAmmo - 1);
        MagazineRounds = Mathf.Max(0, MagazineRounds - 1);
        if (RemainingAmmo <= 0) { reloadEndsAt = 0f; return; } // empty: nothing left to reload
        if (MagazineRounds > 0) return;
        MagazineRounds = magazine > 0 ? Mathf.Min(magazine, RemainingAmmo) : RemainingAmmo;
        if (magazine <= 0) return;
        reloadEndsAt = Time.time + Mathf.Max(0f, reload);
        ReadyAt = Mathf.Max(ReadyAt, reloadEndsAt);
    }
    // RemainingAmmo includes the loaded rounds. Reloading moves existing rounds
    // into the magazine; it neither consumes nor creates ammunition.
    public bool TryReload()
    {
        if (!initialized || Stats == null || IsReloading) return false;
        int capacity = Stats.MagazineSize;
        int loaded = Mathf.Min(capacity, RemainingAmmo);
        if (capacity <= 0 || MagazineRounds >= loaded) return false;
        RecordCompletedWeaponCooldown();
        WeaponCooldownCycle++;
        cooldownUsesAmmo = true;
        MagazineRounds = loaded;
        readyStartedAt = Time.time;
        reloadEndsAt = Time.time + Mathf.Max(0f, Stats.GetStat(StatType.Reload));
        ReadyAt = Mathf.Max(ReadyAt, reloadEndsAt);
        return true;
    }
    void RecordCompletedWeaponCooldown()
    {
        if (WeaponCooldownCycle != 0 && ReadyAt > readyStartedAt && CooldownRemaining <= 0
            && (!cooldownUsesAmmo || RemainingAmmo > 0)) completedWeaponCue = WeaponCooldownCycle;
    }
    public bool TryConsumeWeaponReadyCue(uint firstObservedCycle)
    {
        RecordCompletedWeaponCooldown();
        if (completedWeaponCue == 0 || completedWeaponCue < firstObservedCycle || consumedWeaponCue == completedWeaponCue) return false;
        consumedWeaponCue = completedWeaponCue;
        return true;
    }
    void CachePhysics()
    {
        if (colliders == null)
        {
            var own = new System.Collections.Generic.List<Collider>();
            foreach (var c in GetComponentsInChildren<Collider>(true))
                if (c.GetComponentInParent<Part>() == this) own.Add(c);
            colliders = own.ToArray(); colliderStates = new bool[colliders.Length];
            colliderLayers = new int[colliders.Length];
            colliderExclusions = new int[colliders.Length];
            for (int i = 0; i < colliders.Length; i++)
            {
                colliderStates[i] = colliders[i].enabled;
                colliderLayers[i] = colliders[i].gameObject.layer;
                colliderExclusions[i] = colliders[i].excludeLayers;
            }
        }
    }
    internal void SetEquippedPhysics(bool equipped)
    {
        CachePhysics();
        int looseLayer = LayerMask.NameToLayer(LoosePhysicsLayerName);
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null)
            {
                // All loose collider children share the pickup layer, including
                // custom authored layers. Attachment restores each original layer.
                colliders[i].gameObject.layer = !equipped && looseLayer >= 0
                    ? looseLayer : colliderLayers[i];
                colliders[i].enabled = !equipped && colliderStates[i];
                if (!equipped) colliders[i].excludeLayers = colliderExclusions[i];
            }
        var body = GetComponent<Rigidbody>();
        if (!equipped && body == null) body = gameObject.AddComponent<Rigidbody>();
        if (body != null) { body.isKinematic = equipped; body.useGravity = !equipped; body.detectCollisions = !equipped; }
        // Stop visual playback immediately on drop, including within the same frame.
        var weaponAnimator = GetComponent<MechWeaponAnimator>();
        if (weaponAnimator != null) weaponAnimator.RefreshEquipment();
        // Only actual loose pickups receive the reusable marker; carried parts/corpses keep their normal visuals.
        if (Application.isPlaying && !equipped && data != null && pickupHighlight == null && GetComponentInParent<CombatHealth>() == null)
            pickupHighlight = PickupHighlight.Attach(transform);
        if (pickupHighlight != null) pickupHighlight.gameObject.SetActive(!equipped);
    }
}
