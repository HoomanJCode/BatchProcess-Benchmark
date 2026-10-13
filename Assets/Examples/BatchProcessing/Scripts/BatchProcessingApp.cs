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
    SingleThreaded,
    CSharpAsync,
    CSharpParallel,
    UnityJobs,
    UnityCoroutine,
    UnityJobsBurst
}

/// <summary>Shared calculation and job types used by this single-file example.</summary>
public static class BatchProcessingMath
{
    public static float ProcessValue(float value, int iterations)
    {
        for (int i = 0; i < iterations; i++)
        {
            value = value * 1.00001f + 0.00002f;
            if (value >= 1f)
                value -= 1f;
        }
        return value;
    }

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
        Values[index] = BatchProcessingMath.ProcessValue(Values[index], Iterations);
    }
}

[BurstCompile]
public struct SimpleBatchBurstJob : IJobParallelFor
{
    public NativeArray<float> Values;
    public int Iterations;

    public void Execute(int index)
    {
        Values[index] = BatchProcessingMath.ProcessValue(Values[index], Iterations);
    }
}

/// <summary>
/// Runs one selected batch-processing strategy with inspector-configured inputs.
/// </summary>
public sealed class BatchProcessingApp : MonoBehaviour
{
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

    private float[] _values;
    private Task<double> _worker;
    private Coroutine _coroutine;
    private ProfilerMarker _marker;

    private void Start()
    {
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
                RunOnMainThread(() => BatchProcessingMath.ProcessRange(_values, _iterations, 0, _values.Length));
                break;
            case BatchMode.CSharpAsync:
            {
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
                RunOnMainThread(() => Parallel.For(0, _values.Length, i =>
                    _values[i] = BatchProcessingMath.ProcessValue(_values[i], _iterations)));
                break;
            case BatchMode.UnityJobs:
                RunJob(false);
                FinishSynchronous(startedAt);
                break;
            case BatchMode.UnityJobsBurst:
                RunJob(true);
                FinishSynchronous(startedAt);
                break;
            case BatchMode.UnityCoroutine:
                _startedAt = startedAt;
                _coroutine = StartCoroutine(ProcessInChunks());
                break;
        }

        if (_mode != BatchMode.CSharpAsync && _mode != BatchMode.UnityCoroutine &&
            _mode != BatchMode.UnityJobs && _mode != BatchMode.UnityJobsBurst)
            FinishSynchronous(startedAt);
    }

    private double _startedAt;

    private void RunOnMainThread(Action work)
    {
        var timer = Stopwatch.StartNew();
        using (_marker.Auto())
            work();
        _computeMilliseconds = (float)timer.Elapsed.TotalMilliseconds;
    }

    private void RunJob(bool useBurst)
    {
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
        _wallMilliseconds = (float)ElapsedMilliseconds(startedAt);
        FinishBatch();
    }

    private void FinishBatch()
    {
        _wallMilliseconds = (float)ElapsedMilliseconds(_startedAt);
        _resultChecksum = SumValues(_values).ToString("0.000000");
        UnityEngine.Debug.Log($"BatchProcessing completed: mode={_mode}, items={_itemCount}, iterations={_iterations}, checksum={_resultChecksum}, computeMs={_computeMilliseconds:0.000}, wallMs={_wallMilliseconds:0.000}.", this);
    }

    private static double ElapsedMilliseconds(double startedAt)
    {
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
        double sum = 0d;
        for (int i = 0; i < values.Length; i++)
            sum += values[i];
        return sum;
    }

    private void StopCurrentBatch()
    {
        if (_coroutine != null)
            StopCoroutine(_coroutine);
        _coroutine = null;
        _worker = null;
    }

    private void OnDestroy()
    {
        StopCurrentBatch();
    }

}
