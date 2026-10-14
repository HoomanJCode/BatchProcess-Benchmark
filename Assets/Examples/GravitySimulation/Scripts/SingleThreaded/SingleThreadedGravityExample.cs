/// <summary>Runs gravity force calculations serially on Unity's main thread.</summary>
public sealed class SingleThreadedGravityExample : GravitySimulationExample
{
    protected override SimulationMode Mode { get { return SimulationMode.SingleThreaded; } }

    protected override void SimulateStep(float gravity, float softening, SimulationDimension dimension)
    {
        SubmitGravityAccelerations(GravityMath.CalculateAccelerations(States, gravity, softening, dimension));
    }
}
