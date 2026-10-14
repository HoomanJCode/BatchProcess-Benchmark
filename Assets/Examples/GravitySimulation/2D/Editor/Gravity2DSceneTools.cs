#if UNITY_EDITOR
using UnityEditor;

/// <summary>Editor menu for regenerating only the 2D gravity teaching scenes.</summary>
public static class Gravity2DSceneTools
{
    #region Scene menu

    [MenuItem("Tools/Gravity 2D/Regenerate Scenes")]
    public static void RegenerateScenes()
    {
        GravitySceneGeneration.BuildTwoD();
    }

    #endregion
}
#endif