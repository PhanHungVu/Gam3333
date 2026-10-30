using UnityEngine;
using UnityEngine.Rendering;

// Lightweight, replaceable world telegraph. Never applies damage or moves actors.
[DisallowMultipleComponent]
public sealed class EnemyThreatMarker : MonoBehaviour
{
    [SerializeField] Color warningColor = new Color(1, 0.4f, 0.12f, 0.85f);
    [SerializeField] Material material;
    LineRenderer line;
    Material ownedMaterial;
    readonly Vector3[] points = new Vector3[48];
    void Ensure()
    {
        if (line != null) return;
        var node = new GameObject("Attack warning"); node.transform.SetParent(transform, false);
        line = node.AddComponent<LineRenderer>(); line.useWorldSpace = true; line.loop = true;
        line.widthMultiplier = 0.07f; line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        ownedMaterial = material != null ? new Material(material) : new Material(shader);
        ownedMaterial.name = "Enemy telegraph (runtime)"; ownedMaterial.hideFlags = HideFlags.DontSave;
        if (ownedMaterial.HasProperty("_Surface")) ownedMaterial.SetFloat("_Surface", 1);
        if (ownedMaterial.HasProperty("_ZWrite")) ownedMaterial.SetFloat("_ZWrite", 0);
        ownedMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); ownedMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        ownedMaterial.renderQueue = 3000; line.sharedMaterial = ownedMaterial;
    }
    float Ground(Vector3 point) => NavTerrain.GroundHeight(point, transform.position.y - 1) + 0.06f;
    void Show(int count, float progress)
    {
        line.positionCount = count; line.enabled = true;
        var color = warningColor; color.a *= Mathf.Lerp(0.45f, 1, Mathf.Clamp01(progress));
        line.startColor = line.endColor = color;
        if (ownedMaterial.HasProperty("_BaseColor")) ownedMaterial.SetColor("_BaseColor", color);
        for (int i = 0; i < count; i++) line.SetPosition(i, points[i]);
    }
    public void Disc(Vector3 center, float radius, float progress)
    {
        Ensure();
        for (int i = 0; i < points.Length; i++)
        { float angle = i * Mathf.PI * 2 / points.Length; var point = center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius; point.y = Ground(point); points[i] = point; }
        Show(points.Length, progress);
    }
    public void Lane(Vector3 start, Vector3 end, float width, float progress)
    {
        Ensure(); var direction = Steering.Flat(end - start).normalized;
        if (direction.sqrMagnitude < 0.01f) direction = transform.forward;
        var side = Vector3.Cross(Vector3.up, direction) * width * 0.5f;
        points[0] = start - side; points[1] = start + side; points[2] = end + side; points[3] = end - side;
        for (int i = 0; i < 4; i++) { var point = points[i]; point.y = Ground(point); points[i] = point; }
        Show(4, progress);
    }
    public void Hide() { if (line != null) line.enabled = false; }
    void OnDisable() => Hide();
    void OnDestroy() { if (ownedMaterial != null) Destroy(ownedMaterial); }
}
