using Stopwatch = System.Diagnostics.Stopwatch;
using Unity.Profiling;
using UnityEngine;

/// <summary>Processes every value in one loop on Unity's main thread. Time: O(N * I), space: O(N).</summary>
public sealed class BatchSingleThreadedExample : MonoBehaviour
{
    #region Settings and reusable data
    [SerializeField, Min(1)] private int _itemCount = 100000;
    [SerializeField, Min(1)] private int _iterations = 100;
    [SerializeField, Min(1)] private int _batchSize = 1000;
    [SerializeField] private string _checksum = "—";
    [SerializeField] private float _computeMilliseconds;
    [SerializeField] private float _wallMilliseconds;

    private readonly ProfilerMarker _marker = new ProfilerMarker("SimpleIteration.SingleThreaded");
    #endregion

    #region Example implementation

    private void Start()
    {
        // Dashboard settings are applied before the workload arrays are allocated.
        _itemCount = Mathf.Max(1, PlayerPrefs.GetInt("SimulationLab.Items", _itemCount));
        _iterations = Mathf.Max(1, PlayerPrefs.GetInt("SimulationLab.Iterations", _iterations));
        _batchSize = Mathf.Max(1, PlayerPrefs.GetInt("SimulationLab.BatchSize", _batchSize));
        float[] values = new float[_itemCount];
        for (int i = 0; i < values.Length; i++)
            values[i] = ((i * 104729L) % 1000003L) / 1000003f;

        long startedAt = Stopwatch.GetTimestamp();
        using (_marker.Auto())
        {
            for (int batchStart = 0; batchStart < values.Length; batchStart += _batchSize)
            {
                int batchEnd = Mathf.Min(batchStart + _batchSize, values.Length);
                for (int i = batchStart; i < batchEnd; i++)
                {
                    float value = values[i];
                    for (int step = 0; step < _iterations; step++)
                    {
                        // This made-up calculation creates work; it is not a real-world formula.
                        // Example: 0.5 becomes 0.500025, then 0.500050, and so on.
                        float product = (float)((double)value * 1.00001f);
                        value = (float)((double)product + 0.00002f);
                        if (value >= 1f)
                            value -= 1f; // Wrap the value back into the 0-to-1 range.
                    }
                    values[i] = value;
                }
            }
        }
        _computeMilliseconds = (float)((Stopwatch.GetTimestamp() - startedAt) * 1000d / Stopwatch.Frequency);

        double sum = 0d;
        for (int i = 0; i < values.Length; i++)
            sum += values[i];
        _checksum = sum.ToString("0.000000");
        _wallMilliseconds = (float)((Stopwatch.GetTimestamp() - startedAt) * 1000d / Stopwatch.Frequency);
        Debug.Log("Single threaded batch: items=" + _itemCount + ", iterations=" + _iterations +
            ", checksum=" + _checksum + ", computeMs=" + _computeMilliseconds + ", wallMs=" + _wallMilliseconds, this);
    }
    #endregion
}
