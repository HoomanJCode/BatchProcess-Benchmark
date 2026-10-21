using Unity.Profiling;
using UnityEngine;
using System.Threading.Tasks;

/// <summary>
/// Self-contained 3D gravity benchmark using C# Parallel.For.
/// The setup and complete O(N^2) gravity calculation are kept in this file for learning.
/// </summary>
public sealed class GravityBenchmark3DCSharpParallelExample : MonoBehaviour
{
    [Header("Benchmark settings")]
    [SerializeField, Min(1)] private int _ballCount = 128;
    [SerializeField] private bool _is2D = false;
    [SerializeField, Min(0f)] private float _gravity = 1.15f;
    [SerializeField, Min(0.001f)] private float _softening = 0.2f;
    [SerializeField, Min(0f)] private float _gravityScale = 1f;

    private Vector3[] _positions;
    private float[] _masses;
    private Vector3[] _accelerations;
    private readonly ProfilerMarker _gravityMarker = new ProfilerMarker("GravitySimulation.CSharpParallel");

    private void Awake()
    {
        // Read the scene-owned workload setting. No player preferences are involved.
        if (BenchmarkRunSettings.Active != null)
            _ballCount = BenchmarkRunSettings.Active.GravityBallCount;

        _ballCount = Mathf.Max(1, _ballCount);
        _positions = new Vector3[_ballCount];
        _masses = new float[_ballCount];
        _accelerations = new Vector3[_ballCount];

        // Place bodies on a repeatable spiral/sphere so each strategy starts with identical data.
        uint randomState = 0xC0FFEEu;
        for (int i = 0; i < _ballCount; i++)
        {
            float y = 1f - (i / (float)Mathf.Max(1, _ballCount - 1)) * 2f;
            float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float angle = i * 2.39996323f;
            Vector3 direction = _is2D
                ? new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f)
                : new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);

            randomState = randomState * 1664525u + 1013904223u;
            float randomRadius = (randomState & 0x00FFFFFFu) / 16777216f;
            _positions[i] = direction * (2f + randomRadius * 4.5f);
            _masses[i] = 0.7f + (i % 5) * 0.15f;
        }

    }

    // Unity calls FixedUpdate for every physics step; the profiler marker measures only this method's calculation.
    private void FixedUpdate()
    {
        using (_gravityMarker.Auto())
        {
            // Parallel.For splits target bodies across worker threads. Each iteration performs
            // the same complete source-body loop and writes only its own result slot.
            Parallel.For(0, _ballCount, target =>
            {
                Vector3 acceleration = Vector3.zero;
                for (int other = 0; other < _ballCount; other++)
                {
                    if (other == target)
                        continue;
                    Vector3 offset = _positions[other] - _positions[target];
                    if (_is2D)
                        offset.z = 0f;
                    float inverseDistance = 1f / Mathf.Sqrt(
                        offset.sqrMagnitude + _softening * _softening);
                    acceleration += offset * (_gravity * _gravityScale * _masses[other] *
                        inverseDistance * inverseDistance * inverseDistance);
                }
                _accelerations[target] = acceleration / Mathf.Max(1, _ballCount - 1);
            });
        }
    }

}
