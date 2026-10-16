using Stopwatch = System.Diagnostics.Stopwatch;
using System.Threading.Tasks;
using Unity.Profiling;
using UnityEngine;

/// <summary>Processes values on a C# worker task. Time: O(N * I), space: O(N).</summary>
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

        // Task.Run moves the same calculation to a worker thread. It runs once for this scene.
        long startedAt = Stopwatch.GetTimestamp();
        long computeStartedAt = Stopwatch.GetTimestamp();
        await Task.Run(() =>
        {
            using (_marker.Auto())
            {
                for (int batchStart = 0; batchStart < _values.Length; batchStart += _batchSize)
                {
                    int batchEnd = batchStart + _batchSize;
                    if (batchEnd > _values.Length)
                        batchEnd = _values.Length;
                    for (int i = batchStart; i < batchEnd; i++)
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
            }
        });
        _computeMilliseconds = (float)((Stopwatch.GetTimestamp() - computeStartedAt) * 1000d / Stopwatch.Frequency);

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
