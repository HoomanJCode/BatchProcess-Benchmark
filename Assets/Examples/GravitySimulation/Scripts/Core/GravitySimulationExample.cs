using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

/// <summary>Shared Inspector settings and visual setup for one gravity strategy example.</summary>
public abstract class GravitySimulationExample : MonoBehaviour
{
    [Header("Simulation")]
    [SerializeField, Min(1)] private int _bodyCount = 128;
    [SerializeField, Min(0f)] private float _gravity = 1.15f;
    [SerializeField, Min(0.001f)] private float _softening = 0.2f;
    [SerializeField, Min(0f)] private float _timeScale = 1f;
    [SerializeField] private SimulationDimension _dimension = SimulationDimension.ThreeD;
    [SerializeField] private Sprite _bodySprite;

    private static Material _gridMaterial;
    private readonly List<GravityBodyView> _views = new List<GravityBodyView>();
    private ProfilerMarker _stepMarker;

    protected BodyState[] States { get; set; }
    protected SimulationDimension Dimension { get { return _dimension; } }
    protected abstract SimulationMode Mode { get; }

    protected abstract void SimulateStep(float deltaTime, float gravity, float softening,
        SimulationDimension dimension);

    protected virtual void Awake()
    {
        ConfigureCamera();
        _stepMarker = new ProfilerMarker("GravitySimulation." + Mode);
        States = GravityMath.CreateInitialStates(Mathf.Max(1, _bodyCount), _dimension);
        CreateBodies();
        CreateGrid();
    }

    protected virtual void Update()
    {
        using (_stepMarker.Auto())
            SimulateStep(Time.deltaTime * _timeScale, _gravity, _softening, _dimension);
        ApplyBodyStates();
    }

    protected virtual void OnDestroy() { }

    private void CreateBodies()
    {
        for (int i = 0; i < States.Length; i++)
        {
            GameObject bodyObject = _dimension == SimulationDimension.TwoD
                ? new GameObject()
                : GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bodyObject.name = "Gravity Body " + i.ToString("D3");
            Collider collider = bodyObject.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            float scale = Mathf.Clamp(0.28f * Mathf.Pow(128f / States.Length, 0.22f), 0.11f, 0.32f);
            bodyObject.transform.localScale = Vector3.one * scale;
            var view = bodyObject.AddComponent<GravityBodyView>();
            view.Initialize(States[i], Color.HSVToRGB((i * 0.6180339f) % 1f, 0.68f, 1f),
                _dimension, _bodySprite);
            _views.Add(view);
        }
    }

    private void ApplyBodyStates()
    {
        int count = Mathf.Min(States.Length, _views.Count);
        for (int i = 0; i < count; i++)
            _views[i].ApplyState(States[i], _dimension);
    }

    private void ConfigureCamera()
    {
        Camera sceneCamera = Camera.main;
        if (sceneCamera == null)
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            sceneCamera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
        }

        sceneCamera.clearFlags = CameraClearFlags.SolidColor;
        sceneCamera.backgroundColor = new Color(0.035f, 0.055f, 0.09f);
        if (_dimension == SimulationDimension.TwoD)
        {
            sceneCamera.transform.position = new Vector3(0f, 0f, -16f);
            sceneCamera.transform.rotation = Quaternion.identity;
            sceneCamera.orthographic = true;
            sceneCamera.orthographicSize = 8f;
        }
        else
        {
            sceneCamera.transform.position = new Vector3(0f, 5.5f, -16f);
            sceneCamera.transform.rotation = Quaternion.Euler(16f, 0f, 0f);
            sceneCamera.orthographic = false;
            sceneCamera.fieldOfView = 55f;
        }
    }

    private void CreateGrid()
    {
        var gridRoot = new GameObject("Simulation Grid");
        float span = _dimension == SimulationDimension.TwoD ? 11f : 12f;
        const int lines = 20;
        for (int i = 0; i <= lines; i++)
        {
            float offset = Mathf.Lerp(-span * 0.5f, span * 0.5f, i / (float)lines);
            CreateGridLine(gridRoot.transform, span, offset, true);
            CreateGridLine(gridRoot.transform, span, offset, false);
        }

        if (_dimension == SimulationDimension.ThreeD)
            gridRoot.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    private static void CreateGridLine(Transform parent, float span, float offset, bool horizontal)
    {
        var lineObject = new GameObject("Grid line");
        lineObject.transform.SetParent(parent, false);
        var line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.positionCount = 2;
        line.SetPosition(0, horizontal
            ? new Vector3(-span * 0.5f, offset, 0f)
            : new Vector3(offset, -span * 0.5f, 0f));
        line.SetPosition(1, horizontal
            ? new Vector3(span * 0.5f, offset, 0f)
            : new Vector3(offset, span * 0.5f, 0f));
        line.startWidth = line.endWidth = 0.012f;
        if (_gridMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
                _gridMaterial = new Material(shader)
                {
                    name = "Gravity Grid Shared Material",
                    hideFlags = HideFlags.HideAndDontSave
                };
        }
        line.sharedMaterial = _gridMaterial;
        line.startColor = line.endColor = new Color(0.16f, 0.28f, 0.4f, 0.4f);
    }
}
