using UnityEngine;
using UnityEngine.InputSystem;

public static class ThirdPersonInput
{
    public static bool ShouldUseSyntheticInput()
    {
        if (AutomatedDataCollector.Instance != null && AutomatedDataCollector.Instance.IsBatchRunning)
        {
            return true;
        }

        NetworkExperimentSettings settings = NetworkExperimentSettings.GetOrDefault();
        if (settings != null && settings.enableSyntheticMovement)
        {
            return true;
        }

        return false;
    }

    public static bool IsSprinting()
    {
        if (ShouldUseSyntheticInput())
        {
            return GetSyntheticSprinting();
        }

        return Keyboard.current != null &&
               Keyboard.current.leftShiftKey.isPressed;
    }

    public static Vector2 ReadRawMovement()
    {
        if (Keyboard.current == null)
        {
            return Vector2.zero;
        }

        float x = 0f;
        float y = 0f;

        if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) x -= 1f;
        if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) x += 1f;
        if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) y -= 1f;
        if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) y += 1f;

        return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
    }

    public static Vector3 ReadWorldMovement(Transform cameraTransform)
    {
        if (ShouldUseSyntheticInput())
        {
            // If manual keys are actively pressed outside batch runs, allow manual override for testing
            if (!(AutomatedDataCollector.Instance != null && AutomatedDataCollector.Instance.IsBatchRunning))
            {
                Vector2 raw = ReadRawMovement();
                if (raw.sqrMagnitude > 0.001f)
                {
                    return ConvertRawToWorld(raw, cameraTransform);
                }
            }

            return GetSyntheticWorldMovement();
        }

        Vector2 input = ReadRawMovement();
        if (input == Vector2.zero)
        {
            return Vector3.zero;
        }

        return ConvertRawToWorld(input, cameraTransform);
    }

    private static Vector3 ConvertRawToWorld(Vector2 input, Transform cameraTransform)
    {
        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        if (cameraTransform == null)
        {
            return new Vector3(input.x, 0f, input.y);
        }

        Vector3 forward = cameraTransform.forward;
        Vector3 right = cameraTransform.right;
        forward.y = 0f;
        right.y = 0f;

        if (forward.sqrMagnitude < 0.001f || right.sqrMagnitude < 0.001f)
        {
            return new Vector3(input.x, 0f, input.y);
        }

        forward.Normalize();
        right.Normalize();
        return (right * input.x + forward * input.y).normalized;
    }

    private static Vector3 GetSyntheticWorldMovement()
    {
        NetworkExperimentSettings settings = NetworkExperimentSettings.GetOrDefault();
        float seedOffset = (settings != null ? settings.movementSeed : 12345) % 10000;
        float t = SimulationClock.Time;

        // Short pause (1 s in every 24 s) to mimic a player stopping briefly
        float pauseCycle = Mathf.Repeat(t * 0.5f + seedOffset * 0.1f, 12f);
        if (pauseCycle < 0.5f)
        {
            return Vector3.zero;
        }

        // Smooth heading angle derived from Perlin noise
        float noise = Mathf.PerlinNoise(t * 0.25f + seedOffset, seedOffset * 0.3f);
        float angle = noise * Mathf.PI * 4f;
        Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

        // Arena boundary avoidance
        Vector3 currentPos = ClientPrediction.LocalPlayer != null
            ? ClientPrediction.LocalPlayer.PredictedPosition
            : Vector3.zero;

        float boundX = GameConstants.ArenaBoundaryX - 3.5f;
        float boundZ = GameConstants.ArenaBoundaryZ - 3.5f;

        Vector3 avoidance = Vector3.zero;
        if (currentPos.x > boundX) avoidance.x -= (currentPos.x - boundX);
        else if (currentPos.x < -boundX) avoidance.x += (-boundX - currentPos.x);

        if (currentPos.z > boundZ) avoidance.z -= (currentPos.z - boundZ);
        else if (currentPos.z < -boundZ) avoidance.z += (-boundZ - currentPos.z);

        if (avoidance.sqrMagnitude > 0.01f)
        {
            float weight = Mathf.Clamp01(avoidance.magnitude / 2.0f);
            direction = Vector3.Lerp(direction, avoidance.normalized, weight).normalized;
        }

        return direction.normalized;
    }

    private static bool GetSyntheticSprinting()
    {
        NetworkExperimentSettings settings = NetworkExperimentSettings.GetOrDefault();
        float seedOffset = (settings != null ? settings.movementSeed : 12345) % 10000;

        // Sprint for ~2.5 seconds every 7 seconds
        float sprintCycle = Mathf.Repeat(SimulationClock.Time + seedOffset * 0.2f, 7f);
        return sprintCycle > 4.5f;
    }
}
