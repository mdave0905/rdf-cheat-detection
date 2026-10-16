# Reconciliation Divergence Fingerprinting for Cheat Detection

This repository contains the Unity 6 prototype, the server-side feature extraction and classification pipeline, and the interactive evaluation dashboard developed for the Master's thesis *Reconciliation Divergence Fingerprinting: A Server-Side Approach to Detecting Movement Cheats in Multiplayer Games*.

## Overview

Modern real-time multiplayer games rely on client-side prediction and server reconciliation to hide network latency while maintaining a server-authoritative state. In this architecture, the client predicts its local position after each movement input and transmits both the input and the predicted state claim to the server. The server executes the same movement step on its authoritative state and returns the resulting position. Whenever the client's claimed position and the server's authoritative position for the same sequence number differ, a reconciliation divergence occurs.

For an honest client running identical movement code, reconciliation divergence is zero under pure latency and arises only when input packets are lost in transit. Movement cheats that alter the client's local kinematics or claimed trajectory (such as speed hacks, position hacks, and input replay attacks) produce distinct statistical patterns in the server-side divergence stream. This project demonstrates that a passive server-side classifier operating solely on these reconciliation residuals can accurately separate honest sessions from movement cheats without requiring client-side anti-cheat software or additional network traffic.

---

## System Architecture

Both the client and the authoritative server execute inside a single Unity process (`Assets/Scenes/MainScene.unity`) over a real loopback UDP transport (`127.0.0.1`), advancing synchronously on a deterministic 30 Hz fixed simulation tick (`GameConstants.FixedDeltaTime = 1/30 s`).

### 1. Client Subsystem (`Assets/Scripts/Client/`)
* **`ThirdPersonInput.cs`**: Generates per-tick movement vectors and sprint state. During automated experiments (or when bot mode is active), it drives a deterministic waypoint-and-turn trajectory seeded by the run index (`movementSeed`) so that honest and cheating sessions traverse identical baseline paths.
* **`ClientPrediction.cs`**: Maintains the client's predicted position, applies local movement (`MovementLogic.Move`), injects configured movement cheats when active, records unconfirmed predictions in `historyBuffer` (`List<StateHistory>`), transmits `InputPacket` datagrams to the server, and processes incoming `StatePacket` acknowledgements. When the server's authoritative state differs from the historical prediction for the acknowledged sequence number by more than `0.01 m`, the client snaps to the server state and replays all unacknowledged inputs.
* **`CameraFollow.cs`**, **`CharacterAnimation.cs`**, **`VisualRotator.cs`**: Provide third-person camera tracking and visual character presentation for interactive mode.

### 2. Transport and Message Serialization (`Assets/Scripts/Transport/` & `Assets/Scripts/Messages/`)
* **`UDPSocket.cs`**: Non-blocking wrapper around `System.Net.Sockets.Socket` (`SocketType.Dgram`, `ProtocolType.Udp`) bound to `127.0.0.1:9001` (client) and `127.0.0.1:9000` (server).
* **`NetworkPackets.cs`**: Fixed-layout binary serializer and deserializer (`BinaryWriter` / `BinaryReader`) for `InputPacket` (29 bytes payload: sequence number, timestamp, horizontal/vertical input, sprint flag, and claimed 3D position) and `StatePacket` (20 bytes payload: acknowledged sequence number, server timestamp, and authoritative 3D position).
* **`NetworkSimulator.cs`**: Deterministic tick-based channel emulator placed on both the uplink and downlink. Packet loss is evaluated on the sending side via a seeded pseudo-random generator (`System.Random`), and surviving packets are prepended with an 8-byte header (`sendTick` and `dueTick = sendTick + D`, where `D = ceil(L / Delta_t)`) and held in a FIFO receive queue until their due tick.

