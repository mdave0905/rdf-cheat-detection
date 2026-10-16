using UnityEngine;

/// <summary>
/// Steps the whole simulation at a fixed 30 Hz. Each tick runs in a fixed order:
///   1. client applies server states that are due,
///   2. client predicts, applies the active cheat and sends its input,
///   3. server applies inputs that are due, logs them and replies.
/// In real-time mode, ticks follow the wall clock (for the demo). In fast-forward mode
/// (batch collection), many ticks run per rendered frame. Because nothing in the
/// simulation reads Unity's frame time, both modes produce the same session.
/// </summary>
[DefaultExecutionOrder(-100)]
public class SimulationDriver : MonoBehaviour
{
    public static SimulationDriver Instance { get; private set; }

    [Tooltip("Run ticks as fast as possible instead of following the wall clock.")]
    public bool fastForward;
    [Tooltip("Upper bound on ticks per frame in real-time mode (avoids a spiral after a hitch).")]
    public int maxRealtimeTicksPerFrame = 10;
    public int fastForwardTicksPerFrame = 5000;

    public bool SessionCompleted { get; private set; }

    private float accumulator;

    private void Awake()
    {
        Instance = this;
        SimulationClock.Reset();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private static bool StopsAtSessionEnd =>
        AutomatedDataCollector.Instance != null && AutomatedDataCollector.Instance.IsBatchRunning;

    private void Update()
    {
        ClientPrediction client = ClientPrediction.LocalPlayer;
        ServerAuthoritativeLoop server = ServerAuthoritativeLoop.Instance;
        if (SessionCompleted || client == null || !client.IsReady || server == null || !server.IsReady)
        {
            return;
        }

        int ticks;
        if (fastForward)
        {
            ticks = fastForwardTicksPerFrame;
        }
        else
        {
            accumulator += Time.deltaTime;
            ticks = Mathf.FloorToInt(accumulator / GameConstants.FixedDeltaTime);
            accumulator -= ticks * GameConstants.FixedDeltaTime;
            if (ticks > maxRealtimeTicksPerFrame)
            {
                ticks = maxRealtimeTicksPerFrame;
                accumulator = 0f;
            }
        }

        for (int i = 0; i < ticks && !SessionCompleted; i++)
        {
            Step(client, server);
        }
    }

    private void Step(ClientPrediction client, ServerAuthoritativeLoop server)
    {
        SimulationClock.Advance();

        NetworkExperimentSettings settings = NetworkExperimentSettings.GetOrDefault();
        uint sessionTicks = settings != null ? settings.SessionTicks : 900;
        bool stopsAtEnd = StopsAtSessionEnd;

        client.ReceiveServerStates();
        if (!stopsAtEnd || SimulationClock.Tick <= sessionTicks)
        {
            client.SimulateTick();
        }
        server.SimulateTick();

        // After the last input, keep ticking until everything in flight has arrived,
        // so the log holds every input that was not dropped.
        uint drainTicks = client.Link.DelayTicks + server.Link.DelayTicks + 2;
        if (stopsAtEnd && SimulationClock.Tick >= sessionTicks + drainTicks)
        {
            CompleteSession(settings, client, server);
        }
    }

    private void CompleteSession(NetworkExperimentSettings settings, ClientPrediction client, ServerAuthoritativeLoop server)
    {
        SessionCompleted = true;
        server.FinishSession();

        ExperimentSessionRecorder recorder = server.GetComponent<ExperimentSessionRecorder>();
        if (recorder == null) recorder = FindAnyObjectByType<ExperimentSessionRecorder>();
        if (recorder != null)
        {
            recorder.RecordCompletedSession(settings, client, server, fastForward ? "fast-forward" : "real-time");
        }

        AutomatedDataCollector.Instance.OnSessionCompleted();
    }
}
