using UnityEngine;

public class Part : MonoBehaviour
{
    // Loose Default-layer colliders keep normal gameplay physics, but do not
    // obstruct the follow camera when a replaced part drops from a socket.
    public const string LoosePhysicsLayerName = "MechPart";
    public Transform recoilVisual;
    [Tooltip("Direction the recoil visual kicks, in its local space.")]
    public Vector3 recoilAxis = Vector3.left;
    public Transform muzzle;
    public Vector3 authoredScale = Vector3.one;
    public PartData data;    
    // What this part is, read from its data (the single source of truth). Parts have no side:
    // which arm/shoulder socket it uses is decided when it is equipped.
    public PartType partType => data != null ? data.partType : default;
    // This part's stats. Systems read stats only through here, so per-part stat rolls can plug in later.
    public PartData Stats => data;
    // Slot this part currently occupies; null while it is loose.
    public PartCategory? Slot { get; internal set; }
    public bool isStartingPart = true;
    public bool isEquipped = false;
    public EquipManager EquippedBy { get; internal set; }
    public int RemainingAmmo { get; private set; }
    public float ReadyAt { get; private set; }
    bool initialized;
    Collider[] colliders;
    bool[] colliderStates;
    int[] colliderLayers;
    void Awake() { if (EquippedBy == null && !isEquipped) SetEquippedPhysics(false); }

    // State belongs to this physical part, not the shared PartData asset.
    internal void InitializeAmmo(int ammunition)
    {
        if (initialized) return;
        RemainingAmmo = ammunition; initialized = true;
    }
    internal void Consume(float cooldown, bool ammunition)
    {
        if (ammunition) RemainingAmmo = Mathf.Max(0, RemainingAmmo - 1);
        ReadyAt = Time.time + cooldown;
    }
    internal void SetEquippedPhysics(bool equipped)
    {
        if (colliders == null)
        {
            var own = new System.Collections.Generic.List<Collider>();
            foreach (var c in GetComponentsInChildren<Collider>(true))
                if (c.GetComponentInParent<Part>() == this) own.Add(c);
            colliders = own.ToArray(); colliderStates = new bool[colliders.Length];
            colliderLayers = new int[colliders.Length];
            for (int i = 0; i < colliders.Length; i++)
            {
                colliderStates[i] = colliders[i].enabled;
                colliderLayers[i] = colliders[i].gameObject.layer;
            }
        }
        int looseLayer = LayerMask.NameToLayer(LoosePhysicsLayerName);
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null)
            {
                // Preserve explicitly authored custom layers and restore the
                // original layer on attachment, including nested limb parts.
                colliders[i].gameObject.layer = !equipped && colliderLayers[i] == 0 && looseLayer >= 0
                    ? looseLayer : colliderLayers[i];
                colliders[i].enabled = !equipped && colliderStates[i];
            }
        var body = GetComponent<Rigidbody>();
        if (!equipped && body == null) body = gameObject.AddComponent<Rigidbody>();
        if (body != null) { body.isKinematic = equipped; body.useGravity = !equipped; body.detectCollisions = !equipped; }
        // Stop visual playback immediately on drop, including within the same frame.
        var weaponAnimator = GetComponent<MechWeaponAnimator>();
        if (weaponAnimator != null) weaponAnimator.RefreshEquipment();
    }
}
