using UnityEngine;
using UnityEngine.Serialization;

public class Part : MonoBehaviour
{
    public Transform recoilVisual;
    public Transform muzzle;
    public Vector3 authoredScale = Vector3.one;
    public PartData data;    
    public PartCategory partType;
    public bool isStartingPart = true;
    public bool isEquipped = false;
    public EquipManager EquippedBy { get; internal set; }
    public int RemainingAmmo { get; private set; }
    public float ReadyAt { get; private set; }
    bool initialized;
    Collider[] colliders;
    bool[] colliderStates;
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
            for (int i = 0; i < colliders.Length; i++) colliderStates[i] = colliders[i].enabled;
        }
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null) colliders[i].enabled = !equipped && colliderStates[i];
        var body = GetComponent<Rigidbody>();
        if (!equipped && body == null) body = gameObject.AddComponent<Rigidbody>();
        if (body != null) { body.isKinematic = equipped; body.useGravity = !equipped; body.detectCollisions = !equipped; }
    }
}
