/// <summary>Calculates each body's force through its view module on the main thread.</summary>
public sealed class UnityModularMainThreadGravityExample : GravitySimulationExample
{
    protected override SimulationMode Mode { get { return SimulationMode.UnityModularMainThread; } }
}
