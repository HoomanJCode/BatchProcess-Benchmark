using Unity.Collections;
using Unity.Profiling;
using UnityEngine;
using Unity.Jobs;

/// <summary>
/// Self-contained 3D gravity benchmark using Unity Jobs.
/// The setup and complete O(N^2) gravity calculation are kept in this file for learning.
/// </summary>
public sealed class GravityBenchmark3DUnityJobsExample : MonoBehaviour
{
    [Header("Benchmark settings")]
    [SerializeField, Min(1)] private int _ballCount = 128;
    [SerializeField] private bool _is2D = false;
    [SerializeField, Min(0f)] private float _gravity = 1.15f;
    [SerializeField, Min(0.001f)] private float _softening = 0.2f;
    [SerializeField, Min(0f)] private float _gravityScale = 1f;

    private Vector3[] _positions;
    private float[] _masses;
    private readonly ProfilerMarker _gravityMarker = new ProfilerMarker("GravitySimulation.UnityJobs");
    private NativeArray<Vector3> _nativePositions;
    private NativeArray<float> _nativeMasses;
    private NativeArray<Vector3> _nativeAccelerations;

    private void Awake()
    {
        // Read the scene-owned workload setting. No player preferences are involved.
        if (BenchmarkRunSettings.Active != null)
            _ballCount = BenchmarkRunSettings.Active.GravityBallCount;

        _ballCount = Mathf.Max(1, _ballCount);
        _positions = new Vector3[_ballCount];
        _masses = new float[_ballCount];

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
        _nativePositions = new NativeArray<Vector3>(_positions, Allocator.Persistent);
        _nativeMasses = new NativeArray<float>(_masses, Allocator.Persistent);
        _nativeAccelerations = new NativeArray<Vector3>(_ballCount, Allocator.Persistent);
    }

    // Unity calls FixedUpdate for every physics step; the profiler marker measures only this method's calculation.
    private void FixedUpdate()
    {
        using (_gravityMarker.Auto())
        {
            // Each IJobParallelFor iteration computes one target body's gravity against every source.
            var gravityJob = new GravityJob
            {
                Positions = _nativePositions,
                Masses = _nativeMasses,
                Accelerations = _nativeAccelerations,
                Gravity = _gravity * _gravityScale,
                SofteningSquared = _softening * _softening,
                Is2D = _is2D
            };
            gravityJob.Schedule(_ballCount, 32).Complete();
        }
    }
    private void OnDestroy()
    {
        if (_nativePositions.IsCreated) _nativePositions.Dispose();
        if (_nativeMasses.IsCreated) _nativeMasses.Dispose();
        if (_nativeAccelerations.IsCreated) _nativeAccelerations.Dispose();
    }
    private struct GravityJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Vector3> Positions;
        [ReadOnly] public NativeArray<float> Masses;
        [WriteOnly] public NativeArray<Vector3> Accelerations;
        public float Gravity;
        public float SofteningSquared;
        public bool Is2D;

        public void Execute(int target)
        {
            // One job iteration calculates one target body's acceleration.
            Vector3 acceleration = Vector3.zero;
            for (int other = 0; other < Positions.Length; other++)
            {
                if (other == target)
                    continue;
                Vector3 offset = Positions[other] - Positions[target];
                if (Is2D)
                    offset.z = 0f;
                float inverseDistance = 1f / Mathf.Sqrt(offset.sqrMagnitude + SofteningSquared);
                acceleration += offset * (Gravity * Masses[other] *
                    inverseDistance * inverseDistance * inverseDistance);
            }
            Accelerations[target] = acceleration;
        }
    }
}
