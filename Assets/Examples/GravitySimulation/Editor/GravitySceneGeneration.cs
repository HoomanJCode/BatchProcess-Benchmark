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
    private const string SceneFolder = "Assets/Examples/GravitySimulation/Scenes";
    private const string TwoDSceneFolder = SceneFolder + "/2D";
    private const string ThreeDSceneFolder = SceneFolder + "/3D";

    [MenuItem("Tools/Gravity Simulation/Regenerate Example Scenes")]
    public static void Build()
    {
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

        // Keep the gravity examples first (as before) and preserve the order of other project scenes.
        var buildScenes = generatedScenes
            .Concat(EditorBuildSettings.scenes.Where(scene => !IsManagedGravityScene(scene.path)))
            .ToArray();
        EditorBuildSettings.scenes = buildScenes;
        AssetDatabase.SaveAssets();
        Debug.Log("GRAVITY_SCENES_REGENERATED count=" + generatedScenes.Count);
    }

    private static void EnsureSceneFolders()
    {
        if (!AssetDatabase.IsValidFolder(SceneFolder))
            AssetDatabase.CreateFolder("Assets/Examples/GravitySimulation", "Scenes");
        if (!AssetDatabase.IsValidFolder(TwoDSceneFolder))
            AssetDatabase.CreateFolder(SceneFolder, "2D");
        if (!AssetDatabase.IsValidFolder(ThreeDSceneFolder))
            AssetDatabase.CreateFolder(SceneFolder, "3D");
    }

    private static void DeleteLegacyScenes()
    {
        AssetDatabase.DeleteAsset(SceneFolder + "/SampleScene.unity");
        AssetDatabase.DeleteAsset(SceneFolder + "/Gravity_UnityModularMainThread.unity");
        foreach (SimulationMode mode in System.Enum.GetValues(typeof(SimulationMode)))
            AssetDatabase.DeleteAsset(SceneFolder + "/Gravity_" + SceneName(mode) + ".unity");
    }

    private static bool IsManagedGravityScene(string path)
    {
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
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = sceneName;
        CreateCamera(dimension);
        if (dimension == SimulationDimension.ThreeD)
            CreateKeyLight();
        CreateController(mode, dimension);

        if (!EditorSceneManager.SaveScene(scene, path))
            throw new System.Exception("Could not create example scene: " + path);
    }

    private static void CreateCamera(SimulationDimension dimension)
    {
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
        var controllerObject = new GameObject("Gravity Simulation");
        GravitySimulationExample controller;
        switch (mode)
        {
            case SimulationMode.CSharpAsync: controller = controllerObject.AddComponent<CSharpAsyncGravityExample>(); break;
            case SimulationMode.CSharpParallel: controller = controllerObject.AddComponent<CSharpParallelGravityExample>(); break;
            case SimulationMode.UnityJobs: controller = controllerObject.AddComponent<UnityJobsGravityExample>(); break;
            case SimulationMode.UnityCoroutine: controller = controllerObject.AddComponent<UnityCoroutineGravityExample>(); break;
            case SimulationMode.UnityJobsBurst: controller = controllerObject.AddComponent<UnityJobsBurstGravityExample>(); break;
            default: controller = controllerObject.AddComponent<SingleThreadedGravityExample>(); break;
        }

        var serializedController = new SerializedObject(controller);
        serializedController.FindProperty("_dimension").enumValueIndex = (int)dimension;
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
