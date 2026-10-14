using Stopwatch = System.Diagnostics.Stopwatch;
using UnityEngine;

/// <summary>
/// Tries random points to minimize the same two-dimensional Sphere function as PSO.
/// It is a simple baseline for comparing search quality and calculation time.
/// </summary>
public sealed class BatchRandomSearchExample : MonoBehaviour
{
    [SerializeField, Min(1)] private int _evaluationBudgetPerTrial = 6400;
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

    private void Start()
    {
        // Random Search work is O(evaluations × dimensions) and needs O(1) extra memory.
        _evaluationBudgetPerTrial = Mathf.Max(1, _evaluationBudgetPerTrial);
        _trialCount = Mathf.Max(1, _trialCount);

        long wallStartedAt = Stopwatch.GetTimestamp();
        long computeStartedAt = Stopwatch.GetTimestamp();
        double totalTrialBestScore = 0d;
        _evaluationCount = 0;
        _bestScore = float.MaxValue;

        for (int trial = 0; trial < _trialCount; trial++)
        {
            // Use the same repeatable seeds as the PSO example.
            Random.InitState(_randomSeed + trial);
            float trialBestScore = float.MaxValue;
            float trialBestX = 0f;
            float trialBestY = 0f;

            for (int evaluation = 0; evaluation < _evaluationBudgetPerTrial; evaluation++)
            {
                float x = Random.Range(-5f, 5f);
                float y = Random.Range(-5f, 5f);

                // Sphere function: score = x^2 + y^2. Its best answer is (0, 0), with score 0.
                float score = x * x + y * y;
                _evaluationCount++;

                if (score < trialBestScore)
                {
                    trialBestScore = score;
                    trialBestX = x;
                    trialBestY = y;
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

        Debug.Log("Random Search Sphere: evaluations=" + _evaluationCount + ", trials=" + _trialCount +
            ", best=(" + _bestX.ToString("0.0000") + ", " + _bestY.ToString("0.0000") + ")" +
            ", bestScore=" + _bestScore.ToString("0.000000") +
            ", averageBestScore=" + _averageBestScore.ToString("0.000000") +
            ", computeMs=" + _computeMilliseconds + ", wallMs=" + _wallMilliseconds, this);
    }
}
