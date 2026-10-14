using System;
using System.Collections;
using System.Diagnostics;
using System.Threading.Tasks;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;

/// <summary>Parallel processing strategies compared by the batch example.</summary>
public enum BatchMode
{
    SingleThreaded, // One main-thread loop handles every value.
    CSharpAsync, // One Task handles the array on a worker thread.
    CSharpParallel, // Parallel.For shares values across worker threads.
    UnityJobs, // Unity Jobs handles native data without Burst.
    UnityCoroutine, // A coroutine handles a chunk each frame.
    UnityJobsBurst // Unity Jobs with Burst compilation enabled.
}

/// <summary>Shared calculation and job types used by this single-file example.</summary>
public static class BatchProcessingMath
{
    /// <summary>
    /// Repeats the same small multiply-and-add calculation to create measurable work.
    /// Keeping this calculation fixed lets the demo compare scheduling methods fairly.
    /// Complexity: O(iterations) for one value; processing N values takes O(N * iterations) time.
    /// </summary>
    public static float ProcessValue(float value, int iterations)
    {
        for (int i = 0; i < iterations; i++)
        {
            // Keep the result in the 0-to-1 range while doing a little work each iteration.
            value = value * 1.00001f + 0.00002f;
            if (value >= 1f)
                value -= 1f;
        }
        return value;
    }

    /// <summary>Processes count values starting at the requested array index.</summary>
    public static void ProcessRange(float[] values, int iterations, int start, int count)
    {
        int end = start + count;
        for (int i = start; i < end; i++)
            values[i] = ProcessValue(values[i], iterations);
    }
}

public struct SimpleBatchJob : IJobParallelFor
{
    public NativeArray<float> Values;
    public int Iterations;

    public void Execute(int index)
    {
        // Unity schedules one Execute call for each array element.
        float value = Values[index];
        for (int i = 0; i < Iterations; i++)
        {
            value = value * 1.00001f + 0.00002f;
            if (value >= 1f)
                value -= 1f;
        }
        Values[index] = value;
    }
}

[BurstCompile]
public struct SimpleBatchBurstJob : IJobParallelFor
{
    public NativeArray<float> Values;
    public int Iterations;

    public void Execute(int index)
    {
        // This is the same per-value calculation as SimpleBatchJob, compiled with Burst.
        float value = Values[index];
        for (int i = 0; i < Iterations; i++)
        {
            value = value * 1.00001f + 0.00002f;
            if (value >= 1f)
                value -= 1f;
        }
        Values[index] = value;
    }
}

/// <summary>
/// Runs one selected batch-processing strategy with inspector-configured inputs.
/// </summary>
public sealed class BatchProcessingApp : MonoBehaviour
{
    // Separate marker identifies the part of async work that runs off the main thread.
    private static readonly ProfilerMarker AsyncWorkerMarker =
        new ProfilerMarker("BatchProcessing.CSharpAsync.Worker");

    private const int DefaultItemCount = 100000;
    private const int DefaultIterations = 100;
    private const int CoroutineChunkSize = 4096;

    [Header("Batch Settings")]
    [SerializeField] private BatchMode _mode = BatchMode.SingleThreaded;
    [SerializeField, Min(1)] private int _itemCount = DefaultItemCount;
    [SerializeField, Min(1)] private int _iterations = DefaultIterations;
    [SerializeField] private bool _runOnStart = true;

    [Header("Last Run")]
    [SerializeField] private string _resultChecksum = "—";
    [SerializeField] private float _computeMilliseconds;
    [SerializeField] private float _wallMilliseconds;

    private float[] _values; // The input data, overwritten with the processed results.
    private Task<double> _worker; // The running async task; Update collects its result.
    private Coroutine _coroutine; // The running chunked calculation, if coroutine mode is selected.
    private ProfilerMarker _marker; // Profiler label for the selected mode.

    private void Start()
    {
        // Start runs when the scene opens; the Inspector can start a demo automatically.
        _marker = new ProfilerMarker("BatchProcessing." + _mode);
        if (_runOnStart)
            StartBatch();
    }

    private void Update()
    {
        // A Task must never touch Unity objects; the main thread collects it here.
        if (_worker != null && _worker.IsCompleted)
        {
            if (_worker.IsFaulted)
            {
                UnityEngine.Debug.LogException(_worker.Exception.GetBaseException(), this);
            }
            else
            {
                _computeMilliseconds = (float)_worker.Result;
                FinishBatch();
            }
            _worker = null;
        }
    }

