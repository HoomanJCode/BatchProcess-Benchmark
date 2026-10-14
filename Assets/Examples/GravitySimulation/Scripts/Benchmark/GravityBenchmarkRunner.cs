using System;
using System.Collections;
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
/// Runs the existing gravity scenes one at a time and sends live profiler readings
/// to the local browser dashboard. The gravity calculation stays in each beginner
/// example; this script only controls the comparison and reads profiler data.
/// </summary>
public sealed class GravityBenchmarkRunner : MonoBehaviour
{
    [Header("Comparison settings")]
    [SerializeField, Min(0)] private int _warmupSeconds = 2;
    [SerializeField, Min(1)] private int _measurementSeconds = 8;
    [SerializeField, Min(1)] private int _dashboardPort = 8765;
    [SerializeField] private bool _is2D;
    [SerializeField] private bool _openDashboardInBrowser = true;

    private const int RecorderCapacity = 512;
    private const float DashboardUpdateInterval = 0.2f;
    // Collect one summed value for the current frame, even if a marker runs more than once.
    private const ProfilerRecorderOptions FrameRecorderOptions =
        ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.WrapAroundWhenCapacityReached;
    private const string TwoDBenchmarkSceneFolder = "Assets/Examples/GravitySimulation/Scenes/2D/Benchmark/";
    private const string ThreeDBenchmarkSceneFolder = "Assets/Examples/GravitySimulation/Scenes/3D/Benchmark/";

    // Keep these entries in the same order as the strategies listed in the README.
    // Async and coroutine work use their own markers because those calculations can
    // happen on a worker thread or in chunks rather than inside FixedUpdate.
    private static readonly ComparisonMode[] Modes =
    {
        new ComparisonMode("Single threaded", "SingleThreaded", "GravitySimulation.SingleThreaded"),
        new ComparisonMode("C# async", "CSharpAsync", "GravitySimulation.CSharpAsync.Worker"),
        new ComparisonMode("C# parallel", "CSharpParallel", "GravitySimulation.CSharpParallel"),
        new ComparisonMode("Unity Jobs", "UnityJobs", "GravitySimulation.UnityJobs"),
        new ComparisonMode("Unity coroutine", "UnityCoroutine", "GravitySimulation.UnityCoroutine.Chunk"),
        new ComparisonMode("Unity Jobs + Burst", "UnityJobsBurst", "GravitySimulation.UnityJobsBurst")
    };

    private readonly List<float> _profileSamplesMs = new List<float>();
    private readonly List<float> _frameSamplesMs = new List<float>();
    private readonly List<BenchmarkResult> _results = new List<BenchmarkResult>();
    private readonly BenchmarkState _state = new BenchmarkState();

    private ProfilerRecorder _profileRecorder;
    private ProfilerRecorder _mainThreadRecorder;
    private LocalDashboardServer _dashboardServer;
    private float _sampleTimer;
    private bool _hasStarted;

    private void Start()
    {
        // The runner must survive while each example scene is loaded in turn.
        DontDestroyOnLoad(gameObject);
        _state.dimension = _is2D ? "2D" : "3D";
        _state.status = "starting";
        _state.phase = "Starting local profiler stream";
        _state.measurementSeconds = _measurementSeconds;
        _state.warmupSeconds = _warmupSeconds;
        string dashboardHtmlPath = Path.GetFullPath(Path.Combine(
            Application.dataPath, "..", "Presentation", "index.html"));
        _dashboardServer = new LocalDashboardServer(_dashboardPort);
        _dashboardServer.Start(dashboardHtmlPath);
        _state.dashboardMessage = _dashboardServer.Message;
        _mainThreadRecorder = ProfilerRecorder.StartNew(
            ProfilerCategory.Internal, "Main Thread", RecorderCapacity, FrameRecorderOptions);
        PublishState();
        if (_openDashboardInBrowser && _dashboardServer.IsRunning)
            Application.OpenURL("http://127.0.0.1:" + _dashboardPort + "/");

        // Starting Play mode in a comparison scene immediately begins the run.
        if (!_hasStarted)
        {
            _hasStarted = true;
            StartCoroutine(RunComparison());
        }
    }

