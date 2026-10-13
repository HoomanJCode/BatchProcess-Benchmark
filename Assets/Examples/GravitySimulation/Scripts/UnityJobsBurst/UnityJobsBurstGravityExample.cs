/// <summary>Schedules Burst-compiled gravity force calculations with Unity Jobs.</summary>
public sealed class UnityJobsBurstGravityExample : GravitySimulationExample
{
    protected override SimulationMode Mode { get { return SimulationMode.UnityJobsBurst; } }
}
