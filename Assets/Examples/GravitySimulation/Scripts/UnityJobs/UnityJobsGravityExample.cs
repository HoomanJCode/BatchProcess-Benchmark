using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

/// <summary>Schedules gravity force calculations with Unity Jobs without Burst.</summary>
public sealed class UnityJobsGravityExample : GravitySimulationExample
{
    private NativeArray<BodyState> _nativeStates;
    private NativeArray<Vector3> _nativeAccelerations;

    protected override SimulationMode Mode { get { return SimulationMode.UnityJobs; } }

    protected override void Awake()
    {
        base.Awake();
        _nativeStates = new NativeArray<BodyState>(States.Length, Allocator.Persistent);
        _nativeAccelerations = new NativeArray<Vector3>(States.Length, Allocator.Persistent);
    }

    protected override void SimulateStep(float gravity, float softening, SimulationDimension dimension)
    {
        for (int i = 0; i < States.Length; i++)
            _nativeStates[i] = States[i];

        var job = new GravityAccelerationJob
        {
            States = _nativeStates,
            Accelerations = _nativeAccelerations,
            Gravity = gravity,
            SofteningSquared = softening * softening,
            Is2D = dimension == SimulationDimension.TwoD
        };
        job.Schedule(States.Length, 32).Complete();
        var accelerations = new Vector3[States.Length];
        for (int i = 0; i < accelerations.Length; i++)
            accelerations[i] = _nativeAccelerations[i];
        SubmitGravityAccelerations(accelerations);
    }

    protected override void OnDestroy()
    {
        if (_nativeStates.IsCreated) _nativeStates.Dispose();
        if (_nativeAccelerations.IsCreated) _nativeAccelerations.Dispose();
        base.OnDestroy();
    }

    private struct GravityAccelerationJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<BodyState> States;
        [WriteOnly] public NativeArray<Vector3> Accelerations;
        public float Gravity;
        public float SofteningSquared;
        public bool Is2D;

        public void Execute(int index)
        {
            Accelerations[index] = GravityMath.Acceleration(
                States, index, Gravity, SofteningSquared, Is2D);
        }
    }
}
