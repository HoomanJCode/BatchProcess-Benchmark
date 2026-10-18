using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Runs gravity, batch, or optimization examples one at a time and sends readings
/// to the local browser dashboard. Each calculation stays in its own beginner
/// example script; this runner only controls the comparison and reads results.
/// </summary>
public sealed class RealtimeBenchmarkDashboard : MonoBehaviour
{
    [Header("Comparison settings")]
    [SerializeField, Min(0)] private int _warmupSeconds = 2;
    [SerializeField, Min(1)] private int _measurementSeconds = 8;
    [SerializeField, Min(1)] private int _dashboardPort = 8765;
    [SerializeField] private bool _is2D;
    [SerializeField] private bool _isBatchComparison;
    [SerializeField] private bool _isOptimizationComparison;
    [SerializeField] private bool _isAllExamplesComparison;
    [SerializeField, Min(1)] private int _batchRunsPerStrategy = 5;
    [SerializeField] private bool _openDashboardInBrowser = true;

    private const int RecorderCapacity = 512;
    private const float DashboardUpdateInterval = 0.2f;
    // Collect one summed value for the current frame, even if a marker runs more than once.
    private const ProfilerRecorderOptions FrameRecorderOptions =
        ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.WrapAroundWhenCapacityReached;
    private const string TwoDBenchmarkSceneFolder = "Assets/Examples/GravitySimulation/2D/Scenes/Benchmark/";
    private const string ThreeDBenchmarkSceneFolder = "Assets/Examples/GravitySimulation/3D/Scenes/Benchmark/";
    private const string SimpleIterationSceneFolder = "Assets/Examples/SimpleIteration/Scenes/";
    private const string PsoSceneFolder = "Assets/Examples/PSOSimulation/Scenes/";
    private const string RandomSearchSceneFolder = "Assets/Examples/RandomSearchOptimization/Scenes/";

    // Keep these entries in the same order as the strategies listed in the README.
    // Async task work uses a complete-calculation marker; each other gravity strategy records its method marker.
    private static readonly ComparisonMode[] Modes =
    {
        new ComparisonMode("Single threaded", "SingleThreaded", "GravitySimulation.SingleThreaded"),
        new ComparisonMode("C# async", "CSharpAsync", "GravitySimulation.CSharpAsync"),
        new ComparisonMode("C# parallel", "CSharpParallel", "GravitySimulation.CSharpParallel"),
        new ComparisonMode("Unity Jobs", "UnityJobs", "GravitySimulation.UnityJobs"),
        new ComparisonMode("Unity Jobs + Burst", "UnityJobsBurst", "GravitySimulation.UnityJobsBurst")
    };

    // Each batch strategy uses the same input size, batch size, and calculation in its own scene.
    private static readonly ComparisonMode[] BatchModes =
    {
        new ComparisonMode("Single threaded", "SingleThreaded", "SimpleIteration.SingleThreaded"),
        new ComparisonMode("C# async", "CSharpAsync", "SimpleIteration.CSharpAsync.Worker"),
        new ComparisonMode("C# parallel", "CSharpParallel", "SimpleIteration.CSharpParallel"),
        new ComparisonMode("Unity Jobs", "UnityJobs", "SimpleIteration.UnityJobs"),
        new ComparisonMode("Unity coroutine", "UnityCoroutine", "SimpleIteration.UnityCoroutine.Chunk"),
        new ComparisonMode("Unity Jobs + Burst", "UnityJobsBurst", "SimpleIteration.UnityJobsBurst")
    };

    // Each algorithm is a workload. We run it once with every processing method.
    private static readonly ComparisonMode[] OptimizationModes =
    {
        new ComparisonMode("Single threaded", "PSO_SingleThreaded", "BatchProcessing.Optimization.PSO.Single", "PSOProcessingExample", "Particle swarm (PSO)"),
        new ComparisonMode("C# async", "PSO_CSharpAsync", "BatchProcessing.Optimization.PSO.Async", "PSOProcessingExample", "Particle swarm (PSO)"),
        new ComparisonMode("C# parallel", "PSO_CSharpParallel", "BatchProcessing.Optimization.PSO.Parallel", "PSOProcessingExample", "Particle swarm (PSO)"),
        new ComparisonMode("Unity Jobs", "PSO_UnityJobs", "BatchProcessing.Optimization.PSO.Jobs", "PSOProcessingExample", "Particle swarm (PSO)"),
        new ComparisonMode("Unity coroutine", "PSO_UnityCoroutine", "BatchProcessing.Optimization.PSO.Coroutine", "PSOProcessingExample", "Particle swarm (PSO)"),
        new ComparisonMode("Unity Jobs + Burst", "PSO_UnityJobsBurst", "BatchProcessing.Optimization.PSO.Burst", "PSOProcessingExample", "Particle swarm (PSO)"),
        new ComparisonMode("Single threaded", "RandomSearch_SingleThreaded", "BatchProcessing.Optimization.Random.Single", "RandomSearchProcessingExample", "Random search"),
        new ComparisonMode("C# async", "RandomSearch_CSharpAsync", "BatchProcessing.Optimization.Random.Async", "RandomSearchProcessingExample", "Random search"),
        new ComparisonMode("C# parallel", "RandomSearch_CSharpParallel", "BatchProcessing.Optimization.Random.Parallel", "RandomSearchProcessingExample", "Random search"),
        new ComparisonMode("Unity Jobs", "RandomSearch_UnityJobs", "BatchProcessing.Optimization.Random.Jobs", "RandomSearchProcessingExample", "Random search"),
        new ComparisonMode("Unity coroutine", "RandomSearch_UnityCoroutine", "BatchProcessing.Optimization.Random.Coroutine", "RandomSearchProcessingExample", "Random search"),
        new ComparisonMode("Unity Jobs + Burst", "RandomSearch_UnityJobsBurst", "BatchProcessing.Optimization.Random.Burst", "RandomSearchProcessingExample", "Random search")
    };

    // Capacity covers several minutes of samples at common frame rates, avoiding List growth during measurement.
    private readonly List<float> _profileSamplesMs = new List<float>(120000);
    private readonly List<float> _frameSamplesMs = new List<float>(120000);
    private readonly List<BenchmarkResult> _results = new List<BenchmarkResult>();
    private readonly BenchmarkState _state = new BenchmarkState();

    private ProfilerRecorder _profileRecorder;
    private ProfilerRecorder _mainThreadRecorder;
    private LocalDashboardServer _dashboardServer;
    private float _sampleTimer;
    private bool _hasStarted;
    private bool _headlessMode;
    private bool _allExamplesFinished;
    private string _headlessOutputPath;
    private bool _acceptDashboardCommands;
    private string _requestedAlgorithm = "";
    private string _requestedMethod = "";

    private void Start()
    {
        // The runner must survive while each example scene is loaded in turn.
        DontDestroyOnLoad(gameObject);
        _headlessMode = HasCommandLineArgument("-gravity-benchmark-headless");
        if (_headlessMode)
        {
            // A null graphics device can otherwise advance thousands of empty frames per second.
            // Use the same fixed frame budget for every headless gravity strategy.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;

            string dimension = GetCommandLineValue("-gravity-benchmark-dimension");
            if (!string.IsNullOrEmpty(dimension))
                _is2D = string.Equals(dimension, "2D", StringComparison.OrdinalIgnoreCase);

            string measurement = GetCommandLineValue("-gravity-benchmark-measurement-seconds");
            int parsedMeasurement;
            if (int.TryParse(measurement, out parsedMeasurement) && parsedMeasurement > 0)
                _measurementSeconds = parsedMeasurement;

            string warmup = GetCommandLineValue("-gravity-benchmark-warmup-seconds");
            int parsedWarmup;
            if (int.TryParse(warmup, out parsedWarmup) && parsedWarmup >= 0)
                _warmupSeconds = parsedWarmup;

            _headlessOutputPath = GetCommandLineValue("-gravity-benchmark-output");
            if (string.IsNullOrEmpty(_headlessOutputPath))
            {
                string dimensionName = _is2D ? "2d" : "3d";
                _headlessOutputPath = Path.Combine(
                    Path.GetDirectoryName(Application.dataPath), "gravity-benchmark-" + dimensionName + ".json");
            }
            _headlessOutputPath = Path.GetFullPath(_headlessOutputPath);
        }

        _state.workload = _isOptimizationComparison ? "optimization" : (_isBatchComparison ? "batch" : "gravity");
        _state.dimension = _isOptimizationComparison ? "Optimization" : (_isBatchComparison ? "Batch processing" : (_is2D ? "2D" : "3D"));
        _batchRunsPerStrategy = Mathf.Max(1, _batchRunsPerStrategy);
        _state.batchRunsPerStrategy = _batchRunsPerStrategy;
        _state.status = "starting";
        _state.phase = _headlessMode ? "Starting headless profiler run" : "Starting local profiler stream";
        _state.measurementSeconds = _measurementSeconds;
        _state.warmupSeconds = _warmupSeconds;
        if (!_headlessMode)
        {
            string dashboardHtmlPath = Path.GetFullPath(Path.Combine(
                Application.dataPath, "WebDashboard", "index.html"));
            _dashboardServer = new LocalDashboardServer(_dashboardPort);
            _dashboardServer.Start(dashboardHtmlPath);
            _state.dashboardMessage = _dashboardServer.Message;
        }
        _mainThreadRecorder = ProfilerRecorder.StartNew(
            ProfilerCategory.Internal, "Main Thread", RecorderCapacity, FrameRecorderOptions);
        PublishState();
        if (!_headlessMode && _openDashboardInBrowser && _dashboardServer.IsRunning)
            Application.OpenURL("http://127.0.0.1:" + _dashboardPort + "/");

        // Starting Play mode in a comparison scene immediately begins the run.
        if (!_hasStarted)
        {
            _hasStarted = true;
            if (_isAllExamplesComparison)
                StartCoroutine(RunAllExamplesComparison());
            else
                StartCoroutine(_isBatchComparison || _isOptimizationComparison ? RunBatchComparison() : RunComparison());
        }
    }

