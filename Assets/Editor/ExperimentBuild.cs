using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Builds the Windows player used for headless data collection:
///   Unity.exe -batchmode -quit -projectPath . -executeMethod ExperimentBuild.Build -logFile build.log
/// then
///   Builds\ExperimentPlayer\ThesisPrototype.exe -batchmode -nographics -collect -outdir &lt;folder&gt;
/// </summary>
public static class ExperimentBuild
{
    public const string OutputPath = "Builds/ExperimentPlayer/ThesisPrototype.exe";

    [MenuItem("Thesis/Build Experiment Player")]
    public static void Build()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/MainScene.unity" },
            locationPathName = OutputPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"[ExperimentBuild] {report.summary.result}: {report.summary.totalErrors} errors, output {OutputPath}");
        if (Application.isBatchMode)
        {
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
