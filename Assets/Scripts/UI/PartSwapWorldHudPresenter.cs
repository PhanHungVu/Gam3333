using UnityEngine;
using UnityEngine.InputSystem;

// Observes the existing selected-part/equipment state. Never detects pickups,
// reads gameplay buttons, changes cursor/camera state or performs a swap.
[DisallowMultipleComponent,DefaultExecutionOrder(250)]
public sealed class PartSwapWorldHudPresenter : MonoBehaviour
{
    [SerializeField] PartSwapWorldView viewPrefab;
    [Header("World presentation")]
    [SerializeField,Range(0.15f,0.45f)] float screenWidthFraction=0.30f;
    [Tooltip("Fallback for a part centered exactly on the player. Other parts choose their side from their camera-view position.")]
    [SerializeField] bool preferRightSide;
    [SerializeField,Range(0.08f,0.22f)] float playerScreenClearance=0.13f;
    [SerializeField,Min(0.02f)] float groundClearance=0.20f;
    [SerializeField,Min(0)] float rise=0.14f;
    [SerializeField,Min(0.01f)] float appearDuration=0.20f;
    [SerializeField,Min(0.01f)] float switchDuration=0.10f;
    [SerializeField,Min(0.01f)] float disappearDuration=0.16f;
    [SerializeField,Min(0.01f)] float successDuration=0.18f;
    [SerializeField] bool reducedMotion;
    EquipManager owner;
    PickupDetector detector;
    MechPlayerInput input;
    MechPlayerRig rig;
    PartSwapWorldView view;
    Part displayed;
    bool dirty=true,subscribed;
    int nearbyCount;
    float alpha,successAge,successAlpha;
    PartCategory? confirmed;
    bool? anchorOnRight;
    Vector3 lastAnchor;
    readonly Vector3[] corners=new Vector3[4];
    public bool HasView=>view!=null;
    public PartSwapWorldView View=>view;
    public Part DisplayedPart=>displayed;
    public bool IsVisible=>view!=null && view.Canvas.enabled && view.Group.alpha>0.001f;
    public void SetReducedMotion(bool value) { reducedMotion=value; }
    public void Refresh()=>dirty=true;

