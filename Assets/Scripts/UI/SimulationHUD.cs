using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Demo HUD. Everything the detector uses is measured on the server, comparing the
/// position the client claims with the server's own result for the same input
/// (same sequence number), so normal network delay does not appear as divergence.
/// The verdict comes from the decision tree trained in analysis/classify_sessions.py,
/// applied to the last 30 s of inputs (the length of a training session).
/// </summary>
public class SimulationHUD : MonoBehaviour
{
    public static SimulationHUD Instance { get; private set; }

    private static readonly float[] LatencySteps = { 50f, 150f, 300f };
    private static readonly float[] LossSteps = { 0f, 3f, 10f };
    private const float WindowSeconds = 30f;

    [Header("Visibility & Scaling")]
    public bool showHUD = true;

    [Tooltip("UI scale multiplier. 1.0 = standard 1080p, 1.5-2.2 = high-DPI / 4K. Automatically scales if screen resolution is high.")]
    [Range(0.8f, 3.5f)]
    public float uiScale = 1.5f;

    private float currentScale = -1f;

    private uint windowStartTick;
    private int uplinkSentAtReset, uplinkDroppedAtReset;
    private SessionFeatures features;
    private bool verdictCheating;
    private string verdictReason = "";
    private float nextFeatureUpdate;

    private GUIStyle cardStyle, headerStyle, labelStyle, valueStyle, badgeStyle, verdictStyle, noteStyle, barStyle;
    private Texture2D cardTex, barTex, whiteTex;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        HandleKeys();

