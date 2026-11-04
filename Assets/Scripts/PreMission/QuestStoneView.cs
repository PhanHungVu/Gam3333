using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// One physical offer. Its collider/root never move when the visual is highlighted.
[DisallowMultipleComponent, RequireComponent(typeof(BoxCollider))]
public sealed class QuestStoneView : MonoBehaviour
{
    [Serializable] public sealed class MissionLogo
    {
        public QuestMissionType missionType;
        public GameObject root;
    }
    [SerializeField] Transform visual;
    [SerializeField] MeshRenderer rock;
    [Tooltip("Fitted render meshes from the supplied HUD slabs; logical footprints stay unchanged.")]
    [SerializeField] Mesh[] slabVariants;
    [SerializeField] GameObject selectionRing;
    [SerializeField] TMP_Text label;
    [Tooltip("Supplied SIDE QUEST or MAIN QUEST lettering above the mission type.")]
    [SerializeField] GameObject categoryInscription;
    [SerializeField] MissionLogo[] missionLogos;
    Renderer[] inscriptionRenderers;
    Color[] inscriptionColors;
    MaterialPropertyBlock inscriptionProperties;
    [SerializeField, Min(0.01f)] float hoverDuration = 0.12f;
    [SerializeField, Min(0.01f)] float mainHoverDuration = 0.18f;
    [SerializeField, Min(0f)] float hoverLift = 0.06f;
    [SerializeField, Min(0f)] float mainHoverLift = 0.025f;
    [SerializeField, Min(0f)] float hoverTilt = 2.2f;
    [SerializeField, Min(0f)] float mainHoverTilt = 0.7f;
    MaterialPropertyBlock properties;
    Vector3 visualPosition, visualScale;
    Matrix4x4 rockFromVisual;
    Bounds rockLocalBounds;
    Bounds contentLocalBounds;
    Quaternion visualRotation;
    Color baseColor;
    QuestBoardImpactFeedback feedback;
    float MotionScale => feedback!=null ? feedback.MotionScale : 1;
    public void ConfigureFeedback(QuestBoardImpactFeedback value) => feedback=value;
    float hoverAmount, hoverFrom, hoverTarget, hoverAge;
    Vector3 restCenter;
    Vector3 reactionOffset, reactionAngles;
    BoxCollider hitCollider;
    Vector3 requestedOffset,requestedAngles,requestedScale;
    bool hasRequestedPose;
    float appliedMotionScale;
    public QuestOffer Offer { get; private set; }
    public bool IsHighlighted { get; private set; }
    public bool IsReady { get; private set; }
    public bool IsMoving { get; private set; }
    public bool IsReacting => reactionOffset != Vector3.zero || reactionAngles != Vector3.zero;
    public Transform Visual => visual;
    public bool IsMain => Offer != null && Offer.Quest.category == QuestCategory.Main;
    public Vector3 InspectionPoint => transform.TransformPoint(restCenter) + Vector3.up * 0.12f;
    public Collider HitCollider => hitCollider!=null ? hitCollider : (hitCollider=GetComponent<BoxCollider>());
    public Bounds RockBounds => rock.bounds;
    public int SlabVariantIndex { get; private set; } = -1;
    Vector3 LocalSurfacePoint => rockLocalBounds.center+Vector3.up*rockLocalBounds.extents.y;
    public Vector3 SurfacePoint => rock.transform.TransformPoint(LocalSurfacePoint);
    public Quaternion SurfaceRotation => visual.rotation;
    public float InscriptionOpacity => label.alpha;
    public bool HasBoardInscription => categoryInscription != null;
    public Vector3 CurrentAngles
    {
        get
        {
            var angles=(Quaternion.Inverse(visualRotation)*visual.localRotation).eulerAngles;
            return new Vector3(Mathf.DeltaAngle(0,angles.x),Mathf.DeltaAngle(0,angles.y),Mathf.DeltaAngle(0,angles.z));
        }
    }

