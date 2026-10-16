using System.Net;
using UnityEngine;

/// <summary>
/// Authoritative server. Applies each received input at the legal speed only, logs the
/// client's claimed position against its own result for the same sequence number, and
/// sends the authoritative position back. The claimed position never affects gameplay.
/// </summary>
public class ServerAuthoritativeLoop : MonoBehaviour
{
    public static ServerAuthoritativeLoop Instance { get; private set; }

    public Vector3 AuthoritativePosition => authoritativePosition;
    public bool IsReady { get; private set; }
    public NetworkSimulator Link => serverSocket;
    public int RowsLogged { get; private set; }

    /// <summary>Correction stream since the last reset, used by the on-screen detector.</summary>
    public CorrectionStream LiveStream { get; } = new CorrectionStream { MaxSpanTicks = 900 };

    private NetworkSimulator serverSocket;
    private ServerLogger logger;
    private NetworkExperimentSettings settings;
    private Vector3 authoritativePosition = Vector3.zero;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        logger = GetComponent<ServerLogger>();
        settings = NetworkExperimentSettings.GetOrDefault();

        int seed = settings != null ? settings.randomSeed : 12345;
        serverSocket = new NetworkSimulator(unchecked(seed * 31 + 2));
        serverSocket.Bind("127.0.0.1", ClientPrediction.ServerPort);
        ApplyNetworkSettings();
        IsReady = true;
    }

    private void ApplyNetworkSettings()
    {
        serverSocket.LatencySeconds = settings != null ? settings.LatencySeconds : 0.05f;
        serverSocket.PacketLossProbability = settings != null ? settings.PacketLossProbability : 0f;
    }

    /// <summary>One server tick: process every input that is due, in arrival order.</summary>
    public void SimulateTick()
    {
        ApplyNetworkSettings();

        while (serverSocket.TryReceive(out byte[] payload, out EndPoint senderEndPoint))
        {
            InputPacket packet = InputPacket.Deserialize(payload);
            authoritativePosition = MovementLogic.Move(
                authoritativePosition,
                packet.inputX,
                packet.inputY,
                packet.sprinting ? GameConstants.SprintSpeed : GameConstants.PlayerSpeed,
                GameConstants.FixedDeltaTime
            );

            Vector3 correction = authoritativePosition - packet.claimedPosition;
            float measuredDelayMs = (SimulationClock.Time - packet.timestamp) * 1000f;

            if (logger != null)
            {
                logger.LogInput(packet, authoritativePosition, correction, measuredDelayMs, settings);
            }

            LiveStream.Add(packet.sequenceNumber, correction);
            RowsLogged++;

            StatePacket statePacket = new StatePacket
            {
                ackSequenceNumber = packet.sequenceNumber,
                serverTimestamp = SimulationClock.Time,
                authoritativePosition = authoritativePosition
            };

            serverSocket.SendTo(statePacket.Serialize(), senderEndPoint);
        }
    }

    public void FinishSession()
    {
        if (logger != null) logger.Close();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        serverSocket?.Close();
    }
}
