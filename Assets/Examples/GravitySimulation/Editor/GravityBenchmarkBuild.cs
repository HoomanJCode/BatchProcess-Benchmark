using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Builds the standalone player used by the headless gravity benchmark.</summary>
public static class GravityBenchmarkBuild
{
    /// <summary>Build target for -executeMethod when running Unity in batch mode.</summary>
    public static void BuildWindowsPlayer()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string outputDirectory = Path.Combine(projectRoot, "Build", "Headless");
        Directory.CreateDirectory(outputDirectory);

        EditorBuildSettingsScene[] enabledScenes = EditorBuildSettings.scenes;
        int enabledCount = 0;
        for (int i = 0; i < enabledScenes.Length; i++)
            if (enabledScenes[i].enabled)
                enabledCount++;

        string[] scenes = new string[enabledCount];
        int sceneIndex = 0;
        for (int i = 0; i < enabledScenes.Length; i++)
        {
            if (enabledScenes[i].enabled)
                scenes[sceneIndex++] = enabledScenes[i].path;
        }

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = Path.Combine(outputDirectory, "GravityBenchmark.exe"),
            target = BuildTarget.StandaloneWindows64,
            // Development builds keep profiler marker data available to ProfilerRecorder in the player.
            options = BuildOptions.Development
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
            throw new BuildFailedException("Headless gravity benchmark build failed: " + report.summary.result);

        Debug.Log("Headless gravity benchmark built at " + options.locationPathName);
    }
}
