using System;
using System.Globalization;
using System.Threading.Tasks;
using Stopwatch = System.Diagnostics.Stopwatch;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

/// <summary>Runs the same deterministic Random Search workload with one selected processing method.</summary>
public sealed class RandomSearchProcessingExample : MonoBehaviour
{
    [SerializeField, Range(0, 4)] private int _processingMethod; // Methods follow the dashboard order.
    [SerializeField, Min(1)] private int _particleCount = 1; // Kept as one candidate per iteration for result reporting.
    [SerializeField, Min(0)] private int _iterations = 6399;
    [SerializeField, Min(1)] private int _trialCount = 5;
    [SerializeField] private int _randomSeed = 2468;
    [SerializeField] private int _evaluationCount;
    [SerializeField] private float _bestScore;
    [SerializeField] private float _averageBestScore;
    [SerializeField] private float _bestX;
    [SerializeField] private float _bestY;
    [SerializeField] private float _computeMilliseconds;
    [SerializeField] private float _wallMilliseconds;
    [SerializeField] private string _resultSignature = "—";

    private void Start()
    {
        _particleCount = 1;
        _iterations = Mathf.Max(0, PlayerPrefs.GetInt(
            "SimulationLab.RandomSearchEvaluationsPerTrial", _iterations + 1) - 1);
        _trialCount = Mathf.Max(1, PlayerPrefs.GetInt("SimulationLab.RandomSearchTrials", _trialCount));
        RunSelectedMethod();
    }

    private async void RunSelectedMethod()
    {
        long wallStart = Stopwatch.GetTimestamp();
        long computeStart = wallStart;
        float asyncComputeMilliseconds = 0f;

        if (_processingMethod == 1)
        {
            long computeFinishedAt = await Task.Run(() =>
            {
                RunRandomSearch(6);
                return Stopwatch.GetTimestamp();
            });
            asyncComputeMilliseconds = (float)((computeFinishedAt - computeStart) * 1000d / Stopwatch.Frequency);
        }
        else
        {
            RunRandomSearch(_processingMethod);
        }

        FinishRun(computeStart, wallStart, asyncComputeMilliseconds);
    }

    private void RunRandomSearch(int method)
    {
        _particleCount = 1;
        _evaluationCount = _trialCount * _particleCount * (_iterations + 1);
        _bestScore = float.MaxValue;
        _bestX = 0f;
        _bestY = 0f;
        double trialScoreTotal = 0d;
        int evaluationsPerTrial = _particleCount * (_iterations + 1);
        float[] scores = new float[evaluationsPerTrial];
        NativeArray<float> jobScores = default;

        try
        {
            if (method == 3 || method == 4)
                jobScores = new NativeArray<float>(evaluationsPerTrial, Allocator.TempJob);

            for (int trial = 0; trial < _trialCount; trial++)
            {
                if (method == 2)
                    Parallel.For(0, evaluationsPerTrial, index => ScoreRandomPoint(index, trial, scores));
                else if (method == 6)
                    RunTaskRunChunks(evaluationsPerTrial, index => ScoreRandomPoint(index, trial, scores));
                else if (method == 3 || method == 4)
                    RunRandomJob(trial, scores, jobScores, method == 4);
                else
                    for (int index = 0; index < evaluationsPerTrial; index++)
                        ScoreRandomPoint(index, trial, scores);

                float trialBest = float.MaxValue;
                int trialBestIndex = -1;
                for (int index = 0; index < evaluationsPerTrial; index++)
                {
                    if (scores[index] < trialBest)
                    {
                        trialBest = scores[index];
                        trialBestIndex = index;
                    }
                }

                trialScoreTotal += trialBest;
                if (trialBest < _bestScore)
                {
                    _bestScore = trialBest;
                    GetRandomPoint(trialBestIndex, trial, out _bestX, out _bestY);
                }
            }

            _averageBestScore = (float)(trialScoreTotal / _trialCount);
        }
        finally
        {
            if (jobScores.IsCreated)
                jobScores.Dispose();
        }
    }

