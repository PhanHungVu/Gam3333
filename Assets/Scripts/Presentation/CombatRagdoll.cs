using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Physical proxies drive a renderer-only skeleton copy. Uniform world-space proxies avoid imported bone scales.
public sealed class CombatRagdoll : MonoBehaviour
{
    public const int CorpseLayer = 2; // Built-in Ignore Raycast: deliberately excluded by gameplay queries below.
    public const int QueryMask = Physics.DefaultRaycastLayers;
    static readonly List<CombatRagdoll> active = new List<CombatRagdoll>();
    sealed class Bone
    {
        public Transform source, visual;
        public Rigidbody body;
        public Vector3 center, size;
    }
    readonly List<Bone> bones = new List<Bone>();
    Collider[] colliders;
    float restAge, life, restSeconds, maximumLifetime, restSpeed, restAngularSpeed;
    public static int ActiveCount => active.Count;
    public int BodyCount => bones.Count;
    public float RestAge => restAge;
    public IReadOnlyList<Rigidbody> Bodies => bodies;
    readonly List<Rigidbody> bodies = new List<Rigidbody>();
    CombatVisualSnapshot visualSnapshot;
    public bool PoseLocked { get; private set; }
    public int HiddenLootRendererCount { get; private set; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() => active.Clear();

    public static CombatRagdoll Spawn(Transform actor, Vector3 velocity, int limit = 12, int bodyLimit = 14,
        float restSeconds = 5, float maximumLifetime = 30, float restSpeed = .15f, float restAngularSpeed = .3f, bool lockPose = false)
    {
        active.RemoveAll(item => item == null);
        if (active.Count >= Mathf.Clamp(limit,1,24)) return null;
        var snapshot = CombatVisualSnapshot.Create(actor, actor.name + " Ragdoll", CorpseLayer);
        if (snapshot.RendererCount == 0) { Destroy(snapshot.Root); return null; }
        var root = new GameObject(actor.name + " Corpse"); SceneManager.MoveGameObjectToScene(root,actor.gameObject.scene);
        snapshot.Root.transform.SetParent(root.transform,true);
        var corpse = root.AddComponent<CombatRagdoll>();
        corpse.visualSnapshot = snapshot; corpse.PoseLocked = lockPose;
        corpse.restSeconds = Mathf.Max(0,restSeconds); corpse.maximumLifetime = Mathf.Max(1,maximumLifetime);
        corpse.restSpeed = Mathf.Max(.01f,restSpeed); corpse.restAngularSpeed = Mathf.Max(.01f,restAngularSpeed);
        corpse.Build(actor,snapshot,Mathf.Clamp(bodyLimit,1,20),velocity);
        if (corpse.bones.Count == 0) { Destroy(root); return null; }
        corpse.colliders = root.GetComponentsInChildren<Collider>();
        foreach (var other in active) Ignore(corpse.colliders,other.colliders);
        Ignore(corpse.colliders,corpse.colliders);
        active.Add(corpse);
        foreach (var actorHealth in FindObjectsByType<CombatHealth>()) IgnoreActorCollisions(actorHealth.gameObject);
        return corpse;
    }
    void Build(Transform actor, CombatVisualSnapshot snapshot, int limit, Vector3 velocity)
    {
        var rigid = new Dictionary<Transform,Bounds>();
        var anchors = new HashSet<Transform>();
        if (PoseLocked)
        {
            var bounds = InFrame(snapshot.Bounds, actor);
            bones.Add(new Bone { source = actor, visual = snapshot.Root.transform, center = bounds.center, size = bounds.size });
        }
        else foreach (var renderer in actor.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || !snapshot.Transforms.ContainsKey(renderer.transform)) continue;
            if (renderer is SkinnedMeshRenderer skin)
            {
                foreach (var bone in skin.bones)
                {
                    if (bone == null || bone == skin.rootBone || !anchors.Add(bone) || !snapshot.Transforms.TryGetValue(bone,out var visual)) continue;
                    Transform child = null;
                    foreach (var candidate in skin.bones) if (candidate != null && candidate.parent == bone) { child = candidate; break; }
                    Vector3 end = child != null ? child.position : bone.position + Vector3.down*Mathf.Max(.12f,skin.bounds.size.y*.08f);
                    float radius = Mathf.Clamp(skin.bounds.size.x*.12f,.07f,.4f);
                    Vector3 local = Quaternion.Inverse(bone.rotation)*((bone.position+end)*.5f-bone.position);
                    bones.Add(new Bone { source = bone, visual = visual, center = local,
                        size = new Vector3(radius*2,Mathf.Max(radius*2,Vector3.Distance(bone.position,end)*.75f),radius*2) });
                }
            }
            else if (renderer is MeshRenderer)
            {
                var part = renderer.GetComponentInParent<Part>();
                Transform anchor = part != null && part.transform.IsChildOf(actor) ? part.transform : renderer.transform;
                if (rigid.TryGetValue(anchor,out var bounds)) { bounds.Encapsulate(renderer.bounds); rigid[anchor] = bounds; }
                else rigid[anchor] = renderer.bounds;
            }
        }
        foreach (var pair in rigid)
        {
            if (!anchors.Add(pair.Key) || !snapshot.Transforms.TryGetValue(pair.Key,out var visual)) continue;
            var bounds = InFrame(pair.Value,pair.Key);
            bones.Add(new Bone { source = pair.Key, visual = visual, center = bounds.center, size = bounds.size });
        }
        // Prefer large body pieces over tiny decorations if a future model exceeds the configured budget.
        bones.Sort((a,b) => (b.size.x*b.size.y*b.size.z).CompareTo(a.size.x*a.size.y*a.size.z));
        if (bones.Count > limit) bones.RemoveRange(limit,bones.Count-limit);
        Bone core = null;
        foreach (var bone in bones)
            if (bone.source.TryGetComponent<Part>(out var part) && part.partType == PartType.Torso) { core = bone; break; }
        if (core == null && bones.Count > 0) core = bones[0];
        foreach (var bone in bones)
        {
            var proxy = new GameObject(bone.source.name + " Physics"); proxy.layer = CorpseLayer;
            proxy.transform.SetParent(transform,false); proxy.transform.SetPositionAndRotation(bone.source.position,bone.source.rotation);
            var box = proxy.AddComponent<BoxCollider>(); box.center = bone.center;
            int lootLayer = LayerMask.NameToLayer(Part.LoosePhysicsLayerName);
            if (lootLayer >= 0) box.excludeLayers |= 1 << lootLayer;
            box.size = Vector3.Max(bone.size*.85f,Vector3.one*.06f);
            var body = proxy.AddComponent<Rigidbody>(); body.mass = bone == core ? 8 : 2;
            if (PoseLocked) body.constraints = RigidbodyConstraints.FreezeRotation;
            body.linearDamping = .35f; body.angularDamping = 1.5f; body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.solverIterations = 8; body.maxDepenetrationVelocity = 3; body.maxAngularVelocity = 12;
            body.linearVelocity = velocity; bone.body = body; bodies.Add(body);
        }
        foreach (var bone in bones)
        {
            if (bone == core) continue;
            var parent = core;
            for (var ancestor=bone.source.parent;ancestor != null && ancestor != actor;ancestor=ancestor.parent)
            {
                var found = bones.Find(candidate => candidate.source == ancestor);
                if (found != null) { parent=found; break; }
            }
            var joint = bone.body.gameObject.AddComponent<ConfigurableJoint>(); joint.connectedBody=parent.body;
            joint.autoConfigureConnectedAnchor=false; joint.anchor=Vector3.zero;
            joint.connectedAnchor=parent.body.transform.InverseTransformPoint(bone.body.position);
            joint.xMotion=joint.yMotion=joint.zMotion=ConfigurableJointMotion.Locked;
            joint.angularXMotion=joint.angularYMotion=joint.angularZMotion=ConfigurableJointMotion.Limited;
            joint.lowAngularXLimit=new SoftJointLimit { limit=-35 }; joint.highAngularXLimit=new SoftJointLimit { limit=35 };
            joint.angularYLimit=joint.angularZLimit=new SoftJointLimit { limit=30 };
            joint.projectionMode=JointProjectionMode.PositionAndRotation; joint.projectionDistance=.1f; joint.projectionAngle=15;
        }
        bones.Sort((a,b) => Depth(a.source).CompareTo(Depth(b.source)));
    }
    static int Depth(Transform node) { int depth=0; while(node != null) { depth++; node=node.parent; } return depth; }
    static Bounds InFrame(Bounds bounds,Transform anchor)
    {
        Bounds result=default; var inverse=Quaternion.Inverse(anchor.rotation);
        for(int i=0;i<8;i++)
        {
            var point=bounds.center+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
            point=inverse*(point-anchor.position); if(i==0) result=new Bounds(point,Vector3.zero); else result.Encapsulate(point);
        }
        return result;
    }
    void LateUpdate()
    {
        foreach(var bone in bones) if(bone.visual != null && bone.body != null) bone.visual.SetPositionAndRotation(bone.body.position,bone.body.rotation);
    }
    public void HideDroppedPart(Part part)
    {
        if (part == null || visualSnapshot == null) return;
        foreach (var renderer in part.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.GetComponentInParent<Part>() != part || !visualSnapshot.Transforms.TryGetValue(renderer.transform, out var visual)) continue;
            foreach (var copy in visual.GetComponents<Renderer>())
                if (copy.enabled) { copy.enabled = false; HiddenLootRendererCount++; }
        }
        foreach (var bone in bones)
            if (bone.source != null && bone.source.GetComponentInParent<Part>() == part && bone.body != null)
            {
                var collider = bone.body.GetComponent<Collider>(); if (collider != null) collider.enabled = false;
            }
    }
    void Update() => TickLifetime(Time.deltaTime,Time.unscaledDeltaTime);
    public void TickLifetime(float scaledDelta,float unscaledDelta)
    {
        life+=Mathf.Max(0,unscaledDelta); bool resting=bodies.Count>0;
        foreach(var body in bodies)
            if(body != null && (body.linearVelocity.sqrMagnitude>restSpeed*restSpeed || body.angularVelocity.sqrMagnitude>restAngularSpeed*restAngularSpeed)) { resting=false; break; }
        restAge=resting ? restAge+Mathf.Max(0,scaledDelta) : 0;
        if(restAge>=restSeconds || life>=maximumLifetime) { gameObject.SetActive(false); Destroy(gameObject); }
    }
    public static void IgnoreActorCollisions(GameObject actor)
    {
        if(active.Count==0) return;
        var actorColliders=actor.GetComponentsInChildren<Collider>(true);
        foreach(var corpse in active) if(corpse != null) Ignore(corpse.colliders,actorColliders);
    }
    static void Ignore(Collider[] a,Collider[] b)
    {
        if(a==null || b==null) return;
        foreach(var first in a) foreach(var second in b) if(first != null && second != null && first != second) Physics.IgnoreCollision(first,second);
    }
    void OnDisable() => active.Remove(this);
    void OnDestroy() => active.Remove(this);
}
