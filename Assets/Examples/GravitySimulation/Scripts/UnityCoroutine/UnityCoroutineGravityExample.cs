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

    protected override SimulationMode Mode { get { return SimulationMode.UnityCoroutine; } }

    protected override void SimulateStep(float deltaTime, float gravity, float softening,
        SimulationDimension dimension)
    {
        if (_stepCoroutine == null)
            _stepCoroutine = StartCoroutine(AdvanceOverFrames(deltaTime, gravity, softening, dimension));
    }

    private IEnumerator AdvanceOverFrames(float deltaTime, float gravity, float softening,
        SimulationDimension dimension)
    {
        yield return null;
        bool is2D = dimension == SimulationDimension.TwoD;
        var accelerations = new Vector3[States.Length];
        for (int start = 0; start < States.Length; start += BodiesPerFrame)
        {
            int end = Mathf.Min(start + BodiesPerFrame, States.Length);
            using (ChunkMarker.Auto())
            {
                for (int i = start; i < end; i++)
                    accelerations[i] = GravityMath.Acceleration(States, i, gravity, softening, is2D);
            }
            if (end < States.Length)
                yield return null;
        }

        States = GravityMath.Integrate(States, accelerations, deltaTime, is2D);
        _stepCoroutine = null;
    }

    protected override void OnDestroy()
    {
        if (_stepCoroutine != null)
            StopCoroutine(_stepCoroutine);
        base.OnDestroy();
    }
}
