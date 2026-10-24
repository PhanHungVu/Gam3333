using UnityEngine;

public class AutoMoveTarget : MonoBehaviour
{
    [SerializeField] float leftSpeed = 2f;
    [SerializeField] bool useWorldLeft = true;

    void Update()
    {
        Vector3 direction = useWorldLeft ? Vector3.left : -transform.right;
        transform.position += direction * leftSpeed * Time.deltaTime;
    }
}