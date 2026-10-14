using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Creates one clean scene for each self-contained batch example.</summary>
public static class BatchProcessingSceneGeneration
{
    private const string SceneFolder = "Assets/Examples/BatchProcessing/Scenes";
    private const string OldScenePath = SceneFolder + "/BatchProcessing.unity";
    private const string BenchmarkScenePath = SceneFolder + "/Batch_Benchmark.unity";
    private const string OptimizationScenePath = SceneFolder + "/Batch_OptimizationBenchmark.unity";

    [MenuItem("Tools/Batch Processing/Create Example Scenes")]
    public static void Build()
    {
        string[] sceneNames =
        {
            "Batch_SingleThreaded",
            "Batch_CSharpAsync",
            "Batch_CSharpParallel",
            "Batch_UnityJobs",
            "Batch_UnityCoroutine",
            "Batch_UnityJobsBurst",
            "Batch_ParticleSwarm",
            "Batch_RandomSearch"
        };
        System.Type[] exampleTypes =
        {
            typeof(BatchSingleThreadedExample),
            typeof(BatchCSharpAsyncExample),
            typeof(BatchCSharpParallelExample),
            typeof(BatchUnityJobsExample),
            typeof(BatchUnityCoroutineExample),
            typeof(BatchUnityJobsBurstExample),
            typeof(BatchParticleSwarmExample),
            typeof(BatchRandomSearchExample)
        };

        List<EditorBuildSettingsScene> newScenes = new List<EditorBuildSettingsScene>();
        for (int i = 0; i < sceneNames.Length; i++)
        {
            string path = SceneFolder + "/" + sceneNames[i] + ".unity";
            // Batch mode starts with an untitled scene, so replace scenes there; in the Editor, preserve the open scene.
            NewSceneMode sceneMode = Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive;
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, sceneMode);
            scene.name = sceneNames[i];

            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.055f, 0.09f);
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            cameraObject.AddComponent<AudioListener>();

            GameObject exampleObject = new GameObject("Batch Example");
            Component example = exampleObject.AddComponent(exampleTypes[i]);
            if (example is BatchParticleSwarmExample)
            {
                SerializedObject settings = new SerializedObject(example);
                settings.FindProperty("_particleCount").intValue = 64;
                settings.FindProperty("_iterations").intValue = 99;
                settings.FindProperty("_trialCount").intValue = 5;
                settings.FindProperty("_randomSeed").intValue = 2468;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
            else if (example is BatchRandomSearchExample)
            {
                SerializedObject settings = new SerializedObject(example);
                settings.FindProperty("_evaluationBudgetPerTrial").intValue = 6400;
                settings.FindProperty("_trialCount").intValue = 5;
                settings.FindProperty("_randomSeed").intValue = 2468;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }

            if (!EditorSceneManager.SaveScene(scene, path))
                throw new System.Exception("Could not save batch example scene: " + path);
            if (sceneMode == NewSceneMode.Additive)
                EditorSceneManager.CloseScene(scene, true);
            newScenes.Add(new EditorBuildSettingsScene(path, true));
        }

        // PSO and Random Search are workloads; make one scene per workload and method.
        string[] optimizationScenes = CreateOptimizationWorkloadScenes();
        for (int i = 0; i < optimizationScenes.Length; i++)
            newScenes.Add(new EditorBuildSettingsScene(optimizationScenes[i], true));

        CreateBatchBenchmarkScene();
        newScenes.Add(new EditorBuildSettingsScene(BenchmarkScenePath, true));
        CreateOptimizationBenchmarkScene();
        newScenes.Add(new EditorBuildSettingsScene(OptimizationScenePath, true));

        // Replace the old selector scene while preserving unrelated Build Settings scenes.
        List<EditorBuildSettingsScene> buildScenes = newScenes;
        foreach (EditorBuildSettingsScene existing in EditorBuildSettings.scenes)
        {
            if (!existing.path.StartsWith(SceneFolder + "/Batch_"))
                if (existing.path != OldScenePath)
                    buildScenes.Add(existing);
        }
        EditorBuildSettings.scenes = buildScenes.ToArray();
        AssetDatabase.DeleteAsset(OldScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("BATCH_PROCESSING_SCENES_GENERATED count=" + sceneNames.Length);
    }

