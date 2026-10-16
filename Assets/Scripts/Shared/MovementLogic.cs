using UnityEngine;

public static class MovementLogic
{
    public static Vector3 Move(Vector3 currentPosition, float inputX, float inputY, float speed, float deltaTime)
    {
        Vector3 inputDirection = new Vector3(inputX, 0f, inputY);
        if (inputDirection.sqrMagnitude > 1f)
        {
            inputDirection.Normalize();
        }

        Vector3 displacement = inputDirection * speed * deltaTime;
        Vector3 newPosition = currentPosition + displacement;

        newPosition.x = Mathf.Clamp(newPosition.x, -GameConstants.ArenaBoundaryX, GameConstants.ArenaBoundaryX);
        newPosition.z = Mathf.Clamp(newPosition.z, -GameConstants.ArenaBoundaryZ, GameConstants.ArenaBoundaryZ);
        newPosition.y = 0f;

        return newPosition;
    }
}
