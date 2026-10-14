using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Rebuilds clean, standalone scenes for the gravity execution examples.</summary>
public static class GravitySceneGeneration
{
    // These enums are editor-only: each runtime scene has its own simple controller script.
    private enum SimulationMode
    {
        SingleThreaded,
        CSharpAsync,
        CSharpParallel,
        UnityJobs,
        UnityCoroutine,
        UnityJobsBurst
    }

    private enum SimulationDimension
    {
        TwoD,
        ThreeD
    }

    private const string SceneFolder = "Assets/Examples/GravitySimulation/Scenes";
    private const string TwoDSceneFolder = SceneFolder + "/2D";
    private const string ThreeDSceneFolder = SceneFolder + "/3D";

    [MenuItem("Tools/Gravity Simulation/Regenerate Example Scenes")]
    public static void Build()
    {
        // Build one 2D and one 3D scene for every strategy in the enum.
        EnsureSceneFolders();
        var generatedScenes = new List<EditorBuildSettingsScene>();

        foreach (SimulationMode mode in System.Enum.GetValues(typeof(SimulationMode)))
        {
            string sceneFile = "Gravity_" + SceneName(mode) + ".unity";
            string twoDPath = TwoDSceneFolder + "/" + sceneFile;
            string threeDPath = ThreeDSceneFolder + "/" + sceneFile;

            CreateScene(twoDPath, "Gravity " + DisplayName(mode) + " (2D)", mode,
                SimulationDimension.TwoD);
            CreateScene(threeDPath, "Gravity " + DisplayName(mode) + " (3D)", mode,
                SimulationDimension.ThreeD);
            generatedScenes.Add(new EditorBuildSettingsScene(twoDPath, true));
            generatedScenes.Add(new EditorBuildSettingsScene(threeDPath, true));
        }

        // Remove the previous flat scene layout after all replacement scenes are saved.
        DeleteLegacyScenes();

        // Keep gravity scenes first and preserve any unrelated scenes already in Build Settings.
        var buildScenes = generatedScenes
            .Concat(EditorBuildSettings.scenes.Where(scene => !IsManagedGravityScene(scene.path)))
            .ToArray();
        EditorBuildSettings.scenes = buildScenes;
        AssetDatabase.SaveAssets();
        Debug.Log("GRAVITY_SCENES_REGENERATED count=" + generatedScenes.Count);
    }

    private static void EnsureSceneFolders()
    {
        // AssetDatabase creates folders inside the Unity project and keeps their metadata valid.
        if (!AssetDatabase.IsValidFolder(SceneFolder))
            AssetDatabase.CreateFolder("Assets/Examples/GravitySimulation", "Scenes");
        if (!AssetDatabase.IsValidFolder(TwoDSceneFolder))
            AssetDatabase.CreateFolder(SceneFolder, "2D");
        if (!AssetDatabase.IsValidFolder(ThreeDSceneFolder))
            AssetDatabase.CreateFolder(SceneFolder, "3D");
    }

    private static void DeleteLegacyScenes()
    {
        // Remove scenes from the old flat layout after their replacements have been saved.
        AssetDatabase.DeleteAsset(SceneFolder + "/SampleScene.unity");
        AssetDatabase.DeleteAsset(SceneFolder + "/Gravity_UnityModularMainThread.unity");
        foreach (SimulationMode mode in System.Enum.GetValues(typeof(SimulationMode)))
            AssetDatabase.DeleteAsset(SceneFolder + "/Gravity_" + SceneName(mode) + ".unity");
    }

    private static bool IsManagedGravityScene(string path)
    {
        // This lets regeneration replace gravity scenes without removing other project scenes.
        if (path == SceneFolder + "/SampleScene.unity" ||
            path == SceneFolder + "/Gravity_UnityModularMainThread.unity")
            return true;

        foreach (SimulationMode mode in System.Enum.GetValues(typeof(SimulationMode)))
        {
            string sceneFile = "Gravity_" + SceneName(mode) + ".unity";
            if (path == SceneFolder + "/" + sceneFile ||
                path == TwoDSceneFolder + "/" + sceneFile ||
                path == ThreeDSceneFolder + "/" + sceneFile)
                return true;
        }

        return false;
    }

