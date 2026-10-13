using System.Threading.Tasks;
using UnityEngine;

/// <summary>Uses the .NET thread pool to calculate body accelerations in parallel.</summary>
public sealed class CSharpParallelGravityExample : GravitySimulationExample
{
    protected override SimulationMode Mode { get { return SimulationMode.CSharpParallel; } }

    protected override void SimulateStep(float deltaTime, float gravity, float softening,
        SimulationDimension dimension)
    {
        bool is2D = dimension == SimulationDimension.TwoD;
        var accelerations = new Vector3[States.Length];
        Parallel.For(0, States.Length,
            i => accelerations[i] = GravityMath.Acceleration(States, i, gravity, softening, is2D));
        States = GravityMath.Integrate(States, accelerations, deltaTime, is2D);
    }
}
