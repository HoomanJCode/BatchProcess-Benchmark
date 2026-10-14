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
        BuildWindowsPlayer(GetEnabledScenePaths());
    }

    /// <summary>Creates the batch scenes and builds a player for the PSO versus Random Search comparison.</summary>
    public static void BuildOptimizationPlayer()
    {
        // Generate the same comparison and example scenes used by the local dashboard.
        BatchProcessingSceneGeneration.Build();

        string[] scenes =
        {
            "Assets/Examples/BatchProcessing/Scenes/Batch_OptimizationBenchmark.unity",
            "Assets/Examples/BatchProcessing/Scenes/Batch_ParticleSwarm.unity",
            "Assets/Examples/BatchProcessing/Scenes/Batch_RandomSearch.unity"
        };
        BuildWindowsPlayer(scenes, "OptimizationBenchmark.exe");
    }

    /// <summary>Creates a player for the six beginner batch-processing methods.</summary>
    public static void BuildBatchPlayer()
    {
        // Regenerate the same standalone scenes used by the Editor comparison launcher.
        BatchProcessingSceneGeneration.Build();

        string[] scenes =
        {
            "Assets/Examples/BatchProcessing/Scenes/Batch_Benchmark.unity",
            "Assets/Examples/BatchProcessing/Scenes/Batch_SingleThreaded.unity",
            "Assets/Examples/BatchProcessing/Scenes/Batch_CSharpAsync.unity",
            "Assets/Examples/BatchProcessing/Scenes/Batch_CSharpParallel.unity",
            "Assets/Examples/BatchProcessing/Scenes/Batch_UnityJobs.unity",
            "Assets/Examples/BatchProcessing/Scenes/Batch_UnityCoroutine.unity",
            "Assets/Examples/BatchProcessing/Scenes/Batch_UnityJobsBurst.unity"
        };
        BuildWindowsPlayer(scenes, "BatchBenchmark.exe");
    }

    private static string[] GetEnabledScenePaths()
    {
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
        return scenes;
    }

    private static void BuildWindowsPlayer(string[] scenes, string fileName = "GravityBenchmark.exe")
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string outputDirectory = Path.Combine(projectRoot, "Build", "Headless");
        Directory.CreateDirectory(outputDirectory);

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = Path.Combine(outputDirectory, fileName),
            target = BuildTarget.StandaloneWindows64,
            // Development builds keep profiler marker data available to ProfilerRecorder in the player.
            options = BuildOptions.Development
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
            throw new BuildFailedException("Headless benchmark build failed: " + report.summary.result);

        Debug.Log("Headless gravity benchmark built at " + options.locationPathName);
    }
}
