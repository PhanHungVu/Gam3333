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
                if (socket.socketType == part.compatibleSocket && socket.GetComponentInChildren<Part>() == null)
                {
                    EquipToSocket(socket, part);
                    break;
                }
            }
        }
    }

    public void SetNearbyPart(Part part)
    {
        nearbyPart = part;
        Debug.Log($"Nearby part set: {part.name}");
    }

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
        Socket target = FindSocketForType(part.compatibleSocket);
        Debug.Log($"Target socket for {part.name} ({part.compatibleSocket}) is {target?.name}, CanEquip: {(target != null ? rulesConfig.CanEquip(target, part) : false)}");
        if (target == null || !rulesConfig.CanEquip(target, part)) return;

        Part current = target.GetComponentInChildren<Part>();
        if (current != null)
        {
            current.transform.SetParent(null);
            var currentPickup = current.GetComponent<PartPickup>();
            if (currentPickup != null) currentPickup.enabled = true;
        }

        EquipToSocket(target, part);
        nearbyPart = null;
    }

    void EquipToSocket(Socket target, Part part)
    {
        Vector3 offset = target.transform.position - part.connector.position;
        part.transform.position += offset;
        part.transform.rotation = target.transform.rotation;
        part.transform.SetParent(target.transform);

        var pickup = part.GetComponent<PartPickup>();
        if (pickup != null) pickup.enabled = false;
    }

    Socket FindSocketForType(SocketType type)
    {
        foreach (var s in sockets)
            if (s.socketType == type) return s;
        return null;
    }
}