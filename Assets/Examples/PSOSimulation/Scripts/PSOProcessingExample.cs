using System;
using System.Threading.Tasks;
using Stopwatch = System.Diagnostics.Stopwatch;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

/// <summary>
/// Runs PSO or Random Search using one selected processing method.
/// Time complexity: PSO O(particles × iterations); Random Search O(evaluations).
/// </summary>
public sealed class PSOProcessingExample : MonoBehaviour
{
    #region Settings and measured results
    [SerializeField, Range(0, 1)] private int _algorithm; // 0 = PSO, 1 = Random Search.
    [SerializeField, Range(0, 4)] private int _processingMethod; // five methods, in the README order.
    [SerializeField, Min(2)] private int _particleCount = 64;
    [SerializeField, Min(1)] private int _iterations = 99;
    [SerializeField, Min(1)] private int _trialCount = 5;
    [SerializeField] private int _randomSeed = 2468;
    [SerializeField] private int _evaluationCount;
    [SerializeField] private float _bestScore;
    [SerializeField] private float _averageBestScore;
    [SerializeField] private float _bestX;
    [SerializeField] private float _bestY;
    [SerializeField] private float _computeMilliseconds;
    [SerializeField] private float _wallMilliseconds;
    [SerializeField] private string _checksum = "—";
    #endregion

    #region Workload entry points
    private void Start()
    {
        // Dashboard settings are applied before this example starts its selected method.
        _particleCount = Mathf.Max(2, PlayerPrefs.GetInt("SimulationLab.PsoParticles", _particleCount));
        _iterations = Mathf.Max(1, PlayerPrefs.GetInt("SimulationLab.PsoIterations", _iterations));
        _trialCount = Mathf.Max(1, PlayerPrefs.GetInt("SimulationLab.PsoTrials", _trialCount));

        RunSelectedMethod();
    }

    private async void RunSelectedMethod()
    {
        long wallStart = Stopwatch.GetTimestamp();
        long computeStart = wallStart;
        float asyncComputeMilliseconds = 0f;

        // C# async runs the workload coordinator off the Unity thread and fans independent work out to worker tasks.
        if (_processingMethod == 1)
        {
            asyncComputeMilliseconds = await Task.Run(() =>
            {
                long workerStart = Stopwatch.GetTimestamp();
                RunSearch(6);
                return (float)((Stopwatch.GetTimestamp() - workerStart) * 1000d / Stopwatch.Frequency);
            });
        }
        else
        {
            RunSearch(_processingMethod);
        }

        FinishRun(computeStart, wallStart, asyncComputeMilliseconds);
    }

    private void RunSearch(int method)
    {
        // The method selection applies to the same seeded workload and evaluation budget.
        if (_algorithm == 0)
            RunParticleSwarm(method);
        else
            RunRandomSearch(method);
    }
    #endregion

    #region Random Search workload
    private void RunRandomSearch(int method)
    {
        _evaluationCount = _trialCount * _particleCount * (_iterations + 1);
        _bestScore = float.MaxValue;
        _bestX = 0f;
        _bestY = 0f;
        double trialScoreTotal = 0d;

        // Each candidate point is generated from its index, so all processing methods see identical inputs.
        int evaluationsPerTrial = _particleCount * (_iterations + 1);
        if (method == 6 && _trialCount > 1)
        {
            RunRandomSearchTaskRuns(evaluationsPerTrial);
            return;
        }

        float[] scores = new float[evaluationsPerTrial];
        NativeArray<float> jobScores = default;
        if (method == 3 || method == 4)
            jobScores = new NativeArray<float>(evaluationsPerTrial, Allocator.TempJob);

        for (int trial = 0; trial < _trialCount; trial++)
        {
            if (method == 2)
                System.Threading.Tasks.Parallel.For(0, evaluationsPerTrial, index => ScoreRandomPoint(index, trial, scores));
            else if (method == 6)
                RunTaskRunChunks(evaluationsPerTrial, index => ScoreRandomPoint(index, trial, scores));
            else if (method == 3 || method == 4)
                RunRandomJob(trial, scores, jobScores, method == 4);
            else
                for (int index = 0; index < evaluationsPerTrial; index++)
                    ScoreRandomPoint(index, trial, scores);

            float trialBest = float.MaxValue;
            for (int index = 0; index < evaluationsPerTrial; index++)
            {
                if (scores[index] < trialBest)
                    trialBest = scores[index];
            }
            trialScoreTotal += trialBest;
            if (trialBest < _bestScore)
                _bestScore = trialBest;
        }

        _averageBestScore = (float)(trialScoreTotal / _trialCount);
        if (jobScores.IsCreated)
            jobScores.Dispose();
    }

