using UnityEngine;

// Optional temporary landing cue. Existing lights and real shadows are untouched.
[DisallowMultipleComponent]
public sealed class PreMissionContactShadow : MonoBehaviour
{
    [SerializeField] MeshRenderer surface;
    [SerializeField] bool showApproachingShadow=true;
    [SerializeField] Color shadowTint=Color.black;
    [SerializeField, Range(0,0.1f)] float distantOpacity=0.025f;
    [SerializeField, Range(0,0.5f)] float smallContactOpacity=0.17f;
    [SerializeField, Range(0,0.5f)] float mainContactOpacity=0.23f;
    [SerializeField, Range(1,2)] float distantSpread=1.55f;
    [SerializeField, Min(0.01f)] float contactFade=0.10f;
    [SerializeField] float surfaceHeight=0.064f;
    PreMissionStoneBoard board;
    MaterialPropertyBlock properties;
    Vector2 footprint;
    float contactOpacity;
    static readonly int BaseColor=Shader.PropertyToID("_BaseColor");
    public QuestStoneView Stone { get; private set; }
    public float Opacity { get; private set; }
    public Vector2 Footprint => surface!=null ? new Vector2(surface.transform.localScale.x,surface.transform.localScale.y) : Vector2.zero;
    public bool IsVisible => surface!=null && surface.enabled && Opacity>0;
    public MeshRenderer Surface => surface;
    bool Suppressed => !isActiveAndEnabled || !showApproachingShadow || surface==null || surface.sharedMaterial==null
        || (board!=null && board.Motion!=null && board.Motion.Feedback!=null && board.Motion.Feedback.ReducedMotion);
    public void Bind(PreMissionStoneBoard owner) { board=owner; End(); }
    public void Begin(QuestStoneView stone)
    {
        End(); Stone=stone;
        if(stone==null || surface==null) return;
        var bounds=QuestStoneView.TransformBounds(stone.MotionBounds(Vector3.zero,Vector3.zero,Vector3.one,1),surface.transform.parent.worldToLocalMatrix);
        footprint=new Vector2(bounds.size.x,bounds.size.z)*0.98f;
        surface.transform.localPosition=new Vector3(bounds.center.x,surfaceHeight,bounds.center.z);
        contactOpacity=stone.IsMain ? mainContactOpacity : smallContactOpacity;
        Approach(1);
    }
    public void Approach(float normalizedHeight,float gain=1)
    {
        if(Stone==null || surface==null) return;
        float proximity=1-Mathf.Clamp01(normalizedHeight), definition=Mathf.SmoothStep(0,1,proximity);
        float spread=Mathf.Lerp(distantSpread,1,definition);
        surface.transform.localScale=new Vector3(footprint.x*spread,footprint.y*spread,1);
        Draw(Mathf.Lerp(distantOpacity,contactOpacity,definition)*Mathf.Clamp01(gain));
    }
    public void Contact(float elapsed)
    {
        if(Stone==null || surface==null) return;
        surface.transform.localScale=new Vector3(footprint.x,footprint.y,1);
        Draw(contactOpacity*(1-Mathf.SmoothStep(0,1,Mathf.Clamp01(elapsed/Mathf.Max(0.01f,contactFade)))));
    }
    void Draw(float opacity)
    {
        Opacity=Suppressed ? 0 : Mathf.Clamp01(opacity*shadowTint.a);
        if(surface==null) return;
        if(properties==null) properties=new MaterialPropertyBlock();
        properties.SetColor(BaseColor,new Color(shadowTint.r,shadowTint.g,shadowTint.b,Opacity));
        surface.SetPropertyBlock(properties); surface.enabled=Opacity>0;
    }
    public void End() { Stone=null; Draw(0); }
    void OnDisable() => End();
}
