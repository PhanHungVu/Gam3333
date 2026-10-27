using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Unity.Profiling;

// World presentation/input only. QuestProgressionSession remains the offer authority.
[DisallowMultipleComponent]
public sealed class PreMissionStoneBoard : MonoBehaviour
{
    [Serializable] public sealed class Composition
    {
        public string name;
        public Vector2[] smallSlots;
        public float[] angles;
    }
    [SerializeField] Camera boardCamera;
    [SerializeField] Transform stoneParent;
    [SerializeField] QuestStoneView smallStonePrefab;
    [SerializeField] QuestStoneView mainStonePrefab;
    [SerializeField] Composition[] compositions;
    [Header("Whole-table quest slabs")]
    [Tooltip("Scatter side slabs across the table, then clear the main landing area only when it unlocks. Positions remain stable for the run.")]
    [SerializeField] bool wholeTableLayout=true;
    [Tooltip("Side slab centers before the main quest arrives. Full collider corners are also checked against the table ellipse.")]
    [SerializeField] Rect freePlacementBounds=new Rect(-6.9f,-3.3f,14.2f,6.6f);
    [SerializeField] Vector2 tableRadii=new Vector2(10.05f,5.05f);
    [SerializeField, Min(0f)] float slabSpacing=0.10f;
    [Header("Legacy authored layout (Whole Table Layout off)")]
    [SerializeField] bool randomizePlacement = true;
    [SerializeField] Vector2 positionVariation = new Vector2(0.45f, 0.28f);
    [SerializeField, Range(0f, 0.15f)] float endPositionVariation = 0.08f;
    [SerializeField, Range(0f, 6f)] float rotationVariation = 2f;
    [Tooltip("Allowed small-stone centers in board-local X/Z. Leaves space for the left progress track and board frame.")]
    [SerializeField] Rect placementBounds = new Rect(-5.75f, -3.22f, 12.05f, 6.44f);
    [SerializeField] Vector3 framingCenter = new Vector3(0f, 1f, 0f);
    [SerializeField] Vector3 framingSize = new Vector3(19.4f, 3f, 9f);
    [SerializeField] Rect screenArea = new Rect(0.035f, 0.19f, 0.93f, 0.59f);
    readonly List<QuestStoneView> stones = new List<QuestStoneView>();
    readonly List<RaycastResult> uiHits = new List<RaycastResult>();
    static readonly ProfilerMarker LayoutMarker=new ProfilerMarker("PreMission.Layout");
    IReadOnlyList<QuestStoneView> stoneView;
    EventSystem raycastSystem;
    PointerEventData uiPointer;
    PreMissionFlowController flow;
    QuestProgressionSession run;
    QuestStoneView focused;
    bool keyboardFocus;
    bool cameraHomeKnown, wasDisabled;
    Quaternion cameraHomeRotation;
    PreMissionStoneMotion motion;
    Vector2 lastPointer;
    Vector2[] slotPositions;
    Vector2[] landingPositions, impactPositions;
    float[] slotAngles;
    Rect[] projectedFootprints;
    Vector2[][] tabletopCorners;
    Vector2 projectedX,projectedZ;
    bool footprintCacheValid;
    public IReadOnlyList<QuestStoneView> Stones => stoneView ?? (stoneView=stones.AsReadOnly());
    public Camera BoardCamera => boardCamera;
    public QuestProgressionSession Progression => run;
    public PreMissionStoneMotion Motion => motion;
    public bool IsPosting => motion != null && motion.IsPosting;
    public bool IsTransitioning => motion != null && motion.IsTransitioning;
    public bool IsOperational
    {
        get
        {
            if (motion == null) motion = GetComponent<PreMissionStoneMotion>();
            return isActiveAndEnabled && boardCamera != null && boardCamera.isActiveAndEnabled && (motion == null || motion.IsOperational);
        }
    }
    public int CompositionIndex { get; private set; }
    public bool UsesWholeTableLayout => wholeTableLayout;
    public bool InputEnabled => IsOperational
        && flow != null && flow.CanSelectBoard && !flow.IsPosting;

