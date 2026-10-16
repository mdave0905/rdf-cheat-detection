using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Runs the experiment grid (player type x latency x packet loss x runs). Each session
/// gets a fresh scene, is stepped tick by tick by SimulationDriver (no Time.timeScale),
/// and ends after exactly SessionTicks inputs plus a short drain for in-flight packets.
///
/// Start it from a player build:
///   ThesisPrototype.exe -batchmode -nographics -collect [-runs 200] [-runstart 0]
///       [-players Legitimate,SpeedHack] [-latencies 50,150] [-losses 0,3,10]
///       [-outdir C:\path\dataset] [-realtime]
/// or in the editor by adding this component to a scene object with autoStartOnPlay.
/// </summary>
public class AutomatedDataCollector : MonoBehaviour
{
    public static AutomatedDataCollector Instance { get; private set; }

    public static readonly ExperimentPlayerType[] AllPlayers =
    {
        ExperimentPlayerType.Legitimate,
        ExperimentPlayerType.SpeedHack,
        ExperimentPlayerType.PositionHack,
        ExperimentPlayerType.InputReplay
    };
    public static readonly float[] AllLatencies = { 50f, 150f, 300f };
    public static readonly float[] AllLosses = { 0f, 3f, 10f };

    private struct Session
    {
        public ExperimentPlayerType playerType;
        public float latencyMs;
        public float lossPercent;
        public int run;
        public int gridIndex;
    }

    [Header("Experiment configuration")]
    public int runsPerCondition = 200;
    public int firstRun = 0;
    [Tooltip("Follow the wall clock instead of fast-forwarding (only useful for checking that both give identical logs).")]
    public bool realTime = false;
    [Tooltip("Start collecting as soon as Play mode is entered (editor use).")]
    public bool autoStartOnPlay = false;

    public bool IsBatchRunning { get; private set; }
    public bool UseRealTime => realTime;

    private readonly List<Session> queue = new List<Session>();
    private int current;
    private float batchStartTime;

    public static void CreateFromCommandLine()
    {
        if (Instance != null) return;
        GameObject go = new GameObject("AutomatedDataCollector");
        AutomatedDataCollector collector = go.AddComponent<AutomatedDataCollector>();
        collector.ReadCommandLine();
        collector.StartBatch();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (autoStartOnPlay && !IsBatchRunning) StartBatch();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void ReadCommandLine()
    {
        string runs = CommandLine.GetValue("-runs");
        if (runs != null) runsPerCondition = int.Parse(runs, CultureInfo.InvariantCulture);
        string runStart = CommandLine.GetValue("-runstart");
        if (runStart != null) firstRun = int.Parse(runStart, CultureInfo.InvariantCulture);
        realTime = CommandLine.HasFlag("-realtime");
    }

    private static List<T> Filter<T>(T[] all, string flag, Func<string, T> parse)
    {
        string value = CommandLine.GetValue(flag);
        if (string.IsNullOrEmpty(value)) return new List<T>(all);
        List<T> selected = new List<T>();
        foreach (string part in value.Split(','))
        {
            T item = parse(part.Trim());
            if (Array.IndexOf(all, item) < 0) throw new ArgumentException($"{flag}: '{part}' is not part of the experiment grid");
            selected.Add(item);
        }
        return selected;
    }

    private void BuildQueue()
    {
        queue.Clear();
        List<ExperimentPlayerType> players = Filter(AllPlayers, "-players",
            s => (ExperimentPlayerType)Enum.Parse(typeof(ExperimentPlayerType), s, true));
        List<float> latencies = Filter(AllLatencies, "-latencies", s => float.Parse(s, CultureInfo.InvariantCulture));
        List<float> losses = Filter(AllLosses, "-losses", s => float.Parse(s, CultureInfo.InvariantCulture));

        foreach (ExperimentPlayerType player in players)
        foreach (float latency in latencies)
        foreach (float loss in losses)
        for (int run = firstRun; run < firstRun + runsPerCondition; run++)
        {
            // The grid index comes from the full grid, so seeds do not depend on filters.
            int gridIndex = Array.IndexOf(AllPlayers, player) * 9
                            + Array.IndexOf(AllLatencies, latency) * 3
                            + Array.IndexOf(AllLosses, loss);
            queue.Add(new Session
            {
                playerType = player,
                latencyMs = latency,
                lossPercent = loss,
                run = run,
                gridIndex = gridIndex
            });
        }
    }

    [ContextMenu("Start Batch Data Collection")]
    public void StartBatch()
    {
        if (!Application.isPlaying || IsBatchRunning) return;

        BuildQueue();
        if (queue.Count == 0)
        {
            Debug.LogError("[Collector] Nothing to run.");
            Finish();
            return;
        }

        current = 0;
        IsBatchRunning = true;
        batchStartTime = Time.realtimeSinceStartup;
        Time.timeScale = 1f;
        Application.runInBackground = true;
        if (Application.isBatchMode) Application.targetFrameRate = -1;
        QualitySettings.vSyncCount = 0;

        Debug.Log($"[Collector] {queue.Count} sessions -> {ExperimentPaths.DatasetDirectory} " +
                  $"({(realTime ? "real-time" : "fast-forward")})");
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    /// <summary>Called by NetworkExperimentSettings.Awake before any other component reads the settings.</summary>
    public void ApplyOverrides(NetworkExperimentSettings target)
    {
        if (!IsBatchRunning || current >= queue.Count) return;

        Session s = queue[current];
        target.playerType = s.playerType;
        target.latencyMilliseconds = s.latencyMs;
        target.packetLossPercent = s.lossPercent;
        // Paired design: every condition of run r uses the same bot path.
        target.movementSeed = 1000 + s.run;
        target.randomSeed = 1000000 + s.gridIndex * 1000 + s.run;
        target.sessionId = string.Format(CultureInfo.InvariantCulture,
            "run_{0}_L{1}_P{2}_{3}", s.playerType, s.latencyMs, s.lossPercent, s.run);
        target.enableSyntheticMovement = true;
        target.sessionDurationSeconds = 30f;
    }

    /// <summary>Called by SimulationDriver once the session has ended and its files are closed.</summary>
    public void OnSessionCompleted()
    {
        current++;
        if (current % 100 == 0 || current == queue.Count)
        {
            float elapsed = Time.realtimeSinceStartup - batchStartTime;
            Debug.Log($"[Collector] {current}/{queue.Count} sessions done in {elapsed:F0} s");
        }

        if (current >= queue.Count)
        {
            Finish();
            return;
        }

        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void Finish()
    {
        IsBatchRunning = false;
        Debug.Log("[Collector] All sessions completed.");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
