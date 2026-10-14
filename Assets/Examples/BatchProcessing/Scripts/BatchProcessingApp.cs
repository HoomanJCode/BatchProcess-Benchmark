using System;
using System.Collections;
using System.Diagnostics;
using System.Threading;
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
    CSharpParallel, // Persistent C# worker threads split values into ranges.
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

    private float[] _values; // Reused input data, overwritten with the processed results.
    private NativeArray<float> _nativeValues; // Reused native memory for both job modes.
    private AutoResetEvent _asyncWorkAvailable;
    private ManualResetEvent _asyncWorkComplete;
    private Task _asyncWorkerTask;
    private volatile bool _stopAsyncWorker;
    private bool _asyncWorkPending;
    private float _asyncComputeMilliseconds;
    private AutoResetEvent[] _parallelWorkerSignals;
    private Thread[] _parallelWorkers;
    private CountdownEvent _parallelWorkersDone;
    private volatile bool _stopParallelWorkers;
    private bool _runInProgress;
    private bool _coroutineRequested;
    private int _stepIterations;
    private ProfilerMarker _marker; // Profiler label for the selected mode.

    private void Start()
    {
        // Make reusable storage and workers before any measured batch begins.
        _itemCount = Mathf.Max(1, _itemCount);
        _iterations = Mathf.Max(1, _iterations);
        _values = new float[_itemCount];
        ResetInput(_values);
        _nativeValues = new NativeArray<float>(_itemCount, Allocator.Persistent);

        _asyncWorkAvailable = new AutoResetEvent(false);
        _asyncWorkComplete = new ManualResetEvent(true);
        _asyncWorkerTask = Task.Factory.StartNew(
            AsyncWorkerLoop, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        StartParallelWorkers();
        StartCoroutine(ProcessInChunks());

        // Start runs when the scene opens; the Inspector can start a demo automatically.
        _marker = new ProfilerMarker("BatchProcessing." + _mode);
        if (_runOnStart)
            StartBatch();
    }

    private void Update()
    {
        // A worker never touches Unity objects; the main thread collects its finished timing here.
        if (_asyncWorkPending && _asyncWorkComplete.WaitOne(0))
        {
            _asyncWorkPending = false;
            _computeMilliseconds = _asyncComputeMilliseconds;
            FinishBatch();
        }
    }

    [ContextMenu("Run Batch")]
    private void StartBatch()
    {
        // Do not replace arrays or start overlapping work; every mode reuses the same input buffer.
        if (_runInProgress)
            return;
        if (_values == null || _itemCount != _values.Length)
        {
            UnityEngine.Debug.LogWarning("Choose the item count before entering Play mode so reusable buffers stay the right size.", this);
            return;
        }
        ResetInput(_values);
        _stepIterations = Mathf.Max(1, _iterations);
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
                _runInProgress = true;
                RunSingleThreaded();
                FinishSynchronous(startedAt);
                break;
            case BatchMode.CSharpAsync:
            {
                // Wake the existing Task worker instead of allocating a new Task and closure.
                _runInProgress = true;
                _asyncWorkPending = true;
                _asyncWorkComplete.Reset();
                _asyncWorkAvailable.Set();
                break;
            }
            case BatchMode.CSharpParallel:
                // Wake the existing worker threads; each processes a fixed range of array indices.
                _runInProgress = true;
                RunParallelWorkers();
                FinishSynchronous(startedAt);
                break;
            case BatchMode.UnityJobs:
                // Jobs use native memory, then copy the finished values back to the managed array.
                _runInProgress = true;
                RunJob(false);
                FinishSynchronous(startedAt);
                break;
            case BatchMode.UnityJobsBurst:
                // This job runs the same work with Burst compilation enabled.
                _runInProgress = true;
                RunJob(true);
                FinishSynchronous(startedAt);
                break;
            case BatchMode.UnityCoroutine:
                // The waiting coroutine processes chunks without creating another iterator.
                _runInProgress = true;
                _coroutineRequested = true;
                break;
        }
    }

    private double _startedAt; // Stopwatch timestamp used to calculate total wall time.

    private void RunSingleThreaded()
    {
        // The profiler marker and stopwatch measure only the selected calculation.
        long startedAt = Stopwatch.GetTimestamp();
        using (_marker.Auto())
            BatchProcessingMath.ProcessRange(_values, _stepIterations, 0, _values.Length);
        _computeMilliseconds = (float)ElapsedMilliseconds(startedAt);
    }

    private void RunJob(bool useBurst)
    {
        // Reuse native memory so allocation time does not get mixed into either job measurement.
        long startedAt = Stopwatch.GetTimestamp();
        _nativeValues.CopyFrom(_values);
        JobHandle handle;
        if (useBurst)
        {
            var job = new SimpleBatchBurstJob { Values = _nativeValues, Iterations = _stepIterations };
            handle = job.Schedule(_nativeValues.Length, 64);
        }
        else
        {
            var job = new SimpleBatchJob { Values = _nativeValues, Iterations = _stepIterations };
            handle = job.Schedule(_nativeValues.Length, 64);
        }

        using (_marker.Auto())
            handle.Complete();
        _nativeValues.CopyTo(_values);
        _computeMilliseconds = (float)ElapsedMilliseconds(startedAt);
    }

    private IEnumerator ProcessInChunks()
    {
        while (true)
        {
            if (!_coroutineRequested)
            {
                yield return null;
                continue;
            }

            _coroutineRequested = false;
            // Compute time sums chunk work; wall time also includes the frames spent yielding.
            double totalCpuMilliseconds = 0d;
            for (int start = 0; start < _values.Length; start += CoroutineChunkSize)
            {
                int count = Mathf.Min(CoroutineChunkSize, _values.Length - start);
                long chunkStartedAt = Stopwatch.GetTimestamp();
                using (_marker.Auto())
                    BatchProcessingMath.ProcessRange(_values, _stepIterations, start, count);
                totalCpuMilliseconds += ElapsedMilliseconds(chunkStartedAt);
                yield return null;
            }

            _computeMilliseconds = (float)totalCpuMilliseconds;
            FinishBatch();
        }
    }

    private void AsyncWorkerLoop()
    {
        while (true)
        {
            _asyncWorkAvailable.WaitOne();
            if (_stopAsyncWorker)
                return;

            long startedAt = Stopwatch.GetTimestamp();
            using (AsyncWorkerMarker.Auto())
            {
                for (int i = 0; i < _values.Length; i++)
                    _values[i] = BatchProcessingMath.ProcessValue(_values[i], _stepIterations);
            }
            _asyncComputeMilliseconds = (float)ElapsedMilliseconds(startedAt);
            _asyncWorkComplete.Set();
        }
    }

    private void StartParallelWorkers()
    {
        int workerCount = Mathf.Clamp(SystemInfo.processorCount - 1, 1, _itemCount);
        _parallelWorkerSignals = new AutoResetEvent[workerCount];
        _parallelWorkers = new Thread[workerCount];
        _parallelWorkersDone = new CountdownEvent(workerCount);
        for (int workerIndex = 0; workerIndex < workerCount; workerIndex++)
        {
            _parallelWorkerSignals[workerIndex] = new AutoResetEvent(false);
            _parallelWorkers[workerIndex] = new Thread(ParallelWorkerLoop);
            _parallelWorkers[workerIndex].IsBackground = true;
            _parallelWorkers[workerIndex].Name = "Batch worker " + workerIndex;
            _parallelWorkers[workerIndex].Start(workerIndex);
        }
    }

    private void RunParallelWorkers()
    {
        long startedAt = Stopwatch.GetTimestamp();
        using (_marker.Auto())
        {
            _parallelWorkersDone.Reset(_parallelWorkers.Length);
            for (int workerIndex = 0; workerIndex < _parallelWorkers.Length; workerIndex++)
                _parallelWorkerSignals[workerIndex].Set();
            _parallelWorkersDone.Wait();
        }
        _computeMilliseconds = (float)ElapsedMilliseconds(startedAt);
    }

    // Every worker reuses its thread and assigned range for later batch runs.
    private void ParallelWorkerLoop(object workerIndexValue)
    {
        int workerIndex = (int)workerIndexValue;
        int firstItem = workerIndex * _itemCount / _parallelWorkers.Length;
        int afterLastItem = (workerIndex + 1) * _itemCount / _parallelWorkers.Length;
        while (true)
        {
            _parallelWorkerSignals[workerIndex].WaitOne();
            if (_stopParallelWorkers)
                return;

            for (int i = firstItem; i < afterLastItem; i++)
                _values[i] = BatchProcessingMath.ProcessValue(_values[i], _stepIterations);
            _parallelWorkersDone.Signal();
        }
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
        UnityEngine.Debug.Log($"BatchProcessing completed: mode={_mode}, items={_itemCount}, iterations={_stepIterations}, checksum={_resultChecksum}, computeMs={_computeMilliseconds:0.000}, wallMs={_wallMilliseconds:0.000}.", this);
        _runInProgress = false;
    }

    private static double ElapsedMilliseconds(double startedAt)
    {
        // Stopwatch timestamps provide a monotonic clock suitable for elapsed-time measurements.
        return (Stopwatch.GetTimestamp() - startedAt) * 1000d / Stopwatch.Frequency;
    }

    private void ResetInput(float[] values)
    {
        // Deterministic input makes each mode start from identical data without a new array.
        for (int i = 0; i < values.Length; i++)
            values[i] = ((i * 104729) % 1000003) / 1000003f;
    }

    private static double SumValues(float[] values)
    {
        // A double accumulator reduces rounding error while creating the checksum.
        double sum = 0d;
        for (int i = 0; i < values.Length; i++)
            sum += values[i];
        return sum;
    }

    private void OnDestroy()
    {
        // Stop and join persistent workers before releasing their reusable buffers.
        _stopAsyncWorker = true;
        if (_asyncWorkerTask != null)
        {
            _asyncWorkAvailable.Set();
            _asyncWorkerTask.Wait();
            _asyncWorkAvailable.Dispose();
            _asyncWorkComplete.Dispose();
        }

        if (_parallelWorkers != null)
        {
            _stopParallelWorkers = true;
            for (int i = 0; i < _parallelWorkers.Length; i++)
                _parallelWorkerSignals[i].Set();
            for (int i = 0; i < _parallelWorkers.Length; i++)
            {
                _parallelWorkers[i].Join();
                _parallelWorkerSignals[i].Dispose();
            }
            _parallelWorkersDone.Dispose();
        }

        if (_nativeValues.IsCreated)
            _nativeValues.Dispose();
    }

}