    public void Bind(PreMissionFlowController owner, QuestProgressionSession progression)
    {
        flow = owner;
        if (!cameraHomeKnown) { cameraHomeRotation = boardCamera.transform.localRotation; cameraHomeKnown = true; }
        motion = GetComponent<PreMissionStoneMotion>();
        if (motion != null) { motion.Bind(this); motion.Settle(PreMissionView.Board, null); }
        if (run != progression)
        {
            foreach (var stone in stones) if (stone != null) { stone.gameObject.SetActive(false); Destroy(stone.gameObject); }
            stones.Clear(); focused = null; keyboardFocus = false;
            run = progression;
            CompositionIndex = new System.Random(run.Seed).Next(compositions.Length);
            PreparePlacements();
        }
        Synchronize();
        if(motion!=null) motion.PrepareProgressTrack();
        RefreshCameraFrame();
    }
    public void Synchronize()
    {
        if (run == null) return;
        for (int i = stones.Count - 1; i >= 0; i--)
        {
            var stone = stones[i];
            if (stone != null && run.FindOffer(stone.Offer.Id) != null) continue;
            if (focused == stone) focused = null;
            stones.RemoveAt(i);
            if (stone != null) { stone.gameObject.SetActive(false); Destroy(stone.gameObject); }
        }
        foreach (var offer in run.Offers)
        {
            if (FindStone(offer.Id) != null) continue;
            bool main = offer.Quest.category == QuestCategory.Main;
            var stone = Instantiate(main ? mainStonePrefab : smallStonePrefab, stoneParent);
            Vector2 slot = main ? Vector2.zero : slotPositions[offer.Slot];
            if(!main) slot+=QuestProgressionRuntime.StoneDisplacement(run,offer.Slot);
            stone.transform.localPosition = new Vector3(slot.x, 0.03f, slot.y);
            float angle = main ? 0f : slotAngles[offer.Slot];
            stone.transform.localRotation = Quaternion.Euler(0, angle, 0);
            stone.Bind(offer);
            stone.ConfigureFeedback(motion!=null ? motion.Feedback : null);
            if (motion != null && !QuestProgressionRuntime.WasPresented(offer.Id)) stone.PrepareArrival();
            stones.Add(stone);
        }
        stones.Sort((a, b) => a.Offer.Slot.CompareTo(b.Offer.Slot));
        var inlays=transform.Find("Impact Inlays");
        if(inlays!=null) inlays.gameObject.SetActive(run.BigQuest!=null);
        Physics.SyncTransforms();
    }
    void PreparePlacements()
    {
        using(LayoutMarker.Auto()) PreparePlacementData();
    }
    void PreparePlacementData()
    {
        if(wholeTableLayout && PrepareWholeTablePlacements()) return;
        PrepareAuthoredPlacements();
        footprintCacheValid=false;
        landingPositions=(Vector2[])slotPositions.Clone(); impactPositions=null;
    }
    sealed class PlacementPair
    {
        public Vector2 landing,impact; public Rect landingArea,impactArea; public float cost;
    }
    bool PrepareWholeTablePlacements()
    {
        int count=compositions[CompositionIndex].smallSlots.Length;
        var random=new System.Random(unchecked(run.Seed ^ 0x73A219D));
        slotPositions=new Vector2[count]; slotAngles=new float[count];
        for(int i=0;i<count;i++) slotAngles[i]=Signed(random)*(4+rotationVariation);
        CacheFootprints(count);
        var center=ProjectedHitArea(mainStonePrefab,Vector2.zero,0);
        var main=Padded(center,slabSpacing);
        // Restart bounded dart throwing instead of falling back to an overlapping
        // final sample. No future main footprint is excluded from this first view.
        for(int restart=0;restart<64;restart++)
        {
            var occupied=new List<Rect>(); bool complete=true,usesCenter=false;
            for(int slot=0;slot<count;slot++)
            {
                bool placed=false;
                for(int attempt=0;attempt<320;attempt++)
                {
                    var point=RandomTablePoint(random); if(!FitsTable(point,slot,0.35f)) continue;
                    var raw=SlotArea(slot,point); var area=Padded(raw,slabSpacing);
                    if(occupied.Exists(r=>r.Overlaps(area))) continue;
                    slotPositions[slot]=point; occupied.Add(area); usesCenter|=center.Overlaps(raw); placed=true; break;
                }
                if(!placed) { complete=false; break; }
            }
            if(complete && usesCenter && PrepareLandingPairs(random,main,count)) return true;
        }
        Debug.LogWarning("Quest layout settings are too crowded; using the authored safe layout.");
        return false;
    }
    Vector2 RandomTablePoint(System.Random random) => new Vector2(
        Mathf.Lerp(freePlacementBounds.xMin,freePlacementBounds.xMax,(float)random.NextDouble()),
        Mathf.Lerp(freePlacementBounds.yMin,freePlacementBounds.yMax,(float)random.NextDouble()));
    bool PrepareLandingPairs(System.Random random,Rect main,int count)
    {
        float preferred=motion!=null && motion.Shockwave!=null ? motion.Shockwave.SlideDistance : 0.85f;
        var candidates=new List<PlacementPair>[count]; var order=new List<int>();
        for(int slot=0;slot<count;slot++)
        {
            var options=candidates[slot]=new List<PlacementPair>(); order.Add(slot);
            for(int sample=0;sample<640;sample++)
            {
                var point=sample==0 ? slotPositions[slot] : RandomTablePoint(random);
                if(!FitsTable(point,slot,0.12f)) continue;
                var area=Padded(SlotArea(slot,point),slabSpacing);
                if(area.Overlaps(main)) continue;
                for(int step=0;step<6;step++)
                {
                    float distance=Mathf.Lerp(preferred,0.35f,step/5f);
                    var target=point+point.normalized*distance;
                    if(!FitsTable(target,slot,0)) continue;
                    var targetArea=Padded(SlotArea(slot,target),slabSpacing);
                    if(targetArea.Overlaps(main)) continue;
                    options.Add(new PlacementPair { landing=point,impact=target,landingArea=area,impactArea=targetArea,
                        cost=(point-slotPositions[slot]).sqrMagnitude+(float)random.NextDouble()*0.025f }); break;
                }
            }
            options.Sort((a,b)=>a.cost.CompareTo(b.cost));
            if(options.Count>180) options.RemoveRange(180,options.Count-180);
            if(options.Count==0) return false;
        }
        // Outer slabs have fewer choices, so resolve them before relocating the
        // central ones. Backtracking can move an outer slab when keeping it would
        // prevent a complete, readable six-offer arrangement.
        order.Sort((a,b)=>slotPositions[b].sqrMagnitude.CompareTo(slotPositions[a].sqrMagnitude));
        var chosen=new PlacementPair[count]; int budget=24000;
        bool Search(int at)
        {
            if(at==count) return true;
            int slot=order[at];
            foreach(var option in candidates[slot])
            {
                if(--budget<0) return false;
                bool clear=true;
                for(int i=0;i<at;i++)
                {
                    var other=chosen[order[i]];
                    if(option.landingArea.Overlaps(other.landingArea)||option.impactArea.Overlaps(other.impactArea)) { clear=false; break; }
                }
                if(!clear) continue;
                chosen[slot]=option; if(Search(at+1)) return true;
            }
            return false;
        }
        if(!Search(0)) return false;
        landingPositions=new Vector2[count]; impactPositions=new Vector2[count];
        for(int i=0;i<count;i++) { landingPositions[i]=chosen[i].landing; impactPositions[i]=chosen[i].impact; }
        return true;
    }
    void CacheFootprints(int count)
    {
        projectedFootprints=new Rect[count]; tabletopCorners=new Vector2[count][];
        var rotation=transform.rotation*cameraHomeRotation; var parent=stoneParent.localToWorldMatrix;
        var right=rotation*Vector3.right; var up=rotation*Vector3.up;
        projectedX=new Vector2(Vector3.Dot(parent.MultiplyVector(Vector3.right),right),Vector3.Dot(parent.MultiplyVector(Vector3.right),up));
        projectedZ=new Vector2(Vector3.Dot(parent.MultiplyVector(Vector3.forward),right),Vector3.Dot(parent.MultiplyVector(Vector3.forward),up));
        var hit=(BoxCollider)smallStonePrefab.HitCollider; var center=hit.center; var half=hit.size*0.5f;
        for(int i=0;i<count;i++)
        {
            projectedFootprints[i]=ProjectedHitArea(smallStonePrefab,Vector2.zero,slotAngles[i]);
            var corners=tabletopCorners[i]=new Vector2[4]; var yaw=Quaternion.Euler(0,slotAngles[i],0); int at=0;
            for(int x=-1;x<=1;x+=2) for(int z=-1;z<=1;z+=2)
            {
                var corner=yaw*(center+Vector3.Scale(half,new Vector3(x,0,z))); corners[at++]=new Vector2(corner.x,corner.z);
            }
        }
        footprintCacheValid=true;
    }
    Rect SlotArea(int slot,Vector2 point)
    {
        if(!footprintCacheValid) return ProjectedHitArea(smallStonePrefab,point,slotAngles[slot]);
        var area=projectedFootprints[slot]; var offset=projectedX*point.x+projectedZ*point.y;
        area.position+=offset; return area;
    }
    bool FitsTable(Vector2 point,int slot,float inset)
    {
        float xRadius=Mathf.Max(0.1f,tableRadii.x-inset),zRadius=Mathf.Max(0.1f,tableRadii.y-inset);
        // The left rail and its labels occupy the otherwise usable table edge.
        foreach(var corner in tabletopCorners[slot])
        {
            float px=point.x+corner.x,pz=point.y+corner.y;
            if(px< -8.45f || px*px/(xRadius*xRadius)+pz*pz/(zRadius*zRadius)>1) return false;
        }
        return true;
    }
    void PrepareAuthoredPlacements()
    {
        var composition=compositions[CompositionIndex];
        int count=composition.smallSlots.Length;
        slotPositions=new Vector2[count]; slotAngles=new float[count];
        var order=new int[count]; for(int i=0;i<count;i++) order[i]=i;
        var occupied=new List<Rect> { Padded(ProjectedHitArea(mainStonePrefab,Vector2.zero,0),0.04f) };
        // A separate seeded stream never consumes gameplay Random or changes the composition seed.
        var random=new System.Random(unchecked(run.Seed ^ 0x53a1847));
        if(randomizePlacement && count==6)
        {
            // Each authored half is a balanced triangle around the center. Swap the
            // triangles, then vary which quest occupies each point within that half.
            if(random.Next(2)==1) for(int i=0;i<3;i++) { order[i]=i+3; order[i+3]=i; }
            Shuffle(order,0,3,random); Shuffle(order,3,3,random);
        }
        for(int i=0;i<count;i++)
        {
            int source=order[i]; var position=composition.smallSlots[source];
            float angle=composition.angles[source%composition.angles.Length];
            if(randomizePlacement)
            {
                var anchor=position; float authoredAngle=angle;
                for(int attempt=0;attempt<24;attempt++)
                {
                    // End stones have less depth variation to stay clear of the board's
                    // narrow top/bottom edges and the larger future main collider.
                    float depth=Mathf.Abs(anchor.x)<2f ? endPositionVariation : Mathf.Abs(positionVariation.y);
                    var candidate=anchor+new Vector2(Signed(random)*Mathf.Abs(positionVariation.x),Signed(random)*depth);
                    candidate.x=Mathf.Clamp(candidate.x,placementBounds.xMin,placementBounds.xMax);
                    candidate.y=Mathf.Clamp(candidate.y,placementBounds.yMin,placementBounds.yMax);
                    float candidateAngle=authoredAngle+Signed(random)*rotationVariation;
                    var area=ProjectedHitArea(smallStonePrefab,candidate,candidateAngle);
                    bool clear=true; foreach(var other in occupied) if(area.Overlaps(other)) { clear=false; break; }
                    if(!clear) continue;
                    position=candidate; angle=candidateAngle; break;
                }
            }
            slotPositions[i]=position; slotAngles[i]=angle;
            occupied.Add(Padded(ProjectedHitArea(smallStonePrefab,position,angle),0.04f));
        }
    }
    Rect ProjectedHitArea(QuestStoneView prefab,Vector2 position,float angle)
    {
        var hit=(BoxCollider)prefab.HitCollider;
        var matrix=stoneParent.localToWorldMatrix*Matrix4x4.TRS(new Vector3(position.x,0.03f,position.y),Quaternion.Euler(0,angle,0),prefab.transform.localScale);
        var rotation=transform.rotation*cameraHomeRotation;
        var right=rotation*Vector3.right; var up=rotation*Vector3.up;
        Vector2 min=Vector2.one*float.MaxValue,max=Vector2.one*float.MinValue;
        for(int x=-1;x<=1;x+=2) for(int y=-1;y<=1;y+=2) for(int z=-1;z<=1;z+=2)
        {
            var point=matrix.MultiplyPoint3x4(hit.center+Vector3.Scale(hit.size*0.5f,new Vector3(x,y,z)));
            var projected=new Vector2(Vector3.Dot(point,right),Vector3.Dot(point,up));
            min=Vector2.Min(min,projected); max=Vector2.Max(max,projected);
        }
        return Rect.MinMaxRect(min.x,min.y,max.x,max.y);
    }
    static Rect Padded(Rect area,float margin) => Rect.MinMaxRect(area.xMin-margin,area.yMin-margin,area.xMax+margin,area.yMax+margin);
    public Vector3 ShockwaveSlotPosition(int slot,float height)
    {
        var position=slotPositions[slot]+QuestProgressionRuntime.StoneDisplacement(run,slot);
        return new Vector3(position.x,height,position.y);
    }
    public Dictionary<int,Vector2> LandingOffsets()
    {
        var offsets=new Dictionary<int,Vector2>();
        foreach(var stone in stones) if(!stone.IsMain) offsets[stone.Offer.Slot]=landingPositions[stone.Offer.Slot]-slotPositions[stone.Offer.Slot];
        return offsets;
    }
    public Vector3 LandingSlotPosition(int slot,float height) => new Vector3(landingPositions[slot].x,height,landingPositions[slot].y);
    public List<Rect> ImpactExclusions(float clearance)
    {
        var result=new List<Rect> { Padded(ProjectedHitArea(mainStonePrefab,Vector2.zero,0),clearance) };
        var positions=impactPositions ?? slotPositions;
        for(int slot=0;slot<positions.Length;slot++) result.Add(Padded(SlotArea(slot,positions[slot]),clearance));
        return result;
    }
    public Dictionary<int,Vector2> PlanShockwaveSlides(float distance,IReadOnlyList<Bounds> obstacles)
    {
        var result=new Dictionary<int,Vector2>();
        if(wholeTableLayout && impactPositions!=null)
        {
            foreach(var stone in stones) if(!stone.IsMain) result[stone.Offer.Slot]=impactPositions[stone.Offer.Slot]-slotPositions[stone.Offer.Slot];
            return result;
        }
        var occupied=new Dictionary<int,Rect>();
        foreach(var stone in stones)
            if(!stone.IsMain) occupied[stone.Offer.Slot]=ProjectedHitArea(smallStonePrefab,new Vector2(stone.transform.localPosition.x,stone.transform.localPosition.z),slotAngles[stone.Offer.Slot]);
        foreach(var stone in stones)
        {
            if(stone.IsMain) continue;
            int slot=stone.Offer.Slot; var start=new Vector2(stone.transform.localPosition.x,stone.transform.localPosition.z);
            var direction=start.normalized; Vector2 target=start; bool found=false;
            for(int step=0;step<10&&!found;step++) foreach(float angle in new[]{0f,-12f,12f,-24f,24f})
            {
                float radians=angle*Mathf.Deg2Rad;
                var heading=new Vector2(direction.x*Mathf.Cos(radians)-direction.y*Mathf.Sin(radians),direction.x*Mathf.Sin(radians)+direction.y*Mathf.Cos(radians));
                var candidate=start+heading*(distance*Mathf.Pow(0.88f,step));
                candidate.x=Mathf.Clamp(candidate.x,-7.5f,7.5f); candidate.y=Mathf.Clamp(candidate.y,-4.05f,4.05f);
                var area=Padded(ProjectedHitArea(smallStonePrefab,candidate,slotAngles[slot]),0.04f);
                bool clear=true;
                foreach(var other in occupied) if(other.Key!=slot && area.Overlaps(other.Value)) { clear=false; break; }
                if(clear && obstacles!=null) foreach(var bounds in obstacles) if(area.Overlaps(ProjectTerrainBounds(bounds))) { clear=false; break; }
                if(!clear) continue;
                target=candidate; occupied[slot]=area; found=true; break;
            }
            result[slot]=target-slotPositions[slot];
        }
        return result;
    }
    // Terrain avoids the initial, landing and impact footprints. This does not
    // reserve the main footprint from side quests before it unlocks.
    public List<Rect> TerrainExclusions(float clearance)
    {
        var result=new List<Rect> { Padded(ProjectedHitArea(mainStonePrefab,Vector2.zero,0),clearance) };
        for(int i=0;i<slotPositions.Length;i++)
        {
            result.Add(Padded(SlotArea(i,slotPositions[i]),clearance));
            if(wholeTableLayout && impactPositions!=null)
            {
                result.Add(Padded(SlotArea(i,landingPositions[i]),clearance));
                result.Add(Padded(SlotArea(i,impactPositions[i]),clearance));
            }
        }
        return result;
    }
    public Rect ProjectTerrainBounds(Bounds localBounds)
    {
        var world=QuestStoneView.TransformBounds(localBounds,transform.localToWorldMatrix);
        var rotation=transform.rotation*cameraHomeRotation;
        var right=rotation*Vector3.right; var up=rotation*Vector3.up;
        Vector2 min=Vector2.one*float.MaxValue,max=Vector2.one*float.MinValue;
        for(int x=-1;x<=1;x+=2) for(int y=-1;y<=1;y+=2) for(int z=-1;z<=1;z+=2)
        {
            var point=world.center+Vector3.Scale(world.extents,new Vector3(x,y,z));
            var projected=new Vector2(Vector3.Dot(point,right),Vector3.Dot(point,up));
            min=Vector2.Min(min,projected); max=Vector2.Max(max,projected);
        }
        return Rect.MinMaxRect(min.x,min.y,max.x,max.y);
    }
    static float Signed(System.Random random) => (float)random.NextDouble()*2f-1f;
    static void Shuffle(int[] order,int start,int count,System.Random random)
    {
        for(int i=start+count-1;i>start;i--)
        {
            int other=random.Next(start,i+1); int value=order[i]; order[i]=order[other]; order[other]=value;
        }
    }
    public QuestStoneView FindStone(string offerId) => stones.Find(s => s != null && s.Offer.Id == offerId);
    public void FocusFirst() { focused = stones.Count > 0 ? stones[0] : null; keyboardFocus = false; ClearHighlights(); }
    public void FocusOffer(string offerId) { focused = FindStone(offerId); keyboardFocus = false; }
    public void ClearHighlights() { foreach (var stone in stones) if (stone != null) stone.SetHighlighted(false); }
    public void StartArrivals(Action ready) { if (motion != null) motion.StartArrivals(ready); else ready?.Invoke(); }
    public void FinishArrivals() { if (motion != null) motion.FinishArrivals(); }
    public void Inspect(string offerId, Action<float> frame, Action ready)
    { if (motion != null) motion.Inspect(FindStone(offerId), frame, ready); else ready?.Invoke(); }
    public void ReturnInspection(Action<float> frame, Action ready)
    { if (motion != null) motion.Return(frame, ready); else ready?.Invoke(); }
    public void RecoverView(PreMissionView view, string offerId) { if (motion != null) motion.Settle(view, offerId); ClearHighlights(); }
    public void RecoverOwner() { if (Application.isPlaying && flow != null && flow.Session != null) flow.RecoverPresentation(); }
    public void DetachOwner(PreMissionFlowController owner)
    {
        if (!ReferenceEquals(flow, owner)) return;
        motion?.Settle(PreMissionView.Board, null); flow = null; ClearHighlights();
    }
    void OnDisable() { wasDisabled = true; ClearHighlights(); RecoverOwner(); }
    void OnEnable() { if (wasDisabled) { wasDisabled = false; RecoverOwner(); } }

