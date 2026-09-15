using UnityEngine;

public class Part : MonoBehaviour
{
    public SocketType compatibleSocket;
    public bool isStartingPart = true;
    public bool isEquipped = false;

    public void EnableDropPhysics()
    {
        if (GetComponent<Rigidbody>() == null)
        {
            var rb = gameObject.AddComponent<Rigidbody>();
            rb.useGravity = true;
        }
    }

    public void DisableDropPhysics()
    {
        var rb = GetComponent<Rigidbody>();
        if (rb != null) Destroy(rb);
    }
}