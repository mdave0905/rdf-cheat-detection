using UnityEngine;

public class CharacterAnimation : MonoBehaviour
{
    private Animator animator;
    private static readonly int Speed = Animator.StringToHash("Speed");
    private static readonly int MotionSpeed = Animator.StringToHash("MotionSpeed");

    private void Awake()
    {
        animator = GetComponent<Animator>();
    }

    private void Update()
    {
        if (animator == null || !animator.isActiveAndEnabled)
            return;

        Vector3 movement = ThirdPersonInput.ReadWorldMovement(Camera.main?.transform);

        bool isMoving = movement.sqrMagnitude > 0.001f;
        bool isSprinting = isMoving && ThirdPersonInput.IsSprinting();
        float speed = !isMoving ? 0f : isSprinting ? 6f : 2f;
        float motionSpeed = !isMoving ? 0f : isSprinting ? 1.15f : 1f;

        animator.SetFloat(
            Speed,
            speed,
            0.1f,
            Time.deltaTime
        );

        animator.SetFloat(
            MotionSpeed,
            motionSpeed,
            0.1f,
            Time.deltaTime
        );
    }

    // Starter Assets clips contain footstep events. Keep the receiver available
    // so those events do not produce warnings while audio is out of scope.
    public void OnFootstep(AnimationEvent animationEvent)
    {
    }
}
