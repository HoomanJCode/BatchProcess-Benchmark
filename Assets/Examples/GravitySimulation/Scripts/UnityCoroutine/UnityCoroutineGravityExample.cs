/// <summary>Splits gravity force calculations over coroutine frames.</summary>
public sealed class UnityCoroutineGravityExample : GravitySimulationExample
{
    protected override SimulationMode Mode { get { return SimulationMode.UnityCoroutine; } }
}