    #region Browser controls for new benchmark runs
    // The editor stays in Play mode after a comparison so the local page can start another run.
    private void Update()
    {
        if (_headlessMode || _dashboardServer == null || !_acceptDashboardCommands)
            return;

        string requestJson;
        if (!_dashboardServer.TryTakeRunRequest(out requestJson))
            return;

        DashboardRunRequest request = JsonUtility.FromJson<DashboardRunRequest>(requestJson);
        if (request == null || !IsValidRunRequest(request))
        {
            _state.phase = "Settings were invalid; correct them and run again";
            PublishState();
            return;
        }

        ApplyRunRequest(request);
        _acceptDashboardCommands = false;
        _state.status = "starting";
        _state.workload = request.workload == "pso" || request.workload == "randomSearch" ? "optimization" : request.workload;
        _state.dimension = request.workload == "gravity" ? request.dimension :
            (request.workload == "batch" ? "Batch processing" : "Optimization");
        _state.warmupSeconds = _warmupSeconds;
        _state.measurementSeconds = _measurementSeconds;
        _state.batchRunsPerStrategy = _batchRunsPerStrategy;
        _state.selectedMethod = request.selectedMethod ?? "";
        _state.completed = 0;
        _state.total = 0;
        _state.progress = 0f;
        _state.results = new BenchmarkResult[0];
        _state.phase = "Starting requested comparison";
        PublishState();
        StartCoroutine(RunRequestedComparison(request));
    }

    private IEnumerator RunRequestedComparison(DashboardRunRequest request)
    {
        _is2D = request.dimension == "2D";
        _isAllExamplesComparison = false;
        _isBatchComparison = request.workload == "batch";
        _isOptimizationComparison = request.workload == "pso" || request.workload == "randomSearch";
        _requestedAlgorithm = request.workload == "pso" ? "Particle swarm (PSO)" :
            (request.workload == "randomSearch" ? "Random search" : "");
        _requestedMethod = request.selectedMethod ?? "";
        _state.workload = request.workload == "pso" || request.workload == "randomSearch" ? "optimization" : request.workload;
        _state.dimension = request.workload == "gravity" ? request.dimension :
            (request.workload == "batch" ? "Batch processing" : "Optimization");
        _results.Clear();
        _state.results = new BenchmarkResult[0];

        if (request.workload == "gravity")
            yield return RunComparison();
        else
            yield return RunBatchComparison();

        _acceptDashboardCommands = _state.status == "complete" || _state.status == "error";
    }

    private bool IsValidRunRequest(DashboardRunRequest request)
    {
        bool validWorkload = request.workload == "gravity" || request.workload == "batch" ||
            request.workload == "pso" || request.workload == "randomSearch";
        bool validDimension = request.dimension == "2D" || request.dimension == "3D";
        return validWorkload && (request.workload != "gravity" || validDimension) &&
            IsValidSelectedMethod(request.workload, request.selectedMethod) &&
            request.balls >= 1 && request.balls <= 2000 &&
            request.measurementSeconds >= 1 && request.measurementSeconds <= 60 &&
            request.warmupSeconds >= 0 && request.warmupSeconds <= 30 &&
            request.runs >= 1 && request.runs <= 20 &&
            request.items >= 1 && request.items <= 5000000 &&
            request.iterations >= 1 && request.iterations <= 100000 &&
            request.batchSize >= 1 && request.batchSize <= 5000000 &&
            request.particles >= 2 && request.particles <= 10000 &&
            request.evaluations >= 1 && request.evaluations <= 5000000 &&
            request.trials >= 1 && request.trials <= 100;
    }

    private void ApplyRunRequest(DashboardRunRequest request)
    {
        _measurementSeconds = request.measurementSeconds;
        _warmupSeconds = request.warmupSeconds;
        _batchRunsPerStrategy = request.runs;
        // Every strategy scene reads these shared values before it allocates or starts work.
        PlayerPrefs.SetInt("SimulationLab.Balls", request.balls);
        PlayerPrefs.SetInt("SimulationLab.Items", request.items);
        PlayerPrefs.SetInt("SimulationLab.Iterations", request.iterations);
        PlayerPrefs.SetInt("SimulationLab.BatchSize", request.batchSize);
        // Random Search uses one candidate per iteration, so this maps exactly to evaluations per trial.
        PlayerPrefs.SetInt("SimulationLab.PsoParticles", request.particles);
        PlayerPrefs.SetInt("SimulationLab.PsoIterations", request.iterations);
        PlayerPrefs.SetInt("SimulationLab.PsoTrials", request.trials);
        PlayerPrefs.SetInt("SimulationLab.RandomSearchEvaluationsPerTrial", Mathf.Max(1, request.evaluations));
        PlayerPrefs.SetInt("SimulationLab.RandomSearchTrials", request.trials);
        PlayerPrefs.Save();
    }

    [Serializable]
    private sealed class DashboardRunRequest
    {
        public string workload;
        public string dimension;
        public int balls;
        public int measurementSeconds;
        public int warmupSeconds;
        public int runs;
        public int items;
        public int iterations;
        public int batchSize;
        public int particles;
        public int evaluations;
        public int trials;
        public string selectedMethod;
    }

    private static bool IsValidSelectedMethod(string workload, string method)
    {
        if (string.IsNullOrEmpty(method))
            return true;

        if (method == "unity-single" || method == "unity-async" || method == "unity-parallel" ||
            method == "unity-jobs" || method == "unity-coroutine" || method == "unity-burst" ||
            method == "python-single" || method == "python-threads" || method == "python-processes" ||
            method == "python-numpy")
            return true;

        return false;
    }

    private bool IsPythonMethodSelected
    {
        get { return _requestedMethod.StartsWith("python-", StringComparison.Ordinal); }
    }

    private static string GetUnityMethodId(string displayName)
    {
        switch (displayName)
        {
            case "Single threaded": return "unity-single";
            case "C# async": return "unity-async";
            case "C# parallel": return "unity-parallel";
            case "Unity Jobs": return "unity-jobs";
            case "Unity coroutine": return "unity-coroutine";
            case "Unity Jobs + Burst": return "unity-burst";
            default: return "";
        }
    }

    private ComparisonMode[] SelectUnityModes(ComparisonMode[] allModes)
    {
        List<ComparisonMode> selected = new List<ComparisonMode>();
        for (int i = 0; i < allModes.Length; i++)
        {
            ComparisonMode mode = allModes[i];
            if (_isOptimizationComparison && !string.IsNullOrEmpty(_requestedAlgorithm) &&
                mode.algorithm != _requestedAlgorithm)
                continue;
            if (IsPythonMethodSelected)
                continue;
            if (!string.IsNullOrEmpty(_requestedMethod) && GetUnityMethodId(mode.displayName) != _requestedMethod)
                continue;
            selected.Add(mode);
        }
        return selected.ToArray();
    }

