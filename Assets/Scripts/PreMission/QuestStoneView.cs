using TMPro;
using UnityEngine;

// One physical offer. Its collider/root never move when the visual is highlighted.
[DisallowMultipleComponent, RequireComponent(typeof(BoxCollider))]
public sealed class QuestStoneView : MonoBehaviour
{
    [SerializeField] Transform visual;
    [SerializeField] MeshRenderer rock;
    [SerializeField] GameObject selectionRing;
    [SerializeField] TMP_Text label;
    [SerializeField, Min(0.01f)] float hoverDuration = 0.12f;
    [SerializeField, Min(0.01f)] float mainHoverDuration = 0.18f;
    [SerializeField, Min(0f)] float hoverLift = 0.06f;
    [SerializeField, Min(0f)] float mainHoverLift = 0.025f;
    [SerializeField, Min(0f)] float hoverTilt = 2.2f;
    [SerializeField, Min(0f)] float mainHoverTilt = 0.7f;
    MaterialPropertyBlock properties;
    Vector3 visualPosition, visualScale;
    Quaternion visualRotation;
    Color baseColor;
    QuestBoardImpactFeedback feedback;
    float MotionScale => feedback!=null ? feedback.MotionScale : 1;
    public void ConfigureFeedback(QuestBoardImpactFeedback value) => feedback=value;
    float hoverAmount, hoverFrom, hoverTarget, hoverAge;
    Vector3 restCenter;
    Vector3 reactionOffset, reactionAngles;
    public QuestOffer Offer { get; private set; }
    public bool IsHighlighted { get; private set; }
    public bool IsReady { get; private set; }
    public bool IsMoving { get; private set; }
    public bool IsReacting => reactionOffset != Vector3.zero || reactionAngles != Vector3.zero;
    public Transform Visual => visual;
    public bool IsMain => Offer != null && Offer.Quest.category == QuestCategory.Main;
    public Vector3 InspectionPoint => transform.TransformPoint(restCenter) + Vector3.up * 0.12f;
    public Collider HitCollider => GetComponent<BoxCollider>();
    public Bounds RockBounds => rock.bounds;
    public float InscriptionOpacity => label.alpha;
    public Vector3 CurrentAngles
    {
        get
        {
            var angles=(Quaternion.Inverse(visualRotation)*visual.localRotation).eulerAngles;
            return new Vector3(Mathf.DeltaAngle(0,angles.x),Mathf.DeltaAngle(0,angles.y),Mathf.DeltaAngle(0,angles.z));
        }
    }

    public void Bind(QuestOffer offer)
    {
        properties = new MaterialPropertyBlock();
        Offer = offer;
        name = (offer.Quest.category == QuestCategory.Main ? "Main Stone - " : "Small Stone - ") + offer.Quest.title;
        visualPosition = visual.localPosition; visualScale = visual.localScale;
        visualRotation = visual.localRotation;
        restCenter = transform.InverseTransformPoint(rock.bounds.center);
        baseColor = rock.sharedMaterial.color;
        label.text = "<size=65%>" + (offer.Quest.category == QuestCategory.Main ? "MAIN QUEST" : "SMALL QUEST") + "</size>\n" + offer.Quest.title;
        RestoreRest();
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
    void ApplyHover()
    {
        if (visual == null) return;
        visual.localPosition = visualPosition + (Vector3.up * ((IsMain ? mainHoverLift : hoverLift) * hoverAmount) + reactionOffset)*MotionScale;
        visual.localRotation = visualRotation * Quaternion.Euler((reactionAngles+new Vector3(-1,0,0.35f)*((IsMain ? mainHoverTilt : hoverTilt)*hoverAmount))*MotionScale);
        visual.localScale = visualScale * (1f + (IsMain ? 0.01f : 0.025f) * hoverAmount*MotionScale);
        var color = Color.Lerp(baseColor, new Color(0.83f, 0.76f, 0.54f), 0.22f * hoverAmount);
        properties.SetColor("_BaseColor", color); properties.SetColor("_Color", color);
        rock.SetPropertyBlock(properties);
    }
    public void PrepareArrival()
    {
        RestoreRest(); IsReady = false; IsMoving = true; HitCollider.enabled = false; visual.gameObject.SetActive(false);
    }
    public void SetMotionFrame(Vector3 offset, Vector3 angles, Vector3 scale)
    {
        reactionOffset = reactionAngles = Vector3.zero;
        IsMoving = true; IsHighlighted = false; selectionRing.SetActive(false); HitCollider.enabled = false;
        visual.gameObject.SetActive(true);
        visual.localPosition = visualPosition + offset*MotionScale; visual.localRotation = visualRotation * Quaternion.Euler(angles*MotionScale);
        visual.localScale = Vector3.Scale(visualScale, Vector3.Lerp(Vector3.one,scale,MotionScale));
    }
    // Secondary impact motion never changes readiness, the layout root or its collider.
    public void SetImpactReaction(Vector3 offset, Vector3 angles)
    {
        if (!IsReady || IsMoving) return;
        reactionOffset = offset; reactionAngles = angles; ApplyHover();
    }
    public void RestoreRest()
    {
        label.alpha=1;
        IsReady = true; IsMoving = false; IsHighlighted = false;
        hoverAmount = hoverFrom = hoverTarget = hoverAge = 0;
        reactionOffset = reactionAngles = Vector3.zero;
        visual.gameObject.SetActive(true); visual.localRotation = visualRotation;
        selectionRing.SetActive(false); HitCollider.enabled = true; ApplyHover();
    }
    public void SetInscriptionVisibility(float value) => label.alpha=Mathf.Clamp01(value);
    public Vector3 CurrentOffset => visual.localPosition - visualPosition;
    public Vector3 CurrentScale => new Vector3(visual.localScale.x / visualScale.x, visual.localScale.y / visualScale.y, visual.localScale.z / visualScale.z);
    void OnDisable() { if (Offer != null) RestoreRest(); }
}
