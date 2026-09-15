using UnityEngine;

public class SimpleMove : MonoBehaviour
{
    public float speed = 4f;
    private Rigidbody rb;

    void Awake() => rb = GetComponent<Rigidbody>();

    void FixedUpdate()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        Vector3 move = new Vector3(h, 0, v) * speed;
        rb.MovePosition(rb.position + move * Time.fixedDeltaTime);
    }
}