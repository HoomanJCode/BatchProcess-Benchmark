#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>Editor menu for opening the Random Search example scene.</summary>
public static class RandomSearchSceneTools
{
    #region Scene menu

    [MenuItem("Tools/Random Search/Open Example Scene")]
    public static void OpenExampleScene()
    {
        EditorSceneManager.OpenScene("Assets/Examples/RandomSearchOptimization/Scenes/RandomSearch_Optimization.unity");
    }

    #endregion
}
#endif