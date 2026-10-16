using UnityEngine;

public class VisualRotator : MonoBehaviour
{
    public float rotationSpeed = 10f;
    public Transform cameraTransform;

    private void Start()
    {
        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }
    }

    private void Update()
    {
        Vector3 worldDirection = ThirdPersonInput.ReadWorldMovement(cameraTransform);
        if (worldDirection.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(worldDirection, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }
    }
}
