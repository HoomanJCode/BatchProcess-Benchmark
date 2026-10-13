using System.Diagnostics;
using System.Threading.Tasks;
using Unity.Profiling;

/// <summary>Runs each gravity step on a managed worker task.</summary>
public sealed class CSharpAsyncGravityExample : GravitySimulationExample
{
    private static readonly ProfilerMarker WorkerMarker =
        new ProfilerMarker("GravitySimulation.CSharpAsync.Worker");
    private Task<BodyState[]> _pendingStep;

    protected override SimulationMode Mode { get { return SimulationMode.CSharpAsync; } }

    protected override void SimulateStep(float deltaTime, float gravity, float softening,
        SimulationDimension dimension)
    {
        if (_pendingStep != null && _pendingStep.IsCompleted)
        {
            States = _pendingStep.GetAwaiter().GetResult();
            _pendingStep = null;
        }

        if (_pendingStep == null)
        {
            BodyState[] snapshot = (BodyState[])States.Clone();
            _pendingStep = Task.Run(() =>
            {
                using (WorkerMarker.Auto())
                    return GravityMath.Advance(snapshot, deltaTime, gravity, softening, dimension);
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
