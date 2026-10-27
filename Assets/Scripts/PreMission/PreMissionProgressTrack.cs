using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// A view of quest data. Never begins/completes missions or changes offer availability.
[DisallowMultipleComponent]
public sealed class PreMissionProgressTrack : MonoBehaviour
{
    [SerializeField] Transform visual;
    [SerializeField] Transform nodeParent;
    [SerializeField] Transform marker;
    [SerializeField] TMP_Text counter;
    [SerializeField] TMP_FontAsset labelFont;
    [SerializeField] Material pendingMaterial;
    [SerializeField] Material completeMaterial;
    [Header("Track geometry")]
    [SerializeField, Min(0.1f)] float trackLength=5f;
    [Header("Unscaled animation")]
    [SerializeField, Min(0.01f)] float revealDuration=0.55f;
    [SerializeField, Min(0f)] float revealLift=0.24f;
    [SerializeField, Min(0.01f)] float stepDuration=0.38f;
    [SerializeField, Min(0f)] float stepLift=0.10f;
    [SerializeField, Min(0.01f)] float settleDuration=0.10f;
    [SerializeField, Min(0.01f)] float unlockAcknowledgement=0.18f;
    [Tooltip("Optional camera tracking of the progress marker. Keep at zero for a stationary board during entry and quest completion.")]
    [SerializeField, Range(0f,0.25f)] float cameraAttention=0f;
    readonly List<MeshRenderer> nodes=new List<MeshRenderer>();
    PreMissionStoneBoard board;
    QuestProgressionSession run;
    Vector3 visualPosition, visualScale, markerScale;
    Quaternion visualRotation, markerRotation;
    bool initialized, revealed, wasDisabled;
    public int ShownStep { get; private set; }
    public int RequiredSteps => run!=null ? run.RequiredSmallCompletions : 0;
    public int TargetStep => run!=null ? Math.Min(run.SuccessfulSmallCompletions,RequiredSteps) : 0;
    public int NodeCount => nodes.Count;
    public bool IsRevealing { get; private set; }
    public bool IsAdvancing { get; private set; }
    public bool IsRevealed => revealed;
    public bool HasPending => run!=null && (!revealed || ShownStep<TargetStep);
    public float CameraFocus { get; private set; }
    public Transform Marker => marker;
    public Transform Visual => visual;
    public event Action<int> StepPresented;
    bool ReducedMotion => board!=null && board.Motion!=null && board.Motion.Feedback!=null && board.Motion.Feedback.ReducedMotion;
    float MotionScale => ReducedMotion ? 0 : 1;

