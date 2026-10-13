using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Creates the small standalone batch-processing example scene.</summary>
public static class BatchProcessingSceneGeneration
{
    private const string ScenePath = "Assets/Examples/BatchProcessing/Scenes/BatchProcessing.unity";

    [MenuItem("Tools/Batch Processing/Create Example Scene")]
    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "BatchProcessing";

        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.035f, 0.055f, 0.09f);
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        cameraObject.AddComponent<AudioListener>();

        var appObject = new GameObject("Batch Processing App");
        appObject.AddComponent<BatchProcessingApp>();

        if (!EditorSceneManager.SaveScene(scene, ScenePath))
            throw new System.Exception("Could not save batch-processing scene: " + ScenePath);

        var buildScenes = EditorBuildSettings.scenes.ToList();
        if (!buildScenes.Any(item => item.path == ScenePath))
            buildScenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = buildScenes.ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log("BATCH_PROCESSING_SCENE_GENERATED path=" + ScenePath);
    }
}
