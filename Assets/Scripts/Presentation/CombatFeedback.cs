using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(MechCombat))]
[DefaultExecutionOrder(400)]
public sealed class CombatFeedback : MonoBehaviour
{
    public static CombatFeedback Current { get; private set; }
    [Header("Master / Pool")]
    [SerializeField] bool presentationEnabled = true;
    [SerializeField] Material effectMaterial;
    [SerializeField] Material smokeMaterial;
    [SerializeField, Range(0, 32)] int prewarmEffects = 8;
    [SerializeField, Range(4, 64)] int maximumEffects = 32;
    [SerializeField] bool projectileTrails = true;
    [SerializeField] bool weaponImpacts = true;
    [SerializeField, Range(4, 128)] int maximumTrails = 64;
    [SerializeField] Color dustColor = new Color(.63f, .58f, .48f, .65f);
    [Header("Cannon Fire")]
    [SerializeField] bool muzzleFlash = true;
    [SerializeField, Min(0.04f)] float muzzleDuration = 0.1f;
    [SerializeField, Min(0.1f)] float muzzleSize = 1.8f;
    [SerializeField] bool visualRecoil = true;
    [Tooltip("A mesh-only child, separate from the root that boost presentation rotates.")]
    [SerializeField] Transform recoilVisual;
    [SerializeField, Range(0, 0.5f)] float recoilDistance = 0.22f;
    [SerializeField, Range(0, 12)] float recoilDegrees = 5f;
    [SerializeField, Min(0.04f)] float recoilDuration = 0.22f;
    [Header("Cannon Explosion")]
    [SerializeField] bool explosionFlash = true;
    [SerializeField] bool shockwaveRing = true;
    [SerializeField] bool secondaryEmbers = true;
    [SerializeField, Min(0.1f)] float explosionDuration = 0.55f;
    [SerializeField] Color explosionColor = new Color(1f, 0.35f, 0.04f);
    [Header("Enemies")]
    [SerializeField] bool enemyHitFlash = true;
    [SerializeField] bool enemyDeathEffects = true;
    [SerializeField, Min(0.1f)] float deathDuration = 0.65f;
    [SerializeField] Color deathColor = new Color(1, 0.35f, 0.08f);
    [Header("Kill Confirmation")]
    [SerializeField] bool killConfirmation = true;
    [SerializeField, Min(0.1f)] float confirmationDuration = 0.9f;
    [Header("Cinemachine Impact Impulse")]
    [SerializeField] bool cameraImpulse = true;
    [SerializeField] CameraControlThirdPerson cameraControl;
    [SerializeField, Range(0, 0.2f)] float impulseStrength = 0.09f;
    [SerializeField, Range(0.05f, 0.3f)] float impulseDuration = 0.14f;
    [SerializeField, Min(1)] float impulseRange = 45f;
    [Header("Firing and player damage impulses")]
    [SerializeField] bool firingCameraImpulse = true;
    [SerializeField] bool damageCameraImpulse = true;
    [SerializeField, Range(0, .2f)] float damageImpulseStrength = .16f;
    [SerializeField, Range(.05f, .5f)] float damageImpulseDuration = .24f;
    [Header("Confirmed hits and actor hitstop")]
    [SerializeField] bool hitCameraImpulse = true;
    [SerializeField, Range(0,.2f)] float hitImpulseStrength = .12f;
    [SerializeField, Range(.05f,.5f)] float hitImpulseDuration = .14f;
    [SerializeField, Min(0)] float hitShakeInterval = .06f;
    [SerializeField] bool hitStop = true;
    [SerializeField, Range(0,.15f)] float meleeHitStop = .07f;
    [SerializeField, Range(0,.15f)] float projectileHitStop = .035f;
    [SerializeField, Range(0,.15f)] float blastHitStop = .085f;
    [SerializeField, Range(0,.15f)] float damageHitStop = .08f;
    float nextHitShake;
    [Header("Optional Audio (empty is silent)")]
    [SerializeField] bool fireAudio = true;
    [SerializeField] bool impactAudio = true;
    [SerializeField] bool confirmationAudio = true;
    [SerializeField] AudioClip cannonFireClip;
    [SerializeField] AudioClip cannonImpactClip;
    [SerializeField] AudioClip multiKillClip;
    [SerializeField, Range(0, 1)] float audioVolume = 0.5f;