    public void Bind(QuestOffer offer, int appearanceSeed = 0)
    {
        properties = new MaterialPropertyBlock();
        Offer = offer;
        if(slabVariants!=null && slabVariants.Length>0)
        {
            // Ignore the run GUID so rebuilding the same seeded offers keeps their art.
            uint hash=2166136261u ^ (uint)appearanceSeed;
            if(offer.Quest.questId!=null) foreach(char c in offer.Quest.questId) hash=unchecked((hash^c)*16777619u);
            for(int i=offer.Id.LastIndexOf(':')+1;i<offer.Id.Length;i++) hash=unchecked((hash^offer.Id[i])*16777619u);
            SlabVariantIndex=(int)(hash%(uint)slabVariants.Length);
            var mesh=slabVariants[SlabVariantIndex];
            if(mesh!=null) rock.GetComponent<MeshFilter>().sharedMesh=mesh;
        }
        name = (offer.Quest.category == QuestCategory.Main ? "Main Stone - " : "Small Stone - ") + offer.Quest.title;
        visualPosition = visual.localPosition; visualScale = visual.localScale;
        visualRotation = visual.localRotation;
        rockFromVisual=visual.worldToLocalMatrix*rock.localToWorldMatrix;
        rockLocalBounds=rock.localBounds;
        restCenter = transform.InverseTransformPoint(rock.bounds.center);
        baseColor = rock.sharedMaterial.color;
        label.text = categoryInscription != null ? PreMissionFlowController.MissionTypeShortName(offer.Quest.missionType)
            : "<size=65%>" + (offer.Quest.category == QuestCategory.Main ? "MAIN QUEST" : "SMALL QUEST") + "</size>\n" + offer.Quest.title;
        if (missionLogos != null) foreach (var logo in missionLogos)
            if (logo.root != null) logo.root.SetActive(logo.missionType == offer.Quest.missionType);
        CacheInscriptions();
        contentLocalBounds=TransformBounds(rockLocalBounds,rockFromVisual);
        foreach(var renderer in inscriptionRenderers)
            if(renderer.gameObject.activeInHierarchy) contentLocalBounds.Encapsulate(TransformBounds(renderer.localBounds,visual.worldToLocalMatrix*renderer.localToWorldMatrix));
        RestoreRest();
    }
    void CacheInscriptions()
    {
        if (inscriptionRenderers != null) return;
        var renderers = new List<Renderer>();
        if (categoryInscription != null) renderers.AddRange(categoryInscription.GetComponentsInChildren<Renderer>(true));
        if (missionLogos != null) foreach (var logo in missionLogos)
            if (logo.root != null) renderers.AddRange(logo.root.GetComponentsInChildren<Renderer>(true));
        inscriptionRenderers = renderers.ToArray(); inscriptionColors = new Color[renderers.Count];
        inscriptionProperties = new MaterialPropertyBlock();
        for (int i = 0; i < renderers.Count; i++) inscriptionColors[i] = renderers[i].sharedMaterial.color;
    }
    public void SetHighlighted(bool highlighted)
    {
        highlighted &= IsReady && !IsMoving;
        if (IsHighlighted == highlighted) return;
        IsHighlighted = highlighted;
        hoverFrom = hoverAmount; hoverTarget = highlighted ? 1f : 0f; hoverAge = 0;
        selectionRing.SetActive(highlighted);
    }
    void Update()
    {
        if (Offer == null || IsMoving) return;
        hoverAge += Time.unscaledDeltaTime;
        hoverAmount = Mathf.Lerp(hoverFrom, hoverTarget, Mathf.SmoothStep(0, 1, hoverAge / Mathf.Max(0.01f,IsMain ? mainHoverDuration : hoverDuration)));
        ApplyHover();
    }
    void LateUpdate() => RefreshMotionPreference();
    internal void RefreshMotionPreference()
    {
        if(IsMoving && hasRequestedPose && appliedMotionScale!=MotionScale) ApplyRequestedPose();
    }
    void ApplyHover()
    {
        if (visual == null) return;
        visual.localPosition = visualPosition + (Vector3.up * ((IsMain ? mainHoverLift : hoverLift) * hoverAmount) + reactionOffset)*MotionScale;
        visual.localRotation = visualRotation * Quaternion.Euler((reactionAngles+new Vector3(-1,0,0.35f)*((IsMain ? mainHoverTilt : hoverTilt)*hoverAmount))*MotionScale);
        visual.localScale = visualScale * (1f + (IsMain ? 0.01f : 0.025f) * hoverAmount*MotionScale);
        SetTint(Color.Lerp(baseColor, new Color(0.83f, 0.76f, 0.54f), 0.22f * hoverAmount));
    }
    void SetTint(Color color)
    {
        properties.SetColor("_BaseColor", color); properties.SetColor("_Color", color);
        rock.SetPropertyBlock(properties);
    }
    public void PrepareArrival()
    {
        RestoreRest(); IsReady = false; IsMoving = true; HitCollider.enabled = false; visual.gameObject.SetActive(false);
    }
    public void SetMotionFrame(Vector3 offset, Vector3 angles, Vector3 scale)
    {
        // Handling owns the pose now. Clear hover colour immediately, without
        // applying the resting pose over a partial-hover pickup/return frame.
        if(!IsMoving || IsHighlighted || hoverAmount>0)
        {
            hoverAmount=hoverFrom=hoverTarget=hoverAge=0;
            SetTint(baseColor);
        }
        reactionOffset = reactionAngles = Vector3.zero;
        IsMoving = true; IsHighlighted = false; selectionRing.SetActive(false); HitCollider.enabled = false;
        visual.gameObject.SetActive(true);
        requestedOffset=offset; requestedAngles=angles; requestedScale=scale; hasRequestedPose=true;
        ApplyRequestedPose();
    }
    void ApplyRequestedPose()
    {
        appliedMotionScale=MotionScale;
        visual.localPosition=visualPosition+requestedOffset*appliedMotionScale;
        visual.localRotation=visualRotation*Quaternion.Euler(requestedAngles*appliedMotionScale);
        visual.localScale=Vector3.Scale(visualScale,Vector3.Lerp(Vector3.one,requestedScale,appliedMotionScale));
    }
    // Secondary impact motion never changes readiness, the layout root or its collider.
    public void SetImpactReaction(Vector3 offset, Vector3 angles)
    {
        if (!IsReady || IsMoving) return;
        reactionOffset = offset; reactionAngles = angles; ApplyHover();
    }
    public void RestoreRest()
    {
        SetInscriptionVisibility(1);
        IsReady = true; IsMoving = false; IsHighlighted = false;
        hasRequestedPose=false;
        hoverAmount = hoverFrom = hoverTarget = hoverAge = 0;
        reactionOffset = reactionAngles = Vector3.zero;
        visual.gameObject.SetActive(true); visual.localRotation = visualRotation;
        selectionRing.SetActive(false); HitCollider.enabled = true; ApplyHover();
    }
    public void SetInscriptionVisibility(float value)
    {
        float alpha = Mathf.Clamp01(value); label.alpha = alpha;
        if (inscriptionRenderers == null) return;
        for (int i = 0; i < inscriptionRenderers.Length; i++)
        {
            var renderer = inscriptionRenderers[i]; if (renderer == null) continue;
            var color = inscriptionColors[i]; color.a *= alpha;
            inscriptionProperties.Clear(); inscriptionProperties.SetColor("_BaseColor", color); inscriptionProperties.SetColor("_Color", color);
            renderer.SetPropertyBlock(inscriptionProperties); renderer.enabled = alpha > .001f;
        }
    }
    public Vector3 CurrentOffset => visual.localPosition - visualPosition;
    public Vector3 CurrentScale => new Vector3(visual.localScale.x / visualScale.x, visual.localScale.y / visualScale.y, visual.localScale.z / visualScale.z);
    // Predict the final inspection bounds without temporarily moving visible geometry.
    public Bounds MotionBounds(Vector3 offset,Vector3 angles,Vector3 scale,float motionAmount)
    {
        return TransformBounds(contentLocalBounds,MotionMatrix(offset,angles,scale,motionAmount));
    }
    Matrix4x4 MotionMatrix(Vector3 offset,Vector3 angles,Vector3 scale,float motionAmount) =>
        visual.parent.localToWorldMatrix*Matrix4x4.TRS(visualPosition+offset*motionAmount,
            visualRotation*Quaternion.Euler(angles*motionAmount),Vector3.Scale(visualScale,Vector3.Lerp(Vector3.one,scale,motionAmount)));
    internal Matrix4x4 PreviewFrame(Vector3 offset,Vector3 angles,Vector3 scale,float motionAmount)
    {
        var rotation=visual.parent.rotation*visualRotation*Quaternion.Euler(angles*motionAmount);
        var point=(MotionMatrix(offset,angles,scale,motionAmount)*rockFromVisual).MultiplyPoint3x4(LocalSurfacePoint);
        return Matrix4x4.TRS(point+rotation*Vector3.up*0.015f,rotation,Vector3.one*(IsMain?1.18f:1f));
    }
    internal static Bounds TransformBounds(Bounds local,Matrix4x4 matrix)
    {
        var x=matrix.MultiplyVector(Vector3.right*local.extents.x);
        var y=matrix.MultiplyVector(Vector3.up*local.extents.y);
        var z=matrix.MultiplyVector(Vector3.forward*local.extents.z);
        var extents=new Vector3(Mathf.Abs(x.x)+Mathf.Abs(y.x)+Mathf.Abs(z.x),
            Mathf.Abs(x.y)+Mathf.Abs(y.y)+Mathf.Abs(z.y),Mathf.Abs(x.z)+Mathf.Abs(y.z)+Mathf.Abs(z.z));
        return new Bounds(matrix.MultiplyPoint3x4(local.center),extents*2);
    }
    void OnDisable() { if (Offer != null) RestoreRest(); }
}
