#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>Editor menu for opening the simple iteration comparison scene.</summary>
public static class SimpleIterationSceneTools
{
    #region Scene menu

    [MenuItem("Tools/Simple Iteration/Open Comparison Scene")]
    public static void OpenComparisonScene()
    {
        EditorSceneManager.OpenScene("Assets/Examples/SimpleIteration/Scenes/SimpleIteration_Benchmark.unity");
    }

    #endregion
}
#endif