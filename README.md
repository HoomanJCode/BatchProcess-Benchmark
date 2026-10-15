# Unity Processing and Simulation Examples

This Unity project presents five workloads. Gravity, PSO, and Random Search use six Unity/C# processing methods. Simple Iteration adds a seventh batched C# Tasks method and can also compare standalone Python methods in the local dashboard.

## The five examples

1. **Gravity simulation (2D)** — circle sprites and Unity 2D physics. Read [`GravitySimulation/2D/README.md`](Assets/Examples/GravitySimulation/2D/README.md).
2. **Gravity simulation (3D)** — sphere meshes and Unity 3D physics. Read [`GravitySimulation/3D/README.md`](Assets/Examples/GravitySimulation/3D/README.md).
3. **Simple iteration** — the beginner-friendly batch calculation. Read [`SimpleIteration/README.md`](Assets/Examples/SimpleIteration/README.md).
4. **PSO simulation** — Particle Swarm Optimization. Read [`PSOSimulation/README.md`](Assets/Examples/PSOSimulation/README.md).
5. **Random Search optimization** — candidate sampling with a best-so-far result. Read [`RandomSearchOptimization/README.md`](Assets/Examples/RandomSearchOptimization/README.md).

## Local real-time dashboard

To fill every dashboard tab in one run, open `Assets/WebDashboard/Scenes/All_Examples_Benchmark.unity` and press **Play**. It runs Gravity 2D, Gravity 3D, Simple Iteration, PSO, and Random Search one after another. Unity serves the page locally at [http://127.0.0.1:8765/](http://127.0.0.1:8765/), and the browser saves each finished example on this PC. You can still run an individual `Gravity_Benchmark`, `SimpleIteration_Benchmark`, or `Optimization_Benchmark` scene when you only want that workload.

Dashboard assets and its README are under [`Assets/WebDashboard`](Assets/WebDashboard/README.md).

### Compare Python with Unity

Python runs as a separate local process. It does not require Unity to calculate its results. Install the optional NumPy dependency and run the benchmark from PowerShell:

```powershell
py -m pip install -r Tools/PythonBatchBenchmark/requirements.txt
py Tools/PythonBatchBenchmark/benchmark.py
```

The report is written to `Library/PythonBatchBenchmark/results.json`. Open the dashboard, select **Simple Iteration**, and click **Load Python timings** to add Python single-threaded, Python threads, Python processes, and NumPy vectorized results beside the Unity rows. Python's thread pool is CPU-bound and limited by Python's Global Interpreter Lock; the process pool uses multiple processes, while NumPy runs numeric operations in native code. Both Python and Unity reports must use the same item count, iterations, and batch size. The default settings match the Unity Simple Iteration scenes.

Python compute and wall values report the elapsed time for one complete calculation; Python does not have Unity frame measurements. Python's built-in float uses 64-bit precision, while the Unity example uses 32-bit values, so small checksum differences are expected.

## Headless benchmarks

Close the Unity Editor before using a headless helper. Use the Unity Editor executable installed on your computer:

```powershell
.\Tools\Run-HeadlessGravityBenchmark.ps1 `
  -UnityEditorPath "C:\Program Files\Unity\Hub\Editor\6000.5.1f1\Editor\Unity.exe" `
  -Dimension 2D

.\Tools\Run-HeadlessBatchBenchmark.ps1 `
  -UnityEditorPath "C:\Program Files\Unity\Hub\Editor\6000.5.1f1\Editor\Unity.exe"

.\Tools\Run-HeadlessOptimizationBenchmark.ps1 `
  -UnityEditorPath "C:\Program Files\Unity\Hub\Editor\6000.5.1f1\Editor\Unity.exe"
```

Reports are saved under `Build/Headless/`. The dashboard build can be used for live presentation; headless reports are for command-line profiling.

## Folder layout

```text
Assets/
├── Examples/
│   ├── GravitySimulation/
│   │   ├── 2D/                  Scenes, scripts, resources, editor tools, and docs
│   │   ├── 3D/                  Scenes, scripts, resources, editor tools, and docs
│   │   └── Editor/              Shared gravity comparison-scene tools
│   ├── SimpleIteration/         Scenes, scripts, resources, editor tools, and docs
│   ├── PSOSimulation/           Scenes, scripts, resources, editor tools, and docs
│   └── RandomSearchOptimization/ Scenes, scripts, resources, editor tools, and docs
└── WebDashboard/                Local dashboard page, runner, scenes, editor tools, docs
```

Unity version: **6000.5.1f1**. Packages are listed in `Packages/manifest.json`.