    public void Bind(PreMissionStoneBoard owner)
    {
        board=owner; run=board.Progression;
        if (!initialized)
        {
            visualPosition=visual.localPosition; visualScale=visual.localScale; visualRotation=visual.localRotation;
            markerScale=marker.localScale; markerRotation=marker.localRotation; initialized=true;
        }
        if (nodes.Count!=RequiredSteps+1) BuildNodes();
        revealed=QuestProgressionRuntime.WasProgressTrackRevealed(run);
        ShownStep=Math.Min(QuestProgressionRuntime.LastShownProgressStep(run),TargetStep);
        IsRevealing=IsAdvancing=false; CameraFocus=0;
        RestorePose(); DrawState(); visual.gameObject.SetActive(revealed);
    }
    void BuildNodes()
    {
        foreach(Transform child in nodeParent) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        nodes.Clear();
        float diameter=Mathf.Min(0.34f,trackLength/Mathf.Max(1,RequiredSteps)*0.45f);
        for(int i=0;i<=RequiredSteps;i++)
        {
            var node=GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            node.name="Progress Point "+i; node.layer=gameObject.layer; node.transform.SetParent(nodeParent,false);
            var collider=node.GetComponent<Collider>(); collider.enabled=false; Destroy(collider);
            node.transform.localPosition=NodePosition(i)-Vector3.up*0.03f;
            node.transform.localScale=new Vector3(diameter,0.025f,diameter);
            var renderer=node.GetComponent<MeshRenderer>(); renderer.sharedMaterial=pendingMaterial; nodes.Add(renderer);
            if (RequiredSteps<=6 || i==0 || i==RequiredSteps)
            {
                var label=new GameObject("Step Label "+i,typeof(TextMeshPro)).GetComponent<TextMeshPro>();
                label.gameObject.layer=gameObject.layer; label.transform.SetParent(nodeParent,false);
                label.transform.localPosition=NodePosition(i)+new Vector3(0.25f,0,0);
                label.transform.localRotation=Quaternion.Euler(90,0,0); label.font=labelFont; label.fontSize=1.7f;
                label.rectTransform.sizeDelta=new Vector2(0.24f,0.3f); label.alignment=TextAlignmentOptions.Center;
                label.text=i.ToString(); label.raycastTarget=false;
            }
        }
    }
    public Vector3 NodePosition(int step) => new Vector3(-0.08f,0.10f,Mathf.Lerp(-trackLength*0.5f,trackLength*0.5f,(float)step/Mathf.Max(1,RequiredSteps)));
    void DrawState()
    {
        for(int i=0;i<nodes.Count;i++) nodes[i].sharedMaterial=i<=ShownStep ? completeMaterial : pendingMaterial;
        marker.localPosition=NodePosition(ShownStep); marker.localScale=markerScale; marker.localRotation=markerRotation;
        counter.text=ShownStep+" / "+RequiredSteps;
    }
    void RestorePose()
    {
        visual.localPosition=visualPosition; visual.localRotation=visualRotation; visual.localScale=visualScale;
        marker.localScale=markerScale; marker.localRotation=markerRotation;
        foreach(Transform child in nodeParent) child.gameObject.SetActive(true);
        marker.gameObject.SetActive(true);
    }
    public IEnumerator Reveal()
    {
        if (run==null || revealed) yield break;
        IsRevealing=true; visual.gameObject.SetActive(true);
        yield return Animate(revealDuration,t=>
        {
            float eased=Ease(t);
            visual.localPosition=visualPosition-Vector3.up*(revealLift*(1-eased)*MotionScale);
            visual.localRotation=visualRotation*Quaternion.Euler(-6f*(1-eased)*MotionScale,0,0);
            CameraFocus=Mathf.Sin(t*Mathf.PI)*0.35f*MotionScale;
            for(int i=0;i<nodes.Count;i++) nodes[i].gameObject.SetActive(t>=(float)i/nodes.Count*0.65f);
            marker.gameObject.SetActive(t>0.65f);
        });
        revealed=true; IsRevealing=false; CameraFocus=0; RestorePose(); DrawState();
        QuestProgressionRuntime.RememberProgressPresentation(run,ShownStep);
    }
    public IEnumerator Advance(bool focusMainNext)
    {
        if(run==null || ShownStep>=TargetStep) yield break;
        IsAdvancing=true;
        while(ShownStep<TargetStep)
        {
            int next=ShownStep+1; var start=NodePosition(ShownStep); var end=NodePosition(next);
            yield return Animate(stepDuration,t=>
            {
                marker.localPosition=Vector3.Lerp(start,end,Ease(t))+Vector3.up*(Mathf.Sin(t*Mathf.PI)*stepLift*MotionScale);
                marker.localRotation=markerRotation*Quaternion.Euler(Mathf.Sin(t*Mathf.PI)*-7*MotionScale,0,Mathf.Sin(t*Mathf.PI)*3*MotionScale);
                CameraFocus=Ease(Mathf.Clamp01(t*3))*MotionScale;
            });
            board.Motion.Feedback?.PlayCue(QuestBoardImpactFeedback.Cue.Progress);
            yield return Animate(settleDuration,t=>
            {
                float pulse=Mathf.Sin(t*Mathf.PI)*0.07f*MotionScale;
                marker.localScale=Vector3.Scale(markerScale,new Vector3(1+pulse,1-pulse,1+pulse));
                marker.localRotation=Quaternion.Slerp(marker.localRotation,markerRotation,Ease(t));
            });
            ShownStep=next; DrawState();
            QuestProgressionRuntime.RememberProgressPresentation(run,ShownStep); StepPresented?.Invoke(ShownStep);
        }
        if(ShownStep==RequiredSteps)
            yield return Animate(unlockAcknowledgement,t=>marker.localScale=markerScale*(1f+Mathf.Sin(t*Mathf.PI)*0.08f*MotionScale));
        marker.localScale=markerScale; IsAdvancing=false;
        if(!focusMainNext) yield return Animate(0.16f,t=>CameraFocus=(1f-Ease(t))*MotionScale);
    }
    public void ReleaseFocus(float progress) => CameraFocus=(1f-Mathf.Clamp01(progress))*MotionScale;
    public void ApplyCamera(ref Vector3 position, Quaternion rotation)
    {
        if(CameraFocus<=0 || cameraAttention<=0 || ReducedMotion) return;
        var target=marker.position-rotation*Vector3.forward*24;
        position=Vector3.Lerp(position,target,CameraFocus*cameraAttention);
    }
    public void Settle(PreMissionView view, bool remember=false)
    {
        IsRevealing=IsAdvancing=false; CameraFocus=0;
        if(run==null || !initialized) return;
        revealed=true; ShownStep=view==PreMissionView.Board ? TargetStep : Math.Min(QuestProgressionRuntime.LastShownProgressStep(run),TargetStep);
        visual.gameObject.SetActive(true); RestorePose(); DrawState();
        if(remember) RememberResting();
    }
    public void RememberResting()
    {
        if(run!=null && revealed && !IsRevealing && !IsAdvancing) QuestProgressionRuntime.RememberProgressPresentation(run,ShownStep);
    }
    public Bounds WorldBounds
    {
        get
        {
            var bounds=new Bounds(visual.position,Vector3.zero);
            foreach(var renderer in visual.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }
    }
    void OnDisable() { wasDisabled=true; board?.RecoverOwner(); }
    void OnEnable() { if(wasDisabled) { wasDisabled=false; board?.RecoverOwner(); } }
    static float Ease(float t) => t*t*t*(t*(t*6-15)+10);
    IEnumerator Animate(float duration,Action<float> frame)
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
