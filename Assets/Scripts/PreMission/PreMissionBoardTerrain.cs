using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;

// Decorative tabletop geology. No colliders, quest rules, routes or mission data.
[DisallowMultipleComponent]
public sealed class PreMissionBoardTerrain : MonoBehaviour
{
    static readonly ProfilerMarker BuildMarker=new ProfilerMarker("PreMission.TerrainBuild");
    [SerializeField] Material rockMaterial;
    [Header("Seeded ridge groups (board-local units)")]
    [SerializeField, Range(8,32)] int ridgeCount=24;
    [SerializeField] Vector2 ridgeLength=new Vector2(0.9f,1.8f);
    [SerializeField] Vector2 ridgeWidth=new Vector2(0.45f,0.85f);
    [SerializeField] Vector2 ridgeHeight=new Vector2(0.7f,1.9f);
    [SerializeField, Min(0f)] float questClearance=0.18f;
    [Header("Unscaled terrain reveal, before quest drops")]
    [SerializeField, Min(0.01f)] float riseDuration=0.9f;
    [SerializeField, Min(0f)] float revealStagger=0.38f;
    [SerializeField, Min(0f)] float settlePause=0.10f;
    [Header("Heavy impact presentation")]
    [SerializeField, Range(0f,10f)] float impactTilt=5f;
    [SerializeField, Range(0f,0.2f)] float impactLift=0.10f;
    [SerializeField, Min(0f)] float isolationGap=0.72f;
    [SerializeField, Min(0f)] float outwardSlide=0.60f;
    sealed class Fragment
    {
        public Transform visual; public Mesh mesh; public Vector3 start,target; public Quaternion rotation;
    }
    sealed class Ridge
    {
        public Transform visual;
        public Vector3 rest;
        public Quaternion rotation;
        public Mesh mesh;
        public float delay, depth;
        public int index; public Bounds bounds; public MeshRenderer renderer;
        public Vector3 impactStart,impactTarget; public bool willBreak,hitBroken;
        public readonly List<Fragment> fragments=new List<Fragment>();
    }
    readonly List<Ridge> ridges=new List<Ridge>();
    readonly List<Bounds> restBounds=new List<Bounds>();
    PreMissionStoneBoard board;
    QuestProgressionSession run;
    bool revealed, wasDisabled, impactActive;
    public bool IsReacting { get; private set; }
    public bool IsRevealing { get; private set; }
    public bool IsRevealed => revealed;
    public bool HasPending => run!=null && !revealed;
    public float RevealProgress { get; private set; }
    public int RidgeCount => ridges.Count;
    public IReadOnlyList<Bounds> RestBounds => restBounds;
    public int BrokenCount => ridges.Count(r=>r.hitBroken);
    public event Action Revealed;
    bool ReducedMotion => board!=null && board.Motion!=null && board.Motion.Feedback!=null && board.Motion.Feedback.ReducedMotion;