    CombatHealth health;
    Transform defaultRecoilVisual;
    CombatFeedbackEffect[] effects;
    GameObject poolRoot;
    int created;
    CombatProjectileTrail[] trails;
    int createdTrails;
    readonly AudioSource[] voices = new AudioSource[4];
    int nextVoice;
    Vector3 restPosition;
    Quaternion restRotation;
    bool recoiling;
    bool presentationStopped;
    float recoilAge, confirmationAge;
    Canvas confirmationCanvas;
    Text confirmationText;
    CinemachineImpulseListener impulseListener;
    readonly CinemachineImpulseDefinition impulse = new CinemachineImpulseDefinition
    {
        ImpulseChannel = 64,
        ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Bump,
        ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform
    };
    public bool HitFeedbackEnabled => isActiveAndEnabled && presentationEnabled && enemyHitFlash;
    public bool VisualsEnabled => isActiveAndEnabled && presentationEnabled;
    public int TrailPoolSize => createdTrails;
    public int FiringImpulseCount { get; private set; }
    public int DamageImpulseCount { get; private set; }
    public int HitImpulseCount { get; private set; }
    public int PoolSize => created;
    public int KillConfirmationCount { get; private set; }
    public int LastConfirmedKills { get; private set; }
    public int ActiveEffects
    {
        get { int count = 0; for (int i = 0; i < created; i++) if (effects[i] != null && effects[i].IsPlaying) count++; return count; }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Current = null;

    Vector3 recoilAxis = Vector3.back;
    public void BindRecoilVisual(Transform visual, Vector3 localAxis = default)
    {
        RestoreRecoil();
        recoilAxis = localAxis.sqrMagnitude > 0.001f ? localAxis.normalized : Vector3.back;
        recoilVisual = visual != null && visual.IsChildOf(transform) && visual != transform
            && visual.GetComponentInChildren<Collider>(true) == null ? visual : null;
        if (recoilVisual != null) { restPosition = recoilVisual.localPosition; restRotation = recoilVisual.localRotation; }
    }

    void Awake()
    {
        health = GetComponent<CombatHealth>();
        if (recoilVisual == null) recoilVisual = transform.Find("VisualMech/Body");
        // Never displace a gameplay collider or the boost presentation's VisualMech root.
        if (recoilVisual != null && (!recoilVisual.IsChildOf(transform) || recoilVisual == transform
            || recoilVisual == transform.Find("VisualMech") || recoilVisual.GetComponentInChildren<Collider>(true) != null))
            recoilVisual = null;
        if (recoilVisual != null) { restPosition = recoilVisual.localPosition; restRotation = recoilVisual.localRotation; }
        defaultRecoilVisual = recoilVisual;
        effects = new CombatFeedbackEffect[Mathf.Clamp(maximumEffects, 4, 64)];
        trails = new CombatProjectileTrail[Mathf.Clamp(maximumTrails, 4, 128)];
        poolRoot = new GameObject("Combat Feedback Pool");
        if (effectMaterial != null)
            for (int i = 0; i < Mathf.Min(prewarmEffects, effects.Length); i++) CreateEffect();
        CreateConfirmation();
    }

    void Start()
    {
        if (cameraControl == null && Camera.main != null) cameraControl = Camera.main.GetComponent<CameraControlThirdPerson>();
        if (cameraControl != null) impulseListener = cameraControl.GetCombatImpulseListener();
        UpdateImpulseGain();
    }

    void OnEnable()
    {
        Current = this;
        CombatVisualEvents.ShotReleased += OnRangedShot;
        CombatVisualEvents.ProjectileImpact += OnImpact;
        CombatVisualEvents.MeleeImpact += OnMeleeImpact;
        CombatVisualEvents.HitConfirmed += OnConfirmedHit;
        if (health != null) health.Damaged += OnDamaged;
    }

    void OnDisable()
    {
        if (Current == this) Current = null;
        CombatVisualEvents.ShotReleased -= OnRangedShot;
        CombatVisualEvents.ProjectileImpact -= OnImpact;
        CombatVisualEvents.MeleeImpact -= OnMeleeImpact;
        CombatVisualEvents.HitConfirmed -= OnConfirmedHit;
        if (health != null) health.Damaged -= OnDamaged;
        StopPresentation();
        if (impulseListener != null) impulseListener.Gain = 0;
    }

    void Update() => Tick(Time.deltaTime);

    void Tick(float deltaTime)
    {
        UpdateImpulseGain();
        if (!presentationEnabled)
        {
            if (!presentationStopped) StopPresentation();
            presentationStopped = true;
            return;
        }
        presentationStopped = false;
        for (int i = 0; i < created; i++) if (effects[i] != null) effects[i].Tick(deltaTime);
        for (int i = 0; i < createdTrails; i++) if (trails[i] != null) trails[i].Tick(deltaTime);
        if (recoiling)
        {
            recoilAge += deltaTime;
            if (!visualRecoil || recoilAge >= recoilDuration) RestoreRecoil();
            else ApplyRecoil();
        }
        if (confirmationCanvas.enabled)
        {
            confirmationAge += deltaTime;
            confirmationCanvas.enabled = killConfirmation && confirmationAge < confirmationDuration;
            float t = Mathf.Clamp01(confirmationAge / Mathf.Max(0.1f, confirmationDuration));
            Color tint = LastConfirmedKills > 1 ? new Color(1, 0.75f, 0.25f) : Color.white;
            tint.a = Mathf.Clamp01((1 - t) * 3);
            confirmationText.color = tint;
            confirmationText.transform.localScale = Vector3.one * (1 + (LastConfirmedKills > 1 ? 0.22f : 0.1f) * Mathf.Exp(-t * 12));
        }
    }

    void OnRangedShot(MechProjectile shot, Vector3 muzzle, Transform shooter)
    {
        if (!presentationEnabled || shot == null) return;
        var settings = Settings(shot.Source);
        bool heavy = shot.Subtype == PartSubtype.Splash;
        float size = settings != null ? settings.muzzleSize : heavy ? muzzleSize : .35f;
        float duration = settings != null ? settings.muzzleDuration : heavy ? muzzleDuration : .1f;
        Color color = settings != null ? settings.color : heavy ? explosionColor : new Color(1, .8f, .4f);
        if (muzzleFlash && (settings == null || settings.muzzleFlash))
            PlayEffect(CombatFeedbackEffect.Kind.Muzzle, muzzle, size, duration, color, true, false, false);
        if (settings == null || settings.muzzleSmoke)
            PlayEffect(CombatFeedbackEffect.Kind.Smoke, muzzle, size, .65f, new Color(.6f, .58f, .52f, .4f), false, false, false);
        if (projectileTrails && shot.IsLive && (settings == null || settings.projectileTrail)) PlayTrail(shot, settings, color);
        if (shooter != transform) return;
        if (firingCameraImpulse && EmitImpulse(muzzle, -shot.Direction,
            settings != null ? settings.firingShake : heavy ? .075f : .025f,
            settings != null ? settings.firingShakeDuration : heavy ? .26f : .12f)) FiringImpulseCount++;
        if (visualRecoil && heavy)
        {
            // Recoil plays on the part that fired, along its own axis; otherwise on the mech's default visual.
            var source = shot.Source;
            bool own = source != null && source.recoilVisual != null;
            BindRecoilVisual(own ? source.recoilVisual : defaultRecoilVisual, own ? source.recoilAxis : Vector3.zero);
            if (recoilVisual != null) { recoiling = true; recoilAge = 0; ApplyRecoil(); }
        }
        if (fireAudio && heavy) PlayAudio(cannonFireClip, muzzle, false);
    }

    void OnImpact(MechProjectileImpact result)
    {
        if (!presentationEnabled) return;
        var settings = Settings(result.Source);
        if (weaponImpacts) PlayEffect(CombatFeedbackEffect.Kind.Dust, result.ContactPoint,
            settings != null ? settings.impactSize : .55f, .55f, dustColor, false, false, false, result.Normal);
        if (result.Subtype == PartSubtype.Splash) PlayImpact(result);
        if (result.Owner != transform || result.Subtype != PartSubtype.Splash) return;
        if (result.Kills <= 0) return;
        ConfirmKills(result.Kills);
    }

    void PlayImpact(MechProjectileImpact result)
    {
        if (explosionFlash || shockwaveRing || secondaryEmbers)
            PlayEffect(CombatFeedbackEffect.Kind.Explosion, result.Position, result.Radius,
                explosionDuration, explosionColor, explosionFlash, shockwaveRing, secondaryEmbers);
        PlayEffect(CombatFeedbackEffect.Kind.Smoke, result.Position, Mathf.Max(.5f, result.Radius * .55f),
            1.25f, new Color(.35f, .32f, .28f, .6f), false, false, false);
        if (result.Owner != transform) return;
        if (impactAudio) PlayAudio(cannonImpactClip, result.Position, false);
        if (cameraImpulse && impulseListener != null)
        {
            float proximity = Mathf.Clamp01(1 - Vector3.Distance(transform.position, result.Position) / Mathf.Max(1, impulseRange));
            float strength = Mathf.Min(0.2f, impulseStrength * (1 + Mathf.Clamp(result.Kills - 1, 0, 3) * 0.2f));
            EmitImpulse(result.Position, Vector3.down, strength * proximity, impulseDuration);
        }
    }

    static WeaponVisualSettings Settings(Part source) => source != null ? source.GetComponent<WeaponVisualSettings>() : null;
    void OnMeleeImpact(Part source, Transform owner, Vector3 point, Vector3 normal)
    {
        if (!presentationEnabled || !weaponImpacts) return;
        var settings = Settings(source);
        PlayEffect(CombatFeedbackEffect.Kind.Dust, point, settings != null ? settings.impactSize : .65f,
            .55f, dustColor, false, false, false, normal);
    }
    void OnDamaged(float amount)
    {
        if (presentationEnabled && damageCameraImpulse && EmitImpulse(transform.position, Vector3.down,
            damageImpulseStrength, damageImpulseDuration)) DamageImpulseCount++;
        if (presentationEnabled && hitStop) CombatHitStop.Request(transform,damageHitStop,this);
    }
    void OnConfirmedHit(Transform owner,CombatHealth victim,CombatHitKind kind)
    {
        if(!presentationEnabled || victim==null) return;
        if(owner==transform)
        {
            if(hitCameraImpulse && Time.unscaledTime>=nextHitShake && EmitImpulse(victim.transform.position,new Vector3(.4f,1,0),
                hitImpulseStrength*(victim.IsDead?1.15f:1),hitImpulseDuration))
            { HitImpulseCount++; nextHitShake=Time.unscaledTime+hitShakeInterval; }
            if(hitStop)
            {
                float seconds=kind==CombatHitKind.Melee ? meleeHitStop : kind==CombatHitKind.Blast ? blastHitStop : projectileHitStop;
                CombatHitStop.Request(transform,seconds,this); CombatHitStop.Request(victim.transform,seconds,this);
            }
        }
        else if(victim==health && hitStop && kind!=CombatHitKind.Projectile)
            CombatHitStop.Request(owner,damageHitStop,this);
    }
    bool EmitImpulse(Vector3 position, Vector3 direction, float strength, float duration)
    {
        if (!cameraImpulse || strength <= 0 || impulseListener == null) return false;
        impulse.ImpulseDuration = Mathf.Clamp(duration, .05f, .5f);
        impulse.CreateEvent(position, direction.normalized * Mathf.Clamp(strength, 0, .2f)); return true;
    }
    void PlayTrail(MechProjectile shot, WeaponVisualSettings settings, Color color)
    {
        var material = settings != null && settings.material != null ? settings.material : effectMaterial;
        if (material == null || poolRoot == null) return;
        CombatProjectileTrail trail = null;
        for (int i = 0; i < createdTrails; i++) if (trails[i] != null && !trails[i].IsPlaying) { trail = trails[i]; break; }
        if (trail == null && createdTrails < trails.Length)
        {
            var root = new GameObject("Pooled Projectile Trail " + createdTrails);
            root.transform.SetParent(poolRoot.transform, false);
            trail = root.AddComponent<CombatProjectileTrail>(); trails[createdTrails++] = trail;
        }
        // Saturation skips the new trail rather than cutting off another live shot.
        if (trail != null) trail.Play(shot, material, color, settings != null ? settings.trailWidth : .08f,
            settings != null ? settings.trailLifetime : .2f, settings != null ? settings.projectileTrailLinger : .9f);
    }

    void ConfirmKills(int kills)
    {
        if (killConfirmation)
        {
            LastConfirmedKills = kills;
            KillConfirmationCount++;
            confirmationAge = 0;
            confirmationText.text = kills == 1 ? "DESTROYED" : kills + " DESTROYED";
            confirmationText.fontSize = kills == 1 ? 24 : 32 + Mathf.Min(kills - 2, 3) * 2;
            confirmationText.color = kills > 1 ? new Color(1, 0.75f, 0.25f) : Color.white;
            confirmationCanvas.enabled = true;
        }
        if (confirmationAudio && kills > 1) PlayAudio(multiKillClip, transform.position, true);
    }

    public void PlayDeath(Vector3 position, float size)
    {
        if (!isActiveAndEnabled || !presentationEnabled || !enemyDeathEffects) return;
        PlayEffect(CombatFeedbackEffect.Kind.Death, position, Mathf.Clamp(size, 0.5f, 3), deathDuration,
            deathColor, true, false, true);
    }

    CombatFeedbackEffect PlayEffect(CombatFeedbackEffect.Kind kind, Vector3 position, float size, float duration, Color color,
        bool flash, bool ring, bool secondary, Vector3 normal = default)
    {
        if (effectMaterial == null || poolRoot == null) return null;
        CombatFeedbackEffect effect = null;
        for (int i = 0; i < created; i++) if (!effects[i].IsPlaying) { effect = effects[i]; break; }
        if (effect == null && created < effects.Length) effect = CreateEffect();
        if (effect == null)
        {
            for (int i = 0; i < created; i++)
                if (effect == null || effects[i].Age > effect.Age) effect = effects[i];
        }
        if (effect == null) return null;
        effect.Play(kind, position, size, duration, color, flash, ring, secondary, effectMaterial, normal);
        return effect;
    }

    CombatFeedbackEffect CreateEffect()
    {
        var obj = new GameObject("Pooled Combat Effect " + created);
        obj.transform.SetParent(poolRoot.transform, false);
        var effect = obj.AddComponent<CombatFeedbackEffect>();
        effect.Initialize(effectMaterial, smokeMaterial);
        effects[created++] = effect;
        return effect;
    }

    void ApplyRecoil()
    {
        float kick = Mathf.Pow(1 - Mathf.Clamp01(recoilAge / Mathf.Max(0.04f, recoilDuration)), 2);
        recoilVisual.localPosition = restPosition + recoilAxis * (kick * recoilDistance);
        recoilVisual.localRotation = restRotation * Quaternion.Euler(-recoilDegrees * kick, 0, 0);
    }

    void RestoreRecoil()
    {
        if (recoiling && recoilVisual != null) { recoilVisual.localPosition = restPosition; recoilVisual.localRotation = restRotation; }
        recoiling = false;
    }

    void UpdateImpulseGain()
    { if (impulseListener != null) impulseListener.Gain = presentationEnabled && cameraImpulse ? 1 : 0; }

    void PlayAudio(AudioClip clip, Vector3 position, bool ui)
    {
        if (clip == null) return;
        int index = nextVoice++ % voices.Length;
        if (voices[index] == null)
        {
            var obj = new GameObject("Combat Audio " + index);
            obj.transform.SetParent(poolRoot.transform, false);
            voices[index] = obj.AddComponent<AudioSource>();
            voices[index].playOnAwake = false;
            voices[index].rolloffMode = AudioRolloffMode.Linear;
            voices[index].maxDistance = 50;
        }
        var voice = voices[index];
        voice.Stop();
        voice.transform.position = position;
        voice.clip = clip;
        voice.volume = audioVolume;
        voice.spatialBlend = ui ? 0 : 0.8f;
        voice.Play();
    }

    void CreateConfirmation()
    {
        var obj = new GameObject("Blast Confirmation", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        obj.transform.SetParent(transform, false);
        confirmationCanvas = obj.GetComponent<Canvas>();
        confirmationCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        confirmationCanvas.sortingOrder = 11;
        var scaler = obj.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 0.5f;
        var label = new GameObject("One Confirmation Per Blast", typeof(RectTransform), typeof(Text), typeof(Shadow));
        label.transform.SetParent(obj.transform, false);
        var rect = label.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.77f);
        rect.sizeDelta = new Vector2(400, 60);
        confirmationText = label.GetComponent<Text>();
        confirmationText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        confirmationText.alignment = TextAnchor.MiddleCenter;
        confirmationText.raycastTarget = false;
        label.GetComponent<Shadow>().effectDistance = new Vector2(2, -2);
        confirmationCanvas.enabled = false;
    }

    void StopPresentation()
    {
        CombatHitStop.ClearFrom(this);
        // Scene roots can be destroyed in either order during unload/Play-mode exit.
        for (int i = 0; i < created; i++) if (effects[i] != null) effects[i].Stop();
        for (int i = 0; i < createdTrails; i++) if (trails[i] != null) trails[i].Stop();
        foreach (var voice in voices) if (voice != null) voice.Stop();
        RestoreRecoil();
        if (confirmationCanvas != null) confirmationCanvas.enabled = false;
    }

    void OnDestroy()
    {
        if (Current == this) Current = null;
        if (poolRoot != null) Destroy(poolRoot);
    }
}
