using UnityEngine;

// Reusable local feedback. No quest progression, physics displacement or persistent objects.
[DisallowMultipleComponent]
public sealed class QuestBoardImpactFeedback : MonoBehaviour
{
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
    public Vector3 CameraOffset { get; private set; }
    public float CameraRoll { get; private set; }
    public int SmallImpacts { get; private set; }
    public int HeavyImpacts { get; private set; }
    public bool IsShaking => age < shakeDuration;
    public bool IsRecoiling => heavyResponse && age < duration;
    public ParticleSystem SmallDust => smallDust;
    public ParticleSystem HeavyDust => heavyDust;
    public ParticleSystem ImpactChips => impactChips;
    public Transform BoardSurface => boardSurface;

    void Awake()
    {
        if (inlays != null) inlayHome = inlays.localPosition;
        if (boardSurface != null) { surfaceHome=boardSurface.localPosition; surfaceRotation=boardSurface.localRotation; }
        initialized = true;
    }
    public void Play(Vector3 position, bool heavy)
    {
        var dust = heavy ? heavyDust : smallDust;
        if (dust != null)
        {
            dust.transform.position = position + Vector3.up * 0.08f;
            dust.Play(); dust.Emit(heavy ? 64 : 14);
        }
        if (heavy && impactChips != null)
        {
            impactChips.transform.position=position+Vector3.up*0.12f;
            impactChips.Play(); impactChips.Emit(12);
        }
        var clip = heavy ? heavyImpact : smallImpact;
        if (impactAudio != null && clip != null) impactAudio.PlayOneShot(clip, heavy ? heavyVolume : smallVolume);
        if (heavy) HeavyImpacts++; else SmallImpacts++;
        age=0; heavyResponse=heavy;
        shakeDuration=Mathf.Max(0.01f,heavy ? heavyShakeDuration : smallShakeDuration);
        duration=heavy ? Mathf.Max(shakeDuration,heavyRecoilDuration) : shakeDuration;
        strength=heavy ? heavyShakeStrength : smallShakeStrength;
        ApplyMotion();
    }
    void Update()
    {
        if (age >= duration) return;
        age = Mathf.Min(duration, age + Time.unscaledDeltaTime);
        ApplyMotion();
        if (age == duration) ResetMotion();
    }
    void ApplyMotion()
    {
        float shake=Mathf.Clamp01(age/Mathf.Max(0.01f,shakeDuration)), impulse=(1-shake)*(1-shake)*(1-shake);
        CameraOffset=new Vector3(Mathf.Sin(shake*19f),-Mathf.Cos(shake*24f)*0.65f,0)*(strength*impulse);
        CameraRoll=Mathf.Sin(shake*22f)*impulse*strength*4f;
        float t=Mathf.Clamp01(age/Mathf.Max(0.01f,duration)), envelope=(1-t)*(1-t);
        if (inlays != null) inlays.localPosition=inlayHome+new Vector3(Mathf.Sin(t*38f),0,Mathf.Sin(t*31f))*(strength*0.16f*envelope);
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
        if (smallDust != null) smallDust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (heavyDust != null) heavyDust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (impactChips != null) impactChips.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        if (impactAudio != null) impactAudio.Stop();
    }
    void OnDisable() => Cancel();
}