    public void Bind(PreMissionStoneBoard owner)
    {
        board=owner;
        if(run!=board.Progression)
        {
            ClearRidges(); run=board.Progression; BuildRidges();
        }
        revealed=QuestProgressionRuntime.WasTerrainRevealed(run);
        IsRevealing=false; RevealProgress=revealed ? 1 : 0; restBounds.Clear();
        foreach(var ridge in ridges)
        {
            ridge.visual.localPosition=RestPosition(ridge);
            ridge.visual.localRotation=ridge.rotation;
            ridge.impactStart=ridge.impactTarget=RestPosition(ridge);
            ridge.visual.gameObject.SetActive(revealed);
            if(revealed) RestoreDamage(ridge);
            var bounds=ridge.bounds; bounds.center+=RestPosition(ridge)-ridge.rest; restBounds.Add(bounds);
        }
    }
    void BuildRidges()
    {
        using(BuildMarker.Auto()) BuildGeometry();
    }
    void BuildGeometry()
    {
        if(rockMaterial==null) return;
        var random=new System.Random(unchecked(run.Seed ^ 0x4A73C19));
        var exclusions=board.TerrainExclusions(questClearance);
        var occupied=new List<Rect>();
        // Rim ranges frame the locations; inner outcrops break up the open slate.
        var anchors=new List<Vector2>();
        foreach(float z in new[]{3.55f,-3.55f})
            foreach(float x in new[]{-6.7f,-4.1f,-2.3f,2.3f,4.1f,6.7f}) anchors.Add(new Vector2(x,z));
        foreach(float z in new[]{-2.6f,-1.2f,0.3f,1.7f,2.9f}) anchors.Add(new Vector2(7.7f,z));
        foreach(float x in new[]{-3.5f,3.5f})
            foreach(float z in new[]{-1.7f,0f,1.7f}) anchors.Add(new Vector2(x,z));
        anchors.Add(new Vector2(-6.9f,0));
        for(int i=anchors.Count-1;i>0;i--) { int j=random.Next(i+1); var saved=anchors[i]; anchors[i]=anchors[j]; anchors[j]=saved; }
        // Bounded sampling: crowded custom layouts get fewer ridges, never an
        // overlapping fallback on top of a quest, progress marker or frame.
        for(int i=0;i<Mathf.Min(ridgeCount,32);i++)
        {
            Vector2 anchor=anchors[i%anchors.Count];
            for(int attempt=0;attempt<64;attempt++)
            {
                float length=Between(random,ridgeLength), width=Between(random,ridgeWidth);
                if(attempt>=10) { length*=0.65f; width*=0.75f; }
                if(attempt>=32) { length*=0.70f; width*=0.75f; }
                Vector3 position=new Vector3(anchor.x+Signed(random)*0.32f,0.045f,anchor.y+Signed(random)*0.20f);
                // If an authored rim anchor is crowded, try small outcrops in
                // other gaps before giving up. They obey the same full bounds.
                if(attempt>=14) position=new Vector3(Mathf.Lerp(-6.8f,8f,(float)random.NextDouble()),0.045f,Mathf.Lerp(-3.6f,3.6f,(float)random.NextDouble()));
                float height=Between(random,ridgeHeight)*(position.z< -2.5f ? 0.65f : 1)*(attempt>=10 ? 0.7f : 1);
                if(attempt>=32) height*=0.7f;
                float angle=(position.x>7 ? 90 : 0)+Signed(random)*26f;
                var points=RidgePoints(random,length,width,height);
                var rotation=Quaternion.Euler(0,angle,0);
                var bounds=new Bounds(rotation*points[0]+position,Vector3.zero);
                foreach(var point in points) bounds.Encapsulate(rotation*point+position);
                // Keep the whole mesh inside the tabletop and reserve the left rail.
                if(bounds.min.x< -7.2f || bounds.max.x>8.35f || bounds.min.z< -3.92f || bounds.max.z>3.92f) continue;
                var area=board.ProjectTerrainBounds(bounds);
                bool clear=true;
                foreach(var other in exclusions) if(area.Overlaps(other)) { clear=false; break; }
                if(!clear) continue;
                var footprint=Rect.MinMaxRect(bounds.min.x-0.04f,bounds.min.z-0.04f,bounds.max.x+0.04f,bounds.max.z+0.04f);
                foreach(var other in occupied) if(footprint.Overlaps(other)) { clear=false; break; }
                if(!clear) continue;
                var mesh=BuildMesh(points); mesh.name="Tabletop Ridge "+ridges.Count;
                var visual=new GameObject(mesh.name,typeof(MeshFilter),typeof(MeshRenderer)).transform;
                visual.SetParent(transform,false); visual.gameObject.layer=gameObject.layer;
                visual.localPosition=position; visual.localRotation=rotation;
                visual.GetComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=visual.GetComponent<MeshRenderer>(); renderer.sharedMaterial=rockMaterial;
                renderer.shadowCastingMode=ShadowCastingMode.On;
                var properties=new MaterialPropertyBlock();
                var color=rockMaterial.color*Mathf.Lerp(0.74f,0.94f,(float)random.NextDouble()); color.a=1;
                properties.SetColor("_BaseColor",color); properties.SetColor("_Color",color); renderer.SetPropertyBlock(properties);
                float wave=(3.92f-position.z)/7.84f;
                ridges.Add(new Ridge { visual=visual,rest=position,rotation=rotation,mesh=mesh,renderer=renderer,index=ridges.Count,bounds=bounds,depth=bounds.size.y+0.18f,
                    delay=Mathf.Clamp01(wave*0.75f+(float)random.NextDouble()*0.25f) });
                restBounds.Add(bounds); occupied.Add(footprint); break;
            }
        }
    }
    static Vector3[] RidgePoints(System.Random random,float length,float width,float height)
    {
        var points=new Vector3[25];
        for(int section=0;section<5;section++)
        {
            float x=(section/4f-0.5f)*length;
            float peak=height*(section==0 || section==4 ? 0.06f : Mathf.Lerp(0.65f,1,(float)random.NextDouble()));
            float spread=width*(section==0 || section==4 ? 0.35f : Mathf.Lerp(0.8f,1,(float)random.NextDouble()));
            points[section*5]=new Vector3(x,0,-spread*0.5f);
            points[section*5+1]=new Vector3(x+Signed(random)*length*0.045f,peak*0.4f,-spread*0.25f);
            points[section*5+2]=new Vector3(x+Signed(random)*length*0.065f,peak,Signed(random)*spread*0.13f);
            points[section*5+3]=new Vector3(x+Signed(random)*length*0.045f,peak*0.35f,spread*0.27f);
            points[section*5+4]=new Vector3(x,0,spread*0.5f);
        }
        return points;
    }
    static Mesh BuildMesh(Vector3[] points)
    {
        var vertices=new List<Vector3>(); var triangles=new List<int>();
        void Face(int a,int b,int c)
        {
            int start=vertices.Count; vertices.Add(points[a]); vertices.Add(points[b]); vertices.Add(points[c]);
            triangles.Add(start); triangles.Add(start+1); triangles.Add(start+2);
        }
        for(int s=0;s<4;s++) for(int band=0;band<4;band++)
        {
            int a=s*5+band,b=a+5;
            Face(a,a+1,b); Face(a+1,b+1,b);
        }
        // End caps close the cut ends; underside sits in the board slab.
        for(int end=0;end<2;end++) for(int band=1;band<4;band++)
        {
            int first=end*20;
            if(end==0) Face(first,first+band+1,first+band); else Face(first,first+band,first+band+1);
        }
        var mesh=new Mesh(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles,0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
    }
    public IEnumerator Reveal()
    {
        if(run==null || revealed) yield break;
        IsRevealing=true;
        foreach(var ridge in ridges) ridge.visual.gameObject.SetActive(true);
        float elapsed=0,total=ReducedMotion ? 0.12f : Mathf.Max(0.01f,riseDuration+revealStagger+settlePause);
        ApplyReveal(0);
        while(elapsed<total)
        {
            if(ReducedMotion) break;
            yield return null; elapsed+=Mathf.Min(Time.unscaledDeltaTime,0.05f);
            RevealProgress=Mathf.Clamp01(elapsed/total); ApplyReveal(elapsed);
        }
        Settle(true); Revealed?.Invoke();
    }
    void ApplyReveal(float elapsed)
    {
        foreach(var ridge in ridges)
        {
            float t=ReducedMotion ? 1 : Mathf.Clamp01((elapsed-ridge.delay*revealStagger)/Mathf.Max(0.01f,riseDuration));
            float eased=t*t*t*(t*(t*6-15)+10);
            ridge.visual.localPosition=RestPosition(ridge)-Vector3.up*(ridge.depth*(1-eased));
        }
    }
    public void Settle(bool remember=false)
    {
        if(run==null) return;
        IsRevealing=false; revealed=true; RevealProgress=1; impactActive=IsReacting=false;
        restBounds.Clear();
        foreach(var ridge in ridges)
        {
            ridge.visual.gameObject.SetActive(true); ridge.visual.localPosition=RestPosition(ridge); ridge.visual.localRotation=ridge.rotation;
            RestoreDamage(ridge); var bounds=ridge.bounds; bounds.center+=RestPosition(ridge)-ridge.rest; restBounds.Add(bounds);
        }
        if(remember) RememberResting();
    }
    public void RememberResting() { if(revealed && !IsRevealing) QuestProgressionRuntime.RememberTerrain(run); }
    Vector3 RestPosition(Ridge ridge)
    {
        var offset=QuestProgressionRuntime.TerrainDisplacement(run,ridge.index);
        return ridge.rest+new Vector3(offset.x,0,offset.y);
    }
    public Dictionary<int,Vector2> PlanDisplacements(Vector3 origin)
    {
        var result=new Dictionary<int,Vector2>();
        var quests=board.UsesWholeTableLayout ? board.ImpactExclusions(0.05f) : null;
        foreach(var ridge in ridges)
        {
            var direction=new Vector2(ridge.rest.x-origin.x,ridge.rest.z-origin.z).normalized;
            var target=new Vector2(ridge.rest.x,ridge.rest.z)+direction*outwardSlide;
            target.x=Mathf.Clamp(target.x,-9.4f+ridge.bounds.extents.x,9.4f-ridge.bounds.extents.x);
            target.y=Mathf.Clamp(target.y,-4.85f+ridge.bounds.extents.z,4.85f-ridge.bounds.extents.z);
            var offset=target-new Vector2(ridge.rest.x,ridge.rest.z);
            if(quests!=null)
            {
                // Terrain yields to the planned quest destinations. Staying at
                // its original clear footprint is the final bounded fallback.
                for(int attempt=0;attempt<9;attempt++)
                {
                    var bounds=ridge.bounds; bounds.center+=new Vector3(offset.x,0,offset.y);
                    var area=board.ProjectTerrainBounds(bounds);
                    if(!quests.Exists(q=>q.Overlaps(area))) break;
                    offset=attempt==7 ? Vector2.zero : offset*0.6f;
                }
            }
            result[ridge.index]=offset;
        }
        return result;
    }
    public List<int> PlanBreaks()
    {
        var result=new List<int>(); var seen=new HashSet<int>();
        foreach(var ridge in ridges)
        {
            if(!seen.Add(ridge.index)) continue;
            var group=new List<int> { ridge.index };
            for(int at=0;at<group.Count;at++) foreach(var other in ridges)
            {
                if(seen.Contains(other.index)) continue;
                var a=ridges[group[at]].bounds; var b=other.bounds;
                float dx=Mathf.Max(0,Mathf.Max(a.min.x-b.max.x,b.min.x-a.max.x));
                float dz=Mathf.Max(0,Mathf.Max(a.min.z-b.max.z,b.min.z-a.max.z));
                if(dx*dx+dz*dz>isolationGap*isolationGap) continue;
                seen.Add(other.index); group.Add(other.index);
            }
            if(group.Count<=2) result.AddRange(group);
        }
        return result;
    }
    public List<Bounds> SurvivingDestinationBounds(Dictionary<int,Vector2> offsets,List<int> broken)
    {
        var result=new List<Bounds>();
        foreach(var ridge in ridges)
        {
            if(broken.Contains(ridge.index)) continue;
            var bounds=ridge.bounds; var offset=offsets[ridge.index]; bounds.center+=new Vector3(offset.x,0,offset.y); result.Add(bounds);
        }
        return result;
    }
    public void PrepareImpact()
    {
        foreach(var ridge in ridges)
        {
            ridge.impactStart=ridge.visual.localPosition; ridge.impactTarget=RestPosition(ridge);
            ridge.willBreak=QuestProgressionRuntime.IsTerrainBroken(run,ridge.index);
            if(ridge.willBreak) MakeFragments(ridge);
        }
    }
    public void ApplyImpact(Vector3 worldImpact,float progress)
    {
        if(!isActiveAndEnabled || !revealed) { ClearImpact(); return; }
        if(ReducedMotion || (board.Motion.Shockwave!=null && board.Motion.Shockwave.MotionSuppressed)) { Settle(); return; }
        impactActive=true; IsReacting=false;
        var origin=transform.InverseTransformPoint(worldImpact);
        foreach(var ridge in ridges)
        {
            var radial=ridge.impactStart-origin; radial.y=0;
            float wave=board.Motion.SurfaceResponse(radial.magnitude,progress);
            float local=board.Motion.Shockwave!=null ? board.Motion.Shockwave.LocalProgress(radial.magnitude,progress) : progress;
            float slide=1-Mathf.Pow(1-local,3);
            var axis=new Vector3(radial.normalized.z,0,-radial.normalized.x);
            ridge.visual.localPosition=Vector3.Lerp(ridge.impactStart,ridge.impactTarget,slide)+Vector3.up*(Mathf.Max(0,wave)*impactLift);
            ridge.visual.localRotation=Quaternion.AngleAxis(wave*impactTilt,axis)*ridge.rotation;
            if(ridge.willBreak && local>0)
            {
                if(!ridge.hitBroken) { ridge.hitBroken=true; board.Motion.Feedback?.PlayTerrainBreak(ridge.visual.position); }
                ridge.visual.localRotation=ridge.rotation; DrawFragments(ridge,local);
            }
            IsReacting|=Mathf.Abs(wave)>0.0001f && (impactTilt>0 || impactLift>0);
        }
    }
    public void ClearImpact()
    {
        if(!impactActive) return;
        impactActive=IsReacting=false; Settle();
    }
    void RestoreDamage(Ridge ridge)
    {
        ridge.willBreak=ridge.hitBroken=QuestProgressionRuntime.IsTerrainBroken(run,ridge.index);
        if(ridge.willBreak) { MakeFragments(ridge); DrawFragments(ridge,1); }
        else { ridge.renderer.enabled=true; foreach(var fragment in ridge.fragments) fragment.visual.gameObject.SetActive(false); }
    }
    void MakeFragments(Ridge ridge)
    {
        if(ridge.fragments.Count>0) return;
        var source=ridge.mesh.vertices; var triangles=ridge.mesh.triangles;
        var random=new System.Random(unchecked(run.Seed ^ (ridge.index*713+193)));
        var block=new MaterialPropertyBlock(); ridge.renderer.GetPropertyBlock(block);
        for(int piece=0;piece<3;piece++)
        {
            var points=new List<Vector3>();
            for(int i=0;i<triangles.Length;i+=3)
            {
                float x=(source[triangles[i]].x+source[triangles[i+1]].x+source[triangles[i+2]].x)/3;
                int region=Mathf.Clamp(Mathf.FloorToInt(Mathf.InverseLerp(ridge.mesh.bounds.min.x,ridge.mesh.bounds.max.x,x)*3),0,2);
                if(region!=piece) continue;
                points.Add(source[triangles[i]]); points.Add(source[triangles[i+1]]); points.Add(source[triangles[i+2]]);
            }
            if(points.Count==0) continue;
            var center=points.Aggregate(Vector3.zero,(sum,v)=>sum+v)/points.Count;
            for(int i=0;i<points.Count;i++) points[i]-=center;
            var mesh=new Mesh { name="Broken Ridge "+ridge.index+" Piece "+piece };
            mesh.SetVertices(points); mesh.SetTriangles(Enumerable.Range(0,points.Count).ToArray(),0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var visual=new GameObject(mesh.name,typeof(MeshFilter),typeof(MeshRenderer)).transform; visual.SetParent(ridge.visual,false); visual.gameObject.layer=gameObject.layer;
            visual.GetComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=visual.GetComponent<MeshRenderer>(); renderer.sharedMaterial=rockMaterial; renderer.SetPropertyBlock(block);
            var rotation=Quaternion.Euler(Signed(random)*30,Signed(random)*50,Signed(random)*30);
            var rotated=QuestStoneView.TransformBounds(mesh.bounds,Matrix4x4.TRS(Vector3.zero,rotation,Vector3.one*0.42f));
            var target=new Vector3(center.x*0.9f+Signed(random)*0.14f,0.015f-rotated.min.y,center.z*0.9f+Signed(random)*0.14f);
            ridge.fragments.Add(new Fragment { visual=visual,mesh=mesh,start=center,target=target,rotation=rotation });
            visual.gameObject.SetActive(false);
        }
    }
    void DrawFragments(Ridge ridge,float progress)
    {
        ridge.renderer.enabled=false; float t=Mathf.Clamp01(progress), eased=1-Mathf.Pow(1-t,3);
        foreach(var fragment in ridge.fragments)
        {
            fragment.visual.gameObject.SetActive(true);
            fragment.visual.localPosition=Vector3.Lerp(fragment.start,fragment.target,eased)+Vector3.up*(Mathf.Sin(t*Mathf.PI)*0.28f);
            fragment.visual.localRotation=Quaternion.Slerp(Quaternion.identity,fragment.rotation,eased);
            fragment.visual.localScale=Vector3.one*Mathf.Lerp(1,0.42f,eased);
        }
    }
    void ClearRidges()
    {
        foreach(var ridge in ridges)
        {
            if(ridge.visual!=null) { ridge.visual.gameObject.SetActive(false); Destroy(ridge.visual.gameObject); }
            if(ridge.mesh!=null) Destroy(ridge.mesh);
            foreach(var fragment in ridge.fragments) if(fragment.mesh!=null) Destroy(fragment.mesh);
        }
        ridges.Clear(); restBounds.Clear();
    }
    void OnDisable() { wasDisabled=true; board?.RecoverOwner(); }
    void OnEnable() { if(wasDisabled) { wasDisabled=false; board?.RecoverOwner(); } }
    void OnDestroy() => ClearRidges();
    static float Between(System.Random random,Vector2 range) => Mathf.Lerp(Mathf.Max(0.05f,range.x),Mathf.Max(0.05f,range.y),(float)random.NextDouble());
    static float Signed(System.Random random) => (float)random.NextDouble()*2-1;
}