        if (Time.unscaledTime >= nextFeatureUpdate && ServerAuthoritativeLoop.Instance != null)
        {
            nextFeatureUpdate = Time.unscaledTime + 0.25f;
            features = ServerAuthoritativeLoop.Instance.LiveStream.ComputeFeatures();
            verdictCheating = TrainedDetector.IsCheating(features, out verdictReason);
        }
    }

    private void HandleKeys()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        if (kb.hKey.wasPressedThisFrame) showHUD = !showHUD;

        NetworkExperimentSettings settings = NetworkExperimentSettings.GetOrDefault();
        if (settings == null) return;

        ExperimentPlayerType? selected = null;
        if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) selected = ExperimentPlayerType.Legitimate;
        if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) selected = ExperimentPlayerType.SpeedHack;
        if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) selected = ExperimentPlayerType.PositionHack;
        if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame) selected = ExperimentPlayerType.InputReplay;
        if (selected.HasValue)
        {
            settings.playerType = selected.Value;
            ResetWindow();
        }

        if (kb.lKey.wasPressedThisFrame)
        {
            settings.latencyMilliseconds = NextStep(LatencySteps, settings.latencyMilliseconds);
            ResetWindow();
        }

        if (kb.pKey.wasPressedThisFrame)
        {
            settings.packetLossPercent = NextStep(LossSteps, settings.packetLossPercent);
            ResetWindow();
        }

        if (kb.mKey.wasPressedThisFrame) settings.enableSyntheticMovement = !settings.enableSyntheticMovement;
        if (kb.rKey.wasPressedThisFrame) ResetWindow();
    }

    private static float NextStep(float[] steps, float current)
    {
        for (int i = 0; i < steps.Length; i++)
        {
            if (current < steps[i] - 0.001f) return steps[i];
            if (Mathf.Abs(current - steps[i]) < 0.001f) return steps[(i + 1) % steps.Length];
        }
        return steps[0];
    }

    /// <summary>Start a fresh 30 s window so the numbers describe only the current behaviour and network.</summary>
    public void ResetWindow()
    {
        windowStartTick = SimulationClock.Tick;
        if (ServerAuthoritativeLoop.Instance != null) ServerAuthoritativeLoop.Instance.LiveStream.Clear();

        ClientPrediction client = ClientPrediction.LocalPlayer;
        if (client != null)
        {
            client.ResetCounters();
            if (client.Link != null)
            {
                uplinkSentAtReset = client.Link.DatagramsSent;
                uplinkDroppedAtReset = client.Link.DatagramsDropped;
            }
        }

        features = default;
        verdictCheating = false;
        verdictReason = "";
        nextFeatureUpdate = 0f;
    }

    private void OnGUI()
    {
        if (!showHUD) return;

        // Auto-scale for high-resolution displays (e.g. 1440p / 4K) while respecting uiScale setting
        float s = uiScale;
        if (Screen.height > 1200)
        {
            s = Mathf.Max(uiScale, (Screen.height / 1080f) * 1.1f);
        }
        InitStyles(s);

        NetworkExperimentSettings settings = NetworkExperimentSettings.GetOrDefault();
        ClientPrediction client = ClientPrediction.LocalPlayer;
        ServerAuthoritativeLoop server = ServerAuthoritativeLoop.Instance;
        if (settings == null || client == null || server == null || client.Link == null) return;

        float windowElapsed = Mathf.Min(WindowSeconds, (SimulationClock.Tick - windowStartTick) * GameConstants.FixedDeltaTime);
        bool windowFull = windowElapsed >= WindowSeconds - 0.001f;

        float margin = 16f * s;
        float spacing = 4f * s;

        // Left card: what is being simulated.
        float leftWidth = 350f * s;
        float leftHeight = 230f * s;
        float badgeHeight = 28f * s;

        GUILayout.BeginArea(new Rect(margin, margin, leftWidth, leftHeight), cardStyle);
        GUILayout.Label("SESSION", headerStyle);
        GUILayout.Space(spacing);

        GUI.backgroundColor = BehaviourColour(settings.playerType);
        string behaviour = BehaviourName(settings.playerType);
        if (settings.playerType == ExperimentPlayerType.InputReplay)
        {
            behaviour += client.IsReplayingInput ? "  (replaying now)" : "  (recording / idle)";
        }
        GUILayout.Box(behaviour, badgeStyle, GUILayout.Height(badgeHeight));
        GUI.backgroundColor = Color.white;
        GUILayout.Space(spacing);

        int sent = client.Link.DatagramsSent - uplinkSentAtReset;
        int dropped = client.Link.DatagramsDropped - uplinkDroppedAtReset;
        float effectiveDelayMs = client.Link.DelayTicks * GameConstants.FixedDeltaTime * 1000f;

        DrawRow("Movement", ThirdPersonInput.ShouldUseSyntheticInput() ? "bot (seeded path)" : "keyboard (WASD, Shift)");
        DrawRow("One-way delay", $"{settings.latencyMilliseconds:F0} ms set, {effectiveDelayMs:F0} ms applied");
        DrawRow("Packet loss (each way)", $"{settings.packetLossPercent:F0} % set");
        DrawRow("Inputs lost since reset", sent > 0 ? $"{dropped} of {sent} ({100f * dropped / sent:F1} %)" : "-");
        DrawRow("Simulation time", $"{SimulationClock.Time:F1} s (tick {SimulationClock.Tick})");
        GUILayout.EndArea();

        // Right card: the server-side check and the trained model's verdict.
        float rightWidth = 420f * s;
        float rightHeight = 310f * s;
        float verdictHeight = 34f * s;

        GUILayout.BeginArea(new Rect(Screen.width - rightWidth - margin, margin, rightWidth, rightHeight), cardStyle);
        GUILayout.Label("SERVER-SIDE CHECK  (claimed vs. server, same input)", headerStyle);
        GUILayout.Space(spacing);

        DrawRow("Latest input: divergence", $"{server.LiveStream.LatestMagnitude:F3} m");
        DrawRow("Inputs with divergence > 0.01 m", $"{features.CorrectionCount}  ({features.CorrectionsPerSecond:F2} /s)");
        DrawRow("Largest divergence", $"{features.MaxMagnitude:F2} m");
        DrawRow("Direction consistency R", features.CorrectionCount > 0 ? $"{features.DirectionConsistency:F2}" : "-");
        DrawRow("Client snap-backs (reconciliations)", $"{client.TotalCorrections}");
        DrawRow("Window", $"{windowElapsed:F0} / {WindowSeconds:F0} s");
        GUILayout.Space(6f * s);

        string verdict;
        Color verdictColour;
        if (!TrainedDetector.IsTrained)
        {
            verdict = "NO MODEL LOADED";
            verdictColour = new Color(0.5f, 0.5f, 0.5f);
        }
        else if (!windowFull)
        {
            verdict = verdictCheating ? "FLAGGED (window not full yet)" : $"COLLECTING ({windowElapsed:F0} / {WindowSeconds:F0} s)";
            verdictColour = verdictCheating ? new Color(0.85f, 0.35f, 0.25f) : new Color(0.45f, 0.5f, 0.58f);
        }
        else
        {
            verdict = verdictCheating ? "FLAGGED AS CHEATING" : "NO CHEAT DETECTED";
            verdictColour = verdictCheating ? new Color(0.85f, 0.25f, 0.25f) : new Color(0.15f, 0.65f, 0.4f);
        }

        GUI.backgroundColor = verdictColour;
        GUILayout.Box(verdict, verdictStyle, GUILayout.Height(verdictHeight));
        GUI.backgroundColor = Color.white;
        GUILayout.Label(verdictCheating ? $"Rule: {verdictReason}" : TrainedDetector.Description, noteStyle);
        GUILayout.EndArea();

        // Bottom bar: Controls
        float barWidth = Mathf.Min(Screen.width - 32f * s, 1020f * s);
        float barHeight = 42f * s;
        GUILayout.BeginArea(new Rect((Screen.width - barWidth) / 2f, Screen.height - barHeight - 14f * s, barWidth, barHeight), barStyle);
        GUILayout.Label(
            "<b>[1]</b> Honest  <b>[2]</b> Speed hack  <b>[3]</b> Position hack  <b>[4]</b> Input replay   |   " +
            "<b>[L]</b> Delay  <b>[P]</b> Loss  <b>[M]</b> Bot/Keyboard  <b>[R]</b> Reset window  <b>[G]</b> Ghost  <b>[H]</b> Hide",
            labelStyle);
        GUILayout.EndArea();
    }

    private static string BehaviourName(ExperimentPlayerType type)
    {
        switch (type)
        {
            case ExperimentPlayerType.SpeedHack: return "SPEED HACK (2x speed)";
            case ExperimentPlayerType.PositionHack: return "POSITION HACK (+5,+5 m every 2 s)";
            case ExperimentPlayerType.InputReplay: return "INPUT REPLAY";
            default: return "HONEST PLAYER";
        }
    }

    private static Color BehaviourColour(ExperimentPlayerType type)
    {
        switch (type)
        {
            case ExperimentPlayerType.SpeedHack: return new Color(0.85f, 0.45f, 0.25f);
            case ExperimentPlayerType.PositionHack: return new Color(0.85f, 0.6f, 0.2f);
            case ExperimentPlayerType.InputReplay: return new Color(0.6f, 0.4f, 0.85f);
            default: return new Color(0.3f, 0.55f, 0.8f);
        }
    }

    private void DrawRow(string label, string value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, labelStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label(value, valueStyle);
        GUILayout.EndHorizontal();
    }

    private void InitStyles(float s)
    {
        if (cardStyle != null && Mathf.Approximately(currentScale, s)) return;
        currentScale = s;

        if (cardTex == null) cardTex = MakeTex(new Color(0.06f, 0.09f, 0.14f, 0.9f));
        if (barTex == null) barTex = MakeTex(new Color(0.04f, 0.05f, 0.08f, 0.85f));
        if (whiteTex == null) whiteTex = MakeTex(Color.white);

        int padX = Mathf.RoundToInt(14f * s);
        int padY = Mathf.RoundToInt(12f * s);
        cardStyle = new GUIStyle { normal = { background = cardTex }, padding = new RectOffset(padX, padX, padY, padY) };
        barStyle = new GUIStyle { normal = { background = barTex }, padding = new RectOffset(Mathf.RoundToInt(12f * s), Mathf.RoundToInt(12f * s), Mathf.RoundToInt(8f * s), Mathf.RoundToInt(8f * s)), alignment = TextAnchor.MiddleCenter };

        headerStyle = new GUIStyle { fontSize = Mathf.RoundToInt(12f * s), fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.45f, 0.65f, 0.95f) } };
        labelStyle = new GUIStyle { fontSize = Mathf.RoundToInt(12f * s), richText = true, normal = { textColor = new Color(0.72f, 0.78f, 0.85f) } };
        valueStyle = new GUIStyle { fontSize = Mathf.RoundToInt(12f * s), fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
        noteStyle = new GUIStyle { fontSize = Mathf.RoundToInt(10.5f * s), wordWrap = true, normal = { textColor = new Color(0.6f, 0.66f, 0.74f) } };
        badgeStyle = new GUIStyle
        {
            normal = { background = whiteTex, textColor = Color.white },
            fontSize = Mathf.RoundToInt(12f * s),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        verdictStyle = new GUIStyle(badgeStyle) { fontSize = Mathf.RoundToInt(15f * s) };
    }

    private static Texture2D MakeTex(Color colour)
    {
        Texture2D texture = new Texture2D(2, 2);
        texture.SetPixels(new[] { colour, colour, colour, colour });
        texture.Apply();
        return texture;
    }
}
