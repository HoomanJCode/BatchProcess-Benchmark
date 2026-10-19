using System.Threading.Tasks;

public sealed class GravityBenchmarkCSharpParallelExample : GravityBenchmarkExampleBase
{
    protected override string MarkerName => "GravitySimulation.CSharpParallel";

    protected override void CalculateGravity()
    {
        Parallel.For(0, BallCount, CalculateTargetAcceleration);
    }
}
