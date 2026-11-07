using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CombatHealth))]
public sealed class EnemyCombatFeedback : MonoBehaviour
{
    [SerializeField] CombatFeedback feedback;
    [SerializeField] bool hitFlash = true;
    [SerializeField] bool deathEffect = true;
    [SerializeField, Range(0.02f, 0.3f)] float hitFlashDuration = 0.08f;
    [SerializeField, ColorUsage(false, true)] Color hitColor = new Color(2, 1.3f, 0.6f);
    [Header("Hurt Audio")]
    [Tooltip("BigRockHit1: one of the three is picked at random when this enemy is hurt.")]
    public AudioClip BigRockHit1;
    [Tooltip("BigRockHit2: one of the three is picked at random when this enemy is hurt.")]
    public AudioClip BigRockHit2;
    [Tooltip("BigRockHit3: one of the three is picked at random when this enemy is hurt.")]
    public AudioClip BigRockHit3;
    [Tooltip("Volume of the hurt sounds, on top of the clip's own level.")]
    [SerializeField, Range(0f, 1f)] float hitAudioVolume = 0.5f;
    CombatHealth health;
    AudioSource voice;
    Renderer[] renderers;
    MaterialPropertyBlock[] original;
    MaterialPropertyBlock[] flash;
    float flashRemaining;
    bool flashing;
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    static readonly int ColorProperty = Shader.PropertyToID("_Color");
    CombatFeedback Feedback => feedback != null ? feedback : CombatFeedback.Current;

    void Awake()
    {
        health = GetComponent<CombatHealth>();
        // Exclude health-bar lines and particle renderers from mesh tinting.
        var found = GetComponentsInChildren<Renderer>(true);
        int count = 0;
        foreach (var renderer in found) if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer) count++;
        renderers = new Renderer[count];
        original = new MaterialPropertyBlock[count];
        flash = new MaterialPropertyBlock[count];
        int index = 0;
        foreach (var renderer in found)
        {
            if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
            renderers[index] = renderer;
            original[index] = new MaterialPropertyBlock();
            flash[index++] = new MaterialPropertyBlock();
        }
    }

    void OnEnable() { health.Damaged += OnDamaged; health.Died += OnDied; }
    void OnDisable() { health.Damaged -= OnDamaged; health.Died -= OnDied; Restore(); }

    void OnDamaged(float amount)
    {
        var receiver = Feedback;
        // Audio first: it is damage feedback, not part of the hit-flash tint, so a disabled flash does
        // not silence it. The master presentation switch still does.
        if (receiver == null || receiver.VisualsEnabled) PlayRockHit();
        if (!hitFlash || receiver == null || !receiver.HitFeedbackEnabled) return;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            if (!flashing) renderers[i].GetPropertyBlock(original[i]);
            renderers[i].GetPropertyBlock(flash[i]);
            flash[i].SetColor(BaseColor, hitColor);
            flash[i].SetColor(ColorProperty, hitColor);
            renderers[i].SetPropertyBlock(flash[i]);
        }
        flashing = true;
        flashRemaining = hitFlashDuration;
    }

    // One of BigRockHit1..3 at random, re-pitched up to a quarter either way so repeated hits on the
    // same enemy do not sound identical. Only clips that are actually assigned take part in the pick.
    void PlayRockHit()
    {
        var clip = PickRockHit();
        if (clip == null || !isActiveAndEnabled) return;
        if (voice == null)
        {
            voice = gameObject.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.loop = false;
            voice.spatialBlend = 1f;                       // enemies are placed in the world
            voice.rolloffMode = AudioRolloffMode.Linear;
            voice.maxDistance = 40f;
        }
        voice.pitch = Random.Range(.75f, 1.25f);
        voice.PlayOneShot(clip, hitAudioVolume);
    }

    AudioClip PickRockHit()
    {
        int count = (BigRockHit1 != null ? 1 : 0) + (BigRockHit2 != null ? 1 : 0) + (BigRockHit3 != null ? 1 : 0);
        if (count == 0) return null;
        int pick = Random.Range(0, count);
        if (BigRockHit1 != null && pick-- == 0) return BigRockHit1;
        if (BigRockHit2 != null && pick-- == 0) return BigRockHit2;
        return BigRockHit3;
    }

    void OnDied()
    {
        var receiver = Feedback;
        if (!deathEffect || receiver == null) return;
        var bounds = new Bounds(transform.position, Vector3.one);
        bool found = false;
        foreach (var renderer in renderers)
        {
            if (renderer == null || !renderer.enabled) continue;
            if (!found) { bounds = renderer.bounds; found = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        // The receiver owns the effect at scene root, independently of this enemy.
        receiver.PlayDeath(bounds.center, bounds.extents.magnitude);
    }

    void LateUpdate()
    {
        if (!flashing) return;
        flashRemaining -= Time.deltaTime;
        var receiver = Feedback;
        if (flashRemaining <= 0 || !hitFlash || receiver == null || !receiver.HitFeedbackEnabled) Restore();
    }

    void Restore()
    {
        if (!flashing) return;
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null) renderers[i].SetPropertyBlock(original[i]);
        flashing = false;
    }
}
