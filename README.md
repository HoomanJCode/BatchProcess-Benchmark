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

Python runs as a separate local process after the seven Unity Simple Iteration methods have finished. Press **Play** on `SimpleIteration_Benchmark` or `All_Examples_Benchmark`, or run the headless batch benchmark below; Python timings are added to the same dashboard and headless report automatically.

The first run checks for NumPy. If needed, it creates a project-local Python environment under ignored `Library/PythonBatchBenchmark/venv` and installs NumPy there. Python 3 must be installed and available as `py` or `python` on `PATH`. Later runs reuse that environment. The benchmark can also be run on its own from PowerShell:

```powershell
py -3 Tools/PythonBatchBenchmark/setup_and_run.py
```

The report is written to `Library/PythonBatchBenchmark/results.json`. It compares Python single-threaded loops, Python threads, Python processes, and NumPy vectorized operations with the Unity methods. Unity starts Python after its own timing passes and supplies the same item count, iterations, batch size, and number of measured runs.

The Python thread pool starts one task per batch; CPU-bound work in a standard CPython build is limited by the Global Interpreter Lock. The process pool uses multiple processes and includes data transfer costs. NumPy performs vector operations in native code. Python compute times cover the calculation, while wall times also include the checksum pass; Python has no Unity frame timings. Python's built-in float uses 64-bit precision, while the Unity example uses 32-bit values, so small checksum differences are expected.

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
