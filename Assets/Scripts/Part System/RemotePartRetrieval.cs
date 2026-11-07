using System;
using System.Collections.Generic;
using UnityEngine;

public enum RetrievalPhase { None, Lifting, Hovering, Flying }

// Item targeting/travel only. Normal equipment owns the swap; combat effects subscribe to travel events.
[DisallowMultipleComponent, DefaultExecutionOrder(-100)]
public sealed class RemotePartRetrieval : MonoBehaviour
{
    [Header("Camera-centre pickup targeting")]
    [Min(0.1f)] public float pickupRange = 20f;
    [Range(0.03f, 0.5f)] public float aimRadius = .18f;
    [Min(1)] public float rangePreviewMultiplier = 1.25f;
    public bool enableRemoteTargeting = true;
    [Header("Travel")]
    [Min(.02f)] public float travelDuration = .2f;
    [Min(0)] public float arcHeight = .3f;
    [Min(0)] public float obstructionRadius = .06f;
    [Header("Weapon anticipation and fast return")]
    [Min(0)] public float weaponLiftHeight = .8f;
    [Min(0)] public float weaponLiftDuration = .12f;
    [Min(0)] public float weaponHoverDuration = .18f;
    [Min(.02f)] public float weaponFlightDuration = .10f;
    public Part Target { get; private set; }
    public Part ActivePart { get; private set; }
    public PartCategory? Destination { get; private set; }
    public EquipManager Equipment { get; private set; }
    public bool IsBusy => Destination.HasValue;
    public RetrievalPhase Phase { get; private set; }
    public bool HasDeparted => IsBusy && departed;
    public Vector3 ActivePickupPoint => ActivePart != null ? ActivePart.transform.TransformPoint(pickupProbe) : transform.position;
    public static bool IsWeapon(Part part) => part != null && (part.partType == PartType.Arm || part.partType == PartType.Shoulder);
    // Combat extension points; movement never applies damage itself.
    public event Action<Part, PartCategory> Started, Arrived, Cancelled;
    public event Action<Part, PartCategory> Departed;
    // Accepted physical travel segment, after the terrain check and before final equipment.
    public event Action<Part, Vector3, Vector3> Travelled;
    readonly HashSet<Part> candidates = new HashSet<Part>();
    Collider[] overlaps = new Collider[128];
    RaycastHit[] hits = new RaycastHit[32];
    MechPlayerRig rig;
    CombatHealth health;
    Rigidbody body;
    Part destinationOccupant;
    Collider[] travellingColliders;
    bool[] colliderStates;
    bool wasKinematic, usedGravity, detectedCollisions;
    Vector3 velocity, angularVelocity, origin, originScale, launchOrigin, pickupProbe;
    Quaternion originRotation;
    float age;
    bool weapon, departed;
    int pickupMask;

