using System;
using UnityEngine;

// Symbolic briefing geometry only. No gameplay actors, map generation or result handling.
[DisallowMultipleComponent]
public sealed class PreMissionMissionMiniature : MonoBehaviour
{
    [Serializable] public sealed class Variant
    {
        public QuestMissionType missionType;
        public Transform root;
        [Tooltip("The supplied logo includes its own base and forms as one object.")]
        public bool authoredLogo;
        [NonSerialized] public Transform[] pieces;
        [NonSerialized] public Vector3[] positions, scales;
        [NonSerialized] public Bounds fullBounds;
    }
    [SerializeField] Transform display;
    [SerializeField] Variant[] variants;
    [SerializeField] GameObject prototypePlinth;
    [SerializeField, Min(0f)] float emergenceDepth=0.14f;
    [SerializeField, Range(0f,0.3f)] float pieceStagger=0.12f;
    [SerializeField, Range(0.1f,0.5f)] float baseRevealFraction=0.32f;
    [SerializeField, Range(0f,0.3f)] float objectiveStart=0.18f;
    PreMissionStoneBoard board;
    QuestStoneView stone;
    Variant active;
    bool initialized, wasDisabled;
    Renderer[] renderers;
    Vector3 displayPosition,displayScale;
    public QuestDefinition Quest => stone!=null ? stone.Offer.Quest : null;
    public float Progress { get; private set; }
    public bool IsVisible => display.gameObject.activeInHierarchy;
    public int PieceCount => active!=null ? active.pieces.Length : 0;
    public QuestMissionType? ActiveType => active!=null ? active.missionType : (QuestMissionType?)null;
    public bool UsesAuthoredLogo => active!=null && active.authoredLogo;
    public void Initialize(PreMissionStoneBoard owner)
    {
        board=owner;
        if(initialized) return;
        displayPosition=display.localPosition; displayScale=display.localScale;
        renderers=display.GetComponentsInChildren<Renderer>(true);
        foreach(var variant in variants)
        {
            int count=variant.root.childCount;
            variant.pieces=new Transform[count]; variant.positions=new Vector3[count]; variant.scales=new Vector3[count];
            for(int i=0;i<count;i++)
            {
                var piece=variant.root.GetChild(i); variant.pieces[i]=piece; variant.positions[i]=piece.localPosition; variant.scales[i]=piece.localScale;
            }
            bool hasBounds=false;
            foreach(var renderer in renderers)
            {
                if(variant.authoredLogo && prototypePlinth!=null && renderer.transform.IsChildOf(prototypePlinth.transform)) continue;
                bool belongs=renderer.transform.IsChildOf(variant.root), shared=true;
                foreach(var other in variants) if(renderer.transform.IsChildOf(other.root)) { shared=false; break; }
                if(!belongs && !shared) continue;
                var bounds=QuestStoneView.TransformBounds(renderer.localBounds,transform.worldToLocalMatrix*renderer.localToWorldMatrix);
                if(hasBounds) variant.fullBounds.Encapsulate(bounds); else { variant.fullBounds=bounds; hasBounds=true; }
            }
        }
        initialized=true; Clear();
    }
    public void Bind(QuestStoneView selected)
    {
        Clear(); stone=selected;
        // Authored slabs already carry their complete category/type/logo layout.
        // Inspection brings that same object closer instead of replacing it.
        if(selected!=null && selected.HasBoardInscription) { SetProgress(0); return; }
        if(stone==null) return;
        foreach(var variant in variants)
        {
            bool match=variant.missionType==stone.Offer.Quest.missionType;
            variant.root.gameObject.SetActive(match); if(match) active=variant;
        }
        if(prototypePlinth!=null) prototypePlinth.SetActive(active==null || !active.authoredLogo);
        SetProgress(0);
    }
    public void SetProgress(float value)
    {
        Progress=Mathf.Clamp01(value);
        display.gameObject.SetActive(stone!=null && active!=null && Progress>0.001f);
        if(stone==null) return;
        // Attach to the actual slab plane; an axis-aligned bounding-box maximum
        // floats above the highest corner as a slab turns during inspection.
        transform.SetPositionAndRotation(stone.SurfacePoint+stone.SurfaceRotation*Vector3.up*0.015f,stone.SurfaceRotation);
        transform.localScale=Vector3.one*(stone.IsMain ? 1.18f : 1f);
        if(stone.HasBoardInscription) { stone.SetInscriptionVisibility(1); return; }
        stone.SetInscriptionVisibility(active!=null ? 1-Progress : 1);
        if(active==null) return;
        bool reduced=board!=null && board.Motion!=null && board.Motion.Feedback!=null && board.Motion.Feedback.ReducedMotion;
        float foundation=reduced ? 1 : Mathf.SmoothStep(0,1,Mathf.Clamp01(Progress/Mathf.Max(0.01f,baseRevealFraction)));
        display.localPosition=displayPosition-Vector3.up*(emergenceDepth*(1-foundation));
        display.localScale=Vector3.Scale(displayScale,new Vector3(foundation,Mathf.Lerp(0.1f,1,foundation),foundation));
        float formation=reduced ? 1 : Mathf.Clamp01((Progress-objectiveStart)/Mathf.Max(0.01f,1-objectiveStart));
        for(int i=0;i<active.pieces.Length;i++)
        {
            if(active.authoredLogo)
            {
                // Keep the artist's complete coin intact. The shared display
                // transform supplies its existing emergence/retraction motion.
                active.pieces[i].gameObject.SetActive(true);
                active.pieces[i].localPosition=active.positions[i]; active.pieces[i].localScale=active.scales[i];
                continue;
            }
            float part=Mathf.Clamp01(formation*(1+pieceStagger*(active.pieces.Length-1))-pieceStagger*i);
            float eased=part*part*(3-2*part);
            active.pieces[i].gameObject.SetActive(part>0);
            active.pieces[i].localPosition=active.positions[i]-Vector3.up*(emergenceDepth*(1-eased));
            active.pieces[i].localScale=Vector3.Scale(active.scales[i],new Vector3(1,Mathf.Lerp(0.05f,1,eased),1));
        }
    }
    public void Clear()
    {
        if(stone!=null) stone.SetInscriptionVisibility(1);
        stone=null; active=null; Progress=0;
        if(display!=null)
        {
            display.gameObject.SetActive(false);
            if(initialized) { display.localPosition=displayPosition; display.localScale=displayScale; }
        }
    }
    void LateUpdate()
    {
        if(stone==null || (!IsVisible && !stone.HasBoardInscription)) return;
        // Preference changes must update a held pose too, not just the next
        // coroutine sample. Refresh the slab first so the preview cannot lag it.
        stone.RefreshMotionPreference(); SetProgress(Progress);
    }
    public Bounds InspectionBounds(QuestStoneView selected,Vector3 offset,Vector3 angles,Vector3 scale,float motionAmount)
    {
        if(selected.HasBoardInscription) return selected.MotionBounds(offset,angles,scale,motionAmount);
        foreach(var variant in variants)
        {
            if(variant.missionType!=selected.Offer.Quest.missionType) continue;
            return QuestStoneView.TransformBounds(variant.fullBounds,selected.PreviewFrame(offset,angles,scale,motionAmount));
        }
        return selected.MotionBounds(offset,angles,scale,motionAmount);
    }
    public Bounds WorldBounds
    {
        get
        {
            var bounds=new Bounds(transform.position,Vector3.zero);
            if(renderers!=null) foreach(var renderer in renderers)
                if(renderer!=null && renderer.gameObject.activeInHierarchy) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }
    }
    void OnDisable() { wasDisabled=true; board?.RecoverOwner(); }
    void OnEnable() { if(wasDisabled) { wasDisabled=false; board?.RecoverOwner(); } }
}
