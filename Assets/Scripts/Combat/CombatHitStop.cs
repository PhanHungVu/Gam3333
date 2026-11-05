using System.Collections.Generic;
using UnityEngine;

// Brief actor-local hitstop. The world clock, camera, projectiles and unrelated actors keep running.
[DisallowMultipleComponent, RequireComponent(typeof(CombatHealth))]
public sealed class CombatHitStop : MonoBehaviour
{
    [SerializeField, Range(.01f,.2f)] float maximumStop = .10f;
    [SerializeField, Range(0,.3f)] float recoveryGap = .08f;
    static readonly Dictionary<Transform,CombatHitStop> stopped = new Dictionary<Transform,CombatHitStop>();
    static readonly List<CombatHitStop> active = new List<CombatHitStop>();
    readonly List<Animator> animators = new List<Animator>();
    readonly List<float> speeds = new List<float>();
    float until, nextAllowed;
    int beganFrame;
    Object requestedBy;
    bool holding;
    public bool IsStopped => holding && isActiveAndEnabled && Time.unscaledTime < until;
    public float Remaining => IsStopped ? until-Time.unscaledTime : 0;
    public int StopsStarted { get; private set; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { stopped.Clear(); active.Clear(); }
    public static bool IsFrozen(Transform actor) => actor != null && stopped.TryGetValue(actor,out var stop) && stop != null && stop.IsStopped;
    // Other weapons released in the same frame retain their independent attack admission.
    public static bool BlocksNewActions(Transform actor) => IsFrozen(actor) && stopped[actor].beganFrame != Time.frameCount;
    public static bool Request(Transform actor,float seconds,Object source)
    {
        if(actor==null || !actor.gameObject.activeInHierarchy || Time.timeScale<=0 || seconds<=0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) return false;
        var health=actor.GetComponent<CombatHealth>(); if(health==null || health.IsDead) return false;
        var stop=actor.GetComponent<CombatHitStop>() ?? actor.gameObject.AddComponent<CombatHitStop>();
        return stop.Begin(seconds,source);
    }
    bool Begin(float seconds,Object source)
    {
        if(holding && !IsStopped) Clear();
        // Repeated pellets/AoE victims never accumulate or prolong the same pause.
        if(holding || Time.unscaledTime<nextAllowed) return false;
        enabled=true; holding=true; beganFrame=Time.frameCount; requestedBy=source;
        until=Time.unscaledTime+Mathf.Min(seconds,maximumStop); nextAllowed=until+recoveryGap;
        animators.Clear(); speeds.Clear(); GetComponentsInChildren(true,animators);
        foreach(var animator in animators) { speeds.Add(animator.speed); animator.speed=0; }
        stopped[transform]=this; active.Add(this); StopsStarted++; return true;
    }
    void Update() { if(holding && Time.unscaledTime>=until) Clear(); }
    public void Clear()
    {
        for(int i=0;i<animators.Count;i++) if(animators[i]!=null) animators[i].speed=speeds[i];
        animators.Clear(); speeds.Clear(); holding=false; requestedBy=null;
        stopped.Remove(transform); active.Remove(this);
    }
    public static void ClearFrom(Object source)
    {
        for(int i=active.Count-1;i>=0;i--) if(active[i]!=null && active[i].requestedBy==source) active[i].Clear();
    }
    void OnDisable() { Clear(); nextAllowed=0; }
    void OnDestroy() => Clear();
}
