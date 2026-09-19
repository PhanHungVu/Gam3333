using UnityEngine;

public class EquipManager : MonoBehaviour
{
    public MechRulesConfig rulesConfig;
    public KeyCode interactKey = KeyCode.E;

    private Part nearbyPart;
    private Socket[] sockets;

    void Awake()
    {
        sockets = GetComponentsInChildren<Socket>();
    }

    void Start()
    {
        foreach (var part in FindObjectsByType<Part>(FindObjectsSortMode.None))
        {
            if (!part.isStartingPart) continue;

            foreach (var socket in sockets)
            {
                if (socket.socketType == part.partType && socket.GetComponentInChildren<Part>() == null)
                {
                    EquipToSocket(socket, part);
                    break;
                }
            }
        }
    }

    public void SetNearbyPart(Part part) => nearbyPart = part;
    public void ClearNearbyPart(Part part)
    {
        if (nearbyPart == part) nearbyPart = null;
    }

    void Update()
    {
        if (nearbyPart != null && Input.GetKeyDown(interactKey))
            TryEquip(nearbyPart);
    }

    void TryEquip(Part part)
    {
        Socket target = FindSocketForType(part.partType);
        if (target == null || !rulesConfig.CanEquip(target, part)) return;

        Part current = target.GetComponentInChildren<Part>();
        if (current != null)
        {
            current.transform.SetParent(null);
            current.isEquipped = false;
            EnableDropPhysics(current);
        }

        EquipToSocket(target, part);
        nearbyPart = null;
    }

    void EquipToSocket(Socket target, Part part)
    {
        DisableDropPhysics(part);
        part.transform.position = target.transform.position;
        part.transform.rotation = target.transform.rotation;
        part.transform.SetParent(target.transform);
        part.isEquipped = true;
    }

    void EnableDropPhysics(Part part)
    {
        if (part.GetComponent<Rigidbody>() == null)
        {
            var rb = part.gameObject.AddComponent<Rigidbody>();
            rb.useGravity = true;
        }
    }

    void DisableDropPhysics(Part part)
    {
        var rb = part.GetComponent<Rigidbody>();
        if (rb != null) Destroy(rb);
    }

    // EquipManager.cs — the FindSocketForType signature
    Socket FindSocketForType(PartCategory type)
    {
        foreach (var s in sockets)
            if (s.socketType == type) return s;
        return null;
    }
}