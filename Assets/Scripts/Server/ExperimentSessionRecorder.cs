using System;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// Appends one row per completed batch session to experiment_sessions.csv. The row
/// records the configuration and what actually happened on the emulated links, so the
/// analysis can check every log file (row count, measured loss, late deliveries).
/// </summary>
public class ExperimentSessionRecorder : MonoBehaviour
{
    public const string ManifestFileName = "experiment_sessions.csv";

    private const string Header =
        "SessionId,PlayerType,LatencyMs,EffectiveOneWayDelayMs,PacketLossPercent,RandomSeed,MovementSeed," +
        "SessionTicks,InputsSent,InputsDropped,StatesSent,StatesDropped,LateDeliveries," +
        "RowsLogged,ClientCorrections,TimeMode,CompletedUtc," +
        "LiveCorrectionsPerSecond,LiveMeanMagnitude,LiveStdMagnitude,LiveMaxMagnitude,LiveP95Magnitude," +
        "LiveLargeCorrectionRatio,LiveMeanCorrectionMagnitude,LiveMeanCorrectionInterval,LiveDirectionConsistency," +
        "LiveVerdict";

    public void RecordCompletedSession(
        NetworkExperimentSettings settings,
        ClientPrediction client,
        ServerAuthoritativeLoop server,
        string timeMode)
    {
        if (settings == null || client == null || server == null) return;

        string path = Path.Combine(ExperimentPaths.DatasetDirectory, ManifestFileName);
        if (!File.Exists(path)) File.WriteAllText(path, Header + "\n");

        // Features as computed in C# by the live detector, so the analysis can check
        // that they match the Python features and the trained model's verdict.
        SessionFeatures live = server.LiveStream.ComputeFeatures();
        bool verdict = TrainedDetector.IsCheating(live, out _);

        CultureInfo c = CultureInfo.InvariantCulture;
        NetworkSimulator up = client.Link;
        NetworkSimulator down = server.Link;
        string row = string.Join(",", new[]
        {
            settings.sessionId,
            settings.playerType.ToString(),
            settings.latencyMilliseconds.ToString(c),
            (up.DelayTicks * GameConstants.FixedDeltaTime * 1000f).ToString("F1", c),
            settings.packetLossPercent.ToString(c),
            settings.randomSeed.ToString(c),
            settings.movementSeed.ToString(c),
            settings.SessionTicks.ToString(c),
            up.DatagramsSent.ToString(c),
            up.DatagramsDropped.ToString(c),
            down.DatagramsSent.ToString(c),
            down.DatagramsDropped.ToString(c),
            (up.LateDeliveries + down.LateDeliveries).ToString(c),
            server.RowsLogged.ToString(c),
            client.TotalCorrections.ToString(c),
            timeMode,
            DateTime.UtcNow.ToString("o", c),
            live.CorrectionsPerSecond.ToString("R", c),
            live.MeanMagnitude.ToString("R", c),
            live.StdMagnitude.ToString("R", c),
            live.MaxMagnitude.ToString("R", c),
            live.P95Magnitude.ToString("R", c),
            live.LargeCorrectionRatio.ToString("R", c),
            live.MeanCorrectionMagnitude.ToString("R", c),
            live.MeanCorrectionInterval.ToString("R", c),
            live.DirectionConsistency.ToString("R", c),
            verdict ? "1" : "0"
        });
        File.AppendAllText(path, row + "\n");
    }
}