    private int GetPythonMethodCount()
    {
        return string.IsNullOrEmpty(_requestedMethod) ? 4 : (IsPythonMethodSelected ? 1 : 0);
    }

    private static string GetPythonMethodArgument(string selectedMethod)
    {
        switch (selectedMethod)
        {
            case "python-single": return "single";
            case "python-threads": return "threads";
            case "python-processes": return "processes";
            case "python-numpy": return "numpy";
            default: return "all";
        }
    }
    #endregion

    #region Run every example for the full dashboard
    // The all-examples scene runs one comparison at a time so each dashboard tab gets its own saved result.
    private IEnumerator RunAllExamplesComparison()
    {
        _is2D = true;
        _isBatchComparison = false;
        _isOptimizationComparison = false;
        _state.workload = "gravity";
        _state.dimension = "2D";
        _results.Clear();
        _state.results = new BenchmarkResult[0];
        yield return RunComparison();
        if (_state.status == "error") yield break;
        yield return new WaitForSecondsRealtime(0.75f);

        _is2D = false;
        _state.workload = "gravity";
        _state.dimension = "3D";
        _results.Clear();
        _state.results = new BenchmarkResult[0];
        _state.status = "starting";
        PublishState();
        yield return RunComparison();
        if (_state.status == "error") yield break;
        yield return new WaitForSecondsRealtime(0.75f);

        _isBatchComparison = true;
        _state.workload = "batch";
        _state.dimension = "Batch processing";
        _results.Clear();
        _state.results = new BenchmarkResult[0];
        _state.status = "starting";
        PublishState();
        yield return RunBatchComparison();
        if (_state.status == "error") yield break;
        yield return new WaitForSecondsRealtime(0.75f);

        _isBatchComparison = false;
        _isOptimizationComparison = true;
        _state.workload = "optimization";
        _state.dimension = "Optimization";
        _results.Clear();
        _state.results = new BenchmarkResult[0];
        _state.status = "starting";
        PublishState();
        yield return RunBatchComparison();
        if (_state.status == "error") yield break;

        _allExamplesFinished = true;
        if (_headlessMode)
            FinishHeadlessRun(0);
        else
            _acceptDashboardCommands = true;
    }
    #endregion

    // Run each self-contained batch or optimization scene and collect its timings.
    // The measured calculations stay inside their beginner example scripts.
    private IEnumerator RunBatchComparison()
    {
        ComparisonMode[] allModes = _isOptimizationComparison ? OptimizationModes : BatchModes;
        ComparisonMode[] modes = SelectUnityModes(allModes);
        _state.status = "running";
        _state.pythonMessage = "";
        _state.total = modes.Length + GetPythonMethodCount();
        _state.completed = 0;
        _results.Clear();
        PublishState();

        for (int modeIndex = 0; modeIndex < modes.Length; modeIndex++)
        {
            ComparisonMode mode = modes[modeIndex];
            if (_headlessMode)
                Debug.Log("Headless comparison loading " + mode.displayName);
            List<float> computeSamples = new List<float>(_batchRunsPerStrategy);
            List<float> wallSamples = new List<float>(_batchRunsPerStrategy);
            List<float> optimizationScoreSamples = _isOptimizationComparison ? new List<float>(_batchRunsPerStrategy) : null;
            int itemCount = 0;
            int iterations = 0;
            int batchSize = 0;
            int evaluationCount = 0;
            int trialCount = 0;
            int particleCount = 0;
            float bestScore = float.MaxValue;
            float bestX = 0f;
            float bestY = 0f;
            _state.activeExample = string.IsNullOrEmpty(mode.algorithm)
                ? mode.displayName : mode.algorithm + " · " + mode.displayName;
            _state.markerName = mode.markerName;
            _state.phase = _isOptimizationComparison ? "Preparing " + mode.algorithm + " · " + mode.displayName : "Preparing repeated batch runs";
            _state.completed = modeIndex;
            _state.progress = (float)modeIndex / modes.Length;
            _state.chartProfileMs = new float[0];
            _state.chartMainThreadMs = new float[0];
            _state.chartFrameMs = new float[0];
            _state.evaluationCount = 0;
            _state.trialCount = 0;
            _state.batchSize = 0;
            _state.bestScore = 0f;
            _state.averageBestScore = 0f;
            _state.bestX = 0f;
            _state.bestY = 0f;
            PublishState();

            // Run one unmeasured pass first so JIT compilation, worker startup, and Burst compilation
            // do not get charged to the first recorded sample for this processing method.
            for (int runIndex = -1; runIndex < _batchRunsPerStrategy; runIndex++)
            {
                bool isWarmupRun = runIndex < 0;
                string scenePath;
                if (_isOptimizationComparison)
                {
                    string optimizationFolder = mode.algorithm == "Particle swarm (PSO)" ? PsoSceneFolder : RandomSearchSceneFolder;
                    scenePath = optimizationFolder + mode.sceneName + ".unity";
                }
                else
                {
                    scenePath = SimpleIterationSceneFolder + "SimpleIteration_" + mode.sceneName + ".unity";
                }
                int buildIndex = SceneUtility.GetBuildIndexByScenePath(scenePath);
                if (buildIndex < 0)
                {
                    SetBatchError("A batch example scene is missing from Build Settings", scenePath, 2);
                    yield break;
                }

                _state.phase = isWarmupRun
                    ? "Warming up " + mode.displayName
                    : "Loading " + mode.displayName + " · run " + (runIndex + 1) + " of " + _batchRunsPerStrategy;
                _state.progress = (modeIndex + Mathf.Clamp01((runIndex + 1f) / _batchRunsPerStrategy)) / modes.Length;
                PublishState();
                AsyncOperation load = SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Single);
                if (load == null)
                {
                    SetBatchError("Unity could not load a batch example scene", scenePath, 3);
                    yield break;
                }
                yield return load;

                MonoBehaviour example = null;
                float computeMs = 0f;
                float wallMs = 0f;
                float runBestScore = 0f;
                float runAverageBestScore = 0f;
                float runBestX = 0f;
                float runBestY = 0f;
                string checksum = "—";
                float timeout = Time.realtimeSinceStartup + 120f;
                while (Time.realtimeSinceStartup < timeout)
                {
                    GameObject exampleObject = GameObject.Find("Batch Example");
                    if (exampleObject != null)
                    {
                        MonoBehaviour[] behaviours = exampleObject.GetComponents<MonoBehaviour>();
                        for (int behaviourIndex = 0; behaviourIndex < behaviours.Length; behaviourIndex++)
                        {
                            if (behaviours[behaviourIndex] != null &&
                                behaviours[behaviourIndex].GetType().Name == (string.IsNullOrEmpty(mode.componentName)
                                    ? "Batch" + mode.sceneName + "Example" : mode.componentName))
                            {
                                example = behaviours[behaviourIndex];
                                break;
                            }
                        }
                    }

                    bool hasFinished = false;
                    if (example != null)
                    {
                        if (_isOptimizationComparison)
                        {
                            hasFinished = TryReadOptimizationValues(example, out computeMs, out wallMs,
                                out evaluationCount, out trialCount, out particleCount, out iterations,
                                out runBestScore, out runAverageBestScore,
                                out runBestX, out runBestY, out checksum);
                        }
                        else
                        {
                            hasFinished = TryReadBatchValues(example, out computeMs, out wallMs,
                                out itemCount, out iterations, out batchSize, out checksum);
                        }
                    }
                    if (hasFinished)
                        break;

                    _state.phase = "Waiting for " + mode.displayName + " to finish";
                    yield return null;
                }

                if (example == null || checksum == "—")
                {
                    Debug.LogError("Headless comparison timed out: exampleFound=" + (example != null) + ", checksum=" + checksum);
                    SetBatchError(mode.displayName + " did not finish before the 120-second timeout", scenePath, 4);
                    yield break;
                }

                if (isWarmupRun)
                {
                    // Discard this result; it only primes the exact scene and processing path.
                    _state.phase = "Warm-up complete for " + mode.displayName;
                    PublishState();
                    yield return null;
                    continue;
                }

                if (_headlessMode)
                    Debug.Log("Headless comparison read " + mode.algorithm + " · " + mode.displayName + ": evaluations=" + evaluationCount +
                        ", bestScore=" + runBestScore + ", meanBestScore=" + runAverageBestScore);

                computeSamples.Add(computeMs);
                wallSamples.Add(wallMs);
                if (_isOptimizationComparison)
                {
                    optimizationScoreSamples.Add(runAverageBestScore);
                    if (runBestScore < bestScore)
                    {
                        bestScore = runBestScore;
                        bestX = runBestX;
                        bestY = runBestY;
                    }
                }
                _state.profileMs = computeMs;
                _state.mainThreadMs = 0f;
                _state.frameMs = wallMs;
                _state.averageProfileMs = Average(computeSamples);
                _state.p95ProfileMs = Percentile(computeSamples, 0.95f);
                _state.ballCount = itemCount;
                _state.batchSize = batchSize;
                _state.evaluationCount = evaluationCount;
                _state.trialCount = trialCount * computeSamples.Count;
                _state.bestScore = bestScore;
                _state.averageBestScore = _isOptimizationComparison ? Average(optimizationScoreSamples) : 0f;
                _state.bestX = bestX;
                _state.bestY = bestY;
                _state.progress = (modeIndex + (runIndex + 1f) / _batchRunsPerStrategy) / modes.Length;
                AddChartPoint(computeMs, 0f, wallMs);
                _state.phase = "Finished run " + (runIndex + 1) + " of " + _batchRunsPerStrategy;
                PublishState();
                yield return null;
            }

            BenchmarkResult result = new BenchmarkResult
            {
                algorithm = mode.algorithm,
                strategy = mode.displayName,
                markerName = mode.markerName,
                balls = itemCount,
                workItems = itemCount,
                iterations = iterations,
                batchSize = batchSize,
                evaluationCount = evaluationCount,
                particleCount = particleCount,
                bestScore = _isOptimizationComparison ? bestScore : 0f,
                averageBestScore = _isOptimizationComparison ? Average(optimizationScoreSamples) : 0f,
                bestX = bestX,
                bestY = bestY,
                sampleCount = _isOptimizationComparison ? trialCount * computeSamples.Count : computeSamples.Count,
                averageProfileMs = Average(computeSamples),
                medianProfileMs = Percentile(computeSamples, 0.50f),
                p95ProfileMs = Percentile(computeSamples, 0.95f),
                averageFrameMs = Average(wallSamples),
                p95FrameMs = Percentile(wallSamples, 0.95f)
            };
            _results.Add(result);
            _state.results = _results.ToArray();
            _state.completed = modeIndex + 1;
            PublishState();
        }

