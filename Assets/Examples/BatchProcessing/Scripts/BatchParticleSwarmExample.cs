using Stopwatch = System.Diagnostics.Stopwatch;
using UnityEngine;

/// <summary>
/// Uses Particle Swarm Optimization to minimize the two-dimensional Sphere function.
/// Each particle moves toward its own best answer and the swarm's best answer.
/// </summary>
public sealed class BatchParticleSwarmExample : MonoBehaviour
{
    #region Settings and reusable data
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

    #region Example implementation

    private void Start()
    {
        // PSO work is O(particles × dimensions × iterations); its reusable arrays use O(particles × dimensions) memory.
        _particleCount = Mathf.Max(2, _particleCount);
        _iterations = Mathf.Max(1, _iterations);
        _trialCount = Mathf.Max(1, _trialCount);

        // Keep these arrays and reuse them for every trial so the search loop does not allocate memory.
        float[] x = new float[_particleCount];
        float[] y = new float[_particleCount];
        float[] velocityX = new float[_particleCount];
        float[] velocityY = new float[_particleCount];
        float[] personalBestX = new float[_particleCount];
        float[] personalBestY = new float[_particleCount];
        float[] personalBestScore = new float[_particleCount];

        // Each trial evaluates 64 starting positions and then 99 new positions for each particle.
        // Total per trial: 64 + (64 * 99) = 6,400 function evaluations.
        long wallStartedAt = Stopwatch.GetTimestamp();
        long computeStartedAt = Stopwatch.GetTimestamp();
        double totalTrialBestScore = 0d;
        _evaluationCount = 0;
        _bestScore = float.MaxValue;

        for (int trial = 0; trial < _trialCount; trial++)
        {
            // The same seeds make every trial repeatable when this scene is run again.
            Random.InitState(_randomSeed + trial);
            float trialBestScore = float.MaxValue;
            float trialBestX = 0f;
            float trialBestY = 0f;

            for (int particle = 0; particle < _particleCount; particle++)
            {
                x[particle] = Random.Range(-5f, 5f);
                y[particle] = Random.Range(-5f, 5f);
                velocityX[particle] = Random.Range(-1f, 1f);
                velocityY[particle] = Random.Range(-1f, 1f);

                // Sphere function: score = x^2 + y^2. Its best answer is (0, 0), with score 0.
                float score = x[particle] * x[particle] + y[particle] * y[particle];
                personalBestX[particle] = x[particle];
                personalBestY[particle] = y[particle];
                personalBestScore[particle] = score;
                _evaluationCount++;

                if (score < trialBestScore)
                {
                    trialBestScore = score;
                    trialBestX = x[particle];
                    trialBestY = y[particle];
                }
            }

            for (int iteration = 0; iteration < _iterations; iteration++)
            {
                // All particles in this iteration use the same best answer from the previous iteration.
                float sharedBestX = trialBestX;
                float sharedBestY = trialBestY;

                for (int particle = 0; particle < _particleCount; particle++)
                {
                    float personalPullX = Random.value;
                    float swarmPullX = Random.value;
                    float personalPullY = Random.value;
                    float swarmPullY = Random.value;

                    // v = 0.7v + 1.4r1(pBest - x) + 1.4r2(gBest - x); then position = position + v.
                    // This combines momentum, the particle's own best, and the swarm's best.
                    velocityX[particle] = 0.7f * velocityX[particle]
                        + 1.4f * personalPullX * (personalBestX[particle] - x[particle])
                        + 1.4f * swarmPullX * (sharedBestX - x[particle]);
                    velocityY[particle] = 0.7f * velocityY[particle]
                        + 1.4f * personalPullY * (personalBestY[particle] - y[particle])
                        + 1.4f * swarmPullY * (sharedBestY - y[particle]);

                    // New position = old position + updated velocity; keep it inside the search area.
                    x[particle] = Mathf.Clamp(x[particle] + velocityX[particle], -5f, 5f);
                    y[particle] = Mathf.Clamp(y[particle] + velocityY[particle], -5f, 5f);

                    // Evaluate the same function used to score the starting positions.
                    float score = x[particle] * x[particle] + y[particle] * y[particle];
                    _evaluationCount++;

                    if (score < personalBestScore[particle])
                    {
                        personalBestScore[particle] = score;
                        personalBestX[particle] = x[particle];
                        personalBestY[particle] = y[particle];
                    }

                    if (score < trialBestScore)
                    {
                        trialBestScore = score;
                        trialBestX = x[particle];
                        trialBestY = y[particle];
                    }
                }
            }

            totalTrialBestScore += trialBestScore;
            if (trialBestScore < _bestScore)
            {
                _bestScore = trialBestScore;
                _bestX = trialBestX;
                _bestY = trialBestY;
            }
        }

        _computeMilliseconds = (float)((Stopwatch.GetTimestamp() - computeStartedAt) * 1000d / Stopwatch.Frequency);
        _averageBestScore = (float)(totalTrialBestScore / _trialCount);
        _checksum = _bestScore.ToString("0.000000");
        _wallMilliseconds = (float)((Stopwatch.GetTimestamp() - wallStartedAt) * 1000d / Stopwatch.Frequency);

        Debug.Log("PSO Sphere search: evaluations=" + _evaluationCount + ", trials=" + _trialCount +
            ", best=(" + _bestX.ToString("0.0000") + ", " + _bestY.ToString("0.0000") + ")" +
            ", bestScore=" + _bestScore.ToString("0.000000") +
            ", averageBestScore=" + _averageBestScore.ToString("0.000000") +
            ", computeMs=" + _computeMilliseconds + ", wallMs=" + _wallMilliseconds, this);
    }
    #endregion
}
