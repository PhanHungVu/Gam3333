using UnityEngine;

// Per-physical-weapon presentation. No damage, ammunition or cooldown rules.
[DisallowMultipleComponent, RequireComponent(typeof(Part))]
[DefaultExecutionOrder(150)]
public sealed class WeaponVisualSettings : MonoBehaviour
{
    public Material material;
    public Color color = new Color(1f, .8f, .35f);
    public bool muzzleFlash = true;
    public bool muzzleSmoke = true;
    [Min(.01f)] public float muzzleSize = .35f;
    [Min(.04f)] public float muzzleDuration = .1f;
    public bool projectileTrail = true;
    [Min(.01f)] public float trailWidth = .06f;
    [Range(.03f, 1f)] public float trailLifetime = .2f;
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
    public bool IsSwingEmitting => swing != null && swing.emitting;
    void Awake() { part = GetComponent<Part>(); driver = GetComponent<MechWeaponAnimator>(); }
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
