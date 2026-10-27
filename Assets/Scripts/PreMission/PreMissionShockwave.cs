using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// One visible front drives permanent presentation displacement and terrain damage.
// Quest availability/results remain entirely in QuestProgressionSession.
[DisallowMultipleComponent]
public sealed class PreMissionShockwave : MonoBehaviour
{
    [SerializeField] MeshRenderer dustRing;
    [SerializeField] float travelDuration=1.25f;
    [SerializeField] float responseDuration=0.55f;
    [SerializeField] float maximumRadius=12f;
    [SerializeField] float ringWidth=0.48f;
    [SerializeField] float slideDistance=0.85f;
    [SerializeField] float slideTilt=3f;
    [SerializeField] Rect surfaceBounds=new Rect(-9.9f,-4.95f,19.8f,9.9f);
    const int Segments=192, Bands=4;
    readonly Vector3[] vertices=new Vector3[Segments*Bands];
    readonly Color[] colors=new Color[Segments*Bands];
    readonly List<StoneSlide> slides=new List<StoneSlide>();
    sealed class StoneSlide { public QuestStoneView stone; public Vector3 start,target; public float distance; }
    PreMissionStoneBoard board;
    Mesh mesh;
    Vector3 origin;
    bool active;
    bool motionSuppressed;
    public float TotalDuration => Mathf.Max(0.1f,travelDuration)+Mathf.Max(0.1f,responseDuration);
    public float SlideDistance => Mathf.Max(0.35f,slideDistance);
    public float Radius { get; private set; }
    public bool IsActive => active;
    public bool MotionSuppressed => motionSuppressed;
    public bool IsVisible => dustRing!=null && dustRing.enabled;
    public MeshRenderer Ring => dustRing;
    bool Reduced => board!=null && board.Motion.Feedback!=null && board.Motion.Feedback.ReducedMotion;
    public void Bind(PreMissionStoneBoard owner) { board=owner; Hide(); }
    void EnsureMesh()
    {
        if(mesh!=null || dustRing==null) return;
        mesh=new Mesh { name="Traveling Dust Front" }; mesh.MarkDynamic();
        var uv=new Vector2[vertices.Length]; for(int i=0;i<uv.Length;i++) uv[i]=new Vector2(0.5f,0.5f);
        var triangles=new int[Segments*(Bands-1)*6]; int at=0;
        for(int b=0;b<Bands-1;b++) for(int s=0;s<Segments;s++)
        {
            int a=b*Segments+s,n=b*Segments+(s+1)%Segments;
            triangles[at++]=a; triangles[at++]=a+Segments; triangles[at++]=n;
            triangles[at++]=n; triangles[at++]=a+Segments; triangles[at++]=n+Segments;
        }
        // URP's particle pass still reads normal/tangent streams for an unlit mesh.
        var normals=new Vector3[vertices.Length]; var tangents=new Vector4[vertices.Length];
        for(int i=0;i<vertices.Length;i++) { normals[i]=Vector3.up; tangents[i]=new Vector4(1,0,0,1); }
        mesh.vertices=vertices; mesh.uv=uv; mesh.normals=normals; mesh.tangents=tangents; mesh.triangles=triangles;
        dustRing.GetComponent<MeshFilter>().sharedMesh=mesh;
    }
    public void Begin(Vector3 worldOrigin)
    {
        if(board==null) return;
        motionSuppressed=Reduced;
        origin=transform.InverseTransformPoint(worldOrigin); origin.y=0.09f;
        slides.Clear();
        if(!QuestProgressionRuntime.HasShockwave(board.Progression))
        {
            var terrain=board.Motion.Terrain;
            var terrainMoves=terrain!=null ? terrain.PlanDisplacements(origin) : new Dictionary<int,Vector2>();
            var broken=terrain!=null ? terrain.PlanBreaks() : new List<int>();
            var obstacles=terrain!=null ? terrain.SurvivingDestinationBounds(terrainMoves,broken) : new List<Bounds>();
            var stoneMoves=board.PlanShockwaveSlides(slideDistance,obstacles);
            QuestProgressionRuntime.RememberShockwave(board.Progression,stoneMoves,terrainMoves,broken);
        }
        foreach(var stone in board.Stones.Where(s=>!s.IsMain))
        {
            var start=stone.transform.localPosition;
            var target=board.ShockwaveSlotPosition(stone.Offer.Slot,start.y);
            slides.Add(new StoneSlide { stone=stone,start=start,target=target,distance=new Vector2(start.x-origin.x,start.z-origin.z).magnitude });
        }
        board.Motion.Terrain?.PrepareImpact();
        active=true; EnsureMesh(); Advance(0);
    }
    public float LocalProgress(float distance,float progress)
    {
        float elapsed=Mathf.Clamp01(progress)*TotalDuration;
        float hit=Mathf.Max(0,distance)/Mathf.Max(0.01f,maximumRadius)*Mathf.Max(0.1f,travelDuration);
        return Mathf.Clamp01((elapsed-hit)/Mathf.Max(0.1f,responseDuration));
    }
    public float Response(float distance,float progress)
    {
        float t=LocalProgress(distance,progress);
        if(t<=0 || t>=1 || Reduced || motionSuppressed) return 0;
        return Mathf.Sin(t*Mathf.PI*2f)*(1-t)*Mathf.Lerp(1,0.55f,Mathf.Clamp01(distance/maximumRadius));
    }
    public void Advance(float progress)
    {
        if(!active || board==null) return;
        motionSuppressed|=Reduced;
        float elapsed=Mathf.Clamp01(progress)*TotalDuration;
        Radius=maximumRadius*Mathf.Clamp01(elapsed/Mathf.Max(0.1f,travelDuration));
        foreach(var slide in slides)
        {
            if(slide.stone==null) continue;
            float t=LocalProgress(slide.distance,progress); if(motionSuppressed) t=1;
            float eased=1-Mathf.Pow(1-t,3);
            slide.stone.transform.localPosition=Vector3.Lerp(slide.start,slide.target,eased);
            var radial=new Vector3(slide.start.x-origin.x,0,slide.start.z-origin.z).normalized;
            var local=slide.stone.transform.InverseTransformDirection(radial);
            slide.stone.SetImpactReaction(Vector3.zero,new Vector3(local.z,0,-local.x)*(Response(slide.distance,progress)*slideTilt));
        }
        board.Motion.Terrain?.ApplyImpact(transform.TransformPoint(origin),progress);
        DrawRing(elapsed);
    }
    void DrawRing(float elapsed)
    {
        if(dustRing==null || mesh==null) return;
        float p=elapsed/Mathf.Max(0.1f,travelDuration);
        float alpha=board.Motion.Feedback!=null ? board.Motion.Feedback.ParticleScale : 1;
        dustRing.enabled=!motionSuppressed && !Reduced && alpha>0 && p>0 && p<1;
        if(!dustRing.enabled) return;
        float fade=Mathf.SmoothStep(0,1,p/0.06f)*(1-Mathf.SmoothStep(0,1,(p-0.82f)/0.18f));
        for(int b=0;b<Bands;b++) for(int s=0;s<Segments;s++)
        {
            float angle=s*Mathf.PI*2/Segments, r=Mathf.Max(0,Radius+(b/3f-0.5f)*ringWidth);
            var point=new Vector3(origin.x+Mathf.Cos(angle)*r,origin.y,origin.z+Mathf.Sin(angle)*r);
            bool inside=surfaceBounds.Contains(new Vector2(point.x,point.z));
            point.x=Mathf.Clamp(point.x,surfaceBounds.xMin,surfaceBounds.xMax); point.z=Mathf.Clamp(point.z,surfaceBounds.yMin,surfaceBounds.yMax);
            int index=b*Segments+s; vertices[index]=point;
            float band=b==0 || b==Bands-1 ? 0 : 0.65f;
            colors[index]=new Color(0.76f,0.69f,0.56f,inside ? band*fade*alpha*(0.85f+0.15f*Mathf.Sin(s*0.71f)) : 0);
        }
        mesh.vertices=vertices; mesh.colors=colors; mesh.RecalculateBounds();
    }
    public void Settle()
    {
        if(board!=null && (QuestProgressionRuntime.HasShockwave(board.Progression)||QuestProgressionRuntime.WasLandingCleared(board.Progression)))
        {
            foreach(var stone in board.Stones.Where(s=>!s.IsMain))
            { stone.transform.localPosition=board.ShockwaveSlotPosition(stone.Offer.Slot,stone.transform.localPosition.y); stone.SetImpactReaction(Vector3.zero,Vector3.zero); }
            board.Motion.Terrain?.Settle(); Physics.SyncTransforms();
        }
        Hide();
    }
    public void Hide() { active=motionSuppressed=false; Radius=0; if(dustRing!=null) dustRing.enabled=false; }
    void OnDisable() => Hide();
    void OnDestroy() { if(mesh!=null) Destroy(mesh); }
}
