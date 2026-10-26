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
        [NonSerialized] public Transform[] pieces;
        [NonSerialized] public Vector3[] positions, scales;
        [NonSerialized] public Bounds fullBounds;
    }
    [SerializeField] Transform display;
    [SerializeField] Variant[] variants;
    [SerializeField, Min(0f)] float emergenceDepth=0.14f;
    [SerializeField, Range(0f,0.3f)] float pieceStagger=0.12f;
    PreMissionStoneBoard board;
    QuestStoneView stone;
    Variant active;
    bool initialized, wasDisabled;
    Renderer[] renderers;
    public QuestDefinition Quest => stone!=null ? stone.Offer.Quest : null;
    public float Progress { get; private set; }
    public bool IsVisible => display.gameObject.activeInHierarchy;
    public int PieceCount => active!=null ? active.pieces.Length : 0;
    public QuestMissionType? ActiveType => active!=null ? active.missionType : (QuestMissionType?)null;
    public void Initialize(PreMissionStoneBoard owner)
    {
        board=owner;
        if(initialized) return;
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
        if(stone==null) return;
        foreach(var variant in variants)
        {
            bool match=variant.missionType==stone.Offer.Quest.missionType;
            variant.root.gameObject.SetActive(match); if(match) active=variant;
        }
        SetProgress(0);
    }
    public void SetProgress(float value)
    {
        Progress=Mathf.Clamp01(value);
        display.gameObject.SetActive(stone!=null && active!=null && Progress>0.001f);
        if(stone==null) return;
        stone.SetInscriptionVisibility(active!=null ? 1-Progress : 1);
        var bounds=stone.RockBounds;
        transform.position=new Vector3(bounds.center.x,bounds.max.y+0.015f,bounds.center.z);
        transform.rotation=Quaternion.Euler(0,stone.transform.eulerAngles.y,0);
        transform.localScale=Vector3.one*(stone.IsMain ? 1.18f : 1f);
        if(active==null) return;
        for(int i=0;i<active.pieces.Length;i++)
        {
            bool reduced=board!=null && board.Motion!=null && board.Motion.Feedback!=null && board.Motion.Feedback.ReducedMotion;
            float part=reduced ? 1 : Mathf.Clamp01(Progress*(1+pieceStagger*(active.pieces.Length-1))-pieceStagger*i);
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
        if(display!=null) display.gameObject.SetActive(false);
    }
    public Bounds InspectionBounds(QuestStoneView selected,Bounds rockBounds)
    {
        foreach(var variant in variants)
        {
            if(variant.missionType!=selected.Offer.Quest.missionType) continue;
            var matrix=Matrix4x4.TRS(new Vector3(rockBounds.center.x,rockBounds.max.y+0.015f,rockBounds.center.z),
                Quaternion.Euler(0,selected.transform.eulerAngles.y,0),Vector3.one*(selected.IsMain?1.18f:1f));
            return QuestStoneView.TransformBounds(variant.fullBounds,matrix);
        }
        return rockBounds;
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
