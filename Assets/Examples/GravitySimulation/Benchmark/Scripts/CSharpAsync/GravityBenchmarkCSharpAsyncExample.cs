using System;
using System.Threading.Tasks;
using UnityEngine;

public sealed class GravityBenchmarkCSharpAsyncExample : GravityBenchmarkExampleBase
{
    protected override string MarkerName => "GravitySimulation.CSharpAsync";

    protected override void CalculateGravity()
    {
        int workerCount = Mathf.Min(BallCount, Mathf.Min(8, Math.Max(1, Environment.ProcessorCount)));
        Task[] tasks = new Task[workerCount];
        for (int worker = 0; worker < workerCount; worker++)
        {
            int firstTarget = BallCount * worker / workerCount;
            int afterLastTarget = BallCount * (worker + 1) / workerCount;
            tasks[worker] = Task.Run(() =>
            {
                for (int target = firstTarget; target < afterLastTarget; target++)
                    CalculateTargetAcceleration(target);
            });
        }

        Task.WhenAll(tasks).GetAwaiter().GetResult();
    }
}