    void Awake()
    {
        Equipment = GetComponent<EquipManager>(); rig = GetComponent<MechPlayerRig>(); health = GetComponent<CombatHealth>();
        int layer = LayerMask.NameToLayer(Part.LoosePhysicsLayerName);
        pickupMask = layer >= 0 ? 1 << layer : Physics.DefaultRaycastLayers;
    }
    bool OwnerValid => Equipment != null && Equipment.isActiveAndEnabled
        && (health == null || !health.IsDead) && !MissionOutcomeController.BlocksGameplay(gameObject.scene);
    public bool InRange(Part part) => part != null && Vector3.SqrMagnitude(part.PickupPoint - transform.position) <= pickupRange * pickupRange;
    Vector3 PickupAnchor(Part part) => part.PickupPoint + Vector3.up * (obstructionRadius + .02f);
    public bool CanStart(Part part)
    {
        if (!isActiveAndEnabled || !OwnerValid || IsBusy || part == null || part.IsRetrieving || !InRange(part)
            || part.gameObject.scene != gameObject.scene || !Equipment.PreferredSlotFor(part).HasValue) return false;
        return !Blocked(transform.position + Vector3.up * .5f, PickupAnchor(part), part);
    }
    void Update()
    {
        if (ActivePart != null && !OwnerValid) Cancel();
        RefreshTarget();
    }
    public void RefreshTarget()
    {
        Target = null;
        if (!enableRemoteTargeting || !OwnerValid || IsBusy) return;
        var camera = rig != null ? rig.GameplayCamera : Camera.main;
        if (camera == null || !camera.isActiveAndEnabled) return;
        float radius = pickupRange * Mathf.Max(1, rangePreviewMultiplier);
        int count;
        while ((count = Physics.OverlapSphereNonAlloc(transform.position, radius, overlaps, pickupMask, QueryTriggerInteraction.Collide)) == overlaps.Length)
            Array.Resize(ref overlaps, overlaps.Length * 2);
        candidates.Clear();
        float bestAim = float.PositiveInfinity, bestDistance = float.PositiveInfinity;
        long bestId = long.MaxValue;
        for (int i = 0; i < count; i++)
        {
            var collider = overlaps[i]; var part = collider.GetComponentInParent<Part>();
            if (part == null || part.Stats == null || part.isEquipped || part.IsRetrieving || !part.isActiveAndEnabled
                || part.gameObject.scene != gameObject.scene || !candidates.Add(part) || !Equipment.PreferredSlotFor(part).HasValue) continue;
            // Use the physical pickup bounds, not an imported rig's potentially underground pivot.
            Vector3 point = PickupAnchor(part);
            var screen = camera.WorldToViewportPoint(point);
            if (screen.z <= camera.nearClipPlane || screen.x < 0 || screen.x > 1 || screen.y < 0 || screen.y > 1) continue;
            float aspect = camera.aspect;
            float aim = new Vector2((screen.x - .5f) * aspect, screen.y - .5f).sqrMagnitude;
            float distance = (part.PickupPoint - transform.position).sqrMagnitude;
            if (aim > aimRadius * aimRadius || distance > radius * radius
                || Blocked(camera.transform.position, point, part)) continue;
            long id = part.GetEntityId().GetHashCode();
            if (aim > bestAim + .000001f || (Mathf.Abs(aim - bestAim) <= .000001f
                && (distance > bestDistance + .0001f || (Mathf.Abs(distance - bestDistance) <= .0001f && id >= bestId)))) continue;
            Target = part; bestAim = aim; bestDistance = distance; bestId = id;
        }
    }
    // Hordes do not block pickup rays. Solid world geometry does; never retrieve through a wall.
    bool Blocked(Vector3 from, Vector3 to, Part ignore)
    {
        Vector3 delta = to - from; float length = delta.magnitude;
        if (length < .001f) return false;
        int count;
        while ((count = Physics.SphereCastNonAlloc(from, obstructionRadius, delta / length, hits, length,
            Part.MovementQueryMask, QueryTriggerInteraction.Ignore)) == hits.Length) Array.Resize(ref hits, hits.Length * 2);
        for (int i = 0; i < count; i++)
        {
            var collider = hits[i].collider;
            if (collider == null || collider.transform.IsChildOf(transform) || collider.GetComponentInParent<Part>() != null
                || collider.GetComponentInParent<CombatHealth>() != null) continue;
            return true;
        }
        return false;
    }
    public bool TryStart(Part part)
    {
        Physics.SyncTransforms();
        if (Time.timeScale <= 0 || !CanStart(part) || CombatHitStop.BlocksNewActions(transform)) return false;
        var slot = Equipment.PreferredSlotFor(part);
        Vector3 point = PickupAnchor(part);
        if (!slot.HasValue || !Equipment.TryGetAttachmentPose(part, slot.Value, out _, out _) || !part.TryReserveRetrieval(this)) return false;
        ActivePart = part; Destination = slot; age = 0; origin = part.transform.position;
        pickupProbe = part.transform.InverseTransformPoint(point);
        weapon = IsWeapon(part); departed = false;
        Phase = weapon && weaponLiftDuration > 0 ? RetrievalPhase.Lifting : weapon && weaponHoverDuration > 0 ? RetrievalPhase.Hovering : RetrievalPhase.Flying;
        destinationOccupant = Equipment.GetEquipped(slot.Value);
        originRotation = part.transform.rotation; originScale = part.transform.lossyScale;
        body = part.GetComponent<Rigidbody>();
        if (body != null)
        {
            wasKinematic = body.isKinematic; usedGravity = body.useGravity; detectedCollisions = body.detectCollisions;
            velocity = body.linearVelocity; angularVelocity = body.angularVelocity;
            body.isKinematic = true; body.useGravity = false; body.detectCollisions = false;
        }
        var owned = new List<Collider>();
        foreach (var collider in part.GetComponentsInChildren<Collider>(true))
            if (collider.GetComponentInParent<Part>() == part) owned.Add(collider);
        travellingColliders = owned.ToArray(); colliderStates = new bool[owned.Count];
        for (int i = 0; i < owned.Count; i++) { colliderStates[i] = owned[i].enabled; owned[i].enabled = false; }
        Target = null; Started?.Invoke(part, slot.Value); return true;
    }
    void LateUpdate()
    {
        if (ActivePart == null) { if (Destination.HasValue) Cancel(); return; }
        var part = ActivePart;
        if (!OwnerValid || !part.isActiveAndEnabled || part.RetrievalOwner != this || !Destination.HasValue
            || part.gameObject.scene != gameObject.scene || Equipment.GetEquipped(Destination.Value) != destinationOccupant
            || !Equipment.TryGetAttachmentPose(part, Destination.Value, out var pose, out var scale)) { Cancel(); return; }
        if (Time.timeScale <= 0 || CombatHitStop.IsFrozen(transform)) return;
        age += Time.deltaTime;
        float lift = weapon ? Mathf.Max(0, weaponLiftDuration) : 0;
        float anticipation = lift + (weapon ? Mathf.Max(0, weaponHoverDuration) : 0);
        if (weapon && age < anticipation)
        {
            Phase = age < lift ? RetrievalPhase.Lifting : RetrievalPhase.Hovering;
            float progress = lift > 0 ? Mathf.SmoothStep(0, 1, age / lift) : 1;
            if (!MovePart(part, origin + Vector3.up * (weaponLiftHeight * progress), originRotation, originScale)) Cancel();
            return;
        }
        if (!departed)
        {
            launchOrigin = origin + Vector3.up * (weapon ? weaponLiftHeight : 0);
            if (!MovePart(part, launchOrigin, originRotation, originScale)) { Cancel(); return; }
            departed = true; Phase = RetrievalPhase.Flying;
            Departed?.Invoke(part, Destination.Value);
            if (ActivePart != part || !Destination.HasValue || CombatHitStop.IsFrozen(transform)) return;
        }
        float duration = weapon ? weaponFlightDuration : travelDuration;
        float t = Mathf.Clamp01((age - anticipation) / Mathf.Max(.02f, duration));
        // Accelerate out of the suspended pose; avoid a slow glide into the socket.
        float ease = weapon ? t * t : t * t * (3 - 2 * t);
        Vector3 position = Vector3.Lerp(launchOrigin, pose.position, ease) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * arcHeight);
        Vector3 previous = ActivePickupPoint;
        if (!MovePart(part, position, Quaternion.Slerp(originRotation, pose.rotation, ease), Vector3.Lerp(originScale, scale, ease))) { Cancel(); return; }
        // Damage follows the visible/collider anchor, not a potentially offset imported model pivot.
        Travelled?.Invoke(part, previous, ActivePickupPoint);
        // Damage listeners can finish the mission or disable/cancel an owner during this callback.
        if (ActivePart != part || !Destination.HasValue) return;
        if (t < 1) return;
        var slot = Destination.Value;
        // No callbacks between release and the equipment transaction: another owner cannot steal the item.
        RestorePhysics(); part.ReleaseRetrieval(this); ClearFlight();
        if (Equipment.TryEquip(part, slot)) Arrived?.Invoke(part, slot);
        else { RestoreScale(part); Cancelled?.Invoke(part, slot); }
    }
    bool MovePart(Part part, Vector3 position, Quaternion rotation, Vector3 size)
    {
        Vector3 fromProbe = part.transform.TransformPoint(pickupProbe);
        Vector3 toProbe = position + rotation * Vector3.Scale(size, pickupProbe);
        if (Blocked(fromProbe, toProbe, part)) return false;
        part.transform.SetPositionAndRotation(position, rotation);
        Vector3 parentScale = part.transform.parent != null ? part.transform.parent.lossyScale : Vector3.one;
        part.transform.localScale = new Vector3(size.x / parentScale.x, size.y / parentScale.y, size.z / parentScale.z);
        return true;
    }
    void RestorePhysics()
    {
        if (travellingColliders != null) for (int i = 0; i < travellingColliders.Length; i++)
            if (travellingColliders[i] != null) travellingColliders[i].enabled = colliderStates[i];
        if (body != null)
        {
            body.isKinematic = wasKinematic; body.useGravity = usedGravity; body.detectCollisions = detectedCollisions;
            if (!wasKinematic) { body.linearVelocity = velocity; body.angularVelocity = angularVelocity; }
        }
    }
    void RestoreScale(Part part)
    {
        var parent = part.transform.parent != null ? part.transform.parent.lossyScale : Vector3.one;
        part.transform.localScale = new Vector3(originScale.x / parent.x, originScale.y / parent.y, originScale.z / parent.z);
    }
    public void Cancel()
    {
        var part = ActivePart; var slot = Destination;
        RestorePhysics();
        if (part != null) { RestoreScale(part); part.ReleaseRetrieval(this); }
        ClearFlight();
        // Destruction can make Part compare null; still close the flight's lifecycle once.
        if (slot.HasValue) Cancelled?.Invoke(part, slot.Value);
    }
    void ClearFlight() { ActivePart = null; Destination = null; destinationOccupant = null; body = null; travellingColliders = null; colliderStates = null; Target = null; Phase = RetrievalPhase.None; departed = false; }
    void OnDisable() { Cancel(); Target = null; }
}
