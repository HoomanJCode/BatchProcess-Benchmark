using Stopwatch = System.Diagnostics.Stopwatch;
using Unity.Collections;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;

/// <summary>Processes values with Unity Jobs, without Burst. Time: O(N * I), space: O(N).</summary>
public sealed class BatchUnityJobsExample : MonoBehaviour
{
    #region Settings and reusable data
    [SerializeField, Min(1)] private int _itemCount = 100000;
    [SerializeField, Min(1)] private int _iterations = 100;
    [SerializeField] private string _checksum = "—";
    [SerializeField] private float _computeMilliseconds;
    [SerializeField] private float _wallMilliseconds;

    private readonly ProfilerMarker _marker = new ProfilerMarker("BatchProcessing.UnityJobs");
    #endregion

    #region Example implementation

    private struct BatchJob : IJobParallelFor
    {
        public NativeArray<float> Values;
        public int Iterations;

        public void Execute(int index)
        {
            float value = Values[index];
            for (int step = 0; step < Iterations; step++)
            {
                // This same made-up calculation is repeated for every item.
                value = value * 1.00001f + 0.00002f;
                if (value >= 1f)
                    value -= 1f;
            }
            Values[index] = value;
        }
    }

    private void Start()
    {
        _itemCount = Mathf.Max(1, _itemCount);
        _iterations = Mathf.Max(1, _iterations);
        NativeArray<float> values = new NativeArray<float>(_itemCount, Allocator.TempJob);
        for (int i = 0; i < values.Length; i++)
            values[i] = ((i * 104729L) % 1000003L) / 1000003f;

        long startedAt = Stopwatch.GetTimestamp();
        using (_marker.Auto())
        {
            BatchJob job = new BatchJob { Values = values, Iterations = _iterations };
            JobHandle handle = job.Schedule(_itemCount, 64);
            handle.Complete();
        }
        _computeMilliseconds = (float)((Stopwatch.GetTimestamp() - startedAt) * 1000d / Stopwatch.Frequency);

        double sum = 0d;
        for (int i = 0; i < values.Length; i++)
            sum += values[i];
        _checksum = sum.ToString("0.000000");
        _wallMilliseconds = (float)((Stopwatch.GetTimestamp() - startedAt) * 1000d / Stopwatch.Frequency);
        values.Dispose();
        Debug.Log("Unity Jobs batch: items=" + _itemCount + ", iterations=" + _iterations +
            ", checksum=" + _checksum + ", computeMs=" + _computeMilliseconds + ", wallMs=" + _wallMilliseconds, this);
    }
    #endregion
}
