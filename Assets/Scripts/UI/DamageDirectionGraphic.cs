using UnityEngine;
using UnityEngine.UI;

// Small outward-pointing chevron with a feathered halo. Positive local Y points toward the source.
public sealed class DamageDirectionGraphic : MaskableGraphic
{
    static readonly Vector2[] Points = { new Vector2(-.5f, -.22f), new Vector2(0, .48f), new Vector2(.5f, -.22f), new Vector2(0, .05f) };
    protected override void Awake() { base.Awake(); raycastTarget = false; maskable = false; }
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var size = rectTransform.rect.size;
        var core = color;
        for (int i = 0; i < Points.Length; i++) mesh.AddVert(Vector2.Scale(Points[i], size), core, Vector2.zero);
        mesh.AddTriangle(0, 1, 3); mesh.AddTriangle(1, 2, 3);
        var clear = core; clear.a = 0;
        for (int i = 0; i < Points.Length; i++) mesh.AddVert(Vector2.Scale(Points[i] * 1.24f, size), clear, Vector2.zero);
        for (int i = 0; i < Points.Length; i++)
        {
            int next = (i + 1) % Points.Length;
            mesh.AddTriangle(i, i + 4, next + 4); mesh.AddTriangle(i, next + 4, next);
        }
    }
}
