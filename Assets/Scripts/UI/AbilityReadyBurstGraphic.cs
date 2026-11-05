using UnityEngine;
using UnityEngine.UI;

// Canvas-native particles: one cached mesh, no individual particle GameObjects/material instances.
// Soft radial geometry supplies a white glow without requiring camera bloom or changing the HUD Canvas.
public sealed class AbilityReadyBurstGraphic : MaskableGraphic
{
    struct Spark { public float delay, life, travel, x, drift, size; }
    readonly Spark[] sparks = new Spark[128];
    static readonly Vector2[] Directions = { new Vector2(1,0), new Vector2(.5f,.8660254f), new Vector2(-.5f,.8660254f),
        new Vector2(-1,0), new Vector2(-.5f,-.8660254f), new Vector2(.5f,-.8660254f) };
    int count;
    float age, duration, flash, height, spread, brightness;
    uint seed = 1;
    public float Age => age;
    public int ParticleCount => count;

    protected override void Awake() { base.Awake(); raycastTarget = false; maskable = false; }

    public void Begin(int particles, float seconds, float flashSeconds, float columnHeight, float columnSpread, float intensity, Vector2 lifetime)
    {
        count = Mathf.Clamp(particles, 8, sparks.Length); duration = seconds; flash = flashSeconds;
        height = columnHeight; spread = columnSpread; brightness = intensity; age = 0;
        seed = unchecked((uint)(GetEntityId().GetHashCode() ^ Time.frameCount)) | 1u;
        float low = Mathf.Clamp(Mathf.Min(lifetime.x, lifetime.y), .05f, duration);
        float high = Mathf.Clamp(Mathf.Max(lifetime.x, lifetime.y), low, duration);
        for (int i = 0; i < count; i++)
        {
            float life = Sample(low, high);
            sparks[i] = new Spark { delay = Sample(0, 1) * Mathf.Min(.07f, duration - life), life = life,
                travel = Sample(.45f, 1f), x = Sample(-.055f, .055f), drift = Sample(-1f, 1f),
                size = Sample(.014f, .034f) * (i % 9 == 0 ? 1.7f : 1) };
        }
        enabled = true; SetVerticesDirty();
    }
    public void SetAge(float seconds) { age = Mathf.Max(0, seconds); SetVerticesDirty(); }
    float Sample(float min, float max)
    {
        // A private visual RNG must not perturb gameplay's loot/encounter random sequence.
        seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
        return Mathf.Lerp(min, max, (seed & 0xffffff) / 16777216f);
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        float w = rectTransform.rect.width, h = rectTransform.rect.height;
        if (w <= 0 || h <= 0 || age >= duration) return;
        float flashAlpha = Mathf.Clamp01(1 - age / flash);
        if (flashAlpha > 0) Quad(mesh, -w*.5f, -h*.5f, w*.5f, h*.5f, flashAlpha*.9f);

        // A concentrated rising core, brightest at the source, with a broad faint halo.
        float beamAlpha = Mathf.Pow(Mathf.Clamp01(1 - Mathf.Max(0,age-.07f) / (duration * .7f)), 1.2f);
        float reach = h * height * Mathf.Lerp(.35f, 1, Mathf.Clamp01(age / .12f));
        if (beamAlpha > 0)
        {
            Column(mesh, w*.44f, reach, beamAlpha*.18f);
            Column(mesh, w*.24f, reach*.96f, beamAlpha*.4f);
            Column(mesh, w*.12f, reach*.92f, beamAlpha*.95f);
        }
        for (int i = 0; i < count; i++)
        {
            var spark = sparks[i]; float t = (age - spark.delay) / spark.life;
            if (t < 0 || t >= 1) continue;
            float rise = 1 - Mathf.Pow(1 - t, 2.1f);
            var center = new Vector2((spark.x + spark.drift * spread * rise * .5f) * w, rise * height * h * spark.travel);
            float alpha = Mathf.Min(1, t * 18) * Mathf.Pow(1 - t, .8f);
            Glow(mesh, center, w * spark.size, alpha);
        }
    }
    Color Tint(float alpha) => new Color(1, 1, 1, Mathf.Clamp01(alpha * brightness));
    void Quad(VertexHelper mesh, float left, float bottom, float right, float top, float alpha)
    {
        int start = mesh.currentVertCount; var tint = Tint(alpha);
        mesh.AddVert(new Vector3(left,bottom),tint,Vector2.zero); mesh.AddVert(new Vector3(left,top),tint,Vector2.zero);
        mesh.AddVert(new Vector3(right,top),tint,Vector2.zero); mesh.AddVert(new Vector3(right,bottom),tint,Vector2.zero);
        mesh.AddTriangle(start,start+1,start+2); mesh.AddTriangle(start,start+2,start+3);
    }
    void Column(VertexHelper mesh, float width, float top, float alpha)
    {
        int start = mesh.currentVertCount;
        mesh.AddVert(new Vector3(-width*.5f,0),Tint(alpha),Vector2.zero); mesh.AddVert(new Vector3(width*.5f,0),Tint(alpha),Vector2.zero);
        mesh.AddVert(new Vector3(-width*.35f,top*.72f),Tint(alpha*.65f),Vector2.zero); mesh.AddVert(new Vector3(width*.35f,top*.72f),Tint(alpha*.65f),Vector2.zero);
        mesh.AddVert(new Vector3(-width*.2f,top),Tint(0),Vector2.zero); mesh.AddVert(new Vector3(width*.2f,top),Tint(0),Vector2.zero);
        mesh.AddTriangle(start,start+2,start+3); mesh.AddTriangle(start,start+3,start+1);
        mesh.AddTriangle(start+2,start+4,start+5); mesh.AddTriangle(start+2,start+5,start+3);
    }
    void Glow(VertexHelper mesh, Vector2 center, float radius, float alpha)
    {
        int start = mesh.currentVertCount;
        mesh.AddVert(center,Tint(alpha),Vector2.zero);
        for (int i=0;i<6;i++) mesh.AddVert(center+Directions[i]*radius*.5f,Tint(alpha),Vector2.zero);
        for (int i=0;i<6;i++) mesh.AddVert(center+Directions[i]*radius*2.2f,Tint(0),Vector2.zero);
        for (int i=0;i<6;i++)
        {
            int next=(i+1)%6;
            mesh.AddTriangle(start,start+1+i,start+1+next);
            mesh.AddTriangle(start+1+i,start+7+i,start+7+next);
            mesh.AddTriangle(start+1+i,start+7+next,start+1+next);
        }
    }
}
