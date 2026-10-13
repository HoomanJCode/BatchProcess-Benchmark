/// <summary>Schedules gravity force calculations with Unity Jobs without Burst.</summary>
public sealed class UnityJobsGravityExample : GravitySimulationExample
{
    protected override SimulationMode Mode { get { return SimulationMode.UnityJobs; } }
}
