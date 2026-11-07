using UnityEngine;

// Per-physical-weapon presentation. No damage, ammunition or cooldown rules.
[DisallowMultipleComponent, RequireComponent(typeof(Part))]
[DefaultExecutionOrder(150)]
public sealed class WeaponVisualSettings : MonoBehaviour
{
    public Material material;
    public Color color = new Color(1f, .8f, .35f);
    [Header("Weapon fire vfx")]
    [Tooltip("Child object of this weapon that holds its fire particle systems. They are played exactly " +
             "where they sit: nothing here changes their position, rotation or scale. " +
             "Left empty, firing plays nothing.")]
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
    ParticleSystem[] fireSystems;      // cached children of weaponFireVfx
    GameObject fireSystemsOwner;       // which object that cache was gathered from
    public bool IsSwingEmitting => swing != null && swing.emitting;
    void Awake() { part = GetComponent<Part>(); driver = GetComponent<MechWeaponAnimator>(); }

    // Plays every particle system under the assigned child object. The object is used exactly as it
    // was authored and placed: no position, rotation or scale is written here. No object, or one
    // without particle systems, plays nothing.
    public void PlayWeaponFireVfx()
    {
        if (weaponFireVfx == null) return;
        if (fireSystems == null || fireSystemsOwner != weaponFireVfx)
        {
            fireSystemsOwner = weaponFireVfx;
            fireSystems = weaponFireVfx.GetComponentsInChildren<ParticleSystem>(true);
        }
        if (fireSystems == null) return;
        for (int i = 0; i < fireSystems.Length; i++)
            if (fireSystems[i] != null) fireSystems[i].Play(true);
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
