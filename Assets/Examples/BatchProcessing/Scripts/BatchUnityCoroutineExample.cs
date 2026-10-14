using System.Collections;
using Stopwatch = System.Diagnostics.Stopwatch;
using Unity.Profiling;
using UnityEngine;

/// <summary>Processes chunks over several frames. Time: O(N * I), space: O(N).</summary>
public sealed class BatchUnityCoroutineExample : MonoBehaviour
{
    #region Settings and reusable data
    [SerializeField, Min(1)] private int _itemCount = 100000;
    [SerializeField, Min(1)] private int _iterations = 100;
    [SerializeField, Min(1)] private int _itemsPerFrame = 4096;
    [SerializeField] private string _checksum = "—";
    [SerializeField] private float _computeMilliseconds;
    [SerializeField] private float _wallMilliseconds;

    private readonly ProfilerMarker _marker = new ProfilerMarker("BatchProcessing.UnityCoroutine.Chunk");
    #endregion

    #region Example implementation

    private IEnumerator Start()
    {
        _itemCount = Mathf.Max(1, _itemCount);
        _iterations = Mathf.Max(1, _iterations);
        _itemsPerFrame = Mathf.Max(1, _itemsPerFrame);
        float[] values = new float[_itemCount];
        for (int i = 0; i < values.Length; i++)
            values[i] = ((i * 104729L) % 1000003L) / 1000003f;

        long startedAt = Stopwatch.GetTimestamp();
        double computeMilliseconds = 0d;
        for (int firstItem = 0; firstItem < values.Length; firstItem += _itemsPerFrame)
        {
            int afterLastItem = Mathf.Min(firstItem + _itemsPerFrame, values.Length);
            long chunkStartedAt = Stopwatch.GetTimestamp();
            using (_marker.Auto())
            {
                for (int i = firstItem; i < afterLastItem; i++)
                {
                    float value = values[i];
                    for (int step = 0; step < _iterations; step++)
                    {
                        // This same made-up calculation runs for each item in this frame's chunk.
                        value = value * 1.00001f + 0.00002f;
                        if (value >= 1f)
                            value -= 1f;
                    }
                    values[i] = value;
                }
            }
            computeMilliseconds += (Stopwatch.GetTimestamp() - chunkStartedAt) * 1000d / Stopwatch.Frequency;
            yield return null;
        }

        _computeMilliseconds = (float)computeMilliseconds;
        _wallMilliseconds = (float)((Stopwatch.GetTimestamp() - startedAt) * 1000d / Stopwatch.Frequency);
        double sum = 0d;
        for (int i = 0; i < values.Length; i++)
            sum += values[i];
        _checksum = sum.ToString("0.000000");
        Debug.Log("Unity coroutine batch: items=" + _itemCount + ", iterations=" + _iterations +
            ", checksum=" + _checksum + ", computeMs=" + _computeMilliseconds + ", wallMs=" + _wallMilliseconds, this);
    }
    #endregion
}
