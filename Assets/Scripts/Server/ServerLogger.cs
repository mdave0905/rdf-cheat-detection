using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Writes one CSV row per input the server receives. Each session gets its own file,
/// created fresh (never appended to), so a re-run can never mix with an older run.
/// </summary>
public class ServerLogger : MonoBehaviour
{
    public const string Header =
        "SessionId,PlayerType,ConfiguredLatencyMs,ConfiguredPacketLossPercent," +
        "Timestamp,ServerTime,SequenceNumber,InputX,InputY,Sprinting," +
        "ClaimedPositionX,ClaimedPositionY,ClaimedPositionZ," +
        "AuthoritativePositionX,AuthoritativePositionY,AuthoritativePositionZ," +
        "CorrectionVectorX,CorrectionVectorY,CorrectionVectorZ,Magnitude," +
        "IsCorrection,MeasuredDelayMs";

    public string LogFilePath { get; private set; }

    private StreamWriter writer;
    private readonly StringBuilder line = new StringBuilder(256);

    private void Open(NetworkExperimentSettings settings)
    {
        bool batch = AutomatedDataCollector.Instance != null && AutomatedDataCollector.Instance.IsBatchRunning;
        string sessionId = settings != null ? settings.sessionId : "manual";
        string directory;
        if (batch)
        {
            directory = ExperimentPaths.DatasetDirectory;
        }
        else
        {
            // Interactive runs get a timestamped name so every Play press is its own file.
            directory = ExperimentPaths.ManualDirectory;
            sessionId = $"manual_{DateTime.Now:yyyyMMdd_HHmmss}";
        }

        LogFilePath = Path.Combine(directory, $"reconciliation_{ExperimentPaths.SanitizeFileName(sessionId)}.csv");
        writer = new StreamWriter(LogFilePath, false, new UTF8Encoding(false));
        writer.WriteLine(Header);
    }

    public void LogInput(
        InputPacket packet,
        Vector3 authoritativePosition,
        Vector3 correction,
        float measuredDelayMs,
        NetworkExperimentSettings settings)
    {
        if (writer == null) Open(settings);

        CultureInfo c = CultureInfo.InvariantCulture;
        float magnitude = correction.magnitude;
        line.Clear();
        line.Append(settings != null ? settings.sessionId : "manual").Append(',')
            .Append(settings != null ? settings.playerType.ToString() : "Legitimate").Append(',')
            .Append((settings != null ? settings.latencyMilliseconds : 50f).ToString(c)).Append(',')
            .Append((settings != null ? settings.packetLossPercent : 0f).ToString(c)).Append(',')
            .Append(packet.timestamp.ToString("R", c)).Append(',')
            .Append(SimulationClock.Time.ToString("R", c)).Append(',')
            .Append(packet.sequenceNumber.ToString(c)).Append(',')
            .Append(packet.inputX.ToString("R", c)).Append(',')
            .Append(packet.inputY.ToString("R", c)).Append(',')
            .Append(packet.sprinting ? '1' : '0').Append(',')
            .Append(packet.claimedPosition.x.ToString("R", c)).Append(',')
            .Append(packet.claimedPosition.y.ToString("R", c)).Append(',')
            .Append(packet.claimedPosition.z.ToString("R", c)).Append(',')
            .Append(authoritativePosition.x.ToString("R", c)).Append(',')
            .Append(authoritativePosition.y.ToString("R", c)).Append(',')
            .Append(authoritativePosition.z.ToString("R", c)).Append(',')
            .Append(correction.x.ToString("R", c)).Append(',')
            .Append(correction.y.ToString("R", c)).Append(',')
            .Append(correction.z.ToString("R", c)).Append(',')
            .Append(magnitude.ToString("R", c)).Append(',')
            .Append(magnitude > GameConstants.DivergenceThreshold ? '1' : '0').Append(',')
            .Append(measuredDelayMs.ToString("F1", c));
        writer.WriteLine(line.ToString());
    }

    public void Close()
    {
        if (writer == null) return;
        writer.Flush();
        writer.Dispose();
        writer = null;
    }

    private void OnDestroy()
    {
        Close();
    }
}
