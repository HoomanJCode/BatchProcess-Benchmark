using System.Collections;
using Unity.Profiling;
using UnityEngine;

/// <summary>Splits gravity force calculations over coroutine frames.</summary>
public sealed class UnityCoroutineGravityExample : GravitySimulationExample
{
    private const int BodiesPerFrame = 16;
    private static readonly ProfilerMarker ChunkMarker =
        new ProfilerMarker("GravitySimulation.UnityCoroutine.Chunk");
    private Coroutine _stepCoroutine;
    private Vector3[] _lastAccelerations;

    protected override SimulationMode Mode { get { return SimulationMode.UnityCoroutine; } }

    protected override void SimulateStep(float gravity, float softening, SimulationDimension dimension)
    {
        if (_lastAccelerations == null)
            _lastAccelerations = new Vector3[States.Length];

        // Apply the last complete result on each physics tick while this result is built in chunks.
        SubmitGravityAccelerations(_lastAccelerations);
        if (_stepCoroutine == null)
        {
            BodyState[] snapshot = (BodyState[])States.Clone();
            _stepCoroutine = StartCoroutine(CalculateOverFrames(snapshot, gravity, softening, dimension));
        }
    }

    private IEnumerator CalculateOverFrames(BodyState[] snapshot, float gravity, float softening,
        SimulationDimension dimension)
    {
        yield return null;
        bool is2D = dimension == SimulationDimension.TwoD;
        var accelerations = new Vector3[snapshot.Length];
        for (int start = 0; start < snapshot.Length; start += BodiesPerFrame)
        {
            int end = Mathf.Min(start + BodiesPerFrame, snapshot.Length);
            using (ChunkMarker.Auto())
            {
                for (int i = start; i < end; i++)
                    accelerations[i] = GravityMath.Acceleration(snapshot, i, gravity, softening, is2D);
            }
            if (end < snapshot.Length)
                yield return null;
        }

        _lastAccelerations = accelerations;
        _stepCoroutine = null;
    }

    protected override void OnDestroy()
    {
        if (_stepCoroutine != null)
            StopCoroutine(_stepCoroutine);
        base.OnDestroy();
    }
}
