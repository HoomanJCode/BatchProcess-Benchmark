using Unity.Collections;
using Unity.Profiling;
using UnityEngine;

/// <summary>
/// Shared setup and gravity calculation used by the array-only benchmark examples.
/// It does not create per-body GameObjects or touch scene transforms.
/// </summary>
public abstract class GravityBenchmarkExampleBase : MonoBehaviour
{
    [SerializeField, Min(1)] private int _ballCount = 128;
    [SerializeField] private bool _is2D;
    [SerializeField, Min(0f)] private float _gravity = 1.15f;
    [SerializeField, Min(0.001f)] private float _softening = 0.2f;
    [SerializeField, Min(0f)] private float _gravityScale = 1f;

    protected Vector3[] Positions { get; private set; }
    protected float[] Masses { get; private set; }
    protected Vector3[] Accelerations { get; private set; }
    protected NativeArray<Vector3> NativePositions { get; private set; }
    protected NativeArray<float> NativeMasses { get; private set; }
    protected NativeArray<Vector3> NativeAccelerations { get; private set; }
    protected float Gravity => _gravity * _gravityScale;
    protected float SofteningSquared => _softening * _softening;
    protected int BallCount => _ballCount;
    protected bool Is2D => _is2D;

    private ProfilerMarker _gravityMarker;

    protected abstract string MarkerName { get; }
    protected abstract void CalculateGravity();
    protected virtual bool UsesUnityJobs => false;

    protected virtual void Awake()
    {
        _ballCount = BenchmarkRunSettings.GetGravityBallCount(_ballCount);
        Positions = new Vector3[_ballCount];
        Masses = new float[_ballCount];
        Accelerations = new Vector3[_ballCount];
        _gravityMarker = new ProfilerMarker(MarkerName);

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
            Positions[i] = direction * (2f + randomRadius * 4.5f);
            Masses[i] = 0.7f + (i % 5) * 0.15f;
        }

        if (UsesUnityJobs)
        {
            NativePositions = new NativeArray<Vector3>(Positions, Allocator.Persistent);
            NativeMasses = new NativeArray<float>(Masses, Allocator.Persistent);
            NativeAccelerations = new NativeArray<Vector3>(_ballCount, Allocator.Persistent);
        }
    }

    protected virtual void FixedUpdate()
    {
        using (_gravityMarker.Auto())
            CalculateGravity();
    }

    protected void CalculateTargetAcceleration(int target)
    {
        Vector3 acceleration = Vector3.zero;
        for (int other = 0; other < _ballCount; other++)
        {
            if (other == target)
                continue;

            Vector3 offset = Positions[other] - Positions[target];
            if (_is2D)
                offset.z = 0f;
            float inverseDistance = 1f / Mathf.Sqrt(offset.sqrMagnitude + SofteningSquared);
            acceleration += offset * (Gravity * Masses[other] *
                inverseDistance * inverseDistance * inverseDistance);
        }
        Accelerations[target] = acceleration;
    }

    protected virtual void OnDestroy()
    {
        if (NativePositions.IsCreated)
            NativePositions.Dispose();
        if (NativeMasses.IsCreated)
            NativeMasses.Dispose();
        if (NativeAccelerations.IsCreated)
            NativeAccelerations.Dispose();
    }
}