    private void RunRandomSearchTaskRuns(int evaluationsPerTrial)
    {
        int workerCount = Math.Min(_trialCount, Math.Min(8, Math.Max(1, Environment.ProcessorCount)));
        float[] trialBestScores = new float[_trialCount];
        int[] trialBestIndices = new int[_trialCount];
        Task[] tasks = new Task[workerCount];
        for (int worker = 0; worker < workerCount; worker++)
        {
            int firstTrial = _trialCount * worker / workerCount;
            int afterLastTrial = _trialCount * (worker + 1) / workerCount;
            tasks[worker] = Task.Run(() =>
            {
                for (int trial = firstTrial; trial < afterLastTrial; trial++)
                {
                    float bestScore = float.MaxValue;
                    int bestIndex = -1;
                    for (int index = 0; index < evaluationsPerTrial; index++)
                    {
                        float x = ToRange(StableValue(index * 2 + trial * 100003 + _randomSeed));
                        float y = ToRange(StableValue(index * 2 + 1 + trial * 100003 + _randomSeed));
                        float score = x * x + y * y;
                        if (score < bestScore)
                        {
                            bestScore = score;
                            bestIndex = index;
                        }
                    }
                    trialBestScores[trial] = bestScore;
                    trialBestIndices[trial] = bestIndex;
                }
            });
        }

        Task.WhenAll(tasks).GetAwaiter().GetResult();
        double scoreTotal = 0d;
        for (int trial = 0; trial < _trialCount; trial++)
        {
            scoreTotal += trialBestScores[trial];
            if (trialBestScores[trial] < _bestScore)
            {
                _bestScore = trialBestScores[trial];
                GetRandomPoint(trialBestIndices[trial], trial, out _bestX, out _bestY);
            }
        }
        _averageBestScore = (float)(scoreTotal / _trialCount);
    }

