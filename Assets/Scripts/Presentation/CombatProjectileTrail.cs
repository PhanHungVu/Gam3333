using UnityEngine;
using UnityEngine.Rendering;

// Pool-owned, rather than parented to the projectile: impact cannot cut off its tail.
public sealed class CombatProjectileTrail : MonoBehaviour
{
    TrailRenderer trail;
    MechProjectile projectile;
    float tailRemaining;
    public bool IsPlaying { get; private set; }
    public bool IsFollowing => projectile != null;
    public TrailRenderer Renderer => trail;
    public static void Configure(TrailRenderer target, Material material, Color color, float width, float lifetime)
    {
        target.sharedMaterial = material; target.time = Mathf.Max(.03f, lifetime);
        target.startWidth = Mathf.Max(.01f, width); target.endWidth = 0;
        target.startColor = color; color.a = 0; target.endColor = color;
        target.minVertexDistance = .03f; target.numCapVertices = 2;
        target.shadowCastingMode = ShadowCastingMode.Off; target.receiveShadows = false;
        target.autodestruct = false;
    }
    public void Play(MechProjectile shot, Material material, Color color, float width, float lifetime)
    {
        Stop();
        if (shot == null || !shot.IsLive || material == null) return;
        if (trail == null) trail = gameObject.AddComponent<TrailRenderer>();
        Configure(trail, material, color, width, lifetime);
        transform.position = shot.transform.position;
        gameObject.SetActive(true); trail.Clear(); trail.emitting = true;
        projectile = shot; projectile.Finished += OnFinished; IsPlaying = true;
    }
    void OnFinished(Vector3 position)
    {
        transform.position = position;
        if (trail != null) { trail.AddPosition(position); trail.emitting = false; tailRemaining = trail.time; }
        if (projectile != null) projectile.Finished -= OnFinished;
        projectile = null;
    }
    public void Tick(float deltaTime)
    {
        if (!IsPlaying) return;
        if (projectile != null)
        {
            if (projectile.IsLive && projectile.isActiveAndEnabled) { transform.position = projectile.transform.position; return; }
            OnFinished(projectile.transform.position);
        }
        tailRemaining -= Mathf.Max(0, deltaTime);
        if (tailRemaining <= 0) Stop();
    }
    public void Stop()
    {
        if (projectile != null) projectile.Finished -= OnFinished;
        projectile = null; IsPlaying = false;
        if (trail != null) { trail.emitting = false; trail.Clear(); }
        gameObject.SetActive(false);
    }
    void OnDisable() { if (IsPlaying) Stop(); }
}
