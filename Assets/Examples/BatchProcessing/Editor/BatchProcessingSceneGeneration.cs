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
            "Batch_UnityJobsBurst"
        };
        System.Type[] exampleTypes =
        {
            typeof(BatchSingleThreadedExample),
            typeof(BatchCSharpAsyncExample),
            typeof(BatchCSharpParallelExample),
            typeof(BatchUnityJobsExample),
            typeof(BatchUnityCoroutineExample),
            typeof(BatchUnityJobsBurstExample)
        };

        List<EditorBuildSettingsScene> newScenes = new List<EditorBuildSettingsScene>();
        for (int i = 0; i < sceneNames.Length; i++)
        {
            string path = SceneFolder + "/" + sceneNames[i] + ".unity";
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = sceneNames[i];

            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.055f, 0.09f);
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            cameraObject.AddComponent<AudioListener>();

            GameObject exampleObject = new GameObject("Batch Example");
            exampleObject.AddComponent(exampleTypes[i]);

            if (!EditorSceneManager.SaveScene(scene, path))
                throw new System.Exception("Could not save batch example scene: " + path);
            newScenes.Add(new EditorBuildSettingsScene(path, true));
        }

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
}
