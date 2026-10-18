using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Creates the standalone teaching scenes used by each processing workload.</summary>
public static class ExampleSceneGeneration
{
    private const string SimpleIterationSceneFolder = "Assets/Examples/SimpleIteration/Scenes";
    private const string PsoSceneFolder = "Assets/Examples/PSOSimulation/Scenes";
    private const string RandomSearchSceneFolder = "Assets/Examples/RandomSearchOptimization/Scenes";
    private const string BenchmarkScenePath = SimpleIterationSceneFolder + "/SimpleIteration_Benchmark.unity";
    private const string OptimizationScenePath = "Assets/WebDashboard/Scenes/Optimization_Benchmark.unity";
    private const string AllExamplesScenePath = "Assets/WebDashboard/Scenes/All_Examples_Benchmark.unity";

    [MenuItem("Tools/Examples/Regenerate Beginner Scenes")]
    public static void Build()
    {
        string[] sceneNames =
        {
            "SimpleIteration_SingleThreaded",
            "SimpleIteration_CSharpAsync",
            "SimpleIteration_CSharpParallel",
            "SimpleIteration_UnityJobs",
            "SimpleIteration_UnityJobsBurst"
        };
        System.Type[] exampleTypes =
        {
            typeof(BatchSingleThreadedExample),
            typeof(BatchCSharpAsyncExample),
            typeof(BatchCSharpParallelExample),
            typeof(BatchUnityJobsExample),
            typeof(BatchUnityJobsBurstExample)
        };

        List<EditorBuildSettingsScene> newScenes = new List<EditorBuildSettingsScene>();
        for (int i = 0; i < sceneNames.Length; i++)
        {
            string path = SimpleIterationSceneFolder + "/" + sceneNames[i] + ".unity";
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
            if (!EditorSceneManager.SaveScene(scene, path))
                throw new System.Exception("Could not save batch example scene: " + path);
            if (sceneMode == NewSceneMode.Additive)
                EditorSceneManager.CloseScene(scene, true);
            newScenes.Add(new EditorBuildSettingsScene(path, true));
        }

        // PSO and Random Search are workloads; make one scene per workload and method.
        newScenes.Add(new EditorBuildSettingsScene(PsoSceneFolder + "/PSO_Simulation.unity", true));
        newScenes.Add(new EditorBuildSettingsScene(RandomSearchSceneFolder + "/RandomSearch_Optimization.unity", true));
        string[] optimizationScenes = CreateOptimizationWorkloadScenes();
        for (int i = 0; i < optimizationScenes.Length; i++)
            newScenes.Add(new EditorBuildSettingsScene(optimizationScenes[i], true));

        CreateBatchBenchmarkScene();
        newScenes.Add(new EditorBuildSettingsScene(BenchmarkScenePath, true));
        CreateOptimizationBenchmarkScene();
        newScenes.Add(new EditorBuildSettingsScene(OptimizationScenePath, true));
        CreateAllExamplesBenchmarkScene();
        newScenes.Add(new EditorBuildSettingsScene(AllExamplesScenePath, true));

        // Replace the old selector scene while preserving unrelated Build Settings scenes.
        List<EditorBuildSettingsScene> buildScenes = newScenes;
        foreach (EditorBuildSettingsScene existing in EditorBuildSettings.scenes)
        {
            if (!IsGeneratedExampleScene(existing.path))
                buildScenes.Add(existing);
        }
        EditorBuildSettings.scenes = buildScenes.ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log("EXAMPLE_SCENES_GENERATED simpleIteration=" + sceneNames.Length + " optimization=" + optimizationScenes.Length);
    }

    private static bool IsGeneratedExampleScene(string path)
    {
        return path.StartsWith(SimpleIterationSceneFolder + "/") ||
            path.StartsWith(PsoSceneFolder + "/") ||
            path.StartsWith(RandomSearchSceneFolder + "/") ||
            path == OptimizationScenePath ||
            path == AllExamplesScenePath;
    }

    private static void CreateBatchBenchmarkScene()
    {
        // The batch launcher uses the same local dashboard as the gravity comparisons.
        NewSceneMode sceneMode = Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive;
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, sceneMode);
        scene.name = "Batch Benchmark";

        GameObject runnerObject = new GameObject("Batch Benchmark Runner");
        RealtimeBenchmarkDashboard runner = runnerObject.AddComponent<RealtimeBenchmarkDashboard>();
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
        RealtimeBenchmarkDashboard runner = runnerObject.AddComponent<RealtimeBenchmarkDashboard>();
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

    #region Full benchmark launcher scene
    [MenuItem("Tools/Examples/Generate All Examples Benchmark")]
    public static void GenerateAllExamplesBenchmarkScene()
    {
        CreateAllExamplesBenchmarkScene(true);

        List<EditorBuildSettingsScene> buildScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        bool alreadyIncluded = false;
        for (int i = 0; i < buildScenes.Count; i++)
        {
            if (buildScenes[i].path == AllExamplesScenePath)
                alreadyIncluded = true;
        }
        if (!alreadyIncluded)
            buildScenes.Add(new EditorBuildSettingsScene(AllExamplesScenePath, true));
        EditorBuildSettings.scenes = buildScenes.ToArray();
        AssetDatabase.SaveAssets();
    }

    // This launcher runs every example so one Play action fills every dashboard comparison tab.
    private static void CreateAllExamplesBenchmarkScene(bool replaceOpenScenes = false)
    {
        NewSceneMode sceneMode = Application.isBatchMode || replaceOpenScenes ? NewSceneMode.Single : NewSceneMode.Additive;
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, sceneMode);
        scene.name = "All Examples Benchmark";

        GameObject runnerObject = new GameObject("All Examples Benchmark Runner");
        RealtimeBenchmarkDashboard runner = runnerObject.AddComponent<RealtimeBenchmarkDashboard>();
        SerializedObject settings = new SerializedObject(runner);
        settings.FindProperty("_isAllExamplesComparison").boolValue = true;
        settings.FindProperty("_batchRunsPerStrategy").intValue = 1;
        settings.FindProperty("_dashboardPort").intValue = 8765;
        settings.FindProperty("_openDashboardInBrowser").boolValue = true;
        settings.ApplyModifiedPropertiesWithoutUndo();

        if (!EditorSceneManager.SaveScene(scene, AllExamplesScenePath))
            throw new System.Exception("Could not save all-examples comparison scene: " + AllExamplesScenePath);
        if (sceneMode == NewSceneMode.Additive)
            EditorSceneManager.CloseScene(scene, true);
    }
    #endregion

    private static string[] CreateOptimizationWorkloadScenes()
    {
        string[] algorithms = { "PSO", "RandomSearch" };
        string[] methods = { "SingleThreaded", "CSharpAsync", "CSharpParallel", "UnityJobs", "UnityJobsBurst" };
        string[] paths = new string[algorithms.Length * methods.Length];
        int pathIndex = 0;

        for (int algorithm = 0; algorithm < algorithms.Length; algorithm++)
        {
            for (int method = 0; method < methods.Length; method++)
            {
                string sceneName = algorithms[algorithm] + "_" + methods[method];
                string sceneFolder = algorithm == 0 ? PsoSceneFolder : RandomSearchSceneFolder;
                string path = sceneFolder + "/" + sceneName + ".unity";
                NewSceneMode sceneMode = Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive;
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, sceneMode);
                scene.name = sceneName;
                GameObject runnerObject = new GameObject("Batch Example");
                Component example = algorithm == 0
                    ? (Component)runnerObject.AddComponent<PSOProcessingExample>()
                    : runnerObject.AddComponent<RandomSearchProcessingExample>();
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
