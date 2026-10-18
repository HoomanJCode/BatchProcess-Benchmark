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
        UnityJobsBurst
    }

    private enum SimulationDimension
    {
        TwoD,
        ThreeD
    }

    private const string SceneFolder = "Assets/Examples/GravitySimulation/Scenes";
    private const string TwoDSceneFolder = "Assets/Examples/GravitySimulation/2D/Scenes";
    private const string ThreeDSceneFolder = "Assets/Examples/GravitySimulation/3D/Scenes";

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

    public static void BuildTwoD()
    {
        BuildDimensionOnly(SimulationDimension.TwoD);
    }

    public static void BuildThreeD()
    {
        BuildDimensionOnly(SimulationDimension.ThreeD);
    }

    private static void BuildDimensionOnly(SimulationDimension dimension)
    {
        EnsureSceneFolders();
        string sceneFolder = dimension == SimulationDimension.TwoD ? TwoDSceneFolder : ThreeDSceneFolder;
        var generatedScenes = new List<EditorBuildSettingsScene>();

        foreach (SimulationMode mode in System.Enum.GetValues(typeof(SimulationMode)))
        {
            string path = sceneFolder + "/Gravity_" + SceneName(mode) + ".unity";
            CreateScene(path, "Gravity " + DisplayName(mode) + (dimension == SimulationDimension.TwoD ? " (2D)" : " (3D)"), mode, dimension);
            generatedScenes.Add(new EditorBuildSettingsScene(path, true));
        }

        var buildScenes = generatedScenes
            .Concat(EditorBuildSettings.scenes.Where(scene => !IsDimensionScene(scene.path, dimension)))
            .ToArray();
        EditorBuildSettings.scenes = buildScenes;
        AssetDatabase.SaveAssets();
        Debug.Log("GRAVITY_SCENES_REGENERATED dimension=" + dimension + " count=" + generatedScenes.Count);
    }

    private static bool IsDimensionScene(string path, SimulationDimension dimension)
    {
        string sceneFolder = dimension == SimulationDimension.TwoD ? TwoDSceneFolder : ThreeDSceneFolder;
        if (path == sceneFolder + "/Gravity_UnityCoroutine.unity" ||
            path == sceneFolder + "/Benchmark/Gravity_UnityCoroutine.unity")
            return true;
        foreach (SimulationMode mode in System.Enum.GetValues(typeof(SimulationMode)))
            if (path == sceneFolder + "/Gravity_" + SceneName(mode) + ".unity")
                return true;
        return false;
    }

    private static void EnsureSceneFolders()
    {
        // AssetDatabase creates folders inside the Unity project and keeps their metadata valid.
        if (!AssetDatabase.IsValidFolder("Assets/Examples/GravitySimulation/2D"))
            AssetDatabase.CreateFolder("Assets/Examples/GravitySimulation", "2D");
        if (!AssetDatabase.IsValidFolder("Assets/Examples/GravitySimulation/3D"))
            AssetDatabase.CreateFolder("Assets/Examples/GravitySimulation", "3D");
        if (!AssetDatabase.IsValidFolder(TwoDSceneFolder))
            AssetDatabase.CreateFolder("Assets/Examples/GravitySimulation/2D", "Scenes");
        if (!AssetDatabase.IsValidFolder(ThreeDSceneFolder))
            AssetDatabase.CreateFolder("Assets/Examples/GravitySimulation/3D", "Scenes");
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
            path == SceneFolder + "/Gravity_UnityModularMainThread.unity" ||
            path == TwoDSceneFolder + "/Gravity_UnityCoroutine.unity" ||
            path == ThreeDSceneFolder + "/Gravity_UnityCoroutine.unity" ||
            path == TwoDSceneFolder + "/Benchmark/Gravity_UnityCoroutine.unity" ||
            path == ThreeDSceneFolder + "/Benchmark/Gravity_UnityCoroutine.unity")
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
        // Start empty so each scene contains only what its 2D or 3D example needs.
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = sceneName;
        CreateCamera(dimension);
        // Only the 3D scenes use a sphere mesh that needs a directional light.
        if (dimension == SimulationDimension.ThreeD)
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
        // Each dimension owns its own copy so its scenes and scripts are easy to find together.
        string dimensionName = dimension == SimulationDimension.TwoD ? "2D" : "3D";
        string assemblyName = "GravitySimulation.Example." + dimensionName + "." + mode;
        string className = "Gravity" + dimensionName + GetControllerClassName(mode);
        System.Reflection.Assembly controllerAssembly = System.AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => assembly.GetName().Name == assemblyName);
        System.Type controllerType = controllerAssembly == null ? null : controllerAssembly.GetType(className);
        if (controllerType == null)
            throw new System.Exception("Could not find gravity example script: " + assemblyName + "::" + className);
        controllerObject.AddComponent(controllerType);

        // Store the dimension in the scene so each controller script can stay self-contained.
        var serializedController = new SerializedObject(controllerObject.GetComponent<MonoBehaviour>());
        serializedController.FindProperty("_is2D").boolValue = dimension == SimulationDimension.TwoD;
        serializedController.ApplyModifiedPropertiesWithoutUndo();
    }

    private static string GetControllerClassName(SimulationMode mode)
    {
        switch (mode)
        {
            case SimulationMode.CSharpAsync: return "CSharpAsyncGravityExample";
            case SimulationMode.CSharpParallel: return "CSharpParallelGravityExample";
            case SimulationMode.UnityJobs: return "UnityJobsGravityExample";
            case SimulationMode.UnityJobsBurst: return "UnityJobsBurstGravityExample";
            default: return "SingleThreadedGravityExample";
        }
    }

    private static string SceneName(SimulationMode mode)
    {
        switch (mode)
        {
            case SimulationMode.SingleThreaded: return "SingleThreaded";
            case SimulationMode.CSharpAsync: return "CSharpAsync";
            case SimulationMode.CSharpParallel: return "CSharpParallel";
            case SimulationMode.UnityJobs: return "UnityJobs";
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