### 3. Authoritative Server and Telemetry Logging (`Assets/Scripts/Server/`)
* **`ServerAuthoritativeLoop.cs`**: Receives `InputPacket` datagrams in arrival order, advances the authoritative player position using `MovementLogic.Move` at the legal speed (`5.0 m/s` walking, `8.0 m/s` sprinting), computes the per-input reconciliation correction vector (`c_n = p_n - p_hat_n`) and Euclidean magnitude (`d_n = ||p_hat_n - p_n||`), replies with an authoritative `StatePacket`, and forwards the residual to `CorrectionStream` and `ServerLogger`.
* **`CorrectionStream.cs`**: Maintains a rolling 30-second window of received inputs and computes the nine session-level divergence features on the server for live classification.
* **`ServerLogger.cs`** & **`ExperimentSessionRecorder.cs`**: Write one 22-column per-input CSV log per session (`reconciliation_<session>.csv`) and append a session summary row (including configuration, seeds, packet counts, late deliveries, and live C# features/verdicts) to `experiment_sessions.csv`.

### 4. Shared Domain Logic, Automation, and UI (`Assets/Scripts/Shared/`, `Automation/`, `UI/`, `Environment/`, `Visualization/`)
* **`GameConstants.cs`**: Defines global simulation constants: tick rate `30 Hz` (`FixedDeltaTime = 1/30 s`), walk speed `5.0 m/s`, sprint speed `8.0 m/s`, arena bounds `[-15, 15] m` (`30 m x 30 m` arena), and divergence threshold `0.01 m`.
* **`MovementLogic.cs`**: Pure, stateless kinematic step function `Move(currentPosition, inputX, inputY, speed, deltaTime)` shared identically by the client prediction loop and the authoritative server.
* **`SimulationClock.cs`** & **`SimulationDriver.cs`**: Drive the deterministic integer tick counter and step the client reconciliation, client prediction, and server loop in a fixed order during both real-time play and fast-forward batch execution.
* **`TrainedDetector.cs`**: Self-contained C# implementation of the trained depth-1 decision tree (`MeanCorrectionMagnitude > 0.485 m`) generated by `analysis/classify_sessions.py` for live in-game evaluation.
* **`NetworkExperimentSettings.cs`** & **`ExperimentPaths.cs`**: Central configuration state and command-line/path helpers for interactive mode and headless batch collection.
* **`AutomatedDataCollector.cs`**: Orchestrates automated multi-session data collection across the full factorial grid of player types and network profiles, reloading `MainScene` and resetting client, server, and PRNG states deterministically between sessions.
* **`SimulationBootstrap.cs`**, **`SimulationHUD.cs`**, **`ServerGhostVisualizer.cs`**, **`ArenaEnvironment.cs`**: Initialize the scene, construct the arena environment, render the authoritative server ghost alongside the predicted client avatar, and display the diagnostic overlay with live network parameters, divergence metrics, and classifier verdicts.

---

## Threat Model (Implemented Player Types)

The prototype evaluates four player types controlled via `NetworkExperimentSettings.playerType` (`ExperimentPlayerType`):

1. **`Legitimate` (Honest)**: Executes unmodified `MovementLogic.Move` (`5.0 m/s` walk, `8.0 m/s` sprint) and reports true predicted positions.
2. **`SpeedHack`**: Multiplies client-side movement speed by `alpha = 2.0` (`10.0 m/s` walk, `16.0 m/s` sprint) while sending standard directional inputs.
3. **`PositionHack`**: Executes normal movement except on every `60th` tick (`2.0 s`), where it adds a teleportation offset of `(5, 0, 5) m` (`7.07 m` horizontal step) to its predicted position.
4. **`InputReplay`**: Records inputs and predicted positions during ticks `1..90` (`3.0 s`) and replays those recorded inputs and positions as claims during ticks `151..240` (`5.0..8.0 s`) while the server state resides at a different arena location.

---

## Reproducing the Dataset and Analysis

### Prerequisites
* **Unity 6** (`6000.5.6f1` or compatible) for building or running the simulation.
* **Python 3.10+** with `numpy`, `scipy`, and `scikit-learn` (`extract_features.py` uses only the Python standard library).

### Step 1: Collect Experimental Data
Build the standalone player from the Unity Editor via the menu item `Thesis > Build Experiment Player` (defined in `Assets/Editor/ExperimentBuild.cs`), or from the command line:

```powershell
Unity.exe -batchmode -quit -projectPath . -executeMethod ExperimentBuild.Build
```

Then execute the full factorial batch experiment (`4` player types x `3` delays x `3` loss rates x `200` runs = `7,200` sessions of `900` ticks each):

```powershell
$ds = "$env:USERPROFILE\AppData\LocalLow\DefaultCompany\ThesisPrototype\dataset_v2"
Builds\ExperimentPlayer\ThesisPrototype.exe -batchmode -nographics -collect -runs 200 -outdir $ds -logFile collection.log
```

Because the simulation and channel emulator are strictly tick-driven and seeded by `(run, latency, loss)`, running the command above produces byte-identical CSV logs across executions. Add `-realtime` if you wish to run at wall-clock speed instead of fast-forward batch mode.

### Step 2: Feature Extraction and Classifier Training
Run the Python analysis pipeline inside the `analysis/` directory:

```powershell
cd analysis
python extract_features.py --input $ds --output results
python classify_sessions.py --features results\session_features.csv --output results\classifiers --csharp ..\Assets\Scripts\Shared\TrainedDetector.cs
python export_dashboard_data.py --dataset $ds
```

| Script | Description and Outputs |
| :--- | :--- |
| `analysis/extract_features.py` | Parses raw session CSV logs, verifies data integrity (checking for truncated sessions, duplicate sequence numbers, or late deliveries), compares offline features against the live C# features, and writes `analysis/results/session_features.csv` (nine features per session) and `analysis/results/integrity_report.csv`. |
| `analysis/classify_sessions.py` | Splits the dataset by run index (`140` training runs / `5,040` sessions; `60` test runs / `2,160` sessions, seed `2024`) so paired trajectories never leak across splits. Evaluates the depth-1 decision tree, L2-regularized logistic regression, and the baseline threshold rule, writing evaluation tables to `analysis/results/classifiers/` and exporting the trained tree to `Assets/Scripts/Shared/TrainedDetector.cs`. |
| `analysis/export_dashboard_data.py` | Compiles summary statistics, feature distributions, and sample session traces into `analysis/dashboard/dashboard_data.js` for the interactive offline viewer (`analysis/dashboard/index.html`). |

---

## Interactive Inspection

### Unity Interactive Mode
Open the project in Unity and press **Play** in `Assets/Scenes/MainScene.unity` (or launch `ThesisPrototype.exe` without `-collect`). Use the keyboard shortcuts displayed in the bottom bar of `SimulationHUD`:
* **`1` - `4`**: Switch active player behaviour (`Legitimate`, `SpeedHack`, `PositionHack`, `InputReplay`).
* **`L`**: Cycle one-way network delay (`50 ms`, `150 ms`, `300 ms`).
* **`P`**: Cycle packet loss rate (`0%`, `3%`, `10%`).
* **`M`**: Toggle between the deterministic bot path and manual keyboard (`WASD` + `Shift`) movement.
* **`R`**: Reset the current 30-second feature window and counters.
* **`G`**: Toggle the authoritative server ghost and divergence line visualizer (`ServerGhostVisualizer`).
* **`H`**: Show or hide the on-screen diagnostic HUD (`SimulationHUD`).

### Offline Results Dashboard
Open `analysis/dashboard/index.html` directly in any web browser to inspect the 7,200-session dataset, compare feature distributions across network conditions, and view time-series divergence traces for individual runs.
