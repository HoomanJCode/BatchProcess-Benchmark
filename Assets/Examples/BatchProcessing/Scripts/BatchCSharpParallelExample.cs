using Stopwatch = System.Diagnostics.Stopwatch;
using System.Threading;
using Unity.Profiling;
using UnityEngine;

/// <summary>Splits values across C# worker threads. Time: O(N * I), space: O(N).</summary>
public sealed class BatchCSharpParallelExample : MonoBehaviour
{
    [SerializeField, Min(1)] private int _itemCount = 100000;
    [SerializeField, Min(1)] private int _iterations = 100;
    [SerializeField] private string _checksum = "—";
    [SerializeField] private float _computeMilliseconds;
    [SerializeField] private float _wallMilliseconds;

    private readonly ProfilerMarker _marker = new ProfilerMarker("BatchProcessing.CSharpParallel");
    private float[] _values;
    private Thread[] _workers;
    private AutoResetEvent[] _workerSignals;
    private CountdownEvent _workersDone;
    private volatile bool _stopWorkers;

    private void Start()
    {
        _itemCount = Mathf.Max(1, _itemCount);
        _iterations = Mathf.Max(1, _iterations);
        _values = new float[_itemCount];
        for (int i = 0; i < _values.Length; i++)
            _values[i] = ((i * 104729L) % 1000003L) / 1000003f;

        // Create workers and their signals before timing; the measured pass reuses them.
        int workerCount = Mathf.Clamp(SystemInfo.processorCount - 1, 1, _itemCount);
        _workers = new Thread[workerCount];
        _workerSignals = new AutoResetEvent[workerCount];
        _workersDone = new CountdownEvent(workerCount);
        for (int workerIndex = 0; workerIndex < workerCount; workerIndex++)
        {
            _workerSignals[workerIndex] = new AutoResetEvent(false);
            _workers[workerIndex] = new Thread(WorkerLoop);
            _workers[workerIndex].IsBackground = true;
            _workers[workerIndex].Start(workerIndex);
        }

        long startedAt = Stopwatch.GetTimestamp();
        using (_marker.Auto())
        {
            _workersDone.Reset(workerCount);
            for (int workerIndex = 0; workerIndex < workerCount; workerIndex++)
                _workerSignals[workerIndex].Set();
            _workersDone.Wait();
        }
        _computeMilliseconds = (float)((Stopwatch.GetTimestamp() - startedAt) * 1000d / Stopwatch.Frequency);

        double sum = 0d;
        for (int i = 0; i < _values.Length; i++)
            sum += _values[i];
        _checksum = sum.ToString("0.000000");
        _wallMilliseconds = (float)((Stopwatch.GetTimestamp() - startedAt) * 1000d / Stopwatch.Frequency);
        Debug.Log("C# parallel batch: items=" + _itemCount + ", iterations=" + _iterations +
            ", workers=" + workerCount + ", checksum=" + _checksum +
            ", computeMs=" + _computeMilliseconds + ", wallMs=" + _wallMilliseconds, this);
    }

    private void WorkerLoop(object workerIndexValue)
    {
        int workerIndex = (int)workerIndexValue;
        int firstItem = workerIndex * _itemCount / _workers.Length;
        int afterLastItem = (workerIndex + 1) * _itemCount / _workers.Length;
        while (true)
        {
            _workerSignals[workerIndex].WaitOne();
            if (_stopWorkers)
                return;

            for (int i = firstItem; i < afterLastItem; i++)
            {
                float value = _values[i];
                for (int step = 0; step < _iterations; step++)
                {
                    // Each worker repeats the same made-up calculation for its values.
                    value = value * 1.00001f + 0.00002f;
                    if (value >= 1f)
                        value -= 1f;
                }
                _values[i] = value;
            }
            _workersDone.Signal();
        }
    }

    private void OnDestroy()
    {
        if (_workers == null)
            return;
        _stopWorkers = true;
        for (int i = 0; i < _workers.Length; i++)
            _workerSignals[i].Set();
        for (int i = 0; i < _workers.Length; i++)
        {
            _workers[i].Join();
            _workerSignals[i].Dispose();
        }
        _workersDone.Dispose();
    }
}
