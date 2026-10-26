using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Decorative tabletop geology. No colliders, quest rules, routes or mission data.
[DisallowMultipleComponent]
public sealed class PreMissionBoardTerrain : MonoBehaviour
{
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
    sealed class Ridge
    {
        public Transform visual;
        public Vector3 rest;
        public Mesh mesh;
        public float delay, depth;
    }
    readonly List<Ridge> ridges=new List<Ridge>();
    readonly List<Bounds> restBounds=new List<Bounds>();
    PreMissionStoneBoard board;
    QuestProgressionSession run;
    bool revealed, wasDisabled;
    public bool IsRevealing { get; private set; }
    public bool IsRevealed => revealed;
    public bool HasPending => run!=null && !revealed;
    public float RevealProgress { get; private set; }
    public int RidgeCount => ridges.Count;
    public IReadOnlyList<Bounds> RestBounds => restBounds;
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
        IsRevealing=false; RevealProgress=revealed ? 1 : 0;
        foreach(var ridge in ridges)
        {
            ridge.visual.localPosition=ridge.rest;
            ridge.visual.gameObject.SetActive(revealed);
        }
    }
    void BuildRidges()
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
            for(int attempt=0;attempt<20;attempt++)
            {
                float length=Between(random,ridgeLength), width=Between(random,ridgeWidth);
                if(attempt>=10) { length*=0.65f; width*=0.75f; }
                Vector3 position=new Vector3(anchor.x+Signed(random)*0.32f,0.045f,anchor.y+Signed(random)*0.20f);
                // If an authored rim anchor is crowded, try small outcrops in
                // other gaps before giving up. They obey the same full bounds.
                if(attempt>=14) position=new Vector3(Mathf.Lerp(-6.8f,8f,(float)random.NextDouble()),0.045f,Mathf.Lerp(-3.6f,3.6f,(float)random.NextDouble()));
                float height=Between(random,ridgeHeight)*(position.z< -2.5f ? 0.65f : 1)*(attempt>=10 ? 0.7f : 1);
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
                ridges.Add(new Ridge { visual=visual,rest=position,mesh=mesh,depth=bounds.size.y+0.18f,
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
            ridge.visual.localPosition=ridge.rest-Vector3.up*(ridge.depth*(1-eased));
        }
    }
    public void Settle(bool remember=false)
    {
        if(run==null) return;
        IsRevealing=false; revealed=true; RevealProgress=1;
        foreach(var ridge in ridges) { ridge.visual.gameObject.SetActive(true); ridge.visual.localPosition=ridge.rest; }
        if(remember) RememberResting();
    }
    public void RememberResting() { if(revealed && !IsRevealing) QuestProgressionRuntime.RememberTerrain(run); }
    void ClearRidges()
    {
        foreach(var ridge in ridges)
        {
            if(ridge.visual!=null) { ridge.visual.gameObject.SetActive(false); Destroy(ridge.visual.gameObject); }
            if(ridge.mesh!=null) Destroy(ridge.mesh);
        }
        ridges.Clear(); restBounds.Clear();
    }
    void OnDisable() { wasDisabled=true; board?.RecoverOwner(); }
    void OnEnable() { if(wasDisabled) { wasDisabled=false; board?.RecoverOwner(); } }
    void OnDestroy() => ClearRidges();
    static float Between(System.Random random,Vector2 range) => Mathf.Lerp(Mathf.Max(0.05f,range.x),Mathf.Max(0.05f,range.y),(float)random.NextDouble());
    static float Signed(System.Random random) => (float)random.NextDouble()*2-1;
}
