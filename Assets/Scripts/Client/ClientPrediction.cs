using System.Collections.Generic;
using System.Net;
using UnityEngine;

public struct StateHistory
{
    public uint sequenceNumber;
    public Vector3 position;
    public float inputX;
    public float inputY;
    public bool sprinting;
    public float movementSpeed;
}

public struct ReplaySample
{
    public float inputX;
    public float inputY;
    public bool sprinting;
    public Vector3 claimedPosition;
}

/// <summary>
/// Client side of the prototype: predicts its own movement every tick, sends the input
/// and the resulting (claimed) position to the server, and reconciles against the
/// server's authoritative position for the same sequence number. The three cheats are
/// injected here. Ticks are driven by SimulationDriver, not by Unity's frame loop.
/// </summary>
public class ClientPrediction : MonoBehaviour
{
    public const int ClientPort = 9001;
    public const int ServerPort = 9000;

    public static ClientPrediction LocalPlayer { get; private set; }

    public Vector3 PredictedPosition => predictedPosition;
    public uint CurrentSequenceNumber => currentSequenceNumber;
    public int TotalCorrections { get; private set; }
    public float LastCorrectionMagnitude { get; private set; }
    public bool IsReady { get; private set; }
    public bool IsReplayingInput { get; private set; }
    public NetworkSimulator Link => clientSocket;

    public Transform cameraTransform;
    [Tooltip("How quickly the visible character catches up with the fixed-rate prediction.")]
    public float visualSmoothTime = 0.05f;

    private NetworkSimulator clientSocket;
    private NetworkExperimentSettings settings;
    private uint currentSequenceNumber;
    private Vector3 predictedPosition = Vector3.zero;
    private Vector3 visualVelocity = Vector3.zero;

    private ExperimentPlayerType activeCheat;
    private uint cheatStartTick;
    private int replayIndex;
    private readonly List<ReplaySample> replaySamples = new List<ReplaySample>();
    private readonly List<StateHistory> historyBuffer = new List<StateHistory>();

    private void Awake()
    {
        LocalPlayer = this;
    }

    private void Start()
    {
        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        settings = NetworkExperimentSettings.GetOrDefault();

        int seed = settings != null ? settings.randomSeed : 12345;
        clientSocket = new NetworkSimulator(unchecked(seed * 31 + 1));
        clientSocket.Bind("127.0.0.1", ClientPort);
        ApplyNetworkSettings();

        // Client and server both start at the arena centre.
        predictedPosition = Vector3.zero;
        transform.position = Vector3.zero;

        activeCheat = settings != null ? settings.playerType : ExperimentPlayerType.Legitimate;
        RestartCheatTimers(SimulationClock.Tick);
        IsReady = true;
    }

    private void Update()
    {
        // Rendering only: the simulation runs at 30 Hz, the screen at 60+ Hz, so the
        // visible character is smoothed towards the exact simulated position.
        transform.position = Vector3.SmoothDamp(
            transform.position,
            predictedPosition,
            ref visualVelocity,
            visualSmoothTime,
            Mathf.Infinity,
            Time.deltaTime
        );
    }

    private void ApplyNetworkSettings()
    {
        clientSocket.LatencySeconds = settings != null ? settings.LatencySeconds : 0.05f;
        clientSocket.PacketLossProbability = settings != null ? settings.PacketLossProbability : 0f;
    }

    private void RestartCheatTimers(uint lastTickBeforeCheat)
    {
        // Tick 1 of the cheat timeline is the first input after lastTickBeforeCheat.
        cheatStartTick = lastTickBeforeCheat;
        replaySamples.Clear();
        replayIndex = 0;
        IsReplayingInput = false;
    }

    private static uint SecondsToTicks(float seconds)
    {
        return (uint)Mathf.RoundToInt(seconds / GameConstants.FixedDeltaTime);
    }

    public void ResetCounters()
    {
        TotalCorrections = 0;
        LastCorrectionMagnitude = 0f;
    }

    /// <summary>Step 1 of a tick: apply every server state that is due this tick.</summary>
    public void ReceiveServerStates()
    {
        ApplyNetworkSettings();
        while (clientSocket.TryReceive(out byte[] payload, out EndPoint _))
        {
            ReconcileState(StatePacket.Deserialize(payload));
        }
    }

