using System.Threading.Tasks;

public abstract class GravityBenchmarkCSharpParallelStrategy : GravityBenchmarkExampleBase
{
    protected override string MarkerName => "GravitySimulation.CSharpParallel";

    protected override void CalculateGravity()
    {
        Parallel.For(0, BallCount, CalculateTargetAcceleration);
    }
}
