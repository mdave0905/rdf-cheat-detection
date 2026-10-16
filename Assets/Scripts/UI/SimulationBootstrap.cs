using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Sets up every loaded scene without manual inspector work:
///   - the SimulationDriver that steps client and server (always),
///   - the batch data collector when started with -collect,
///   - the arena, server ghost and HUD (interactive runs only).
/// Runs for every scene load, including the reloads between batch sessions.
/// </summary>
public static class SimulationBootstrap
{
    private static bool subscribed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if (!subscribed)
        {
            SceneManager.sceneLoaded += (scene, mode) => SetUpScene(scene);
            subscribed = true;
        }

        if (CommandLine.HasFlag("-collect"))
        {
            AutomatedDataCollector.CreateFromCommandLine();
            if (AutomatedDataCollector.Instance != null && AutomatedDataCollector.Instance.IsBatchRunning)
            {
                // StartBatch reloads the scene; setup happens on that load.
                return;
            }
        }

        SetUpScene(SceneManager.GetActiveScene());
    }

    private static bool BatchRunning =>
        AutomatedDataCollector.Instance != null && AutomatedDataCollector.Instance.IsBatchRunning;

    private static void SetUpScene(Scene scene)
    {
        if (SimulationDriver.Instance == null || SimulationDriver.Instance.gameObject.scene != scene)
        {
            GameObject driverObject = new GameObject("SimulationDriver");
            SceneManager.MoveGameObjectToScene(driverObject, scene);
            SimulationDriver driver = driverObject.AddComponent<SimulationDriver>();
            driver.fastForward = BatchRunning && !AutomatedDataCollector.Instance.UseRealTime;
        }

        if (NetworkExperimentSettings.Instance == null && Object.FindAnyObjectByType<NetworkExperimentSettings>() == null)
        {
            GameObject settingsObj = new GameObject("NetworkExperimentSettings");
            SceneManager.MoveGameObjectToScene(settingsObj, scene);
            settingsObj.AddComponent<NetworkExperimentSettings>();
        }

        if (Application.isBatchMode || BatchRunning)
        {
            return;
        }

        GameObject envObj = GameObject.Find("Environment");
        if (envObj == null)
        {
            envObj = new GameObject("Environment");
        }

        envObj.transform.position = Vector3.zero;
        envObj.transform.rotation = Quaternion.identity;
        envObj.transform.localScale = Vector3.one;

        if (!envObj.TryGetComponent<ArenaEnvironment>(out _))
        {
            envObj.AddComponent<ArenaEnvironment>();
        }

        if (Object.FindAnyObjectByType<ServerGhostVisualizer>() == null)
        {
            new GameObject("Server_Ghost_Visualizer").AddComponent<ServerGhostVisualizer>();
        }

        if (Object.FindAnyObjectByType<SimulationHUD>() == null)
        {
            new GameObject("Simulation_HUD").AddComponent<SimulationHUD>();
        }

        if (ClientPrediction.LocalPlayer == null || ServerAuthoritativeLoop.Instance == null)
        {
            Debug.LogWarning("[Demo Setup] ClientPrediction or ServerAuthoritativeLoop is missing. If the player does not move, please ensure Assets/Scenes/MainScene.unity is the active scene in Unity!");
        }
    }
}
