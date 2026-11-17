using UnityEngine;
using UnityEngine.Rendering;

// Development-only Game-view geometry. Reuses the existing player component and
// displays snapshots of the exact physics queries; never performs a damage query.
[DisallowMultipleComponent]
[DefaultExecutionOrder(301)]
[RequireComponent(typeof(MechCombat))]
public sealed class MeleeBoxPreview : MonoBehaviour
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [SerializeField] Material material;
    [InspectorName("Show Melee Hitboxes")]
    [SerializeField] bool showMeleeBox;
    [SerializeField] bool showSplashHitboxes = true;
    [SerializeField] bool showHitboxesOnlyDuringAttacks = true;
    [Tooltip("Briefly retains each instantaneous damage query so it is visible between frames.")]
    [SerializeField, Range(.03f, .5f)] float displaySeconds = .15f;

    sealed class View
    {
        public Transform Direct, Splash;
        public MeleeAttack.HitboxSnapshot Snapshot;
        public float Until;
    }
    MechCombat combat;
    MeleeAttack subscribed;
    GameObject root;
    Material directMaterial, splashMaterial;
    readonly View[] strikes = new View[3];
    readonly View[] idle = new View[2];
    int next;

    void Awake() => combat = GetComponent<MechCombat>();
    void OnEnable() => Bind();
    void Bind()
    {
        if (combat == null) combat = GetComponent<MechCombat>();
        if (combat.Melee == subscribed) return;
        if (subscribed != null) subscribed.HitboxesResolved -= Record;
        subscribed = combat.Melee;
        if (subscribed != null) subscribed.HitboxesResolved += Record;
    }
    void Record(MeleeAttack.HitboxSnapshot snapshot)
    {
        if (!showMeleeBox) return;
        EnsureViews();
        var view = strikes[next]; next = (next + 1) % strikes.Length;
        view.Snapshot = snapshot; view.Until = Time.unscaledTime + displaySeconds;
        Draw(view, true);
    }

    Material DebugMaterial(Color color)
    {
        var shader = material != null ? material.shader : Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        var result = new Material(shader) { name = "Melee Debug Transparent", hideFlags = HideFlags.DontSave, renderQueue = (int)RenderQueue.Transparent };
        result.SetColor("_BaseColor", color); result.SetColor("_Color", color);
        result.SetFloat("_Surface", 1); result.SetFloat("_Blend", 0); result.SetFloat("_ZWrite", 0);
        result.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); result.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        result.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        result.SetOverrideTag("RenderType", "Transparent");
        return result;
    }
    void EnsureViews()
    {
        if (root != null) return;
        root = new GameObject(name + " - Melee Hitbox Debug") { hideFlags = HideFlags.DontSave };
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, gameObject.scene);
        directMaterial = DebugMaterial(new Color(1, .06f, .04f, .22f));
        splashMaterial = DebugMaterial(new Color(1, .4f, .02f, .16f));
        for (int i = 0; i < strikes.Length; i++) strikes[i] = CreateView("Strike " + (i + 1));
        for (int i = 0; i < idle.Length; i++) idle[i] = CreateView("Idle " + (i + 1));
    }
    View CreateView(string label) => new View
    {
        Direct = Shape(label + " Direct", PrimitiveType.Cube, directMaterial),
        Splash = Shape(label + " Splash", PrimitiveType.Sphere, splashMaterial)
    };
    Transform Shape(string label, PrimitiveType type, Material shared)
    {
        var shape = GameObject.CreatePrimitive(type); shape.name = label;
        shape.transform.SetParent(root.transform, false);
        var collider = shape.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
        var renderer = shape.GetComponent<Renderer>(); renderer.sharedMaterial = shared;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        shape.SetActive(false); return shape.transform;
    }
    void Draw(View view, bool visible)
    {
        view.Direct.gameObject.SetActive(visible);
        view.Splash.gameObject.SetActive(visible && showSplashHitboxes && view.Snapshot.SplashRadius > 0);
        if (!visible) return;
        view.Direct.SetPositionAndRotation(view.Snapshot.Center, view.Snapshot.Rotation);
        view.Direct.localScale = view.Snapshot.Size;
        view.Splash.SetPositionAndRotation(view.Snapshot.SplashCenter, Quaternion.identity);
        view.Splash.localScale = Vector3.one * view.Snapshot.SplashRadius * 2;
    }
    void LateUpdate()
    {
        Bind();
        if (!showMeleeBox || combat == null || !combat.isActiveAndEnabled)
        { if (root != null) root.SetActive(false); return; }
        EnsureViews(); root.SetActive(true);
        for (int i = 0; i < strikes.Length; i++) Draw(strikes[i], strikes[i].Until > Time.unscaledTime);
        for (int i = 0; i < idle.Length; i++)
        {
            var part = combat.GetMeleePart(i == 0 ? PartCategory.ArmL : PartCategory.ArmR);
            bool visible = !showHitboxesOnlyDuringAttacks && part != null;
            if (visible) idle[i].Snapshot = combat.Melee.PreviewHitboxes(part, transform.rotation);
            Draw(idle[i], visible);
        }
    }
    void OnDisable()
    {
        if (subscribed != null) subscribed.HitboxesResolved -= Record;
        subscribed = null;
        if (root != null) root.SetActive(false);
    }
    void OnDestroy()
    {
        if (root != null) Destroy(root);
        if (directMaterial != null) Destroy(directMaterial);
        if (splashMaterial != null) Destroy(splashMaterial);
    }
#endif
}