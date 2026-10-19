using System.Threading.Tasks;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;

public enum GravityBenchmarkMethod
{
    SingleThreaded,
    CSharpAsync,
    CSharpParallel,
    UnityJobs,
    UnityJobsBurst
}

/// <summary>
/// Array-only gravity benchmark driver. Unlike the teaching simulations, this component
/// creates no per-body GameObjects, renderers, colliders, or rigidbodies.
/// </summary>
public sealed class GravityCalculationBenchmarkExample : MonoBehaviour
{
    [SerializeField, Min(1)] private int _ballCount = 128;
    [SerializeField] private bool _is2D;
    [SerializeField] private GravityBenchmarkMethod _method;
    [SerializeField, Min(0f)] private float _gravity = 1.15f;
    [SerializeField, Min(0.001f)] private float _softening = 0.2f;
    [SerializeField, Min(0f)] private float _gravityScale = 1f;

    private Vector3[] _positions;
    private float[] _masses;
    private Vector3[] _accelerations;
    private NativeArray<Vector3> _nativePositions;
    private NativeArray<float> _nativeMasses;
    private NativeArray<Vector3> _nativeAccelerations;
    private ProfilerMarker _gravityMarker;

    private void Awake()
    {
        _ballCount = BenchmarkRunSettings.GetGravityBallCount(_ballCount);
        _positions = new Vector3[_ballCount];
        _masses = new float[_ballCount];
        _accelerations = new Vector3[_ballCount];
        _gravityMarker = new ProfilerMarker("GravitySimulation." + _method);

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

        if (_method == GravityBenchmarkMethod.UnityJobs || _method == GravityBenchmarkMethod.UnityJobsBurst)
        {
            _nativePositions = new NativeArray<Vector3>(_positions, Allocator.Persistent);
            _nativeMasses = new NativeArray<float>(_masses, Allocator.Persistent);
            _nativeAccelerations = new NativeArray<Vector3>(_ballCount, Allocator.Persistent);
        }
    }

    private void FixedUpdate()
    {
        using (_gravityMarker.Auto())
        {
            switch (_method)
            {
                case GravityBenchmarkMethod.CSharpAsync:
                    CalculateWithTasks();
                    break;
                case GravityBenchmarkMethod.CSharpParallel:
                    Parallel.For(0, _ballCount, CalculateTargetAcceleration);
                    break;
                case GravityBenchmarkMethod.UnityJobs:
                    ScheduleJobs(false);
                    break;
                case GravityBenchmarkMethod.UnityJobsBurst:
                    ScheduleJobs(true);
                    break;
                default:
                    for (int target = 0; target < _ballCount; target++)
                        CalculateTargetAcceleration(target);
                    break;
            }
        }
    }

    private void CalculateWithTasks()
    {
        int workerCount = Mathf.Min(_ballCount, Mathf.Min(8, Mathf.Max(1, System.Environment.ProcessorCount)));
        Task[] tasks = new Task[workerCount];
        for (int worker = 0; worker < workerCount; worker++)
        {
            int firstTarget = _ballCount * worker / workerCount;
            int afterLastTarget = _ballCount * (worker + 1) / workerCount;
            tasks[worker] = Task.Run(() =>
            {
                for (int target = firstTarget; target < afterLastTarget; target++)
                    CalculateTargetAcceleration(target);
            });
        }

        Task.WhenAll(tasks).GetAwaiter().GetResult();
    }

    private void CalculateTargetAcceleration(int target)
    {
        Vector3 acceleration = Vector3.zero;
        for (int other = 0; other < _ballCount; other++)
        {
            if (other == target)
                continue;

            Vector3 offset = _positions[other] - _positions[target];
            if (_is2D)
                offset.z = 0f;
            float inverseDistance = 1f / Mathf.Sqrt(offset.sqrMagnitude + _softening * _softening);
            acceleration += offset * (_gravity * _gravityScale * _masses[other] *
                inverseDistance * inverseDistance * inverseDistance);
        }
        _accelerations[target] = acceleration;
    }

    private void ScheduleJobs(bool burst)
    {
        if (burst)
        {
            var job = new GravityBurstJob
            {
                Positions = _nativePositions,
                Masses = _nativeMasses,
                Accelerations = _nativeAccelerations,
                Gravity = _gravity * _gravityScale,
                SofteningSquared = _softening * _softening,
                Is2D = _is2D
            };
            job.Schedule(_ballCount, 32).Complete();
        }
        else
        {
            var job = new GravityJob
            {
                Positions = _nativePositions,
                Masses = _nativeMasses,
                Accelerations = _nativeAccelerations,
                Gravity = _gravity * _gravityScale,
                SofteningSquared = _softening * _softening,
                Is2D = _is2D
            };
            job.Schedule(_ballCount, 32).Complete();
        }
    }

    private void OnDestroy()
    {
        if (_nativePositions.IsCreated)
            _nativePositions.Dispose();
        if (_nativeMasses.IsCreated)
            _nativeMasses.Dispose();
        if (_nativeAccelerations.IsCreated)
            _nativeAccelerations.Dispose();
    }

    private struct GravityJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Vector3> Positions;
        [ReadOnly] public NativeArray<float> Masses;
        [WriteOnly] public NativeArray<Vector3> Accelerations;
        public float Gravity;
        public float SofteningSquared;
        public bool Is2D;

        public void Execute(int index)
        {
            Vector3 acceleration = Vector3.zero;
            for (int other = 0; other < Positions.Length; other++)
            {
                if (other == index)
                    continue;
                Vector3 offset = Positions[other] - Positions[index];
                if (Is2D)
                    offset.z = 0f;
                float inverseDistance = 1f / Mathf.Sqrt(offset.sqrMagnitude + SofteningSquared);
                acceleration += offset * (Gravity * Masses[other] *
                    inverseDistance * inverseDistance * inverseDistance);
            }
            Accelerations[index] = acceleration;
        }
    }

    [BurstCompile]
    private struct GravityBurstJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Vector3> Positions;
        [ReadOnly] public NativeArray<float> Masses;
        [WriteOnly] public NativeArray<Vector3> Accelerations;
        public float Gravity;
        public float SofteningSquared;
        public bool Is2D;

        public void Execute(int index)
        {
            Vector3 acceleration = Vector3.zero;
            for (int other = 0; other < Positions.Length; other++)
            {
                if (other == index)
                    continue;
                Vector3 offset = Positions[other] - Positions[index];
                if (Is2D)
                    offset.z = 0f;
                float inverseDistance = 1f / Mathf.Sqrt(offset.sqrMagnitude + SofteningSquared);
                acceleration += offset * (Gravity * Masses[other] *
                    inverseDistance * inverseDistance * inverseDistance);
            }
            Accelerations[index] = acceleration;
        }
    }
}