    bool PointerOverUI(Vector2 screenPosition)
    {
        if (EventSystem.current == null) return false;
        uiHits.Clear();
        if(raycastSystem!=EventSystem.current || uiPointer==null)
        { raycastSystem=EventSystem.current; uiPointer=new PointerEventData(raycastSystem); }
        uiPointer.Reset(); uiPointer.position=screenPosition;
        raycastSystem.RaycastAll(uiPointer,uiHits);
        foreach (var hit in uiHits) if (hit.module is GraphicRaycaster) return true;
        return false;
    }
    public QuestStoneView StoneAt(Vector2 screenPosition)
    {
        if (!InputEnabled || PointerOverUI(screenPosition)) return null;
        if (!Physics.Raycast(boardCamera.ScreenPointToRay(screenPosition), out var hit, boardCamera.farClipPlane,
            1 << gameObject.layer, QueryTriggerInteraction.Ignore)) return null;
        var stone = hit.collider.GetComponentInParent<QuestStoneView>();
        return stone != null && stone.IsReady && !stone.IsMoving && stones.Contains(stone) && run.FindOffer(stone.Offer.Id) != null ? stone : null;
    }
    public bool SelectAt(Vector2 screenPosition)
    {
        var stone = StoneAt(screenPosition);
        if (stone == null) return false;
        flow.SelectQuest(stone.Offer.Quest);
        return flow.Session.SelectedOfferId == stone.Offer.Id && flow.Session.View == PreMissionView.Details;
    }
    void Update()
    {
        if (!InputEnabled) { ClearHighlights(); return; }
        var mouse = Mouse.current;
        QuestStoneView hovered = null;
        if (mouse != null)
        {
            var point = mouse.position.ReadValue();
            if ((point - lastPointer).sqrMagnitude > 1f) keyboardFocus = false;
            lastPointer = point; hovered = StoneAt(point);
            if (mouse.leftButton.wasPressedThisFrame && SelectAt(point)) { ClearHighlights(); return; }
        }
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            Vector2 direction = keyboard.leftArrowKey.wasPressedThisFrame ? Vector2.left : keyboard.rightArrowKey.wasPressedThisFrame ? Vector2.right
                : keyboard.upArrowKey.wasPressedThisFrame ? Vector2.up : keyboard.downArrowKey.wasPressedThisFrame ? Vector2.down : Vector2.zero;
            if (direction != Vector2.zero)
            {
                FocusNeighbour(direction); keyboardFocus = true;
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            }
            if (keyboardFocus && focused != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
                && (EventSystem.current == null || EventSystem.current.currentSelectedGameObject == null))
            { flow.SelectQuest(focused.Offer.Quest); ClearHighlights(); return; }
        }
        foreach (var stone in stones) stone.SetHighlighted(stone == (keyboardFocus ? focused : hovered));
    }
    void FocusNeighbour(Vector2 direction)
    {
        if (focused == null) { focused = stones.Count > 0 ? stones[0] : null; return; }
        Vector2 origin = boardCamera.WorldToScreenPoint(focused.HitCollider.bounds.center);
        QuestStoneView next = null; float best = float.MaxValue;
        foreach (var stone in stones)
        {
            if (stone == focused) continue;
            Vector2 delta = (Vector2)boardCamera.WorldToScreenPoint(stone.HitCollider.bounds.center) - origin;
            float alignment = Vector2.Dot(delta.normalized, direction);
            if (alignment < 0.25f) continue;
            float score = delta.magnitude / (alignment * alignment);
            if (score < best) { best = score; next = stone; }
        }
        if (next != null) focused = next;
    }
    void LateUpdate() => RefreshCameraFrame();
    // Fixed reserved bounds include the future main stone, so unlock never reframes existing offers.
    public void RefreshCameraFrame()
    {
        if (boardCamera == null) return;
        if (!cameraHomeKnown) { cameraHomeRotation = boardCamera.transform.localRotation; cameraHomeKnown = true; }
        Quaternion rotation = transform.rotation * cameraHomeRotation;
        var right = transform.InverseTransformDirection(rotation * Vector3.right);
        var up = transform.InverseTransformDirection(rotation * Vector3.up);
        float width = Vector3.Dot(Abs(right), framingSize), height = Vector3.Dot(Abs(up), framingSize);
        float size = Mathf.Max(height / (2f * screenArea.height), width / (2f * boardCamera.aspect * screenArea.width));
        Vector2 offset = screenArea.center - Vector2.one * 0.5f;
        Vector3 position = transform.TransformPoint(framingCenter) - rotation * Vector3.forward * 24f
            - rotation * Vector3.up * (offset.y * size * 2f)
            - rotation * Vector3.right * (offset.x * size * 2f * boardCamera.aspect);
        if (motion != null) motion.ApplyCamera(ref position, ref rotation, ref size, boardCamera.aspect);
        boardCamera.transform.SetPositionAndRotation(position, rotation); boardCamera.orthographicSize = size;
    }
    static Vector3 Abs(Vector3 value) => new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
}