    private void LateUpdate()
    {
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
                // LateUpdate reads the marker after this frame's coroutine chunks have run.
                float profileMs = _profileRecorder.CurrentValue / 1000000f;
                _profileSamplesMs.Add(profileMs);
                _state.profileMs = profileMs;
            }

            _sampleTimer += Time.unscaledDeltaTime;
            if (_sampleTimer >= DashboardUpdateInterval)
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
        _state.status = "running";
        _state.total = Modes.Length;
        PublishState();

        for (int modeIndex = 0; modeIndex < Modes.Length; modeIndex++)
        {
            ComparisonMode mode = Modes[modeIndex];
            _state.completed = modeIndex;
            _state.activeExample = mode.displayName;
            _state.markerName = mode.markerName;
            _state.phase = "Loading example scene";
            _state.progress = (float)modeIndex / Modes.Length;
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
                yield break;
            }

            AsyncOperation load = SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Single);
            if (load == null)
            {
                _state.status = "error";
                _state.phase = "Unity could not load an example scene";
                _state.dashboardMessage = scenePath;
                PublishState();
                yield break;
            }
            yield return load;

            // Give Awake and the physics system time to create and settle the balls.
            yield return new WaitForSecondsRealtime(0.5f);
            _state.ballCount = ReadBallCountFromActiveExample();
            _profileRecorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Scripts, mode.markerName, RecorderCapacity, FrameRecorderOptions);

            _state.phase = "Warm-up";
            _state.progress = modeIndex / (float)Modes.Length;
            PublishState();
            float warmupEnd = Time.realtimeSinceStartup + _warmupSeconds;
            while (Time.realtimeSinceStartup < warmupEnd)
                yield return null;

            _profileSamplesMs.Clear();
            _frameSamplesMs.Clear();
            float measurementStart = Time.realtimeSinceStartup;
            float measurementEnd = measurementStart + _measurementSeconds;
            _state.phase = "Measuring";
            _state.progress = (modeIndex + 0.05f) / Modes.Length;
            PublishState();
            while (Time.realtimeSinceStartup < measurementEnd)
            {
                float elapsed = Time.realtimeSinceStartup - measurementStart;
                _state.progress = (modeIndex + 0.05f + 0.9f * elapsed / _measurementSeconds) / Modes.Length;
                yield return null;
            }

            BenchmarkResult result = CreateResult(mode);
            _results.Add(result);
            _state.results = _results.ToArray();
            _profileRecorder.Dispose();
            _state.completed = modeIndex + 1;
            PublishState();
            yield return new WaitForSecondsRealtime(0.4f);
        }

        _state.status = "complete";
        _state.phase = "All examples measured";
        _state.progress = 1f;
        _state.activeExample = "Complete";
        _state.results = _results.ToArray();
        PublishState();
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
        List<float> recent = _profileSamplesMs.GetRange(start, _profileSamplesMs.Count - start);
        _state.averageProfileMs = Average(recent);
        _state.p95ProfileMs = Percentile(recent, 0.95f);
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
        public string dimension = "";
        public string activeExample = "Waiting for Unity";
        public string phase = "";
        public string markerName = "";
        public string dashboardMessage = "";
        public string updatedUtc = "";
        public float progress;
        public float profileMs;
        public float averageProfileMs;
        public float p95ProfileMs;
        public float mainThreadMs;
        public float frameMs;
        public int ballCount;
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
        public string strategy;
        public string markerName;
        public int balls;
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

        public ComparisonMode(string name, string scene, string marker)
        {
            displayName = name;
            sceneName = scene;
            markerName = marker;
        }
    }

    /// <summary>Serves the presentation page and live data over loopback only.</summary>
    private sealed class LocalDashboardServer
    {
        private readonly int _port;
        private string _dashboardHtmlPath;
        private readonly object _jsonLock = new object();
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
                    string header;
                    while (!string.IsNullOrEmpty(header = reader.ReadLine())) { }

                    string route = requestLine == null ? "" : requestLine.Split(' ')[1];
                    bool isStateRequest = route == "/state";
                    bool isPageRequest = route == "/" || route == "/index.html";
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
                "Access-Control-Allow-Methods: GET, OPTIONS\r\n" +
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
