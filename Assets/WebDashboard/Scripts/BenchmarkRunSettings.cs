using UnityEngine;

/// <summary>
/// Scene-owned workload overrides shared by the benchmark runner and additive example scenes.
/// Values are serialized on the benchmark scene object and changed only in memory for each run.
/// </summary>
public sealed class BenchmarkRunSettings : MonoBehaviour
{
    [Header("Gravity simulation")]
    [SerializeField, Min(1)] private int _gravityBallCount = 128;

    [Header("Simple iteration")]
    [SerializeField, Min(1)] private int _simpleIterationItemCount = 100000;
    [SerializeField, Min(1)] private int _simpleIterationIterations = 100;
    [SerializeField, Min(1)] private int _simpleIterationBatchSize = 1000;

    [Header("Random Search")]
    [SerializeField, Min(1)] private int _randomSearchEvaluationsPerTrial = 100000;
    [SerializeField, Min(1)] private int _randomSearchTrials = 5;

    public static BenchmarkRunSettings Active { get; private set; }

    public int GravityBallCount { get { return Mathf.Max(1, _gravityBallCount); } }
    public int SimpleIterationItemCount { get { return Mathf.Max(1, _simpleIterationItemCount); } }
    public int SimpleIterationIterations { get { return Mathf.Max(1, _simpleIterationIterations); } }
    public int SimpleIterationBatchSize { get { return Mathf.Max(1, _simpleIterationBatchSize); } }
    public int RandomSearchEvaluationsPerTrial { get { return Mathf.Max(1, _randomSearchEvaluationsPerTrial); } }
    public int RandomSearchTrials { get { return Mathf.Max(1, _randomSearchTrials); } }

    public static int GetGravityBallCount(int fallback)
    {
        return Active == null ? Mathf.Max(1, fallback) : Active.GravityBallCount;
    }

    public static int GetSimpleIterationItemCount(int fallback)
    {
        return Active == null ? Mathf.Max(1, fallback) : Active.SimpleIterationItemCount;
    }

    public static int GetSimpleIterationIterations(int fallback)
    {
        return Active == null ? Mathf.Max(1, fallback) : Active.SimpleIterationIterations;
    }

    public static int GetSimpleIterationBatchSize(int fallback)
    {
        return Active == null ? Mathf.Max(1, fallback) : Active.SimpleIterationBatchSize;
    }

    public static int GetRandomSearchEvaluationsPerTrial(int fallback)
    {
        return Active == null ? Mathf.Max(1, fallback) : Active.RandomSearchEvaluationsPerTrial;
    }

    public static int GetRandomSearchTrials(int fallback)
    {
        return Active == null ? Mathf.Max(1, fallback) : Active.RandomSearchTrials;
    }

    private void Awake()
    {
        Active = this;
    }

    private void OnDestroy()
    {
        if (Active == this)
            Active = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Active = null;
    }

    public void SetGravityBallCount(int count)
    {
        _gravityBallCount = Mathf.Max(1, count);
    }

    public void SetSimpleIteration(int itemCount, int iterations, int batchSize)
    {
        _simpleIterationItemCount = Mathf.Max(1, itemCount);
        _simpleIterationIterations = Mathf.Max(1, iterations);
        _simpleIterationBatchSize = Mathf.Max(1, batchSize);
    }

    public void SetRandomSearch(int evaluationsPerTrial, int trials)
    {
        _randomSearchEvaluationsPerTrial = Mathf.Max(1, evaluationsPerTrial);
        _randomSearchTrials = Mathf.Max(1, trials);
    }
}
