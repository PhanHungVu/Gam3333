using UnityEngine;
using UnityEngine.Rendering;

// Pool-owned, rather than parented to the projectile: impact cannot cut off its tail.
public sealed class CombatProjectileTrail : MonoBehaviour
{
    TrailRenderer trail;
    MechProjectile projectile;
    float tailRemaining;
    float linger, tailDuration;
    Color headColor;
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
    public void Play(MechProjectile shot, Material material, Color color, float width, float lifetime, float lingerSeconds = 0)
    {
        Stop();
        if (shot == null || !shot.IsLive || material == null) return;
        if (trail == null) trail = gameObject.AddComponent<TrailRenderer>();
        Configure(trail, material, color, width, lifetime);
        linger = Mathf.Max(0,lingerSeconds); headColor = color;
        transform.position = shot.transform.position;
        gameObject.SetActive(true); trail.Clear(); trail.emitting = true;
        projectile = shot; projectile.Finished += OnFinished; IsPlaying = true;
    }
    void OnFinished(Vector3 position)
    {
        transform.position = position;
        if (trail != null)
        {
            trail.AddPosition(position); trail.emitting = false;
            tailRemaining = tailDuration = Mathf.Max(trail.time,linger);
            // Retain the existing short flight path while it fades, without drawing a longer flight history.
            if(linger>0) trail.time += linger;
        }
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
        if(trail!=null && linger>0 && tailDuration>0)
        {
            var color=headColor; color.a*=Mathf.Clamp01(tailRemaining/tailDuration); trail.startColor=color;
        }
        if (tailRemaining <= 0) Stop();
    }
    public void Stop()
    {
        if (projectile != null) projectile.Finished -= OnFinished;
        projectile = null; IsPlaying = false;
        tailRemaining = tailDuration = 0;
        if (trail != null) { trail.emitting = false; trail.Clear(); }
        gameObject.SetActive(false);
    }
    void OnDisable() { if (IsPlaying) Stop(); }
}
