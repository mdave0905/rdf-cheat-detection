using UnityEngine;

public enum ExperimentPlayerType
{
    Legitimate,
    SpeedHack,
    PositionHack,
    InputReplay
}

public class NetworkExperimentSettings : MonoBehaviour
{
    public static NetworkExperimentSettings Instance { get; private set; }

    [Header("Network profile (applied live, on the sending side)")]
    [Tooltip("Fixed one-way delay applied to every datagram in both directions. " +
             "Rounded up to whole 30 Hz ticks (50 ms -> 66.7 ms, 150 ms -> 166.7 ms, 300 ms -> 300 ms).")]
    [Min(0f)] public float latencyMilliseconds = 50f;
    [Tooltip("Probability that a datagram is dropped, applied once per datagram in each direction.")]
    [Range(0f, 100f)] public float packetLossPercent = 0f;

    [Header("Experiment identity")]
    public string sessionId = "manual";
    [Tooltip("Seed for the packet-loss random number generators.")]
    public int randomSeed = 12345;
    [Tooltip("Seed for the synthetic (bot) movement path.")]
    public int movementSeed = 12345;
    public ExperimentPlayerType playerType = ExperimentPlayerType.Legitimate;

    [Header("Cheat parameters")]
    [Min(1f)] public float speedHackMultiplier = 2f;
    [Min(0.1f)] public float positionHackIntervalSeconds = 2f;
    public Vector3 positionHackOffset = new Vector3(5f, 0f, 5f);
    [Tooltip("Inputs and positions are recorded for this long after the cheat is activated.")]
    [Min(0.1f)] public float inputReplayRecordSeconds = 3f;
    [Tooltip("Replay starts this long after the cheat is activated.")]
    [Min(0.1f)] public float inputReplayStartSeconds = 5f;
    [Min(0.1f)] public float inputReplayDurationSeconds = 3f;

    [Header("Session")]
    [Min(1f)] public float sessionDurationSeconds = 30f;

    [Header("Automation")]
    [Tooltip("Drive the player with the synthetic bot instead of the keyboard.")]
    public bool enableSyntheticMovement = false;

    public float LatencySeconds => latencyMilliseconds / 1000f;
    public float PacketLossProbability => packetLossPercent / 100f;
    public uint SessionTicks => (uint)Mathf.RoundToInt(sessionDurationSeconds / GameConstants.FixedDeltaTime);

    private void Awake()
    {
        // One settings object per scene; a newly loaded scene always takes over.
        Instance = this;

        // Batch overrides must be applied before any other component reads the settings.
        if (AutomatedDataCollector.Instance != null && AutomatedDataCollector.Instance.IsBatchRunning)
        {
            AutomatedDataCollector.Instance.ApplyOverrides(this);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public static NetworkExperimentSettings GetOrDefault()
    {
        if (Instance != null) return Instance;

        NetworkExperimentSettings settings = FindAnyObjectByType<NetworkExperimentSettings>();
        if (settings != null)
        {
            Instance = settings;
            return settings;
        }

        // Self-heal: ensure interactive and demo runs always have valid settings and never break key handling or HUD
        GameObject host = GameObject.Find("NetworkExperimentSettings") ?? new GameObject("NetworkExperimentSettings");
        settings = host.AddComponent<NetworkExperimentSettings>();
        Instance = settings;
        return settings;
    }
}