    private static void CreateScene(string path, string sceneName, SimulationMode mode,
        SimulationDimension dimension)
    {
        // Start with an empty scene so each comparison contains only its camera, light, and controller.
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = sceneName;
        CreateCamera(dimension);
        // The 2D scene uses 2D physics, but the sphere mesh still needs a light to be visible.
        CreateKeyLight();
        CreateController(mode, dimension);

        if (!EditorSceneManager.SaveScene(scene, path))
            throw new System.Exception("Could not create example scene: " + path);
    }

    private static void CreateCamera(SimulationDimension dimension)
    {
        // A top-down orthographic camera suits 2D; perspective gives the 3D scene depth.
        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.015f, 0.025f, 0.07f);
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 100f;
        cameraObject.AddComponent<AudioListener>();

        if (dimension == SimulationDimension.TwoD)
        {
            camera.orthographic = true;
            camera.orthographicSize = 8f;
            cameraObject.transform.SetPositionAndRotation(new Vector3(0f, 0f, -16f), Quaternion.identity);
        }
        else
        {
            camera.orthographic = false;
            camera.fieldOfView = 55f;
            cameraObject.transform.SetPositionAndRotation(new Vector3(0f, 5.5f, -16f),
                Quaternion.Euler(16f, 0f, 0f));
        }
    }

    private static void CreateKeyLight()
    {
        // Directional light keeps the generated sphere meshes visible and shaded.
        var lightObject = new GameObject("Key Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(0.72f, 0.84f, 1f);
        light.intensity = 1.2f;
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(42f, -28f, 0f);
    }

    private static void CreateController(SimulationMode mode, SimulationDimension dimension)
    {
        // Attach only the script that demonstrates the selected processing strategy.
        var controllerObject = new GameObject("Gravity Simulation");
        switch (mode)
        {
            case SimulationMode.CSharpAsync:
                controllerObject.AddComponent<CSharpAsyncGravityExample>();
                break;
            case SimulationMode.CSharpParallel:
                controllerObject.AddComponent<CSharpParallelGravityExample>();
                break;
            case SimulationMode.UnityJobs:
                controllerObject.AddComponent<UnityJobsGravityExample>();
                break;
            case SimulationMode.UnityCoroutine:
                controllerObject.AddComponent<UnityCoroutineGravityExample>();
                break;
            case SimulationMode.UnityJobsBurst:
                controllerObject.AddComponent<UnityJobsBurstGravityExample>();
                break;
            default:
                controllerObject.AddComponent<SingleThreadedGravityExample>();
                break;
        }

        // Store the dimension in the scene so each controller script can stay self-contained.
        var serializedController = new SerializedObject(controllerObject.GetComponent<MonoBehaviour>());
        serializedController.FindProperty("_is2D").boolValue = dimension == SimulationDimension.TwoD;
        serializedController.ApplyModifiedPropertiesWithoutUndo();
    }

    private static string SceneName(SimulationMode mode)
    {
        switch (mode)
        {
            case SimulationMode.SingleThreaded: return "SingleThreaded";
            case SimulationMode.CSharpAsync: return "CSharpAsync";
            case SimulationMode.CSharpParallel: return "CSharpParallel";
            case SimulationMode.UnityJobs: return "UnityJobs";
            case SimulationMode.UnityCoroutine: return "UnityCoroutine";
            case SimulationMode.UnityJobsBurst: return "UnityJobsBurst";
            default: return mode.ToString();
        }
    }

    private static string DisplayName(SimulationMode mode)
    {
        return string.Concat(mode.ToString().Select((character, index) =>
            index > 0 && char.IsUpper(character) ? " " + character : character.ToString()));
    }
}
