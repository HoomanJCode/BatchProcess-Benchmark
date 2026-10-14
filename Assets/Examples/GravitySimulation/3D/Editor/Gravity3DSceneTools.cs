#if UNITY_EDITOR
using UnityEditor;

/// <summary>Editor menu for regenerating only the 3D gravity teaching scenes.</summary>
public static class Gravity3DSceneTools
{
    #region Scene menu

    [MenuItem("Tools/Gravity 3D/Regenerate Scenes")]
    public static void RegenerateScenes()
    {
        GravitySceneGeneration.BuildThreeD();
    }

    #endregion
}
#endif