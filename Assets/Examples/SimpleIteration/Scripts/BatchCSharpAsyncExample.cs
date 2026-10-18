using Stopwatch = System.Diagnostics.Stopwatch;
using System.Threading.Tasks;
using Unity.Profiling;
using UnityEngine;

/// <summary>Processes disjoint batches concurrently on C# worker tasks. Time: O(N * I), space: O(N + B).</summary>
public sealed class BatchCSharpAsyncExample : MonoBehaviour
{
    #region Settings and reusable data
    [SerializeField, Min(1)] private int _itemCount = 100000;
    [SerializeField, Min(1)] private int _iterations = 100;
    [SerializeField, Min(1)] private int _batchSize = 1000;
    [SerializeField] private string _checksum = "—";
    [SerializeField] private float _computeMilliseconds;
    [SerializeField] private float _wallMilliseconds;

    private readonly ProfilerMarker _marker = new ProfilerMarker("SimpleIteration.CSharpAsync.Worker");
    private float[] _values;
    #endregion

    #region Example implementation
    private async void Start()
    {
        // Dashboard settings are applied before the workload arrays are allocated.
        _itemCount = Mathf.Max(1, PlayerPrefs.GetInt("SimulationLab.Items", _itemCount));
        _iterations = Mathf.Max(1, PlayerPrefs.GetInt("SimulationLab.Iterations", _iterations));
        _batchSize = Mathf.Max(1, PlayerPrefs.GetInt("SimulationLab.BatchSize", _batchSize));
        _values = new float[_itemCount];
        for (int i = 0; i < _values.Length; i++)
            _values[i] = ((i * 104729L) % 1000003L) / 1000003f;

        // Each Task.Run processes a separate batch; Task.WhenAll waits without blocking Unity's main thread.
        long startedAt = Stopwatch.GetTimestamp();
        long computeStartedAt = Stopwatch.GetTimestamp();
        int batchCount = (_itemCount + _batchSize - 1) / _batchSize;
        Task[] tasks = new Task[batchCount];
        for (int batchIndex = 0; batchIndex < batchCount; batchIndex++)
        {
            int firstItem = batchIndex * _batchSize;
            int afterLastItem = Mathf.Min(firstItem + _batchSize, _itemCount);
            tasks[batchIndex] = Task.Run(() =>
            {
                using (_marker.Auto())
                {
                    for (int i = firstItem; i < afterLastItem; i++)
                    {
                        float value = _values[i];
                        for (int step = 0; step < _iterations; step++)
                        {
                            // Same made-up workload as the other scenes: multiply, add, then wrap at 1.
                            float product = (float)((double)value * 1.00001f);
                            value = (float)((double)product + 0.00002f);
                            if (value >= 1f)
                                value -= 1f;
                        }
                        _values[i] = value;
                    }
                }
            });
        }
        Task allTasks = Task.WhenAll(tasks);
        Task<long> computeFinishedAt = allTasks.ContinueWith(
            completed => Stopwatch.GetTimestamp(),
            System.Threading.CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        await allTasks;
        // Capture the compute end on the completing worker; the async Start continuation resumes
        // on Unity's main thread and may otherwise add up to a frame of idle time to this metric.
        _computeMilliseconds = (float)((await computeFinishedAt - computeStartedAt) * 1000d / Stopwatch.Frequency);

        double sum = 0d;
        for (int i = 0; i < _values.Length; i++)
            sum += _values[i];
        _checksum = sum.ToString("0.000000");
        _wallMilliseconds = (float)((Stopwatch.GetTimestamp() - startedAt) * 1000d / Stopwatch.Frequency);
        Debug.Log("C# async batch: items=" + _itemCount + ", iterations=" + _iterations +
            ", checksum=" + _checksum + ", computeMs=" + _computeMilliseconds + ", wallMs=" + _wallMilliseconds, this);
    }
    #endregion
}
