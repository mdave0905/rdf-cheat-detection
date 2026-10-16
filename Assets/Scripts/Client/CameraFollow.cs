namespace Client
{
    using UnityEngine;

    public class CameraFollow : MonoBehaviour
    {
        public Transform target;
        public Vector3 localOffset = new Vector3(0f, 3f, -5f);
        public float followSpeed = 10f;
        public float rotationSpeed = 10f;
        public float lookAtHeightOffset = 1.5f;

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            // The offset is relative to the character, keeping the camera behind the
            // character as its facing direction changes.
            Vector3 desiredPosition = target.TransformPoint(localOffset);
            transform.position = Vector3.Lerp(transform.position, desiredPosition, followSpeed * Time.deltaTime);

            Vector3 lookPosition = target.position + Vector3.up * lookAtHeightOffset;
            Vector3 lookDirection = lookPosition - desiredPosition;

            if (lookDirection.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(lookDirection);
                transform.rotation =
                    Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            }
        }
    }
}
