using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Creates one 2D and one 3D scene that run every gravity example in order.</summary>
public static class GravityBenchmarkSceneGeneration
{
    private const string SceneRoot = "Assets/Examples/GravitySimulation";
    private const string TwoDPath = SceneRoot + "/2D/Scenes/Gravity_Benchmark.unity";
    private const string ThreeDPath = SceneRoot + "/3D/Scenes/Gravity_Benchmark.unity";
    private const string TwoDBenchmarkFolder = SceneRoot + "/2D/Scenes/Benchmark";
    private const string ThreeDBenchmarkFolder = SceneRoot + "/3D/Scenes/Benchmark";
    private const string TwoDExampleFolder = SceneRoot + "/2D/Scenes";
    private const string ThreeDExampleFolder = SceneRoot + "/3D/Scenes";
    private const int ComparisonBallCount = 128;
    private static readonly string[] ExampleScenes =
    {
        "SingleThreaded", "CSharpAsync", "CSharpParallel", "UnityJobs", "UnityJobsBurst"
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
        // Benchmark copies use a separate array-only driver; the user's visual scenes remain untouched.
        AssetDatabase.DeleteAsset(destinationPath);
        if (!AssetDatabase.CopyAsset(sourcePath, destinationPath))
            throw new System.Exception("Could not copy gravity example for benchmark: " + sourcePath);

        Scene copy = EditorSceneManager.OpenScene(destinationPath, OpenSceneMode.Additive);
        string sceneName = Path.GetFileNameWithoutExtension(destinationPath);
        string methodName = sceneName.Substring("Gravity_".Length);
        int methodIndex = System.Array.IndexOf(ExampleScenes, methodName);
        if (methodIndex < 0)
            throw new System.Exception("Unknown gravity benchmark method scene: " + destinationPath);

        bool is2D = destinationPath.StartsWith(TwoDBenchmarkFolder);
        bool replacedExample = false;
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

                GameObject exampleObject = behaviour.gameObject;
                UnityEngine.Object.DestroyImmediate(behaviour, true);
                GravityCalculationBenchmarkExample benchmarkExample =
                    exampleObject.AddComponent<GravityCalculationBenchmarkExample>();
                SerializedObject serializedBenchmark = new SerializedObject(benchmarkExample);
                serializedBenchmark.FindProperty("_ballCount").intValue = ComparisonBallCount;
                serializedBenchmark.FindProperty("_is2D").boolValue = is2D;
                serializedBenchmark.FindProperty("_method").enumValueIndex = methodIndex;
                serializedBenchmark.ApplyModifiedPropertiesWithoutUndo();
                replacedExample = true;
                break;
            }
            if (replacedExample)
                break;
        }

        if (!replacedExample)
            throw new System.Exception("Could not find gravity example component in " + destinationPath);

        EditorSceneManager.MarkSceneDirty(copy);
        EditorSceneManager.SaveScene(copy, destinationPath);
        EditorSceneManager.CloseScene(copy, true);
    }

    private static void CreateScene(string path, string sceneName, bool is2D)
    {
        Scene openScene = EditorSceneManager.GetSceneByPath(path);
        if (openScene.IsValid() && openScene.isLoaded)
            return;

        // Additive scene creation leaves the user's currently open scene untouched.
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        scene.name = sceneName;

        GameObject runnerObject = new GameObject("Gravity Benchmark Runner");
        RealtimeBenchmarkDashboard runner = runnerObject.AddComponent<RealtimeBenchmarkDashboard>();
        new GameObject("Benchmark Run Settings").AddComponent<BenchmarkRunSettings>();
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
