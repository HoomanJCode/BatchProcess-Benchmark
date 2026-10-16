using System.Threading.Tasks;
using Stopwatch = System.Diagnostics.Stopwatch;
using Unity.Profiling;
using UnityEngine;

/// <summary>Starts one C# task per value batch. Time: O(N * I), temporary task storage: O(B).</summary>
public sealed class BatchCSharpAsyncTasksExample : MonoBehaviour
{
    #region Settings and measured results
    [SerializeField, Min(1)] private int _itemCount = 100000;
    [SerializeField, Min(1)] private int _iterations = 100;
    [SerializeField, Min(1)] private int _batchSize = 1000;
    [SerializeField] private string _checksum = "—";
    [SerializeField] private float _computeMilliseconds;
    [SerializeField] private float _wallMilliseconds;

    private static readonly ProfilerMarker WorkerMarker =
        new ProfilerMarker("SimpleIteration.CSharpAsyncTasks.Batch");
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

        // One task handles one batch; tasks share the array but write to different indexes.
        int batchCount = (_itemCount + _batchSize - 1) / _batchSize;
        long wallStartedAt = Stopwatch.GetTimestamp();
        long computeStartedAt = wallStartedAt;
        Task[] tasks = new Task[batchCount];
        for (int batchIndex = 0; batchIndex < batchCount; batchIndex++)
        {
            int firstItem = batchIndex * _batchSize;
            int afterLastItem = firstItem + _batchSize;
            if (afterLastItem > _itemCount)
                afterLastItem = _itemCount;

            tasks[batchIndex] = Task.Run(() =>
            {
                using (WorkerMarker.Auto())
                {
                    for (int i = firstItem; i < afterLastItem; i++)
                    {
                        float value = _values[i];
                        for (int step = 0; step < _iterations; step++)
                        {
                            // This is the same beginner calculation used by every other method.
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

        await Task.WhenAll(tasks);
        _computeMilliseconds = (float)((Stopwatch.GetTimestamp() - computeStartedAt) * 1000d / Stopwatch.Frequency);

        double sum = 0d;
        for (int i = 0; i < _values.Length; i++)
            sum += _values[i];
        _checksum = sum.ToString("0.000000");
        _wallMilliseconds = (float)((Stopwatch.GetTimestamp() - wallStartedAt) * 1000d / Stopwatch.Frequency);
        Debug.Log("C# batched async tasks: items=" + _itemCount + ", batchSize=" + _batchSize +
            ", taskCount=" + batchCount + ", iterations=" + _iterations +
            ", checksum=" + _checksum + ", computeMs=" + _computeMilliseconds +
            ", wallMs=" + _wallMilliseconds, this);
    }
    #endregion
}
