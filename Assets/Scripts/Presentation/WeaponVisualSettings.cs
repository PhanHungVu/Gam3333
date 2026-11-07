using UnityEngine;

// Per-physical-weapon presentation. No damage, ammunition or cooldown rules.
[DisallowMultipleComponent, RequireComponent(typeof(Part))]
[DefaultExecutionOrder(150)]
public sealed class WeaponVisualSettings : MonoBehaviour
{
    public Material material;
    public Color color = new Color(1f, .8f, .35f);
    [Header("Weapon fire vfx")]
    [Tooltip("Holds the weapon's fire particle systems, all of which play when the weapon fires. " +
             "Use a child object of this weapon, or drop a VFX prefab in and it is spawned under the " +
             "muzzle once. Left empty, firing plays nothing.")]
    public GameObject weaponFireVfx;
    public bool projectileTrail = true;
    [Min(.01f)] public float trailWidth = .06f;
    [Range(.03f, 1f)] public float trailLifetime = .2f;
    [Tooltip("Seconds the last projectile path remains and fades after impact or range expiry.")]
    [Range(0f,3f)] public float projectileTrailLinger = .9f;
    [Min(.05f)] public float impactSize = .55f;
    [Range(0f, .2f)] public float firingShake = .025f;
    [Range(.1f, .5f)] public float firingShakeDuration = .12f;
    [Header("Melee swing trail")]
    public bool meleeTrail;
    public Transform trailAnchor;
    [Range(0f, 1f)] public float swingStart = .12f;
    [Range(0f, 1f)] public float swingEnd = .8f;
    Part part;
    MechWeaponAnimator driver;
    TrailRenderer swing;
    GameObject fireVfx;                // the object used at runtime (a spawned copy for a prefab asset)
    ParticleSystem[] fireSystems;      // cached children of fireVfx
    GameObject fireSystemsOwner;       // which object that cache was gathered from
    public bool IsSwingEmitting => swing != null && swing.emitting;
    void Awake() { part = GetComponent<Part>(); driver = GetComponent<MechWeaponAnimator>(); }

    // Every particle system under the weapon fire vfx object plays at once when the weapon fires.
    // No object, or one without particle systems, plays nothing.
    public void PlayWeaponFireVfx()
    {
        var holder = ResolveFireVfx();
        if (holder == null) return;
        if (!holder.activeSelf) holder.SetActive(true);
        if (fireSystems == null || fireSystemsOwner != holder)
        {
            fireSystemsOwner = holder;
            fireSystems = holder.GetComponentsInChildren<ParticleSystem>(true);
        }
        if (fireSystems == null) return;
        for (int i = 0; i < fireSystems.Length; i++)
        {
            var system = fireSystems[i];
            if (system == null) continue;
            // Designers often leave the puff objects switched off, expecting the shot to wake them.
            if (!system.gameObject.activeSelf) system.gameObject.SetActive(true);
            system.Clear(true);
            system.Play(true);
        }
    }

    // A child object is used where it sits. A prefab dropped in the slot is spawned once under the
    // muzzle, because a prefab asset has no scene to play particles in.
    GameObject ResolveFireVfx()
    {
        if (weaponFireVfx == null) return null;
        if (weaponFireVfx.scene.IsValid()) return weaponFireVfx;
        if (fireVfx == null)
        {
            var parent = part != null && part.muzzle != null ? part.muzzle : transform;
            fireVfx = Instantiate(weaponFireVfx, parent, false);
            fireVfx.name = weaponFireVfx.name + " (fire vfx)";
            fireVfx.transform.localPosition = Vector3.zero;
            fireVfx.transform.localRotation = Quaternion.identity;
        }
        return fireVfx;
    }

    void LateUpdate()
    {
        bool feedbackEnabled = CombatFeedback.Current != null && CombatFeedback.Current.VisualsEnabled;
        bool active = meleeTrail && part != null && part.isEquipped && driver != null && driver.CanAnimate
            && driver.CurrentPose == MechWeaponAnimator.PoseState.Fire
            && driver.NormalizedTime >= swingStart && driver.NormalizedTime <= swingEnd
            && feedbackEnabled;
        if (active && swing == null)
        {
            var anchor = trailAnchor != null ? trailAnchor : part.muzzle;
            if (anchor == null || material == null) return;
            // Bone rigs often have tiny imported scales. Keep width in world
            // metres while sampling the animated tip, as projectile trails do.
            var root = new GameObject("Swing Trail - " + name); root.transform.position = anchor.position;
            swing = root.AddComponent<TrailRenderer>();
            CombatProjectileTrail.Configure(swing, material, color, trailWidth, trailLifetime);
            swing.emitting = false;
        }
        if (swing == null) return;
        var tip = trailAnchor != null ? trailAnchor : part != null ? part.muzzle : null;
        if (active && tip != null) swing.transform.position = tip.position;
        if (active && !swing.emitting) swing.Clear();
        swing.emitting = active;
        // Record after the playable graph has moved the bone in LateUpdate.
        // Native automatic trail sampling may otherwise run before this pose.
        if (active && (swing.positionCount == 0 || Vector3.Distance(swing.GetPosition(swing.positionCount - 1), swing.transform.position) >= .02f))
            swing.AddPosition(swing.transform.position);
        if (!feedbackEnabled || part == null || !part.isEquipped || driver == null || !driver.CanAnimate) swing.Clear();
    }
    void OnDisable() { if (swing != null) { swing.emitting = false; swing.Clear(); } }
    void OnDestroy() { if (swing != null) Destroy(swing.gameObject); }
}
