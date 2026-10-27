using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PreMissionStoneMotion : MonoBehaviour
{
    public enum SmallArrivalStyle { LightDrop, Tumble, TiltAndSettle }
    public readonly struct ArrivalProfile
    {
        public readonly SmallArrivalStyle Style;
        public readonly float Height, FallDuration, SettleDuration, Bounce;
        public readonly Vector3 StartAngles, ContactAngles;
        public ArrivalProfile(SmallArrivalStyle style, float height, float fall, float settle, float bounce, Vector3 start, Vector3 contact)
        { Style=style; Height=height; FallDuration=fall; SettleDuration=settle; Bounce=bounce; StartAngles=start; ContactAngles=contact; }
    }
    [SerializeField] QuestBoardImpactFeedback feedback;
    [SerializeField] PreMissionProgressTrack progressTrack;
    [SerializeField] PreMissionMissionMiniature missionMiniature;
    [SerializeField] PreMissionBoardTerrain terrain;
    [SerializeField] PreMissionContactShadow contactShadow;
    [SerializeField] PreMissionShockwave shockwave;
    [Header("Arrival (unscaled seconds)")]
    [SerializeField, Min(0.01f)] float smallDropDuration = 0.32f;
    [SerializeField, Min(0.01f)] float mainDropDuration = 0.78f;
    [SerializeField, Min(0.01f)] float smallSettleDuration = 0.20f;
    [SerializeField, Min(0.01f)] float mainSettleDuration = 0.96f;
    [SerializeField, Min(0f)] float smallDropHeight = 0.85f;
    [SerializeField, Min(0f)] float mainDropHeight = 5.5f;
    [SerializeField, Min(0f)] float arrivalGap = 0.08f;
    [Header("Small arrival variation")]
    [SerializeField, Range(0f, 0.25f)] float smallHeightVariation = 0.12f;
    [SerializeField, Range(0f, 0.15f)] float smallTimingVariation = 0.04f;
    [SerializeField, Min(0f)] float smallBounce = 0.045f;
    [SerializeField, Min(0f)] float smallTilt = 7f;
    [Header("Main anticipation and secondary motion")]
    [SerializeField, Min(0.01f)] float mainAnticipationDuration = 0.32f;
    [SerializeField, Min(0.01f)] float mainClearanceDuration=0.65f;
    [SerializeField, Range(0f, 0.15f)] float mainFocusZoom = 0.045f;
    [SerializeField, Min(0.01f)] float reactionRadius = 12f;
    [SerializeField, Min(0f)] float reactionTilt = 8f;
    [SerializeField, Min(0f)] float reactionTravel = 0.10f;
    [SerializeField, Min(0f)] float reactionLift = 0.16f;
    [Tooltip("Retains a readable reaction near the outer edge of the impact radius.")]
    [SerializeField, Range(0f,1f)] float reactionEdgeStrength=0.30f;
    [Tooltip("Outward wave travel time across Reaction Radius; capped within the existing main settling timeline.")]
    [SerializeField, Range(0f,0.3f)] float reactionWaveTravel=0.24f;
    [Header("Inspection (unscaled seconds)")]
    [SerializeField, Min(0.01f)] float pressDuration = 0.08f;
    [SerializeField, Min(0.01f)] float inspectDuration = 0.72f;
    [SerializeField, Min(0.01f)] float returnDuration = 0.64f;
    [SerializeField, Min(0.01f)] float returnSettleDuration = 0.12f;
    [SerializeField, Min(0f)] float inspectLift = 0.22f;
    [Header("Slab handling")]
    [SerializeField, Range(0f,0.08f)] float inspectionArc=0.035f;
    [SerializeField, Range(0f,0.04f)] float mainInspectionArc=0.012f;
    [SerializeField] bool alignInspectionYaw=true;
    [Tooltip("Start the briefing during the camera approach, once the selected rock is near its reading position.")]
    [SerializeField, Range(0.5f,1f)] float briefingRevealAt=0.7f;
    [Header("Tabletop inspection")]
    [SerializeField, Min(0f)] float mainInspectLift=0.12f;
    [SerializeField, Min(1f)] float mainHandlingTimeMultiplier=1.15f;
    [SerializeField] Vector2 inspectionViewport=new Vector2(0.25f,0.50f);
    [SerializeField, Range(0.2f,0.45f)] float inspectionWidth=0.34f;
    [SerializeField, Min(0.1f)] float smallInspectionSize=3f;
    [SerializeField, Min(0.1f)] float mainInspectionSize=3.85f;
    PreMissionStoneBoard board;
    QuestStoneView inspected;
    Action arrivalsReady;
    bool wasDisabled;
    float arrivalFocus;
    Vector3 landingPoint;
    Bounds inspectionBounds, reducedInspectionBounds;
    readonly List<QuestStoneView> reactingStones = new List<QuestStoneView>();
    public bool IsPosting { get; private set; }
    public bool IsTransitioning { get; private set; }
    public float InspectionProgress { get; private set; }
    public QuestStoneView Inspected => inspected;
    public QuestBoardImpactFeedback Feedback => feedback;
    public PreMissionProgressTrack ProgressTrack => progressTrack;
    public PreMissionMissionMiniature Miniature => missionMiniature;
    public PreMissionBoardTerrain Terrain => terrain;
    public PreMissionContactShadow ContactShadow => contactShadow;
    public PreMissionShockwave Shockwave => shockwave;
    public bool IsOperational => isActiveAndEnabled && (progressTrack==null || progressTrack.isActiveAndEnabled) && (missionMiniature==null || missionMiniature.isActiveAndEnabled) && (terrain==null || terrain.isActiveAndEnabled);
    public bool IsAnticipatingMain { get; private set; }
    public bool IsClearingMainArea { get; private set; }
    public float ArrivalFocus => arrivalFocus;
    public float BriefingRevealAt => briefingRevealAt;
    public event Action<QuestStoneView> Impacted;
    bool ReducedMotion => feedback!=null && feedback.ReducedMotion;

    public void Bind(PreMissionStoneBoard owner) { board = owner; contactShadow?.Bind(owner); shockwave?.Bind(owner); }
    public void PrepareProgressTrack()
    {
        if(progressTrack!=null) progressTrack.Bind(board);
        if(missionMiniature!=null) missionMiniature.Initialize(board);
        if(terrain!=null) terrain.Bind(board);
    }
    public ArrivalProfile GetArrivalProfile(QuestStoneView stone)
    {
        // Stable per offer occurrence; a slot/seed cycle distributes all three styles.
        int seed = board.Progression.Seed;
        unchecked { foreach (char c in stone.Offer.Id) seed = seed*31+c; }
        var random = new System.Random(seed);
        float height = smallDropHeight * (1f + ((float)random.NextDouble()*2-1)*smallHeightVariation);
        float time = 1f + ((float)random.NextDouble()*2-1)*smallTimingVariation;
        float sign = random.Next(2)==0 ? -1f : 1f;
        var style = (SmallArrivalStyle)((stone.Offer.Slot + (board.Progression.Seed & int.MaxValue)%3)%3);
        float angle = smallTilt * sign;
        switch (style)
        {
            case SmallArrivalStyle.Tumble:
                return new ArrivalProfile(style,height,smallDropDuration*1.02f*time,smallSettleDuration*0.85f,
                    smallBounce*0.65f,new Vector3(angle,angle*0.25f,-angle),new Vector3(-angle*0.3f,0,angle*0.2f));
            case SmallArrivalStyle.TiltAndSettle:
                return new ArrivalProfile(style,height*0.94f,smallDropDuration*0.88f*time,smallSettleDuration*1.02f,
                    smallBounce*0.18f,new Vector3(angle*0.5f,0,-angle*0.7f),new Vector3(angle*0.65f,0,-angle*0.5f));
            default:
                return new ArrivalProfile(style,height,smallDropDuration*0.94f*time,smallSettleDuration*0.7f,
                    smallBounce,new Vector3(angle*0.2f,0,angle*0.15f),Vector3.zero);
        }
    }
    public void StartArrivals(Action ready)
    {
        if (IsPosting || !isActiveAndEnabled) return;
        var pending = board.Stones.Where(s => !s.IsReady).OrderBy(s => s.IsMain ? 1 : 0).ToArray();
        if (pending.Length == 0 && (progressTrack==null || !progressTrack.HasPending) && (terrain==null || !terrain.HasPending)) { ready?.Invoke(); return; }
        IsPosting = true; arrivalsReady = ready; StartCoroutine(Arrivals(pending));
    }
    IEnumerator Arrivals(IReadOnlyList<QuestStoneView> pending)
    {
        yield return null; yield return null;
        if(terrain!=null) yield return terrain.Reveal();
        if(progressTrack!=null) yield return progressTrack.Reveal();
        bool progressShown=false;
        foreach (var stone in pending)
        {
            bool main = stone.IsMain;
            var profile = main ? default : GetArrivalProfile(stone);
            if (main)
            {
                if(progressTrack!=null) yield return progressTrack.Advance(true);
                progressShown=true;
                if(board.UsesWholeTableLayout && !QuestProgressionRuntime.WasLandingCleared(board.Progression))
                {
                    var neighbours=board.Stones.Where(s=>!s.IsMain).ToArray();
                    var starts=neighbours.Select(s=>s.transform.localPosition).ToArray();
                    QuestProgressionRuntime.RememberLandingLayout(board.Progression,board.LandingOffsets());
                    IsClearingMainArea=true;
                    bool clearanceSuppressed=ReducedMotion;
                    yield return Animate(mainClearanceDuration,t=>
                    {
                        clearanceSuppressed|=ReducedMotion; if(clearanceSuppressed) t=1;
                        for(int i=0;i<neighbours.Length;i++)
                            neighbours[i].transform.localPosition=Vector3.Lerp(starts[i],board.LandingSlotPosition(neighbours[i].Offer.Slot,starts[i].y),Ease(t));
                    });
                    Physics.SyncTransforms();
                    IsClearingMainArea=false;
                }
                landingPoint = stone.transform.position + Vector3.up*0.65f;
                IsAnticipatingMain = true;
                contactShadow?.Begin(stone);
                bool releaseTrack=progressTrack!=null && progressTrack.CameraFocus>0;
                yield return Animate(mainAnticipationDuration,t =>
                {
                    arrivalFocus=Ease(t);
                    contactShadow?.Approach(1,arrivalFocus);
                    if(releaseTrack) progressTrack.ReleaseFocus(arrivalFocus);
                });
                IsAnticipatingMain = false;
            }
            float height = main ? mainDropHeight : profile.Height;
            if(!main) contactShadow?.Begin(stone);
            yield return Animate(main ? mainDropDuration : profile.FallDuration, t =>
            {
                float fall = t*t;
                var angles = main ? new Vector3((1-t)*5f,0,(1-t)*-3f) : Vector3.Lerp(profile.StartAngles,profile.ContactAngles,fall);
                stone.SetMotionFrame(Vector3.up * (height*(1-fall)), angles, Vector3.one);
                contactShadow?.Approach(1-fall);
            });
            QuestProgressionRuntime.MarkPresented(board.Progression, stone.Offer.Id);
            feedback?.Play(stone.transform.position, main);
            contactShadow?.Contact(0);
            if (main)
            {
                if(shockwave!=null) shockwave.Begin(stone.transform.position);
                else reactingStones.AddRange(board.Stones.Where(s=>s!=stone && s.IsReady && !s.IsMoving));
                ApplySecondaryMotion(stone.transform.position,0);
            }
            Impacted?.Invoke(stone);
            float recoveryDuration=main ? Mathf.Max(mainSettleDuration,shockwave!=null ? shockwave.TotalDuration : 0) : profile.SettleDuration;
            yield return Animate(recoveryDuration, t =>
            {
                float elapsed=t*recoveryDuration;
                contactShadow?.Contact(elapsed);
                float body=main ? Mathf.Clamp01(elapsed/Mathf.Max(0.01f,mainSettleDuration)) : t;
                float envelope = (1-body)*(1-body);
                float wave = Mathf.Sin(body*Mathf.PI*3f)*envelope;
                if (main)
                {
                    // A short downward seating response, never an upward rubbery bounce.
                    float seat = Mathf.Sin(body*Mathf.PI)*envelope;
                    float compression = 0.012f*envelope;
                    stone.SetMotionFrame(Vector3.down*(seat*0.012f),new Vector3(wave*1.3f,0,-wave*0.8f),
                        new Vector3(1+compression,1-compression,1+compression));
                    ApplySecondaryMotion(stone.transform.position,t);
                    arrivalFocus=1f-Ease(body);
                }
                else
                {
                    float bounce=Mathf.Max(0,Mathf.Sin(t*Mathf.PI*2f))*envelope*profile.Bounce;
                    float compression=0.018f*Mathf.Abs(wave);
                    var angles=profile.ContactAngles*envelope+new Vector3(wave*smallTilt*0.45f,0,-wave*smallTilt*0.25f);
                    stone.SetMotionFrame(Vector3.up*bounce,angles,new Vector3(1+compression,1-compression,1+compression));
                }
            });
            ClearSecondaryMotion(); arrivalFocus=0;
            stone.RestoreRest();
            yield return Animate(arrivalGap, _ => {});
        }
        if(progressTrack!=null && !progressShown) yield return progressTrack.Advance(false);
        IsPosting = false; var ready = arrivalsReady; arrivalsReady = null; ready?.Invoke();
    }
    void ApplySecondaryMotion(Vector3 impact, float t)
    {
        if(shockwave!=null) { shockwave.Advance(t); return; }
        foreach(var stone in reactingStones)
        {
            if (stone==null) continue;
            Vector3 radial=stone.transform.position-impact; radial.y=0;
            float wave=SurfaceResponse(radial.magnitude,t);
            var direction=stone.transform.InverseTransformDirection(radial.normalized);
            var offset=direction*(wave*reactionTravel)+Vector3.up*(Mathf.Max(0,wave)*reactionLift);
            stone.SetImpactReaction(offset,new Vector3(direction.z,0,-direction.x)*(wave*reactionTilt));
        }
        terrain?.ApplyImpact(impact,t);
    }
    // Shared by nearby stones and terrain. Contact is t=0; distance only delays
    // the local response inside this one existing settling timeline.
    public float SurfaceResponse(float distance,float progress)
    {
        if(shockwave!=null) return shockwave.Response(distance,progress);
        if(ReducedMotion) return 0;
        float radial=Mathf.Max(0,distance)/Mathf.Max(0.01f,reactionRadius);
        if(radial>=1) return 0;
        float strength=Mathf.Lerp(1,Mathf.Clamp01(reactionEdgeStrength),radial);
        float delay=Mathf.Min(0.4f,radial*reactionWaveTravel/Mathf.Max(0.01f,mainSettleDuration));
        float t=Mathf.Clamp01((progress-delay)/(1-delay));
        // A short, broad crest survives capped catch-up steps during a hitch.
        // The same crest reaches every piece, so distance remains the weight cue.
        if(t<=0.10f) return Mathf.SmoothStep(0,1,t/0.10f)*strength;
        if(t<=0.22f) return strength;
        float recovery=Mathf.SmoothStep(0,1,(t-0.22f)/0.78f);
        return Mathf.Cos(recovery*Mathf.PI*2f)*(1-recovery)*(1-recovery)*strength;
    }
    void ClearSecondaryMotion()
    {
        foreach(var stone in reactingStones) if(stone!=null) stone.SetImpactReaction(Vector3.zero,Vector3.zero);
        reactingStones.Clear();
        terrain?.ClearImpact(); shockwave?.Settle(); contactShadow?.End();
    }
    public void FinishArrivals()
    {
        if (!IsPosting) return;
        if(shockwave!=null && board.Stones.Any(s=>s.IsMain && !QuestProgressionRuntime.WasPresented(s.Offer.Id)))
            shockwave.Begin(board.Stones.First(s=>s.IsMain).transform.position);
        StopAllCoroutines(); IsPosting = false; IsAnticipatingMain=IsClearingMainArea=false; arrivalFocus=0; ClearSecondaryMotion(); feedback?.Cancel();
        progressTrack?.Settle(PreMissionView.Board,true);
        terrain?.Settle(true);
        foreach (var stone in board.Stones)
        { stone.RestoreRest(); QuestProgressionRuntime.MarkPresented(board.Progression,stone.Offer.Id); }
        var ready = arrivalsReady; arrivalsReady = null; ready?.Invoke();
    }
    public void Inspect(QuestStoneView stone, Action<float> frame, Action ready)
    {
        if (IsPosting || IsTransitioning || stone == null) return;
        inspected = stone; IsTransitioning = true;
        if(missionMiniature!=null) missionMiniature.Bind(stone);
        PrepareInspectionBounds();
        StartCoroutine(InspectRoutine(frame, ready));
    }
    IEnumerator InspectRoutine(Action<float> frame, Action ready)
    {
        feedback?.PlayCue(QuestBoardImpactFeedback.Cue.Pickup);
        if(ReducedMotion)
        {
            InspectionProgress=1;
            missionMiniature?.SetProgress(1);
        }
        var start = inspected.CurrentOffset; var scale = inspected.CurrentScale; var angles=inspected.CurrentAngles;
        float weight=inspected.IsMain ? mainHandlingTimeMultiplier : 1f;
        bool simplified=ReducedMotion;
        yield return Animate(pressDuration*weight, t =>
        {
            simplified|=ReducedMotion;
            inspected.SetMotionFrame(start,angles,scale*(1f-(inspected.IsMain?0.01f:0.025f)*Mathf.Sin(t*Mathf.PI)));
        });
        yield return Animate(inspectDuration*weight, t =>
        {
            simplified|=ReducedMotion;
            float eased=simplified ? 1 : Ease(t); InspectionProgress=eased;
            inspected.SetMotionFrame(Vector3.Lerp(start,InspectionOffset(),eased)+InspectionArc(eased),Vector3.Lerp(angles,InspectionAngles(),eased),Vector3.Lerp(scale,InspectionScale(),eased));
            if(missionMiniature!=null) missionMiniature.SetProgress(simplified ? 1 : Mathf.Clamp01((eased-0.42f)/0.58f));
            frame?.Invoke(eased);
        });
        IsTransitioning = false; ready?.Invoke();
    }
    public void Return(Action<float> frame, Action ready)
    {
        if (IsTransitioning) return;
        if (inspected == null) { InspectionProgress = 0; frame?.Invoke(0); ready?.Invoke(); return; }
        IsTransitioning = true; StartCoroutine(ReturnRoutine(frame,ready));
    }
    IEnumerator ReturnRoutine(Action<float> frame, Action ready)
    {
        var start = inspected.CurrentOffset; var scale = inspected.CurrentScale; var angles=inspected.CurrentAngles; float progress = InspectionProgress;
        bool simplified=ReducedMotion;
        yield return Animate(returnDuration*(inspected.IsMain?mainHandlingTimeMultiplier:1f),t =>
        {
            simplified|=ReducedMotion;
            float eased=simplified ? 1 : Ease(t); InspectionProgress=progress*(1-eased);
            inspected.SetMotionFrame(Vector3.Lerp(start,Vector3.zero,eased)+InspectionArc(eased),Vector3.Lerp(angles,Vector3.zero,eased),Vector3.Lerp(scale,Vector3.one,eased));
            if(missionMiniature!=null) missionMiniature.SetProgress(Mathf.Clamp01((1-eased)*2-1));
            frame?.Invoke(InspectionProgress);
        });
        feedback?.PlayCue(QuestBoardImpactFeedback.Cue.Return);
        yield return Animate(returnSettleDuration,t =>
        {
            float contact=simplified ? 0 : Mathf.Sin(t*Mathf.PI)*0.012f;
            inspected.SetMotionFrame(Vector3.zero,Vector3.zero,new Vector3(1+contact,1-contact,1+contact));
        });
        if(missionMiniature!=null) missionMiniature.Clear();
        inspected.RestoreRest(); inspected=null; InspectionProgress=0; IsTransitioning=false; ready?.Invoke();
    }
    Vector3 InspectionOffset() => Vector3.up*(missionMiniature!=null && inspected.IsMain ? mainInspectLift : inspectLift);
    Vector3 InspectionArc(float progress)
    {
        float height=inspected.IsMain ? mainInspectionArc : inspectionArc;
        height=Mathf.Min(height,InspectionOffset().y/Mathf.PI*0.75f);
        float crest=Mathf.Sin(progress*Mathf.PI); return Vector3.up*(height*crest*crest);
    }
    Vector3 InspectionAngles() => missionMiniature!=null ? new Vector3(inspected.IsMain?-2:-5,
        alignInspectionYaw ? -Mathf.DeltaAngle(0,inspected.transform.localEulerAngles.y) : 0,inspected.IsMain?0.5f:1.5f) : Vector3.zero;
    Vector3 InspectionScale() => Vector3.one*(missionMiniature!=null && inspected.IsMain ? 1.015f : 1.045f);
    void PrepareInspectionBounds()
    {
        inspectionBounds=inspected.MotionBounds(InspectionOffset(),InspectionAngles(),InspectionScale(),1);
        reducedInspectionBounds=inspected.MotionBounds(InspectionOffset(),InspectionAngles(),InspectionScale(),0);
        if(missionMiniature==null) return;
        inspectionBounds.Encapsulate(missionMiniature.InspectionBounds(inspected,InspectionOffset(),InspectionAngles(),InspectionScale(),1));
        reducedInspectionBounds.Encapsulate(missionMiniature.InspectionBounds(inspected,InspectionOffset(),InspectionAngles(),InspectionScale(),0));
    }
    public void Settle(PreMissionView view, string offerId)
    {
        StopAllCoroutines(); IsPosting=IsTransitioning=IsAnticipatingMain=IsClearingMainArea=false; arrivalFocus=0; ClearSecondaryMotion(); arrivalsReady=null; feedback?.Cancel();
        if (board == null) return;
        progressTrack?.Settle(view);
        terrain?.Settle();
        missionMiniature?.Clear();
        foreach(var stone in board.Stones) stone.RestoreRest();
        inspected = view==PreMissionView.Board ? null : board.FindStone(offerId);
        InspectionProgress=inspected!=null ? 1 : 0;
        if(inspected!=null)
        {
            PrepareInspectionBounds();
            inspected.SetMotionFrame(InspectionOffset(),InspectionAngles(),InspectionScale());
            if(missionMiniature!=null) { missionMiniature.Bind(inspected); missionMiniature.SetProgress(1); }
        }
        board.RefreshCameraFrame();
    }
    public void ApplyCamera(ref Vector3 position, ref Quaternion rotation, ref float size, float aspect)
    {
        if(progressTrack!=null) progressTrack.ApplyCamera(ref position,rotation);
        if (arrivalFocus>0 && !ReducedMotion)
        {
            var target=landingPoint-rotation*Vector3.forward*24;
            position=Vector3.Lerp(position,target,arrivalFocus*0.5f);
            size*=1f-mainFocusZoom*arrivalFocus;
        }
        if (inspected != null && InspectionProgress>0)
        {
            // The miniature's animated emergence/retraction must not move the camera target.
            var bounds=ReducedMotion ? reducedInspectionBounds : inspectionBounds;
            bool tabletop=missionMiniature!=null;
            float fit=Mathf.Max(tabletop ? (inspected.IsMain?mainInspectionSize:smallInspectionSize) : (inspected.IsMain?2.7f:1.9f),
                bounds.size.x/(2*aspect*(tabletop?inspectionWidth:0.68f)));
            var target=(tabletop?bounds.center:inspected.InspectionPoint)-rotation*Vector3.forward*24;
            if(tabletop) target-=rotation*Vector3.right*((inspectionViewport.x-0.5f)*fit*2*aspect)+rotation*Vector3.up*((inspectionViewport.y-0.5f)*fit*2);
            float progress=ReducedMotion ? 1 : InspectionProgress;
            position=Vector3.Lerp(position,target,progress); size=Mathf.Lerp(size,fit,progress);
        }
        if (feedback != null)
        {
            position += rotation*feedback.CameraOffset;
            rotation *= Quaternion.Euler(0,0,feedback.CameraRoll);
        }
    }
    void OnDisable()
    {
        wasDisabled = true;
        if (board == null) return;
        board.RecoverOwner();
    }
    void OnEnable() { if (wasDisabled) { wasDisabled=false; board?.RecoverOwner(); } }
    static float Ease(float t) => t*t*t*(t*(t*6-15)+10);
    IEnumerator Animate(float duration, Action<float> frame)
    {
        duration=Mathf.Max(0.01f,duration); float progress=0; frame(0);
        while(progress<1)
        {
            yield return null;
            float activeDuration=ReducedMotion ? Mathf.Min(duration,0.12f) : duration;
            progress=Mathf.Min(1,progress+Mathf.Min(Time.unscaledDeltaTime,0.05f)/activeDuration); frame(progress);
        }
    }
}
