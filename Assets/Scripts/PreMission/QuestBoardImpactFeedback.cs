using UnityEngine;

// Reusable local feedback. No quest progression, physics displacement or persistent objects.
[DisallowMultipleComponent]
public sealed class QuestBoardImpactFeedback : MonoBehaviour
{
    public enum Cue { Pickup, Return, Progress, Accept, Torso, Confirm }
    [SerializeField] ParticleSystem smallDust;
    [SerializeField] ParticleSystem heavyDust;
    [SerializeField] ParticleSystem impactChips;
    [SerializeField] AudioSource impactAudio;
    [SerializeField] AudioClip smallImpact;
    [SerializeField] AudioClip heavyImpact;
    [SerializeField] Transform inlays;
    [SerializeField] Transform boardSurface;
    [SerializeField, Range(0,1)] float smallVolume = 0.22f;
    [SerializeField, Range(0,1)] float heavyVolume = 0.55f;
    [Header("Interaction audio (connect clips)")]
    [SerializeField] AudioSource interactionAudio;
    [SerializeField] AudioClip handlingClip;
    [SerializeField] AudioClip progressClip;
    [SerializeField] AudioClip selectionClip;
    [SerializeField] AudioClip confirmationClip;
    [SerializeField, Range(0,1)] float interactionVolume = 0.14f;
    [SerializeField, Range(0f,0.15f)] float pitchVariation = 0.055f;
    [Header("Presentation preferences")]
    [Tooltip("Suppresses hover movement, recoil, camera excursions and pulses; uses short fades and stable inspection framing. Does not change quest rules.")]
    [SerializeField] bool reducedMotion;
    [SerializeField, Range(0,1)] float cameraShakeScale = 1f;
    [SerializeField, Range(0,1)] float effectsVolume = 1f;
    [SerializeField, Range(0,1)] float particleScale = 1f;
    [Header("Camera impulse")]
    [SerializeField, Min(0f)] float smallShakeStrength = 0.018f;
    [SerializeField, Min(0f)] float heavyShakeStrength = 0.13f;
    [SerializeField, Min(0.01f)] float smallShakeDuration = 0.16f;
    [SerializeField, Min(0.01f)] float heavyShakeDuration = 0.24f;
    [Header("Visual board recoil")]
    [SerializeField, Min(0.01f)] float heavyRecoilDuration = 0.52f;
    [SerializeField, Min(0f)] float heavyRecoilTravel = 0.018f;
    [SerializeField, Min(0f)] float heavyRecoilTilt = 0.12f;
    float age, duration, strength, shakeDuration;
    bool heavyResponse;
    Vector3 inlayHome;
    Vector3 surfaceHome;
    Quaternion surfaceRotation;
    bool initialized;
    int soundSequence;
    bool previousReducedMotion;
    float previousParticleScale=1;
    public Vector3 CameraOffset { get; private set; }
    public float CameraRoll { get; private set; }
    public int SmallImpacts { get; private set; }
    public int HeavyImpacts { get; private set; }
    public bool IsShaking => !ReducedMotion && CameraShakeScale>0 && strength>0 && age < shakeDuration;
    public bool IsRecoiling => !ReducedMotion && heavyResponse && age < duration;
    public bool ReducedMotion => reducedMotion;
    public float MotionScale => ReducedMotion ? 0 : 1;
    public float CameraShakeScale => Mathf.Clamp01(cameraShakeScale);
    public float EffectsVolume => Mathf.Clamp01(effectsVolume);
    public float ParticleScale => ReducedMotion ? 0 : Mathf.Clamp01(particleScale);
    public int InteractionCount { get; private set; }
    public Cue? LastCue { get; private set; }
    public ParticleSystem SmallDust => smallDust;
    public ParticleSystem HeavyDust => heavyDust;
    public ParticleSystem ImpactChips => impactChips;
    public Transform BoardSurface => boardSurface;