    // Keep task count bounded by available worker threads instead of creating one task per candidate.
    private static void RunTaskRunChunks(int itemCount, Action<int> processItem)
    {
        int workerCount = Math.Min(itemCount, Math.Min(8, Math.Max(1, Environment.ProcessorCount)));
        Task[] tasks = new Task[workerCount];
        for (int worker = 0; worker < workerCount; worker++)
        {
            int firstItem = itemCount * worker / workerCount;
            int afterLastItem = itemCount * (worker + 1) / workerCount;
            tasks[worker] = Task.Run(() =>
            {
                for (int item = firstItem; item < afterLastItem; item++)
                    processItem(item);
            });
        }

        Task.WhenAll(tasks).GetAwaiter().GetResult();
    }

    private void ScoreRandomPoint(int index, int trial, float[] scores)
    {
        GetRandomPoint(index, trial, out float x, out float y);
        scores[index] = x * x + y * y;
    }

    private struct RandomJob : IJobParallelFor
    {
        public NativeArray<float> Scores;
        public int Seed;
        public int Trial;

        public void Execute(int index)
        {
            float x = ToRange(StableValue(index * 2 + Trial * 100003 + Seed));
            float y = ToRange(StableValue(index * 2 + 1 + Trial * 100003 + Seed));
            Scores[index] = x * x + y * y;
        }
    }

    [BurstCompile]
    private struct RandomBurstJob : IJobParallelFor
    {
        public NativeArray<float> Scores;
        public int Seed;
        public int Trial;

        public void Execute(int index)
        {
            float x = ToRange(StableValue(index * 2 + Trial * 100003 + Seed));
            float y = ToRange(StableValue(index * 2 + 1 + Trial * 100003 + Seed));
            Scores[index] = x * x + y * y;
        }
    }

    private void RunRandomJob(int trial, float[] scores, NativeArray<float> jobScores, bool useBurst)
    {
        JobHandle handle = useBurst
            ? new RandomBurstJob { Scores = jobScores, Seed = _randomSeed, Trial = trial }.Schedule(scores.Length, 64)
            : new RandomJob { Scores = jobScores, Seed = _randomSeed, Trial = trial }.Schedule(scores.Length, 64);
        handle.Complete();
        for (int index = 0; index < scores.Length; index++)
            scores[index] = jobScores[index];
    }

    private void FinishRun(long computeStart, long wallStart, float asyncComputeMilliseconds = 0f)
    {
        _computeMilliseconds = _processingMethod == 1
            ? asyncComputeMilliseconds
            : (float)((Stopwatch.GetTimestamp() - computeStart) * 1000d / Stopwatch.Frequency);
        _wallMilliseconds = (float)((Stopwatch.GetTimestamp() - wallStart) * 1000d / Stopwatch.Frequency);
        _resultSignature = "best=" + _bestScore.ToString("R", CultureInfo.InvariantCulture) +
            "; mean=" + _averageBestScore.ToString("R", CultureInfo.InvariantCulture) +
            "; x=" + _bestX.ToString("R", CultureInfo.InvariantCulture) +
            "; y=" + _bestY.ToString("R", CultureInfo.InvariantCulture);
        Debug.Log("Optimization workload complete: algorithm=Random Search, evaluations=" + _evaluationCount +
            ", trials=" + _trialCount + ", result=" + _resultSignature +
            ", computeMs=" + _computeMilliseconds + ", wallMs=" + _wallMilliseconds, this);
    }

    private static float StableValue(int value)
    {
        unchecked
        {
            uint number = (uint)value;
            number ^= number >> 16; number *= 0x7feb352d;
            number ^= number >> 15; number *= 0x846ca68b;
            number ^= number >> 16;
            return (number & 0x00ffffff) / 16777216f;
        }
    }

    private static float ToRange(float value) { return value * 10f - 5f; }

    private void GetRandomPoint(int index, int trial, out float x, out float y)
    {
        x = ToRange(StableValue(index * 2 + trial * 100003 + _randomSeed));
        y = ToRange(StableValue(index * 2 + 1 + trial * 100003 + _randomSeed));
    }
}