    [ContextMenu("Run Batch")]
    private void StartBatch()
    {
        // Start every comparison with new deterministic data and reset the displayed results.
        StopCurrentBatch();
        _values = CreateInput(_itemCount);
        _resultChecksum = "—";
        _wallMilliseconds = 0f;
        _computeMilliseconds = 0f;
        _marker = new ProfilerMarker("BatchProcessing." + _mode);
        double startedAt = Stopwatch.GetTimestamp();
        _startedAt = startedAt;

        switch (_mode)
        {
            case BatchMode.SingleThreaded:
                // Process the entire array on Unity's main thread.
                RunOnMainThread(() => BatchProcessingMath.ProcessRange(_values, _iterations, 0, _values.Length));
                break;
            case BatchMode.CSharpAsync:
            {
                // Copy the references and settings the worker needs before it starts.
                float[] workerValues = _values;
                int iterations = _iterations;
                int itemCount = _itemCount;
                _worker = Task.Run(() =>
                {
                    var timer = Stopwatch.StartNew();
                    using (AsyncWorkerMarker.Auto())
                    {
                        for (int i = 0; i < itemCount; i++)
                            workerValues[i] = BatchProcessingMath.ProcessValue(workerValues[i], iterations);
                    }
                    return timer.Elapsed.TotalMilliseconds;
                });
                break;
            }
            case BatchMode.CSharpParallel:
                // Parallel.For distributes different array indices among thread-pool workers.
                RunOnMainThread(() => Parallel.For(0, _values.Length, i =>
                    _values[i] = BatchProcessingMath.ProcessValue(_values[i], _iterations)));
                break;
            case BatchMode.UnityJobs:
                // Jobs use native memory, then copy the finished values back to the managed array.
                RunJob(false);
                FinishSynchronous(startedAt);
                break;
            case BatchMode.UnityJobsBurst:
                // This job runs the same work with Burst compilation enabled.
                RunJob(true);
                FinishSynchronous(startedAt);
                break;
            case BatchMode.UnityCoroutine:
                // The coroutine processes a fixed-size chunk and yields between chunks.
                _startedAt = startedAt;
                _coroutine = StartCoroutine(ProcessInChunks());
                break;
        }

        if (_mode != BatchMode.CSharpAsync && _mode != BatchMode.UnityCoroutine &&
            _mode != BatchMode.UnityJobs && _mode != BatchMode.UnityJobsBurst)
            FinishSynchronous(startedAt);
    }

    private double _startedAt; // Stopwatch timestamp used to calculate total wall time.

    private void RunOnMainThread(Action work)
    {
        // The profiler marker and stopwatch measure only the selected calculation.
        var timer = Stopwatch.StartNew();
        using (_marker.Auto())
            work();
        _computeMilliseconds = (float)timer.Elapsed.TotalMilliseconds;
    }

    private void RunJob(bool useBurst)
    {
        // NativeArray is the memory type Unity Jobs can safely share with worker threads.
        var timer = Stopwatch.StartNew();
        using (var nativeValues = new NativeArray<float>(_values, Allocator.TempJob))
        {
            JobHandle handle;
            if (useBurst)
            {
                var job = new SimpleBatchBurstJob { Values = nativeValues, Iterations = _iterations };
                handle = job.Schedule(nativeValues.Length, 64);
            }
            else
            {
                var job = new SimpleBatchJob { Values = nativeValues, Iterations = _iterations };
                handle = job.Schedule(nativeValues.Length, 64);
            }

            using (_marker.Auto())
                handle.Complete();
            nativeValues.CopyTo(_values);
        }
        _computeMilliseconds = (float)timer.Elapsed.TotalMilliseconds;
    }

    private IEnumerator ProcessInChunks()
    {
        // Add each chunk's CPU time, while wall time also includes the frames spent yielding.
        double totalCpuMilliseconds = 0d;
        for (int start = 0; start < _values.Length; start += CoroutineChunkSize)
        {
            int count = Mathf.Min(CoroutineChunkSize, _values.Length - start);
            var timer = Stopwatch.StartNew();
            using (_marker.Auto())
                BatchProcessingMath.ProcessRange(_values, _iterations, start, count);
            totalCpuMilliseconds += timer.Elapsed.TotalMilliseconds;
            yield return null;
        }

        _computeMilliseconds = (float)totalCpuMilliseconds;
        _coroutine = null;
        FinishBatch();
    }

    private void FinishSynchronous(double startedAt)
    {
        // Main-thread and job modes finish immediately, so compute and wall time are close.
        _wallMilliseconds = (float)ElapsedMilliseconds(startedAt);
        FinishBatch();
    }

    private void FinishBatch()
    {
        // The checksum helps verify that different strategies processed the same input.
        _wallMilliseconds = (float)ElapsedMilliseconds(_startedAt);
        _resultChecksum = SumValues(_values).ToString("0.000000");
        UnityEngine.Debug.Log($"BatchProcessing completed: mode={_mode}, items={_itemCount}, iterations={_iterations}, checksum={_resultChecksum}, computeMs={_computeMilliseconds:0.000}, wallMs={_wallMilliseconds:0.000}.", this);
    }

    private static double ElapsedMilliseconds(double startedAt)
    {
        // Stopwatch timestamps provide a monotonic clock suitable for elapsed-time measurements.
        return (Stopwatch.GetTimestamp() - startedAt) * 1000d / Stopwatch.Frequency;
    }

    private static float[] CreateInput(int count)
    {
        // Deterministic input makes each mode start from identical data.
        var values = new float[count];
        for (int i = 0; i < count; i++)
            values[i] = ((i * 104729) % 1000003) / 1000003f;
        return values;
    }

    private static double SumValues(float[] values)
    {
        // A double accumulator reduces rounding error while creating the checksum.
        double sum = 0d;
        for (int i = 0; i < values.Length; i++)
            sum += values[i];
        return sum;
    }

    private void StopCurrentBatch()
    {
        // Stop an old coroutine before replacing the input for another run.
        if (_coroutine != null)
            StopCoroutine(_coroutine);
        _coroutine = null;
        _worker = null;
    }

    private void OnDestroy()
    {
        // Do not leave a chunked run active after this scene object is removed.
        StopCurrentBatch();
    }

}
