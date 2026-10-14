using System.Threading.Tasks;
using Unity.Profiling;
using UnityEngine;

/// <summary>Runs each gravity step on a managed worker task.</summary>
public sealed class CSharpAsyncGravityExample : GravitySimulationExample
{
    private static readonly ProfilerMarker WorkerMarker =
        new ProfilerMarker("GravitySimulation.CSharpAsync.Worker");
    private Task<Vector3[]> _pendingStep;
    private Vector3[] _lastAccelerations;

    protected override SimulationMode Mode { get { return SimulationMode.CSharpAsync; } }

    protected override void SimulateStep(float gravity, float softening, SimulationDimension dimension)
    {
        if (_lastAccelerations == null)
            _lastAccelerations = new Vector3[States.Length];

        if (_pendingStep != null && _pendingStep.IsCompleted)
        {
            _lastAccelerations = _pendingStep.GetAwaiter().GetResult();
            _pendingStep = null;
        }

        // Keep applying the last completed result while the worker calculates the next snapshot.
        SubmitGravityAccelerations(_lastAccelerations);
        if (_pendingStep == null)
        {
            BodyState[] snapshot = (BodyState[])States.Clone();
            _pendingStep = Task.Run(() =>
            {
                using (WorkerMarker.Auto())
                    return GravityMath.CalculateAccelerations(snapshot, gravity, softening, dimension);
            });
        }
    }

    protected override void OnDestroy()
    {
        if (_pendingStep != null)
            _pendingStep.GetAwaiter().GetResult();
        base.OnDestroy();
    }
}
