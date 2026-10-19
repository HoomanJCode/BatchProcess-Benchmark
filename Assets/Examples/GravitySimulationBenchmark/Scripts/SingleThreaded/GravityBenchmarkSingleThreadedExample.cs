public abstract class GravityBenchmarkSingleThreadedStrategy : GravityBenchmarkExampleBase
{
    protected override string MarkerName => "GravitySimulation.SingleThreaded";

    protected override void CalculateGravity()
    {
        for (int target = 0; target < BallCount; target++)
            CalculateTargetAcceleration(target);
    }
}
