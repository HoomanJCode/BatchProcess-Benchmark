using Unity.Jobs;

public sealed class GravityBenchmarkUnityJobsExample : GravityBenchmarkExampleBase
{
    protected override string MarkerName => "GravitySimulation.UnityJobs";
    protected override bool UsesUnityJobs => true;

    protected override void CalculateGravity()
    {
        var job = new GravityJob
        {
            Positions = NativePositions,
            Masses = NativeMasses,
            Accelerations = NativeAccelerations,
            Gravity = Gravity,
            SofteningSquared = SofteningSquared,
            Is2D = Is2D
        };
        job.Schedule(BallCount, 32).Complete();
    }

    private struct GravityJob : IJobParallelFor
    {
        [Unity.Collections.ReadOnly] public Unity.Collections.NativeArray<UnityEngine.Vector3> Positions;
        [Unity.Collections.ReadOnly] public Unity.Collections.NativeArray<float> Masses;
        [Unity.Collections.WriteOnly] public Unity.Collections.NativeArray<UnityEngine.Vector3> Accelerations;
        public float Gravity;
        public float SofteningSquared;
        public bool Is2D;

        public void Execute(int index)
        {
            UnityEngine.Vector3 acceleration = UnityEngine.Vector3.zero;
            for (int other = 0; other < Positions.Length; other++)
            {
                if (other == index)
                    continue;
                UnityEngine.Vector3 offset = Positions[other] - Positions[index];
                if (Is2D)
                    offset.z = 0f;
                float inverseDistance = 1f / UnityEngine.Mathf.Sqrt(offset.sqrMagnitude + SofteningSquared);
                acceleration += offset * (Gravity * Masses[other] *
                    inverseDistance * inverseDistance * inverseDistance);
            }
            Accelerations[index] = acceleration;
        }
    }
}