    void Awake()
    {
        owner=GetComponentInParent<EquipManager>();
        if(owner==null) return;
        detector=owner.GetComponentInChildren<PickupDetector>(true);
        input=owner.GetComponent<MechPlayerInput>(); rig=owner.GetComponent<MechPlayerRig>();
        if(viewPrefab!=null)
        {
            view=Instantiate(viewPrefab,transform); view.name="Part Swap World UI"; view.Clear();
            view.Group.blocksRaycasts=false; view.Group.interactable=false;
        }
    }
    void OnEnable()
    {
        if(owner==null || subscribed) return;
        owner.EquipmentChanged+=EquipmentChanged; InputSystem.onActionChange+=ActionChanged;
        subscribed=true; dirty=true;
    }
    void OnDisable()
    {
        if(subscribed) { owner.EquipmentChanged-=EquipmentChanged; InputSystem.onActionChange-=ActionChanged; subscribed=false; }
        displayed=null; confirmed=null; anchorOnRight=null; alpha=0; if(view!=null) view.Clear();
    }
    void ActionChanged(object action,InputActionChange change)
    {
        if(change==InputActionChange.BoundControlsChanged || change==InputActionChange.ActionMapEnabled
            || change==InputActionChange.ActionMapDisabled || change==InputActionChange.ActionEnabled
            || change==InputActionChange.ActionDisabled) dirty=true;
    }
    void EquipmentChanged()
    {
        dirty=true;
        if(displayed!=null && displayed.EquippedBy==owner && displayed.Slot.HasValue)
        {
            confirmed=displayed.Slot; successAge=0; successAlpha=alpha;
        }
    }
    void Populate(Part part)
    {
        if(displayed!=part) anchorOnRight=null;
        displayed=part; dirty=false;
        if(part==null) { view.Clear(); return; }
        lastAnchor=part.transform.position; view.Populate(part,owner,input,detector);
    }
    void LateUpdate()
    {
        if(view==null) return;
        if(owner==null || !owner.isActiveAndEnabled || detector==null || !detector.isActiveAndEnabled)
        { displayed=null; confirmed=null; anchorOnRight=null; alpha=0; view.Clear(); return; }
        float dt=Mathf.Min(Time.unscaledDeltaTime,0.05f);
        var selected=owner.GetSelectedPart();
        if(confirmed.HasValue)
        {
            successAge+=dt;
            float progress=Mathf.Clamp01(successAge/(reducedMotion ? 0.08f : successDuration));
            alpha=successAlpha*(1-progress); view.Acknowledge(confirmed.Value,progress);
            view.SetAlpha(alpha); Position();
            if(progress>=1) { confirmed=null; displayed=null; alpha=0; view.Clear(); dirty=true; }
            return;
        }
        if(detector.DistinctNearbyPartCount!=nearbyCount) { nearbyCount=detector.DistinctNearbyPartCount; dirty=true; }
        if(selected!=displayed)
        {
            float duration=selected!=null ? switchDuration : disappearDuration;
            alpha=Mathf.MoveTowards(alpha,0,dt/(reducedMotion ? 0.08f : duration));
            if(alpha<=0) Populate(selected);
        }
        else if(displayed!=null)
        {
            if(dirty) Populate(displayed);
            alpha=Mathf.MoveTowards(alpha,1,dt/(reducedMotion ? 0.08f : appearDuration));
        }
        else { alpha=0; view.Clear(); }
        // Destroyed/disabled drops never retain visible stale content.
        if(displayed!=null && (!displayed.gameObject.activeInHierarchy || displayed.isEquipped))
        { displayed=null; alpha=0; view.Clear(); }
        view.SetAlpha(alpha); Position();
    }
    void Position()
    {
        if(alpha<=0) return;
        var camera=rig!=null && rig.GameplayCamera!=null && rig.GameplayCamera.isActiveAndEnabled ? rig.GameplayCamera : Camera.main;
        if(camera==null) { view.Canvas.enabled=false; return; }
        if(displayed!=null && !confirmed.HasValue) lastAnchor=displayed.transform.position;
        float depth=Vector3.Dot(lastAnchor-camera.transform.position,camera.transform.forward);
        if(depth<=camera.nearClipPlane) { view.Canvas.enabled=false; return; }
        float worldPerPixel=camera.orthographic ? 2*camera.orthographicSize/Mathf.Max(1,camera.pixelHeight)
            : 2*depth*Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*0.5f)/Mathf.Max(1,camera.pixelHeight);
        float pixelScale=Mathf.Min(Mathf.Clamp(camera.pixelWidth*screenWidthFraction,340,640)/660,
            camera.pixelHeight*0.62f/Mathf.Max(1,view.Height));
        float scale=worldPerPixel*pixelScale*(reducedMotion ? 1 : Mathf.Lerp(0.98f,1,alpha));
        float playerX=camera.WorldToViewportPoint(owner.transform.position).x;
        float leftRoom=playerX-playerScreenClearance-0.035f,rightRoom=0.965f-playerX-playerScreenClearance;
        float partOffset=camera.WorldToViewportPoint(lastAnchor).x-playerX;
        // New selections use their actual screen side immediately. Keep that
        // side through tiny center crossings while the player/camera moves.
        if(!anchorOnRight.HasValue)
            anchorOnRight=Mathf.Abs(partOffset)>0.0001f ? partOffset>0 : preferRightSide;
        else if(Mathf.Abs(partOffset)>0.02f) anchorOnRight=partOffset>0;
        bool right=anchorOnRight.Value;
        if((right ? rightRoom : leftRoom)<0.24f && (right ? leftRoom : rightRoom)>(right ? rightRoom : leftRoom)) right=!right;
        scale=Mathf.Min(scale,worldPerPixel*camera.pixelWidth*Mathf.Max(0.14f,right ? rightRoom : leftRoom)/660);
        float side=right ? 1 : -1;
        var position=lastAnchor+Vector3.up*(groundClearance-(reducedMotion ? 0 : rise*(1-alpha)))
            +camera.transform.right*(side*(660*scale*0.58f+0.15f));
        // A tall card must fit above its ground anchor. Shifting it down first
        // and then clamping it back above the ground would crop its upper edge.
        float baseHeight=camera.WorldToViewportPoint(position).y;
        float availableHeight=Mathf.Max(0.08f,0.945f-Mathf.Max(0.045f,baseHeight));
        scale=Mathf.Min(scale,worldPerPixel*camera.pixelHeight*availableHeight/Mathf.Max(1,view.Height));
        var parentScale=view.transform.parent.lossyScale;
        view.transform.localScale=new Vector3(scale/Mathf.Max(0.0001f,parentScale.x),scale/Mathf.Max(0.0001f,parentScale.y),scale/Mathf.Max(0.0001f,parentScale.z));
        position=lastAnchor+Vector3.up*(groundClearance-(reducedMotion ? 0 : rise*(1-alpha)))
            +camera.transform.right*(side*(660*scale*0.58f+0.15f));
        view.transform.SetPositionAndRotation(position,camera.transform.rotation); view.Canvas.worldCamera=camera;
        // Framing corrections affect only the UI, never the gameplay camera or target.
        view.Panel.GetWorldCorners(corners);
        Vector2 min=Vector2.one*float.MaxValue,max=Vector2.one*float.MinValue;
        foreach(var corner in corners)
        {
            var point=(Vector2)camera.WorldToViewportPoint(corner); min=Vector2.Min(min,point); max=Vector2.Max(max,point);
        }
        float dx=right ? Mathf.Max(0,playerX+playerScreenClearance-min.x) : Mathf.Min(0,playerX-playerScreenClearance-max.x);
        min.x+=dx; max.x+=dx;
        dx+=min.x<0.035f ? 0.035f-min.x : max.x>0.965f ? 0.965f-max.x : 0;
        float dy=min.y<0.045f ? 0.045f-min.y : max.y>0.955f ? 0.955f-max.y : 0;
        position+=camera.transform.right*(dx*camera.pixelWidth*worldPerPixel)+camera.transform.up*(dy*camera.pixelHeight*worldPerPixel);
        position.y=Mathf.Max(position.y,lastAnchor.y+0.025f);
        view.transform.position=position;
    }
}
