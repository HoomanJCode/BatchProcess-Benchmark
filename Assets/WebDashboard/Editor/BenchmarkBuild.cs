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

    /// <summary>Builds both gravity dimensions so one headless player can compare either one.</summary>
    public static void BuildGravityPlayer()
    {
        // Use the generated benchmark scenes already in the project; the gravity
        // scene generator creates additive scenes and cannot run in batch mode.
        string[] scenes =
        {
            "Assets/Examples/GravitySimulation/2D/Scenes/Gravity_Benchmark.unity",
            "Assets/Examples/GravitySimulation/3D/Scenes/Gravity_Benchmark.unity",
            "Assets/Examples/GravitySimulation/2D/Scenes/Benchmark/Gravity_SingleThreaded.unity",
            "Assets/Examples/GravitySimulation/2D/Scenes/Benchmark/Gravity_CSharpAsync.unity",
            "Assets/Examples/GravitySimulation/2D/Scenes/Benchmark/Gravity_CSharpParallel.unity",
            "Assets/Examples/GravitySimulation/2D/Scenes/Benchmark/Gravity_UnityJobs.unity",
            "Assets/Examples/GravitySimulation/2D/Scenes/Benchmark/Gravity_UnityJobsBurst.unity",
            "Assets/Examples/GravitySimulation/3D/Scenes/Benchmark/Gravity_SingleThreaded.unity",
            "Assets/Examples/GravitySimulation/3D/Scenes/Benchmark/Gravity_CSharpAsync.unity",
            "Assets/Examples/GravitySimulation/3D/Scenes/Benchmark/Gravity_CSharpParallel.unity",
            "Assets/Examples/GravitySimulation/3D/Scenes/Benchmark/Gravity_UnityJobs.unity",
            "Assets/Examples/GravitySimulation/3D/Scenes/Benchmark/Gravity_UnityJobsBurst.unity"
        };
        BuildWindowsPlayer(scenes);
    }

    /// <summary>Builds a player that compares six processing methods for both optimization workloads.</summary>
    public static void BuildOptimizationPlayer()
    {
        // Generate the same comparison and example scenes used by the local dashboard.
        ExampleSceneGeneration.Build();

        string[] methods = { "SingleThreaded", "CSharpAsync", "CSharpParallel", "UnityJobs", "UnityCoroutine", "UnityJobsBurst" };
        string[] scenes = new string[13];
        scenes[0] = "Assets/WebDashboard/Scenes/Optimization_Benchmark.unity";
        int sceneIndex = 1;
        for (int algorithm = 0; algorithm < 2; algorithm++)
            for (int method = 0; method < methods.Length; method++)
                scenes[sceneIndex++] = "Assets/Examples/" +
                    (algorithm == 0 ? "PSOSimulation/Scenes/PSO_" : "RandomSearchOptimization/Scenes/RandomSearch_") +
                    methods[method] + ".unity";
        BuildWindowsPlayer(scenes, "OptimizationBenchmark.exe");
    }

    /// <summary>Creates a player for the six beginner batch-processing methods.</summary>
    public static void BuildBatchPlayer()
    {
        // Regenerate the same standalone scenes used by the Editor comparison launcher.
        ExampleSceneGeneration.Build();

        string[] scenes =
        {
            "Assets/Examples/SimpleIteration/Scenes/SimpleIteration_Benchmark.unity",
            "Assets/Examples/SimpleIteration/Scenes/SimpleIteration_SingleThreaded.unity",
            "Assets/Examples/SimpleIteration/Scenes/SimpleIteration_CSharpAsync.unity",
            "Assets/Examples/SimpleIteration/Scenes/SimpleIteration_CSharpParallel.unity",
            "Assets/Examples/SimpleIteration/Scenes/SimpleIteration_UnityJobs.unity",
            "Assets/Examples/SimpleIteration/Scenes/SimpleIteration_UnityCoroutine.unity",
            "Assets/Examples/SimpleIteration/Scenes/SimpleIteration_UnityJobsBurst.unity"
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
