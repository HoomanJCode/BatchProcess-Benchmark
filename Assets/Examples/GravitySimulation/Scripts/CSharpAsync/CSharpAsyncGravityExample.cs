/// <summary>Runs each gravity step on a managed worker task.</summary>
public sealed class CSharpAsyncGravityExample : GravitySimulationExample
{
    protected override SimulationMode Mode { get { return SimulationMode.CSharpAsync; } }
}