        // Run Python after Unity's methods finish, so both runtimes do not compete
        // for CPU time during their measurements.
        // Python repeats the same complete workload after Unity is done. Optimization
        // keeps its two algorithm tabs separate while sharing one Unity comparison run.
        if (_isOptimizationComparison)
        {
            if ((string.IsNullOrEmpty(_requestedAlgorithm) || _requestedAlgorithm == "Particle swarm (PSO)") &&
                (string.IsNullOrEmpty(_requestedMethod) || IsPythonMethodSelected))
                yield return RunPythonComparison("pso", "Particle swarm (PSO)");
            if ((string.IsNullOrEmpty(_requestedAlgorithm) || _requestedAlgorithm == "Random search") &&
                (string.IsNullOrEmpty(_requestedMethod) || IsPythonMethodSelected))
                yield return RunPythonComparison("random-search", "Random search");
        }
        else if (string.IsNullOrEmpty(_requestedMethod) || IsPythonMethodSelected)
        {
            yield return RunPythonComparison("simple-iteration", "");
        }

        _state.status = "complete";
        _state.phase = string.IsNullOrEmpty(_state.pythonMessage)
            ? (string.IsNullOrEmpty(_requestedMethod) ? "Unity and Python methods measured" :
                (IsPythonMethodSelected ? "Python method measured" : "Unity method measured"))
            : (IsPythonMethodSelected ? "Python comparison needs attention" : "Unity methods measured · Python comparison needs attention");
        _state.progress = 1f;
        _state.activeExample = "Complete";
        _state.completed = _results.Count;
        _state.total = _results.Count;
        _state.results = _results.ToArray();
        PublishState();
        _requestedAlgorithm = "";
        _requestedMethod = "";
        if (_headlessMode)
            Debug.Log("Headless comparison finished with " + _results.Count + " results.");
        FinishHeadlessRun(0);
        _acceptDashboardCommands = !_headlessMode && !_isAllExamplesComparison;
    }

    #region Run Python methods outside Unity
    // Run Python only after Unity finishes its passes so the two runtimes do not compete.
    private IEnumerator RunPythonComparison(string workload, string algorithmName)
    {
        BenchmarkResult unitySettings = null;
        for (int i = 0; i < _results.Count; i++)
        {
            if ((workload != "pso" && workload != "random-search") || _results[i].algorithm == algorithmName)
            {
                unitySettings = _results[i];
                break;
            }
        }
        if (unitySettings == null && IsPythonMethodSelected)
        {
            // Python-only reruns still use the exact workload settings selected in the dashboard.
            unitySettings = new BenchmarkResult
            {
                algorithm = algorithmName,
                balls = PlayerPrefs.GetInt("SimulationLab.Balls", 128),
                workItems = PlayerPrefs.GetInt("SimulationLab.Items", 100000),
                iterations = PlayerPrefs.GetInt("SimulationLab.Iterations", 100),
                batchSize = PlayerPrefs.GetInt("SimulationLab.BatchSize", 1000),
                particleCount = PlayerPrefs.GetInt("SimulationLab.PsoParticles", 64),
                sampleCount = _batchRunsPerStrategy * PlayerPrefs.GetInt(
                    workload == "pso" ? "SimulationLab.PsoTrials" : "SimulationLab.RandomSearchTrials", 5),
                evaluationCount = PlayerPrefs.GetInt("SimulationLab.RandomSearchEvaluationsPerTrial", 6400) *
                    PlayerPrefs.GetInt("SimulationLab.RandomSearchTrials", 5)
            };
        }
        if (unitySettings == null)
            yield break;

        string projectRoot = FindProjectRoot();
        if (string.IsNullOrEmpty(projectRoot))
        {
            _state.pythonMessage = "Python methods skipped: benchmark files were not found.";
            Debug.LogWarning(_state.pythonMessage);
            yield break;
        }

        string pythonRunner = Path.Combine(projectRoot, "Tools", "PythonBatchBenchmark", "setup_and_run.py");
        string outputPath = Path.Combine(projectRoot, "Library", "PythonBatchBenchmark", "results.json");
        string arguments = "\"" + pythonRunner + "\" --workload " + workload +
            " --runs " + _batchRunsPerStrategy;
        if (IsPythonMethodSelected)
            arguments += " --method " + GetPythonMethodArgument(_requestedMethod);
        if (workload == "gravity")
        {
            arguments += " --dimension " + (_is2D ? "2D" : "3D") + " --balls " + unitySettings.balls;
        }
        else if (workload == "simple-iteration")
        {
            arguments += " --items " + unitySettings.workItems + " --iterations " + unitySettings.iterations +
                " --batch-size " + unitySettings.batchSize;
        }
        else if (workload == "pso")
        {
            int trialCount = Mathf.Max(1, unitySettings.sampleCount / _batchRunsPerStrategy);
            arguments += " --algorithm-name \"" + algorithmName + "\" --particles " + unitySettings.particleCount +
                " --iterations " + unitySettings.iterations + " --trials " + trialCount;
        }
        else
        {
            int trialCount = Mathf.Max(1, unitySettings.sampleCount / _batchRunsPerStrategy);
            int evaluationsPerTrial = Mathf.Max(1, unitySettings.evaluationCount / trialCount);
            arguments += " --algorithm-name \"" + algorithmName + "\" --evaluations " + evaluationsPerTrial +
                " --trials " + trialCount;
        }

        ConcurrentQueue<string> outputLines = new ConcurrentQueue<string>();
        StringBuilder errorOutput = new StringBuilder();
        object errorLock = new object();
        System.Diagnostics.Process pythonProcess = null;
        string startError = "";
        _state.phase = "Starting Python " + (workload == "gravity" ? (_is2D ? "2D gravity" : "3D gravity") : workload) + " methods";
        _state.completed = _results.Count;
        int pythonMethodCount = GetPythonMethodCount();
        _state.total = _results.Count + pythonMethodCount;
        _state.progress = (float)_state.completed / _state.total;
        PublishState();

        string[] pythonLaunchers = Environment.OSVersion.Platform == PlatformID.Win32NT
            ? new[] { "py", "python" }
            : new[] { "python3", "python" };
        for (int launcherIndex = 0; launcherIndex < pythonLaunchers.Length; launcherIndex++)
        {
            string launcher = pythonLaunchers[launcherIndex];
            System.Diagnostics.Process candidate = new System.Diagnostics.Process();
            candidate.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = launcher,
                Arguments = (launcher == "py" ? "-3 " : "") + arguments,
                WorkingDirectory = projectRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            candidate.OutputDataReceived += (sender, eventArgs) =>
            {
                if (eventArgs.Data != null) outputLines.Enqueue(eventArgs.Data);
            };
            candidate.ErrorDataReceived += (sender, eventArgs) =>
            {
                if (eventArgs.Data != null)
                {
                    lock (errorLock)
                    {
                        errorOutput.AppendLine(eventArgs.Data);
                        if (errorOutput.Length > 2500) errorOutput.Remove(0, errorOutput.Length - 2500);
                    }
                }
            };
            try
            {
                if (candidate.Start())
                {
                    pythonProcess = candidate;
                    pythonProcess.BeginOutputReadLine();
                    pythonProcess.BeginErrorReadLine();
                    break;
                }
                startError = "The '" + launcher + "' launcher did not start.";
                candidate.Dispose();
            }
            catch (Exception exception)
            {
                startError = exception.Message;
                candidate.Dispose();
            }
        }

        if (pythonProcess == null)
        {
            _state.pythonMessage = "Python methods could not start. Install Python 3 and make 'py' or 'python' available on PATH. " + startError;
            Debug.LogWarning(_state.pythonMessage);
            yield break;
        }

        int completedPythonMethods = 0;
        float timeoutAt = Time.realtimeSinceStartup + 600f;
        while (!pythonProcess.HasExited && Time.realtimeSinceStartup < timeoutAt)
        {
            string outputLine;
            while (outputLines.TryDequeue(out outputLine))
            {
                if (outputLine.StartsWith("STATUS|", StringComparison.Ordinal))
                {
                    _state.phase = outputLine.Substring("STATUS|".Length);
                    PublishState();
                }
                else if (outputLine.StartsWith("METHOD_DONE|", StringComparison.Ordinal))
                {
                    completedPythonMethods++;
                    _state.completed = _results.Count + completedPythonMethods;
                    _state.progress = (float)_state.completed / _state.total;
                    _state.phase = "Finished " + outputLine.Substring("METHOD_DONE|".Length);
                    PublishState();
                }
            }
            yield return null;
        }

        bool timedOut = !pythonProcess.HasExited;
        if (timedOut) pythonProcess.Kill();
        pythonProcess.WaitForExit();
        int exitCode = pythonProcess.ExitCode;
        pythonProcess.Dispose();
        if (timedOut || exitCode != 0)
        {
            string details;
            lock (errorLock) details = errorOutput.ToString().Trim();
            _state.pythonMessage = timedOut ? "Python comparison timed out after 10 minutes." : "Python comparison failed. Check the Unity log.";
            Debug.LogWarning(_state.pythonMessage + (string.IsNullOrEmpty(details) ? "" : "\n" + details));
            yield break;
        }

        PythonBenchmarkReport report = null;
        try { report = JsonUtility.FromJson<PythonBenchmarkReport>(File.ReadAllText(outputPath)); }
        catch (Exception exception) { _state.pythonMessage = "Python results could not be read: " + exception.Message; }
        bool settingsMatch = false;
        if (report != null && report.settings != null)
        {
            if (workload == "simple-iteration")
                settingsMatch = report.settings.workItems == unitySettings.workItems &&
                    report.settings.iterations == unitySettings.iterations && report.settings.batchSize == unitySettings.batchSize;
            else if (workload == "gravity")
                settingsMatch = report.settings.balls == unitySettings.balls;
            else if (workload == "pso")
                settingsMatch = report.settings.particles == unitySettings.particleCount &&
                    report.settings.iterations == unitySettings.iterations &&
                    report.settings.trials == Mathf.Max(1, unitySettings.sampleCount / _batchRunsPerStrategy);
            else
                settingsMatch = report.settings.evaluations == unitySettings.evaluationCount /
                    Mathf.Max(1, unitySettings.sampleCount / _batchRunsPerStrategy) &&
                    report.settings.trials == Mathf.Max(1, unitySettings.sampleCount / _batchRunsPerStrategy);
        }
        int expectedPythonResults = IsPythonMethodSelected ? 1 : 4;
        if (report == null || report.settings == null || report.results == null || report.results.Length != expectedPythonResults ||
            report.schemaVersion != 2 || report.workload != workload ||
            report.dimension != (_state.workload == "gravity" ? _state.dimension : "") ||
            report.algorithm != algorithmName || !settingsMatch)
        {
            if (string.IsNullOrEmpty(_state.pythonMessage)) _state.pythonMessage = "Python results did not match this example's settings.";
            Debug.LogWarning(_state.pythonMessage);
            yield break;
        }

        for (int i = 0; i < report.results.Length; i++)
        {
            PythonBenchmarkResult pythonResult = report.results[i];
            if (pythonResult == null || string.IsNullOrEmpty(pythonResult.strategy) || pythonResult.algorithm != algorithmName || pythonResult.sampleCount < 1 ||
                (IsPythonMethodSelected && pythonResult.strategy != GetPythonMethodDisplayName(_requestedMethod)) ||
                float.IsNaN(pythonResult.averageProfileMs) || float.IsInfinity(pythonResult.averageProfileMs) ||
                float.IsNaN(pythonResult.p95ProfileMs) || float.IsInfinity(pythonResult.p95ProfileMs))
            {
                _state.pythonMessage = "Python results contained an invalid method timing.";
                Debug.LogWarning(_state.pythonMessage);
                yield break;
            }
        }
        for (int i = 0; i < report.results.Length; i++)
        {
            PythonBenchmarkResult value = report.results[i];
            _results.Add(new BenchmarkResult
            {
                algorithm = value.algorithm,
                strategy = value.strategy,
                markerName = "Python",
                balls = value.balls,
                workItems = value.workItems,
                iterations = value.iterations,
                batchSize = value.batchSize,
                evaluationCount = value.evaluationCount,
                particleCount = value.particleCount,
                sampleCount = value.sampleCount,
                averageProfileMs = value.averageProfileMs,
                medianProfileMs = value.medianProfileMs,
                p95ProfileMs = value.p95ProfileMs,
                averageFrameMs = value.averageFrameMs,
                p95FrameMs = value.p95FrameMs
            });
        }
        _state.total = _results.Count;
        _state.completed = _results.Count;
        _state.progress = 1f;
        _state.results = _results.ToArray();
        PublishState();
        Debug.Log("Added Python methods for " + workload + (string.IsNullOrEmpty(algorithmName) ? "" : " · " + algorithmName) + ". Report: " + outputPath);
    }

    private static string GetPythonMethodDisplayName(string selectedMethod)
    {
        switch (selectedMethod)
        {
            case "python-single": return "Python single threaded";
            case "python-threads": return "Python threads";
            case "python-processes": return "Python processes";
            case "python-numpy": return "Python NumPy vectorized";
            default: return "";
        }
    }

    private static string FindProjectRoot()
    {
        DirectoryInfo folder = new DirectoryInfo(Path.GetDirectoryName(Application.dataPath));
        while (folder != null)
        {
            string runner = Path.Combine(folder.FullName, "Tools", "PythonBatchBenchmark", "setup_and_run.py");
            if (File.Exists(runner))
                return folder.FullName;
            folder = folder.Parent;
        }
        return "";
    }
    #endregion

    private bool TryReadBatchValues(MonoBehaviour example, out float computeMs, out float wallMs,
        out int itemCount, out int iterations, out int batchSize, out string checksum)
    {
        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        FieldInfo computeField = example.GetType().GetField("_computeMilliseconds", flags);
        FieldInfo wallField = example.GetType().GetField("_wallMilliseconds", flags);
        FieldInfo countField = example.GetType().GetField("_itemCount", flags);
        FieldInfo iterationField = example.GetType().GetField("_iterations", flags);
        FieldInfo batchSizeField = example.GetType().GetField("_batchSize", flags);
        FieldInfo checksumField = example.GetType().GetField("_checksum", flags);
        computeMs = computeField == null ? 0f : (float)computeField.GetValue(example);
        wallMs = wallField == null ? 0f : (float)wallField.GetValue(example);
        itemCount = countField == null ? 0 : (int)countField.GetValue(example);
        iterations = iterationField == null ? 0 : (int)iterationField.GetValue(example);
        batchSize = batchSizeField == null ? 0 : (int)batchSizeField.GetValue(example);
        checksum = checksumField == null ? "—" : (string)checksumField.GetValue(example);
        return computeField != null && wallField != null && batchSizeField != null && checksumField != null && checksum != "—";
    }

    private bool TryReadOptimizationValues(MonoBehaviour example, out float computeMs, out float wallMs,
        out int evaluationCount, out int trialCount, out int particleCount, out int iterations,
        out float bestScore, out float averageBestScore,
        out float bestX, out float bestY, out string checksum)
    {
        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Type exampleType = example.GetType();
        FieldInfo computeField = exampleType.GetField("_computeMilliseconds", flags);
        FieldInfo wallField = exampleType.GetField("_wallMilliseconds", flags);
        FieldInfo countField = exampleType.GetField("_evaluationCount", flags);
        FieldInfo trialField = exampleType.GetField("_trialCount", flags);
        FieldInfo particleField = exampleType.GetField("_particleCount", flags);
        FieldInfo iterationField = exampleType.GetField("_iterations", flags);
        FieldInfo scoreField = exampleType.GetField("_bestScore", flags);
        FieldInfo averageField = exampleType.GetField("_averageBestScore", flags);
        FieldInfo xField = exampleType.GetField("_bestX", flags);
        FieldInfo yField = exampleType.GetField("_bestY", flags);
        FieldInfo checksumField = exampleType.GetField("_checksum", flags);
        computeMs = computeField == null ? 0f : (float)computeField.GetValue(example);
        wallMs = wallField == null ? 0f : (float)wallField.GetValue(example);
        evaluationCount = countField == null ? 0 : (int)countField.GetValue(example);
        trialCount = trialField == null ? 0 : (int)trialField.GetValue(example);
        particleCount = particleField == null ? 0 : (int)particleField.GetValue(example);
        iterations = iterationField == null ? 0 : (int)iterationField.GetValue(example);
        bestScore = scoreField == null ? 0f : (float)scoreField.GetValue(example);
        averageBestScore = averageField == null ? 0f : (float)averageField.GetValue(example);
        bestX = xField == null ? 0f : (float)xField.GetValue(example);
        bestY = yField == null ? 0f : (float)yField.GetValue(example);
        checksum = checksumField == null ? "—" : (string)checksumField.GetValue(example);
        return computeField != null && wallField != null && countField != null && trialField != null &&
            particleField != null && iterationField != null && scoreField != null &&
            averageField != null && xField != null && yField != null && checksumField != null && checksum != "—";
    }

    private void SetBatchError(string message, string scenePath, int exitCode)
    {
        _state.status = "error";
        _state.phase = message;
        _state.dashboardMessage = scenePath;
        PublishState();
        FinishHeadlessRun(exitCode);
        _acceptDashboardCommands = !_headlessMode;
    }

    private void LateUpdate()
    {
        if (_state.status == "running" && (_isBatchComparison || _isOptimizationComparison))
        {
            // Batch examples finish once per scene, so they publish measured runs directly.
            // Only refresh the browser while an async or coroutine example is still working.
            _sampleTimer += Time.unscaledDeltaTime;
            if (!_headlessMode && _sampleTimer >= DashboardUpdateInterval)
            {
                _sampleTimer = 0f;
                PublishState();
            }
            return;
        }

        if (_state.status == "running")
        {
            float frameMs = Time.unscaledDeltaTime * 1000f;
            _frameSamplesMs.Add(frameMs);
            _state.frameMs = frameMs;
            _state.mainThreadMs = _mainThreadRecorder.Valid
                ? _mainThreadRecorder.CurrentValue / 1000000f
                : 0f;

            if (_profileRecorder.Valid)
            {
                // LateUpdate reads the profiler marker after this frame's physics steps have run.
                float profileMs = 0f;
                long markerCallsThisFrame = 0;
                int collectedFrameCount = _profileRecorder.Count;
                if (collectedFrameCount > 0)
                {
                    int latestSampleIndex = collectedFrameCount - 1;
                    ProfilerRecorderSample latestSample = _profileRecorder.GetSample(latestSampleIndex);
                    markerCallsThisFrame = latestSample.Count;
                    if (markerCallsThisFrame > 0)
                    {
                        // FixedUpdate can run several times in one rendered frame. The recorder sums
                        // those calls, so divide by their count for a fair per-step comparison.
                        profileMs = (float)(latestSample.Value / (double)markerCallsThisFrame / 1000000d);
                    }
                }

                _state.profileMs = profileMs;
                // Repeat the frame average so catch-up physics frames keep proper weight.
                if (profileMs > 0f)
                {
                    for (int sampleIndex = 0; sampleIndex < markerCallsThisFrame; sampleIndex++)
                        _profileSamplesMs.Add(profileMs);
                }
            }

            _sampleTimer += Time.unscaledDeltaTime;
            if (!_headlessMode && _sampleTimer >= DashboardUpdateInterval)
            {
                _sampleTimer = 0f;
                AddChartPoint(_state.profileMs, _state.mainThreadMs, _state.frameMs);
                UpdateRollingStatistics();
                PublishState();
            }
        }
    }

    private IEnumerator RunComparison()
    {
        ComparisonMode[] modes = SelectUnityModes(Modes);
        _state.status = "running";
        _state.total = modes.Length + GetPythonMethodCount();
        PublishState();

        for (int modeIndex = 0; modeIndex < modes.Length; modeIndex++)
        {
            ComparisonMode mode = modes[modeIndex];
            _state.completed = modeIndex;
            _state.activeExample = mode.displayName;
            _state.markerName = mode.markerName;
            _state.phase = "Loading example scene";
            _state.progress = modes.Length == 0 ? 0f : (float)modeIndex / modes.Length;
            _state.profileMs = 0f;
            _state.mainThreadMs = 0f;
            _state.frameMs = 0f;
            _profileSamplesMs.Clear();
            _frameSamplesMs.Clear();
            _state.chartProfileMs = new float[0];
            _state.chartMainThreadMs = new float[0];
            _state.chartFrameMs = new float[0];
            PublishState();

            string sceneFolder = _is2D ? TwoDBenchmarkSceneFolder : ThreeDBenchmarkSceneFolder;
            string scenePath = sceneFolder + "Gravity_" + mode.sceneName + ".unity";
            int buildIndex = SceneUtility.GetBuildIndexByScenePath(scenePath);
            if (buildIndex < 0)
            {
                _state.status = "error";
                _state.phase = "A comparison scene is missing from Build Settings";
                _state.dashboardMessage = scenePath;
                PublishState();
                FinishHeadlessRun(2);
                _acceptDashboardCommands = !_headlessMode;
                yield break;
            }

            AsyncOperation load = SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Single);
            if (load == null)
            {
                _state.status = "error";
                _state.phase = "Unity could not load an example scene";
                _state.dashboardMessage = scenePath;
                PublishState();
                FinishHeadlessRun(3);
                _acceptDashboardCommands = !_headlessMode;
                yield break;
            }
            yield return load;

            // Give Awake and the physics system time to create and settle the balls.
            yield return new WaitForSecondsRealtime(0.5f);
            _state.ballCount = ReadBallCountFromActiveExample();
            _profileRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Scripts, mode.markerName, RecorderCapacity, FrameRecorderOptions);
            _state.phase = "Warm-up";
            _state.progress = modes.Length == 0 ? 0f : modeIndex / (float)modes.Length;
            PublishState();
            float warmupEnd = Time.realtimeSinceStartup + _warmupSeconds;
            while (Time.realtimeSinceStartup < warmupEnd)
                yield return null;

            _profileSamplesMs.Clear();
            _frameSamplesMs.Clear();
            float measurementStart = Time.realtimeSinceStartup;
            float measurementEnd = measurementStart + _measurementSeconds;
            _state.phase = "Measuring";
            _state.progress = modes.Length == 0 ? 0f : (modeIndex + 0.05f) / modes.Length;
            PublishState();
            while (Time.realtimeSinceStartup < measurementEnd)
            {
                float elapsed = Time.realtimeSinceStartup - measurementStart;
                _state.progress = modes.Length == 0 ? 0f : (modeIndex + 0.05f + 0.9f * elapsed / _measurementSeconds) / modes.Length;
                yield return null;
            }

            BenchmarkResult result = CreateResult(mode);
            _results.Add(result);
            _state.results = _results.ToArray();
            _profileRecorder.Dispose();
            _state.completed = modeIndex + 1;
            PublishState();
            if (!_headlessMode)
                yield return new WaitForSecondsRealtime(0.4f);
        }

        // Python calculates one independent gravity step using the same ball count and formula.
        if (string.IsNullOrEmpty(_requestedMethod) || IsPythonMethodSelected)
            yield return RunPythonComparison("gravity", "");
        _state.status = "complete";
        _state.phase = string.IsNullOrEmpty(_state.pythonMessage)
            ? (string.IsNullOrEmpty(_requestedMethod) ? "Unity and Python gravity methods measured" :
                (IsPythonMethodSelected ? "Python gravity method measured" : "Unity gravity method measured"))
            : (IsPythonMethodSelected ? "Python comparison needs attention" : "Unity gravity methods measured · Python comparison needs attention");
        _state.progress = 1f;
        _state.activeExample = "Complete";
        _state.results = _results.ToArray();
        PublishState();
        _requestedMethod = "";
        FinishHeadlessRun(0);
        _acceptDashboardCommands = !_headlessMode && !_isAllExamplesComparison;
    }

    // A standalone player in batch mode writes a report instead of publishing dashboard updates.
    private void FinishHeadlessRun(int exitCode)
    {
        if (!_headlessMode)
            return;

        try
        {
            string reportPath = _headlessOutputPath;
            if (_isAllExamplesComparison)
            {
                string dimension = _state.dimension.ToLowerInvariant().Replace(' ', '-');
                string reportName = Path.GetFileNameWithoutExtension(_headlessOutputPath) +
                    "-" + _state.workload + "-" + dimension + Path.GetExtension(_headlessOutputPath);
                reportPath = Path.Combine(Path.GetDirectoryName(_headlessOutputPath), reportName);
            }
            string directory = Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(reportPath, JsonUtility.ToJson(_state, true));
            File.WriteAllText(Path.ChangeExtension(reportPath, ".csv"), BuildCsvReport());
            Debug.Log("Benchmark report written to " + reportPath);
        }
        catch (Exception exception)
        {
            Debug.LogError("Could not write benchmark report: " + exception);
            exitCode = 4;
        }

        // Application.Quit ends a standalone player and leaves the Unity Editor alone.
        if (!Application.isEditor && (!_isAllExamplesComparison || _allExamplesFinished || exitCode != 0))
            Application.Quit(exitCode);
    }

    private string BuildCsvReport()
    {
        StringBuilder csv = new StringBuilder();
        csv.AppendLine("workload,dimension,algorithm,strategy,marker,balls,work_items,iterations,evaluation_count,best_score,mean_best_score,best_x,best_y,samples,warmup_seconds,measurement_seconds,average_profile_ms,median_profile_ms,p95_profile_ms,average_frame_ms,p95_frame_ms");
        for (int i = 0; i < _results.Count; i++)
        {
            BenchmarkResult result = _results[i];
            csv.Append(_state.workload).Append(',')
                .Append(_state.dimension).Append(',')
                .Append(CsvCell(result.algorithm)).Append(',')
                .Append(CsvCell(result.strategy)).Append(',')
                .Append(CsvCell(result.markerName)).Append(',')
                .Append(result.balls).Append(',').Append(result.workItems).Append(',').Append(result.iterations).Append(',')
                .Append(result.evaluationCount).Append(',')
                .Append(result.bestScore.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                .Append(result.averageBestScore.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                .Append(result.bestX.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                .Append(result.bestY.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                .Append(result.sampleCount).Append(',')
                .Append(_warmupSeconds).Append(',').Append(_measurementSeconds).Append(',')
                .Append(result.averageProfileMs.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                .Append(result.medianProfileMs.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                .Append(result.p95ProfileMs.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                .Append(result.averageFrameMs.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                .AppendLine(result.p95FrameMs.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }
        return csv.ToString();
    }

    private static string CsvCell(string value)
    {
        return "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
    }

    private static bool HasCommandLineArgument(string name)
    {
        string[] arguments = Environment.GetCommandLineArgs();
        for (int i = 0; i < arguments.Length; i++)
            if (string.Equals(arguments[i], name, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static string GetCommandLineValue(string name)
    {
        string[] arguments = Environment.GetCommandLineArgs();
        string prefix = name + "=";
        for (int i = 0; i < arguments.Length; i++)
        {
            if (arguments[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return arguments[i].Substring(prefix.Length).Trim('"');
            if (string.Equals(arguments[i], name, StringComparison.OrdinalIgnoreCase) && i + 1 < arguments.Length)
                return arguments[i + 1].Trim('"');
        }
        return string.Empty;
    }

    private BenchmarkResult CreateResult(ComparisonMode mode)
    {
        return new BenchmarkResult
        {
            strategy = mode.displayName,
            markerName = mode.markerName,
            balls = _state.ballCount,
            sampleCount = _profileSamplesMs.Count,
            averageProfileMs = Average(_profileSamplesMs),
            medianProfileMs = Percentile(_profileSamplesMs, 0.50f),
            p95ProfileMs = Percentile(_profileSamplesMs, 0.95f),
            averageFrameMs = Average(_frameSamplesMs),
            p95FrameMs = Percentile(_frameSamplesMs, 0.95f)
        };
    }

    private void UpdateRollingStatistics()
    {
        int start = Mathf.Max(0, _profileSamplesMs.Count - 100);
        int count = _profileSamplesMs.Count - start;
        double total = 0d;
        for (int i = start; i < _profileSamplesMs.Count; i++)
            total += _profileSamplesMs[i];
        _state.averageProfileMs = count == 0 ? 0f : (float)(total / count);
        // The dashboard's rolling percentile is intentionally omitted in headless measurements.
        if (!_headlessMode)
        {
            List<float> recent = _profileSamplesMs.GetRange(start, count);
            _state.p95ProfileMs = Percentile(recent, 0.95f);
        }
    }

    private void AddChartPoint(float profileMs, float mainThreadMs, float frameMs)
    {
        _state.chartProfileMs = AddLast(_state.chartProfileMs, profileMs, 120);
        _state.chartMainThreadMs = AddLast(_state.chartMainThreadMs, mainThreadMs, 120);
        _state.chartFrameMs = AddLast(_state.chartFrameMs, frameMs, 120);
    }

    private static float[] AddLast(float[] current, float value, int maximumCount)
    {
        int oldCount = current == null ? 0 : current.Length;
        int newCount = Mathf.Min(maximumCount, oldCount + 1);
        float[] next = new float[newCount];
        int copyCount = newCount - 1;
        int sourceStart = Mathf.Max(0, oldCount - copyCount);
        if (copyCount > 0)
            Array.Copy(current, sourceStart, next, 0, copyCount);
        next[newCount - 1] = value;
        return next;
    }

    private int ReadBallCountFromActiveExample()
    {
        // The example classes intentionally keep their beginner settings private.
        // Reflection reads that Inspector value without changing their implementation.
        GameObject controller = GameObject.Find("Gravity Simulation");
        if (controller == null)
            return 0;

        MonoBehaviour[] components = controller.GetComponents<MonoBehaviour>();
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] == null)
                continue;
            FieldInfo countField = components[i].GetType().GetField(
                "_ballCount", BindingFlags.Instance | BindingFlags.NonPublic);
            if (countField != null)
                return (int)countField.GetValue(components[i]);
        }
        return 128;
    }

    private static float Average(List<float> values)
    {
        if (values == null || values.Count == 0)
            return 0f;
        double total = 0d;
        for (int i = 0; i < values.Count; i++)
            total += values[i];
        return (float)(total / values.Count);
    }

    private static float Percentile(List<float> values, float percentile)
    {
        if (values == null || values.Count == 0)
            return 0f;
        float[] sorted = values.ToArray();
        Array.Sort(sorted);
        int index = Mathf.Clamp(Mathf.CeilToInt(percentile * sorted.Length) - 1, 0, sorted.Length - 1);
        return sorted[index];
    }

    private void PublishState()
    {
        _state.updatedUtc = DateTime.UtcNow.ToString("o");
        if (_dashboardServer != null)
            _dashboardServer.SetJson(JsonUtility.ToJson(_state));
    }

    private void OnApplicationQuit()
    {
        if (_profileRecorder.Valid)
            _profileRecorder.Dispose();
        if (_mainThreadRecorder.Valid)
            _mainThreadRecorder.Dispose();
        if (_dashboardServer != null)
            _dashboardServer.Stop();
    }

    private void OnDestroy()
    {
        // Play mode can stop without the standalone application's quit callback.
        if (_dashboardServer != null)
            _dashboardServer.Stop();
    }

    [Serializable]
    private sealed class BenchmarkState
    {
        public string status = "offline";
        public string workload = "gravity";
        public string dimension = "";
        public int batchRunsPerStrategy;
        public string activeExample = "Waiting for Unity";
        public string phase = "";
        public string markerName = "";
        public string dashboardMessage = "";
        public string pythonMessage = "";
        public string updatedUtc = "";
        public float progress;
        public float profileMs;
        public float averageProfileMs;
        public float p95ProfileMs;
        public float mainThreadMs;
        public float frameMs;
        public int ballCount;
        public int batchSize;
        public int evaluationCount;
        public int trialCount;
        public string selectedMethod = "";
        public float bestScore;
        public float averageBestScore;
        public float bestX;
        public float bestY;
        public int completed;
        public int total;
        public int warmupSeconds;
        public int measurementSeconds;
        public float[] chartProfileMs = new float[0];
        public float[] chartMainThreadMs = new float[0];
        public float[] chartFrameMs = new float[0];
        public BenchmarkResult[] results = new BenchmarkResult[0];
    }

    [Serializable]
    private sealed class BenchmarkResult
    {
        public string algorithm;
        public string strategy;
        public string markerName;
        public int balls;
        public int workItems;
        public int iterations;
        public int batchSize;
        public int evaluationCount;
        public int particleCount;
        public float bestScore;
        public float averageBestScore;
        public float bestX;
        public float bestY;
        public int sampleCount;
        public float averageProfileMs;
        public float medianProfileMs;
        public float p95ProfileMs;
        public float averageFrameMs;
        public float p95FrameMs;
    }

    [Serializable]
    private sealed class PythonBenchmarkReport
    {
        public int schemaVersion;
        public string workload;
        public string dimension;
        public string algorithm;
        public PythonBenchmarkSettings settings;
        public PythonBenchmarkResult[] results;
    }

    [Serializable]
    private sealed class PythonBenchmarkSettings
    {
        public int workItems;
        public int iterations;
        public int batchSize;
        public int balls;
        public int particles;
        public int evaluations;
        public int trials;
        public int measuredRuns;
    }

    [Serializable]
    private sealed class PythonBenchmarkResult
    {
        public string algorithm;
        public string strategy;
        public int balls;
        public int workItems;
        public int iterations;
        public int batchSize;
        public int evaluationCount;
        public int particleCount;
        public int sampleCount;
        public float averageProfileMs;
        public float medianProfileMs;
        public float p95ProfileMs;
        public float averageFrameMs;
        public float p95FrameMs;
    }

    private sealed class ComparisonMode
    {
        public readonly string displayName;
        public readonly string sceneName;
        public readonly string markerName;
        public readonly string componentName;
        public readonly string algorithm;

        public ComparisonMode(string name, string scene, string marker)
        {
            displayName = name;
            sceneName = scene;
            markerName = marker;
            componentName = "";
            algorithm = "";
        }

        public ComparisonMode(string name, string scene, string marker, string component, string workloadAlgorithm)
        {
            displayName = name;
            sceneName = scene;
            markerName = marker;
            componentName = component;
            algorithm = workloadAlgorithm;
        }
    }

    /// <summary>Serves the presentation page and live data over loopback only.</summary>
    private sealed class LocalDashboardServer
    {
        private readonly int _port;
        private string _dashboardHtmlPath;
        private readonly object _jsonLock = new object();
        private readonly ConcurrentQueue<string> _runRequests = new ConcurrentQueue<string>();
        private TcpListener _listener;
        private Thread _thread;
        private bool _isRunning;
        private string _json = "{}";

        public string Message { get; private set; }
        public bool IsRunning { get { return _isRunning; } }

        public LocalDashboardServer(int port)
        {
            _port = port;
        }

        public void Start(string dashboardHtmlPath)
        {
            try
            {
                // The local page is served by Unity so no second server or public host is needed.
                if (!File.Exists(dashboardHtmlPath))
                    throw new FileNotFoundException("Could not find the dashboard page.", dashboardHtmlPath);
                _dashboardHtmlPath = dashboardHtmlPath;
                _listener = new TcpListener(IPAddress.Loopback, _port);
                _listener.Start();
                _isRunning = true;
                _thread = new Thread(ServeRequests) { IsBackground = true, Name = "Gravity dashboard" };
                _thread.Start();
                Message = "Local dashboard: http://127.0.0.1:" + _port + "/";
            }
            catch (Exception exception)
            {
                Message = "Could not start local dashboard API on port " + _port + ": " + exception.Message;
            }
        }

        public void SetJson(string json)
        {
            lock (_jsonLock)
                _json = json;
        }

        public bool TryTakeRunRequest(out string requestJson)
        {
            return _runRequests.TryDequeue(out requestJson);
        }

        public void Stop()
        {
            _isRunning = false;
            if (_listener != null)
                _listener.Stop();
            if (_thread != null && _thread.IsAlive)
                _thread.Join(250);
        }

        private void ServeRequests()
        {
            while (_isRunning)
            {
                try
                {
                    TcpClient client = _listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(HandleRequest, client);
                }
                catch (SocketException)
                {
                    if (_isRunning)
                        Thread.Sleep(50);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }
        }

        private void HandleRequest(object state)
        {
            using (TcpClient client = (TcpClient)state)
            using (NetworkStream stream = client.GetStream())
            using (StreamReader reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true))
            {
                try
                {
                    string requestLine = reader.ReadLine();
                    string method = "";
                    string route = "";
                    if (requestLine != null)
                    {
                        string[] requestParts = requestLine.Split(' ');
                        if (requestParts.Length >= 2)
                        {
                            method = requestParts[0];
                            route = requestParts[1];
                        }
                    }

                    string header;
                    int contentLength = 0;
                    while (!string.IsNullOrEmpty(header = reader.ReadLine()))
                    {
                        if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            int.TryParse(header.Substring("Content-Length:".Length).Trim(), out contentLength);
                    }

                    bool isStateRequest = route == "/state";
                    bool isPageRequest = route == "/" || route == "/index.html";
                    bool isRunRequest = route == "/run" && method == "POST";
                    if (isRunRequest)
                    {
                        if (contentLength <= 0 || contentLength > 4096)
                        {
                            WriteResponse(stream, "400 Bad Request", "application/json; charset=utf-8", "{\"error\":\"Invalid request body\"}");
                            return;
                        }

                        char[] bodyCharacters = new char[contentLength];
                        int charactersRead = 0;
                        while (charactersRead < bodyCharacters.Length)
                        {
                            int count = reader.Read(bodyCharacters, charactersRead, bodyCharacters.Length - charactersRead);
                            if (count <= 0)
                                break;
                            charactersRead += count;
                        }
                        if (charactersRead != bodyCharacters.Length)
                        {
                            WriteResponse(stream, "400 Bad Request", "application/json; charset=utf-8", "{\"error\":\"Incomplete request body\"}");
                            return;
                        }

                        _runRequests.Enqueue(new string(bodyCharacters));
                        WriteResponse(stream, "202 Accepted", "application/json; charset=utf-8", "{\"queued\":true}");
                        return;
                    }
                    if (!isStateRequest && !isPageRequest)
                    {
                        WriteResponse(stream, "404 Not Found", "text/plain; charset=utf-8", "Not found");
                        return;
                    }

                    string body = isStateRequest ? GetJson() : File.ReadAllText(_dashboardHtmlPath);
                    string contentType = isStateRequest ? "application/json; charset=utf-8" : "text/html; charset=utf-8";
                    WriteResponse(stream, "200 OK", contentType, body);
                }
                catch (IOException)
                {
                    // The browser may close a poll while Unity is stopping Play mode.
                }
                catch (ObjectDisposedException)
                {
                    // The Editor may stop the server as the response is being sent.
                }
            }
        }

        private static void WriteResponse(NetworkStream stream, string status, string contentType, string body)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
            string responseHeaders =
                "HTTP/1.1 " + status + "\r\n" +
                "Content-Type: " + contentType + "\r\n" +
                "Access-Control-Allow-Origin: *\r\n" +
                "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
                "Access-Control-Allow-Headers: Content-Type\r\n" +
                "Cache-Control: no-store\r\n" +
                "Connection: close\r\n" +
                "Content-Length: " + bodyBytes.Length + "\r\n\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(responseHeaders);
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(bodyBytes, 0, bodyBytes.Length);
            stream.Flush();
        }

        private string GetJson()
        {
            lock (_jsonLock)
                return _json;
        }
    }
}
