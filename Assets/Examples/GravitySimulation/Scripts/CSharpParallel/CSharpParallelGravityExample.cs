/// <summary>Uses the .NET thread pool to calculate body accelerations in parallel.</summary>
public sealed class CSharpParallelGravityExample : GravitySimulationExample
{
    protected override SimulationMode Mode { get { return SimulationMode.CSharpParallel; } }
}