    // Keep task count bounded by available worker threads instead of creating one task per item.
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
        float x = ToRange(StableValue(index * 2 + trial * 100003 + _randomSeed));
        float y = ToRange(StableValue(index * 2 + 1 + trial * 100003 + _randomSeed));
        // Sphere objective: f(x, y) = x² + y². The minimum is zero at (0, 0).
        scores[index] = x * x + y * y;
    }
    #endregion

    #region Particle Swarm workload
    private void RunParticleSwarm(int method)
    {
        int particleCount = Mathf.Max(2, _particleCount);
        if (method == 6 && _trialCount > 1)
        {
            RunParticleSwarmTaskRuns(particleCount);
            return;
        }

        _evaluationCount = _trialCount * particleCount * (_iterations + 1);
        _bestScore = float.MaxValue;
        _bestX = 0f;
        _bestY = 0f;
        double trialScoreTotal = 0d;
        float[] x = new float[particleCount];
        float[] y = new float[particleCount];
        float[] vx = new float[particleCount];
        float[] vy = new float[particleCount];
        float[] personalX = new float[particleCount];
        float[] personalY = new float[particleCount];
        float[] personalScore = new float[particleCount];
        float[] nextScore = new float[particleCount];
        NativeArray<float> jobX = default; NativeArray<float> jobY = default;
        NativeArray<float> jobVx = default; NativeArray<float> jobVy = default;
        NativeArray<float> jobPersonalX = default; NativeArray<float> jobPersonalY = default;
        NativeArray<float> jobScores = default;
        if (method == 3 || method == 4)
        {
            // Reuse job buffers for every iteration instead of allocating native memory in the loop.
            jobX = new NativeArray<float>(particleCount, Allocator.TempJob);
            jobY = new NativeArray<float>(particleCount, Allocator.TempJob);
            jobVx = new NativeArray<float>(particleCount, Allocator.TempJob);
            jobVy = new NativeArray<float>(particleCount, Allocator.TempJob);
            jobPersonalX = new NativeArray<float>(particleCount, Allocator.TempJob);
            jobPersonalY = new NativeArray<float>(particleCount, Allocator.TempJob);
            jobScores = new NativeArray<float>(particleCount, Allocator.TempJob);
        }

        for (int trial = 0; trial < _trialCount; trial++)
        {
            float globalScore = float.MaxValue;
            float globalX = 0f;
            float globalY = 0f;
            for (int particle = 0; particle < particleCount; particle++)
            {
                x[particle] = ToRange(StableValue(particle * 4 + trial * 100003 + _randomSeed));
                y[particle] = ToRange(StableValue(particle * 4 + 1 + trial * 100003 + _randomSeed));
                vx[particle] = StableValue(particle * 4 + 2 + trial * 100003 + _randomSeed) - 0.5f;
                vy[particle] = StableValue(particle * 4 + 3 + trial * 100003 + _randomSeed) - 0.5f;
                personalX[particle] = x[particle];
                personalY[particle] = y[particle];
                personalScore[particle] = x[particle] * x[particle] + y[particle] * y[particle];
                if (personalScore[particle] < globalScore)
                {
                    globalScore = personalScore[particle];
                    globalX = x[particle];
                    globalY = y[particle];
                }
            }

            for (int iteration = 0; iteration < _iterations; iteration++)
            {
                if (method == 2)
                {
                    float sharedX = globalX;
                    float sharedY = globalY;
                    System.Threading.Tasks.Parallel.For(0, particleCount, particle =>
                        UpdateParticle(particle, iteration, trial, sharedX, sharedY, x, y, vx, vy, personalX, personalY, nextScore));
                }
                else if (method == 6)
                {
                    float sharedX = globalX;
                    float sharedY = globalY;
                    RunTaskRunChunks(particleCount, particle =>
                        UpdateParticle(particle, iteration, trial, sharedX, sharedY, x, y, vx, vy, personalX, personalY, nextScore));
                }
                else if (method == 3 || method == 4)
                {
                    RunParticleJob(iteration, trial, globalX, globalY, x, y, vx, vy, personalX, personalY, nextScore,
                        jobX, jobY, jobVx, jobVy, jobPersonalX, jobPersonalY, jobScores, method == 4);
                }
                else
                {
                    for (int particle = 0; particle < particleCount; particle++)
                        UpdateParticle(particle, iteration, trial, globalX, globalY, x, y, vx, vy, personalX, personalY, nextScore);
                }

                // Each particle writes only its own slot; this reduction stays in one simple loop.
                for (int particle = 0; particle < particleCount; particle++)
                {
                    float score = nextScore[particle];
                    if (score < personalScore[particle])
                    {
                        personalScore[particle] = score;
                        personalX[particle] = x[particle];
                        personalY[particle] = y[particle];
                    }
                    if (score < globalScore)
                    {
                        globalScore = score;
                        globalX = x[particle];
                        globalY = y[particle];
                    }
                }
            }

            trialScoreTotal += globalScore;
            if (globalScore < _bestScore)
            {
                _bestScore = globalScore;
                _bestX = globalX;
                _bestY = globalY;
            }
        }

        _averageBestScore = (float)(trialScoreTotal / _trialCount);
        if (jobX.IsCreated) jobX.Dispose();
        if (jobY.IsCreated) jobY.Dispose();
        if (jobVx.IsCreated) jobVx.Dispose();
        if (jobVy.IsCreated) jobVy.Dispose();
        if (jobPersonalX.IsCreated) jobPersonalX.Dispose();
        if (jobPersonalY.IsCreated) jobPersonalY.Dispose();
        if (jobScores.IsCreated) jobScores.Dispose();
    }

    private void RunParticleSwarmTaskRuns(int particleCount)
    {
        _evaluationCount = _trialCount * particleCount * (_iterations + 1);
        _bestScore = float.MaxValue;
        _bestX = 0f;
        _bestY = 0f;
        int workerCount = Math.Min(_trialCount, Math.Min(8, Math.Max(1, Environment.ProcessorCount)));
        ParticleTrialResult[] results = new ParticleTrialResult[_trialCount];
        Task[] tasks = new Task[workerCount];
        for (int worker = 0; worker < workerCount; worker++)
        {
            int firstTrial = _trialCount * worker / workerCount;
            int afterLastTrial = _trialCount * (worker + 1) / workerCount;
            tasks[worker] = Task.Run(() =>
            {
                for (int trial = firstTrial; trial < afterLastTrial; trial++)
                    results[trial] = RunParticleSwarmTrial(trial, particleCount);
            });
        }

        Task.WhenAll(tasks).GetAwaiter().GetResult();
        double scoreTotal = 0d;
        for (int trial = 0; trial < _trialCount; trial++)
        {
            ParticleTrialResult result = results[trial];
            scoreTotal += result.Score;
            if (result.Score < _bestScore)
            {
                _bestScore = result.Score;
                _bestX = result.X;
                _bestY = result.Y;
            }
        }
        _averageBestScore = (float)(scoreTotal / _trialCount);
    }

    private ParticleTrialResult RunParticleSwarmTrial(int trial, int particleCount)
    {
        float[] x = new float[particleCount]; float[] y = new float[particleCount];
        float[] vx = new float[particleCount]; float[] vy = new float[particleCount];
        float[] personalX = new float[particleCount]; float[] personalY = new float[particleCount];
        float[] personalScore = new float[particleCount]; float[] nextScore = new float[particleCount];
        float globalScore = float.MaxValue; float globalX = 0f; float globalY = 0f;
        for (int particle = 0; particle < particleCount; particle++)
        {
            x[particle] = ToRange(StableValue(particle * 4 + trial * 100003 + _randomSeed));
            y[particle] = ToRange(StableValue(particle * 4 + 1 + trial * 100003 + _randomSeed));
            vx[particle] = StableValue(particle * 4 + 2 + trial * 100003 + _randomSeed) - 0.5f;
            vy[particle] = StableValue(particle * 4 + 3 + trial * 100003 + _randomSeed) - 0.5f;
            personalX[particle] = x[particle];
            personalY[particle] = y[particle];
            personalScore[particle] = x[particle] * x[particle] + y[particle] * y[particle];
            if (personalScore[particle] < globalScore)
            {
                globalScore = personalScore[particle];
                globalX = x[particle];
                globalY = y[particle];
            }
        }

        for (int iteration = 0; iteration < _iterations; iteration++)
        {
            for (int particle = 0; particle < particleCount; particle++)
                UpdateParticle(particle, iteration, trial, globalX, globalY, x, y, vx, vy, personalX, personalY, nextScore);

            for (int particle = 0; particle < particleCount; particle++)
            {
                float score = nextScore[particle];
                if (score < personalScore[particle])
                {
                    personalScore[particle] = score;
                    personalX[particle] = x[particle];
                    personalY[particle] = y[particle];
                }
                if (score < globalScore)
                {
                    globalScore = score;
                    globalX = x[particle];
                    globalY = y[particle];
                }
            }
        }

        return new ParticleTrialResult(globalScore, globalX, globalY);
    }

    private struct ParticleTrialResult
    {
        public readonly float Score;
        public readonly float X;
        public readonly float Y;

        public ParticleTrialResult(float score, float x, float y)
        {
            Score = score;
            X = x;
            Y = y;
        }
    }

    private void UpdateParticle(int particle, int iteration, int trial, float globalX, float globalY,
        float[] x, float[] y, float[] vx, float[] vy, float[] personalX, float[] personalY, float[] scores)
    {
        float r1 = StableValue(particle * 4 + iteration * 7919 + trial * 100003 + _randomSeed);
        float r2 = StableValue(particle * 4 + 1 + iteration * 7919 + trial * 100003 + _randomSeed);
        float r3 = StableValue(particle * 4 + 2 + iteration * 7919 + trial * 100003 + _randomSeed);
        float r4 = StableValue(particle * 4 + 3 + iteration * 7919 + trial * 100003 + _randomSeed);
        // v = 0.7v + 1.4r1(pBest - x) + 1.4r2(gBest - x); position = position + v.
        vx[particle] = 0.7f * vx[particle] + 1.4f * r1 * (personalX[particle] - x[particle]) + 1.4f * r2 * (globalX - x[particle]);
        vy[particle] = 0.7f * vy[particle] + 1.4f * r3 * (personalY[particle] - y[particle]) + 1.4f * r4 * (globalY - y[particle]);
        x[particle] = Mathf.Clamp(x[particle] + vx[particle], -5f, 5f);
        y[particle] = Mathf.Clamp(y[particle] + vy[particle], -5f, 5f);
        // Sphere objective: f(x, y) = x² + y². The minimum is zero at (0, 0).
        scores[particle] = x[particle] * x[particle] + y[particle] * y[particle];
    }
    #endregion

    #region Unity Jobs
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

    private struct ParticleJob : IJobParallelFor
    {
        public NativeArray<float> X; public NativeArray<float> Y;
        public NativeArray<float> VX; public NativeArray<float> VY;
        public NativeArray<float> PersonalX; public NativeArray<float> PersonalY;
        public NativeArray<float> Scores;
        public float GlobalX; public float GlobalY;
        public int Iteration; public int Trial; public int Seed;
        public void Execute(int particle)
        {
            float r1 = StableValue(particle * 4 + Iteration * 7919 + Trial * 100003 + Seed);
            float r2 = StableValue(particle * 4 + 1 + Iteration * 7919 + Trial * 100003 + Seed);
            float r3 = StableValue(particle * 4 + 2 + Iteration * 7919 + Trial * 100003 + Seed);
            float r4 = StableValue(particle * 4 + 3 + Iteration * 7919 + Trial * 100003 + Seed);
            VX[particle] = 0.7f * VX[particle] + 1.4f * r1 * (PersonalX[particle] - X[particle]) + 1.4f * r2 * (GlobalX - X[particle]);
            VY[particle] = 0.7f * VY[particle] + 1.4f * r3 * (PersonalY[particle] - Y[particle]) + 1.4f * r4 * (GlobalY - Y[particle]);
            X[particle] = Mathf.Clamp(X[particle] + VX[particle], -5f, 5f);
            Y[particle] = Mathf.Clamp(Y[particle] + VY[particle], -5f, 5f);
            Scores[particle] = X[particle] * X[particle] + Y[particle] * Y[particle];
        }
    }

    [BurstCompile]
    private struct ParticleBurstJob : IJobParallelFor
    {
        public NativeArray<float> X; public NativeArray<float> Y;
        public NativeArray<float> VX; public NativeArray<float> VY;
        public NativeArray<float> PersonalX; public NativeArray<float> PersonalY;
        public NativeArray<float> Scores;
        public float GlobalX; public float GlobalY;
        public int Iteration; public int Trial; public int Seed;
        public void Execute(int particle)
        {
            float r1 = StableValue(particle * 4 + Iteration * 7919 + Trial * 100003 + Seed);
            float r2 = StableValue(particle * 4 + 1 + Iteration * 7919 + Trial * 100003 + Seed);
            float r3 = StableValue(particle * 4 + 2 + Iteration * 7919 + Trial * 100003 + Seed);
            float r4 = StableValue(particle * 4 + 3 + Iteration * 7919 + Trial * 100003 + Seed);
            VX[particle] = 0.7f * VX[particle] + 1.4f * r1 * (PersonalX[particle] - X[particle]) + 1.4f * r2 * (GlobalX - X[particle]);
            VY[particle] = 0.7f * VY[particle] + 1.4f * r3 * (PersonalY[particle] - Y[particle]) + 1.4f * r4 * (GlobalY - Y[particle]);
            X[particle] = Mathf.Clamp(X[particle] + VX[particle], -5f, 5f);
            Y[particle] = Mathf.Clamp(Y[particle] + VY[particle], -5f, 5f);
            Scores[particle] = X[particle] * X[particle] + Y[particle] * Y[particle];
        }
    }

    private void RunRandomJob(int trial, float[] scores, NativeArray<float> jobScores, bool useBurst)
    {
        JobHandle handle;
        if (useBurst)
            handle = new RandomBurstJob { Scores = jobScores, Seed = _randomSeed, Trial = trial }.Schedule(scores.Length, 64);
        else
            handle = new RandomJob { Scores = jobScores, Seed = _randomSeed, Trial = trial }.Schedule(scores.Length, 64);
        handle.Complete();
        for (int index = 0; index < scores.Length; index++)
            scores[index] = jobScores[index];
    }

    private void RunParticleJob(int iteration, int trial, float globalX, float globalY,
        float[] x, float[] y, float[] vx, float[] vy, float[] personalX, float[] personalY, float[] scores,
        NativeArray<float> jobX, NativeArray<float> jobY, NativeArray<float> jobVx, NativeArray<float> jobVy,
        NativeArray<float> jobPersonalX, NativeArray<float> jobPersonalY, NativeArray<float> jobScores, bool useBurst)
    {
        for (int particle = 0; particle < x.Length; particle++)
        {
            jobX[particle] = x[particle]; jobY[particle] = y[particle];
            jobVx[particle] = vx[particle]; jobVy[particle] = vy[particle];
            jobPersonalX[particle] = personalX[particle]; jobPersonalY[particle] = personalY[particle];
        }
        JobHandle handle;
        if (useBurst)
            handle = new ParticleBurstJob { X = jobX, Y = jobY, VX = jobVx, VY = jobVy, PersonalX = jobPersonalX, PersonalY = jobPersonalY, Scores = jobScores, GlobalX = globalX, GlobalY = globalY, Iteration = iteration, Trial = trial, Seed = _randomSeed }.Schedule(x.Length, 32);
        else
            handle = new ParticleJob { X = jobX, Y = jobY, VX = jobVx, VY = jobVy, PersonalX = jobPersonalX, PersonalY = jobPersonalY, Scores = jobScores, GlobalX = globalX, GlobalY = globalY, Iteration = iteration, Trial = trial, Seed = _randomSeed }.Schedule(x.Length, 32);
        handle.Complete();
        for (int particle = 0; particle < x.Length; particle++)
        {
            x[particle] = jobX[particle]; y[particle] = jobY[particle];
            vx[particle] = jobVx[particle]; vy[particle] = jobVy[particle];
            scores[particle] = jobScores[particle];
        }
    }
    #endregion

    private void FinishRun(long computeStart, long wallStart, float asyncComputeMilliseconds = 0f)
    {
        _computeMilliseconds = _processingMethod == 1
            ? asyncComputeMilliseconds
            : (float)((Stopwatch.GetTimestamp() - computeStart) * 1000d / Stopwatch.Frequency);
        _wallMilliseconds = (float)((Stopwatch.GetTimestamp() - wallStart) * 1000d / Stopwatch.Frequency);
        // Set the completion marker last so the dashboard never reads half-published async results.
        _checksum = _bestScore.ToString("0.000000");
        Debug.Log("Optimization workload complete: algorithm=" + (_algorithm == 0 ? "PSO" : "Random Search") +
            ", evaluations=" + _evaluationCount + ", trials=" + _trialCount +
            ", checksum=" + _checksum + ", computeMs=" + _computeMilliseconds + ", wallMs=" + _wallMilliseconds, this);
    }

    // A deterministic integer hash gives all processing methods the same repeatable pseudo-random values.
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
