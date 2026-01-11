using UnityEngine;

public class TransformRotater : MonoBehaviour
{
    [SerializeField] private Vector3 rotationSpeed = new Vector3(0f, 0f, 0f);

    private void Update()
    {
        transform.Rotate(rotationSpeed * Time.deltaTime);
    }
}
