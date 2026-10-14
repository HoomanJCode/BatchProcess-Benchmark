using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Creates one 2D and one 3D scene that run every gravity example in order.</summary>
public static class GravityBenchmarkSceneGeneration
{
    private const string SceneRoot = "Assets/Examples/GravitySimulation/Scenes";
    private const string TwoDPath = SceneRoot + "/2D/Gravity_Benchmark.unity";
    private const string ThreeDPath = SceneRoot + "/3D/Gravity_Benchmark.unity";
    private const string TwoDBenchmarkFolder = SceneRoot + "/2D/Benchmark";
    private const string ThreeDBenchmarkFolder = SceneRoot + "/3D/Benchmark";
    private const string TwoDExampleFolder = SceneRoot + "/2D";
    private const string ThreeDExampleFolder = SceneRoot + "/3D";
    private const int ComparisonBallCount = 128;
    private static readonly string[] ExampleScenes =
    {
        "SingleThreaded", "CSharpAsync", "CSharpParallel", "UnityJobs", "UnityCoroutine", "UnityJobsBurst"
    };

    [MenuItem("Tools/Gravity Simulation/Create Comparison Scenes")]
    public static void Build()
    {
        EnsureFolder(TwoDBenchmarkFolder, "2D");
        EnsureFolder(ThreeDBenchmarkFolder, "3D");

        // Make private copies so the comparison uses equal ball counts without
        // changing any of the individually editable teaching scenes.
        List<EditorBuildSettingsScene> benchmarkScenes = new List<EditorBuildSettingsScene>();
        foreach (string example in ExampleScenes)
        {
            string twoDSource = TwoDExampleFolder + "/Gravity_" + example + ".unity";
            string threeDSource = ThreeDExampleFolder + "/Gravity_" + example + ".unity";
            string twoDCopy = TwoDBenchmarkFolder + "/Gravity_" + example + ".unity";
            string threeDCopy = ThreeDBenchmarkFolder + "/Gravity_" + example + ".unity";
            CreateBenchmarkCopy(twoDSource, twoDCopy);
            CreateBenchmarkCopy(threeDSource, threeDCopy);
            benchmarkScenes.Add(new EditorBuildSettingsScene(twoDCopy, true));
            benchmarkScenes.Add(new EditorBuildSettingsScene(threeDCopy, true));
        }

        CreateScene(TwoDPath, "Gravity Benchmark (2D)", true);
        CreateScene(ThreeDPath, "Gravity Benchmark (3D)", false);

        // Preserve the project's other scenes while adding the two comparison launchers.
        List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes
            .Where(scene => !IsGeneratedComparisonScene(scene.path))
            .ToList();
        scenes.Insert(0, new EditorBuildSettingsScene(TwoDPath, true));
        scenes.Insert(1, new EditorBuildSettingsScene(ThreeDPath, true));
        scenes.AddRange(benchmarkScenes);
        EditorBuildSettings.scenes = scenes.ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log("Created 2D and 3D gravity comparison scenes.");
    }

    private static void EnsureFolder(string folderPath, string dimensionFolderName)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;
        string parent = SceneRoot + "/" + dimensionFolderName;
        AssetDatabase.CreateFolder(parent, "Benchmark");
    }

    private static bool IsGeneratedComparisonScene(string path)
    {
        return path == TwoDPath || path == ThreeDPath ||
            path.StartsWith(TwoDBenchmarkFolder + "/") ||
            path.StartsWith(ThreeDBenchmarkFolder + "/");
    }

    private static void CreateBenchmarkCopy(string sourcePath, string destinationPath)
    {
        // Rebuild only these generated copies; the user's source scenes are untouched.
        AssetDatabase.DeleteAsset(destinationPath);
        if (!AssetDatabase.CopyAsset(sourcePath, destinationPath))
            throw new System.Exception("Could not copy gravity example for benchmark: " + sourcePath);

        Scene copy = EditorSceneManager.OpenScene(destinationPath, OpenSceneMode.Additive);
        foreach (GameObject root in copy.GetRootGameObjects())
        {
            MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour == null)
                    continue;
                SerializedObject serializedBehaviour = new SerializedObject(behaviour);
                SerializedProperty ballCount = serializedBehaviour.FindProperty("_ballCount");
                if (ballCount == null)
                    continue;
                ballCount.intValue = ComparisonBallCount;
                serializedBehaviour.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        EditorSceneManager.MarkSceneDirty(copy);
        EditorSceneManager.SaveScene(copy, destinationPath);
        EditorSceneManager.CloseScene(copy, true);
    }

    private static void CreateScene(string path, string sceneName, bool is2D)
    {
        // Additive scene creation leaves the user's currently open scene untouched.
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        scene.name = sceneName;

        GameObject runnerObject = new GameObject("Gravity Benchmark Runner");
        GravityBenchmarkRunner runner = runnerObject.AddComponent<GravityBenchmarkRunner>();
        SerializedObject settings = new SerializedObject(runner);
        settings.FindProperty("_is2D").boolValue = is2D;
        settings.FindProperty("_warmupSeconds").intValue = 2;
        settings.FindProperty("_measurementSeconds").intValue = 8;
        settings.FindProperty("_dashboardPort").intValue = 8765;
        settings.FindProperty("_openDashboardInBrowser").boolValue = true;
        settings.ApplyModifiedPropertiesWithoutUndo();

        if (!EditorSceneManager.SaveScene(scene, path))
            throw new System.Exception("Could not save gravity comparison scene: " + path);
        EditorSceneManager.CloseScene(scene, true);
    }
}