    /// <summary>Step 2 of a tick: read input, predict, inject the active cheat, send.</summary>
    public void SimulateTick()
    {
        ExperimentPlayerType cheat = settings != null ? settings.playerType : ExperimentPlayerType.Legitimate;
        if (cheat != activeCheat)
        {
            // Switching behaviour live (HUD keys) restarts the cheat's timeline, so
            // position hack and input replay work no matter when they are selected.
            activeCheat = cheat;
            RestartCheatTimers(SimulationClock.Tick - 1);
        }

        currentSequenceNumber++;
        float now = SimulationClock.Time;

        Vector3 worldInput = ThirdPersonInput.ReadWorldMovement(cameraTransform);
        float inputX = worldInput.x;
        float inputY = worldInput.z;
        bool sprinting = ThirdPersonInput.IsSprinting() && worldInput.sqrMagnitude > 0.001f;

        // Cheat timeline in whole ticks: tick 1 is the first input after the cheat was selected.
        uint cheatTick = SimulationClock.Tick - cheatStartTick;
        bool inputReplayActive = cheat == ExperimentPlayerType.InputReplay;
        bool recordingReplay = inputReplayActive && cheatTick <= SecondsToTicks(settings.inputReplayRecordSeconds);
        uint replayStartTick = inputReplayActive ? SecondsToTicks(settings.inputReplayStartSeconds) : 0;
        IsReplayingInput = inputReplayActive &&
            cheatTick > replayStartTick &&
            cheatTick <= replayStartTick + SecondsToTicks(settings.inputReplayDurationSeconds) &&
            replayIndex < replaySamples.Count;
        Vector3 replayClaimedPosition = Vector3.zero;

        if (IsReplayingInput)
        {
            ReplaySample sample = replaySamples[replayIndex];
            replayIndex++;
            inputX = sample.inputX;
            inputY = sample.inputY;
            sprinting = sample.sprinting;
            replayClaimedPosition = sample.claimedPosition;
        }

        float legalSpeed = sprinting ? GameConstants.SprintSpeed : GameConstants.PlayerSpeed;
        float speed = cheat == ExperimentPlayerType.SpeedHack
            ? legalSpeed * settings.speedHackMultiplier
            : legalSpeed;

        predictedPosition = MovementLogic.Move(predictedPosition, inputX, inputY, speed, GameConstants.FixedDeltaTime);

        if (cheat == ExperimentPlayerType.PositionHack)
        {
            uint interval = SecondsToTicks(settings.positionHackIntervalSeconds);
            if (interval > 0 && cheatTick > 0 && cheatTick % interval == 0)
            {
                predictedPosition += settings.positionHackOffset;
            }
        }

        if (recordingReplay)
        {
            replaySamples.Add(new ReplaySample
            {
                inputX = inputX,
                inputY = inputY,
                sprinting = sprinting,
                claimedPosition = predictedPosition
            });
        }

        historyBuffer.Add(new StateHistory
        {
            sequenceNumber = currentSequenceNumber,
            position = predictedPosition,
            inputX = inputX,
            inputY = inputY,
            sprinting = sprinting,
            movementSpeed = speed
        });

        InputPacket packet = new InputPacket
        {
            sequenceNumber = currentSequenceNumber,
            timestamp = now,
            inputX = inputX,
            inputY = inputY,
            sprinting = sprinting,
            claimedPosition = IsReplayingInput ? replayClaimedPosition : predictedPosition
        };

        clientSocket.SendTo(packet.Serialize(), "127.0.0.1", ServerPort);
    }

    private void ReconcileState(StatePacket statePacket)
    {
        int historyIndex = historyBuffer.FindIndex(h => h.sequenceNumber == statePacket.ackSequenceNumber);
        if (historyIndex == -1) return;

        StateHistory historicalState = historyBuffer[historyIndex];
        float error = Vector3.Distance(historicalState.position, statePacket.authoritativePosition);

        if (error > GameConstants.DivergenceThreshold)
        {
            TotalCorrections++;
            LastCorrectionMagnitude = error;

            // Snap to the server's position for that sequence number, then replay the
            // inputs the server has not confirmed yet.
            predictedPosition = statePacket.authoritativePosition;
            for (int i = historyIndex + 1; i < historyBuffer.Count; i++)
            {
                StateHistory step = historyBuffer[i];
                predictedPosition = MovementLogic.Move(
                    predictedPosition,
                    step.inputX,
                    step.inputY,
                    step.movementSpeed,
                    GameConstants.FixedDeltaTime
                );
                step.position = predictedPosition;
                historyBuffer[i] = step;
            }
        }

        historyBuffer.RemoveRange(0, historyIndex + 1);
    }

    private void OnDestroy()
    {
        if (LocalPlayer == this) LocalPlayer = null;
        clientSocket?.Close();
    }
}