    private static void CreateBatchBenchmarkScene()
    {
        // The batch launcher uses the same local dashboard as the gravity comparisons.
        NewSceneMode sceneMode = Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive;
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, sceneMode);
        scene.name = "Batch Benchmark";

        GameObject runnerObject = new GameObject("Batch Benchmark Runner");
        GravityBenchmarkRunner runner = runnerObject.AddComponent<GravityBenchmarkRunner>();
        SerializedObject settings = new SerializedObject(runner);
        settings.FindProperty("_isBatchComparison").boolValue = true;
        settings.FindProperty("_batchRunsPerStrategy").intValue = 5;
        settings.FindProperty("_dashboardPort").intValue = 8765;
        settings.FindProperty("_openDashboardInBrowser").boolValue = true;
        settings.ApplyModifiedPropertiesWithoutUndo();

        if (!EditorSceneManager.SaveScene(scene, BenchmarkScenePath))
            throw new System.Exception("Could not save batch comparison scene: " + BenchmarkScenePath);
        if (sceneMode == NewSceneMode.Additive)
            EditorSceneManager.CloseScene(scene, true);
    }

    private static void CreateOptimizationBenchmarkScene()
    {
        // The launcher compares processing times within the selected algorithm workload.
        NewSceneMode sceneMode = Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive;
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, sceneMode);
        scene.name = "Batch Optimization Benchmark";

        GameObject runnerObject = new GameObject("Optimization Benchmark Runner");
        GravityBenchmarkRunner runner = runnerObject.AddComponent<GravityBenchmarkRunner>();
        SerializedObject settings = new SerializedObject(runner);
        settings.FindProperty("_isOptimizationComparison").boolValue = true;
        settings.FindProperty("_isBatchComparison").boolValue = false;
        settings.FindProperty("_batchRunsPerStrategy").intValue = 1;
        settings.FindProperty("_dashboardPort").intValue = 8765;
        settings.FindProperty("_openDashboardInBrowser").boolValue = true;
        settings.ApplyModifiedPropertiesWithoutUndo();

        if (!EditorSceneManager.SaveScene(scene, OptimizationScenePath))
            throw new System.Exception("Could not save optimization comparison scene: " + OptimizationScenePath);
        if (sceneMode == NewSceneMode.Additive)
            EditorSceneManager.CloseScene(scene, true);
    }

    private static string[] CreateOptimizationWorkloadScenes()
    {
        string[] algorithms = { "PSO", "Random" };
        string[] methods = { "SingleThreaded", "CSharpAsync", "CSharpParallel", "UnityJobs", "UnityCoroutine", "UnityJobsBurst" };
        string[] paths = new string[algorithms.Length * methods.Length];
        int pathIndex = 0;

        for (int algorithm = 0; algorithm < algorithms.Length; algorithm++)
        {
            for (int method = 0; method < methods.Length; method++)
            {
                string sceneName = "Batch_Optimization" + algorithms[algorithm] + "_" + methods[method];
                string path = SceneFolder + "/" + sceneName + ".unity";
                NewSceneMode sceneMode = Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive;
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, sceneMode);
                scene.name = sceneName;
                GameObject runnerObject = new GameObject("Batch Example");
                BatchOptimizationProcessingExample example = runnerObject.AddComponent<BatchOptimizationProcessingExample>();
                SerializedObject settings = new SerializedObject(example);
                settings.FindProperty("_algorithm").intValue = algorithm;
                settings.FindProperty("_processingMethod").intValue = method;
                settings.FindProperty("_particleCount").intValue = 64;
                settings.FindProperty("_iterations").intValue = 99;
                settings.FindProperty("_trialCount").intValue = 5;
                settings.FindProperty("_randomSeed").intValue = 2468;
                settings.ApplyModifiedPropertiesWithoutUndo();

                if (!EditorSceneManager.SaveScene(scene, path))
                    throw new System.Exception("Could not save optimization workload scene: " + path);
                if (sceneMode == NewSceneMode.Additive)
                    EditorSceneManager.CloseScene(scene, true);
                paths[pathIndex++] = path;
            }
        }
        return paths;
    }
}