    void Awake()
    {
        if (inlays != null) inlayHome = inlays.localPosition;
        if (boardSurface != null) { surfaceHome=boardSurface.localPosition; surfaceRotation=boardSurface.localRotation; }
        initialized = true;
        previousReducedMotion=reducedMotion;
    }
    // These can be connected to a future settings screen without touching quest data.
    public void SetReducedMotion(bool value) { reducedMotion=value; ApplyPreferences(); }
    public void SetCameraShakeScale(float value) { cameraShakeScale=Mathf.Clamp01(value); ApplyMotion(); }
    public void SetEffectsVolume(float value)
    {
        effectsVolume=Mathf.Clamp01(value);
        if(impactAudio!=null) impactAudio.volume=EffectsVolume;
        if(interactionAudio!=null) interactionAudio.volume=EffectsVolume;
    }
    public void SetParticleScale(float value)
    {
        particleScale=Mathf.Clamp01(value);
        if(ParticleScale==0) ClearParticles();
    }
    void ApplyPreferences()
    {
        if(previousReducedMotion!=reducedMotion)
        {
            previousReducedMotion=reducedMotion;
            if(reducedMotion) { ClearParticles(); ResetMotion(); }
        }
        if(ParticleScale==0 && previousParticleScale>0) ClearParticles();
        previousParticleScale=ParticleScale;
    }
    public void Play(Vector3 position, bool heavy)
    {
        var dust = heavy ? heavyDust : smallDust;
        if (!isActiveAndEnabled) return;
        if (dust != null && ParticleScale>0)
        {
            dust.transform.position = position + Vector3.up * 0.08f;
            dust.Play(); dust.Emit(Mathf.RoundToInt((heavy ? 64 : 14)*ParticleScale));
        }
        if (heavy && impactChips != null && ParticleScale>0)
        {
            impactChips.transform.position=position+Vector3.up*0.12f;
            impactChips.Play(); impactChips.Emit(Mathf.RoundToInt(12*ParticleScale));
        }
        var clip = heavy ? heavyImpact : smallImpact;
        // Do not repitch or replace a heavy tail with a light contact.
        if(!(heavyResponse && age<duration && !heavy))
            Sound(impactAudio,clip,heavy ? heavyVolume : smallVolume,heavy ? 0.94f : 1f);
        if (heavy) HeavyImpacts++; else SmallImpacts++;
        if(heavyResponse && age<duration && !heavy) return;
        age=0; heavyResponse=heavy;
        shakeDuration=Mathf.Max(0.01f,heavy ? heavyShakeDuration : smallShakeDuration);
        duration=heavy ? Mathf.Max(shakeDuration,heavyRecoilDuration) : shakeDuration;
        strength=heavy ? heavyShakeStrength : smallShakeStrength;
        ApplyMotion();
    }
    void Update()
    {
        ApplyPreferences();
        if(impactAudio!=null) impactAudio.volume=EffectsVolume;
        if(interactionAudio!=null) interactionAudio.volume=EffectsVolume;
        if (age >= duration) return;
        age = Mathf.Min(duration, age + Time.unscaledDeltaTime);
        ApplyMotion();
        if (age == duration) ResetMotion();
    }
    void ApplyMotion()
    {
        if(ReducedMotion || duration<=0 || age>=duration) { ResetMotion(); return; }
        float shake=Mathf.Clamp01(age/Mathf.Max(0.01f,shakeDuration)), impulse=(1-shake)*(1-shake)*(1-shake);
        CameraOffset=new Vector3(Mathf.Sin(shake*19f),-Mathf.Cos(shake*24f)*0.65f,0)*(strength*impulse*CameraShakeScale);
        CameraRoll=Mathf.Sin(shake*22f)*impulse*strength*4f*CameraShakeScale;
        float t=Mathf.Clamp01(age/Mathf.Max(0.01f,duration)), envelope=(1-t)*(1-t);
        if (inlays != null && initialized) inlays.localPosition=inlayHome+new Vector3(Mathf.Sin(t*38f),0,Mathf.Sin(t*31f))*(strength*0.16f*envelope);
        if (boardSurface != null && initialized)
        {
            float recoil=heavyResponse ? envelope*Mathf.Cos(t*18f) : 0;
            boardSurface.localPosition=surfaceHome+Vector3.down*(recoil*heavyRecoilTravel);
            boardSurface.localRotation=surfaceRotation*Quaternion.Euler(recoil*heavyRecoilTilt,0,-recoil*heavyRecoilTilt*0.5f);
        }
    }
    void ResetMotion()
    {
        CameraOffset = Vector3.zero; CameraRoll = 0;
        if (inlays != null && initialized) inlays.localPosition = inlayHome;
        if (boardSurface != null && initialized) { boardSurface.localPosition=surfaceHome; boardSurface.localRotation=surfaceRotation; }
    }
    public void Cancel()
    {
        age = duration = shakeDuration = 0; heavyResponse=false; ResetMotion();
        ClearParticles();
        if (impactAudio != null) impactAudio.Stop();
        if (interactionAudio != null) interactionAudio.Stop();
    }
    void ClearParticles()
    {
        if (smallDust != null) smallDust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (heavyDust != null) heavyDust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (impactChips != null) impactChips.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
    }
    public void PlayCue(Cue cue)
    {
        if(!isActiveAndEnabled) return;
        InteractionCount++; LastCue=cue;
        AudioClip clip; float pitch, gain=interactionVolume;
        switch(cue)
        {
            case Cue.Pickup: clip=handlingClip; pitch=0.86f; gain*=0.7f; break;
            case Cue.Return: clip=handlingClip; pitch=0.76f; gain*=0.8f; break;
            case Cue.Progress: clip=progressClip; pitch=1.18f; break;
            case Cue.Accept: clip=selectionClip; pitch=1.32f; break;
            case Cue.Torso: clip=selectionClip; pitch=1.45f; gain*=0.65f; break;
            default: clip=confirmationClip; pitch=1.06f; gain*=1.25f; break;
        }
        Sound(interactionAudio,clip,gain,pitch);
    }
    void Sound(AudioSource source,AudioClip clip,float gain,float pitch)
    {
        if(source==null || clip==null || EffectsVolume==0) return;
        // Local deterministic variation avoids consuming gameplay's Random state.
        float variation=((soundSequence++*0.618034f)%1f)*2-1;
        source.Stop(); source.volume=EffectsVolume;
        source.pitch=Mathf.Clamp(pitch+variation*pitchVariation,0.5f,2f);
        source.PlayOneShot(clip,Mathf.Clamp01(gain*(1f+variation*0.06f)));
    }
    void OnDisable() => Cancel();
}
