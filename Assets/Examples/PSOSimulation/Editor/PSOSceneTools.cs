#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>Editor menu for opening the particle swarm example scene.</summary>
public static class PSOSceneTools
{
    #region Scene menu

    [MenuItem("Tools/PSO Simulation/Open Example Scene")]
    public static void OpenExampleScene()
    {
        EditorSceneManager.OpenScene("Assets/Examples/PSOSimulation/Scenes/PSO_Simulation.unity");
    }

    #endregion
}
#endif