# Unity Batch Processing and Gravity Demos

This Unity project contains parallel-processing examples. The batch scenes compare processing strategies over a list of numbers. The gravity scenes show bodies attracting each other in either a 2D plane or 3D space.

## Requirements

- Unity **6000.5.1f1** (see `ProjectSettings/ProjectVersion.txt`)
- Packages listed in `Packages/manifest.json`. Unity Package Manager installs them when the project opens.

## Run the batch examples

Each processing strategy has its own scene and its own self-contained script. Open one of these scenes and press **Play**:

| Strategy | Scene |
| --- | --- |
| Single threaded | [`Batch_SingleThreaded.unity`](Assets/Examples/BatchProcessing/Scenes/Batch_SingleThreaded.unity) |
| C# async | [`Batch_CSharpAsync.unity`](Assets/Examples/BatchProcessing/Scenes/Batch_CSharpAsync.unity) |
| C# parallel | [`Batch_CSharpParallel.unity`](Assets/Examples/BatchProcessing/Scenes/Batch_CSharpParallel.unity) |
| Unity Jobs | [`Batch_UnityJobs.unity`](Assets/Examples/BatchProcessing/Scenes/Batch_UnityJobs.unity) |
| Unity coroutine | [`Batch_UnityCoroutine.unity`](Assets/Examples/BatchProcessing/Scenes/Batch_UnityCoroutine.unity) |
| Unity Jobs with Burst | [`Batch_UnityJobsBurst.unity`](Assets/Examples/BatchProcessing/Scenes/Batch_UnityJobsBurst.unity) |

Each scene's Inspector has the item count and iteration count. After it runs, the same component shows the checksum and compute/wall timings; the Console prints them too. Compare the matching `BatchProcessing.*` marker in **Window → Analysis → Profiler**. Each mode starts from the same deterministic values. Burst can produce small floating-point checksum differences.

### Compare batch strategies in the local analysis page

Choose **Tools → Batch Processing → Create Example Scenes** to create or refresh the six teaching scenes and the `Batch_Benchmark` launcher. Open [`Batch_Benchmark.unity`](Assets/Examples/BatchProcessing/Scenes/Batch_Benchmark.unity) and press **Play**. It serves the same local analysis page as the gravity comparisons and runs each batch scene five times in sequence. The page compares average and P95 calculation time, average and P95 total wall time, item count, and iteration count.

The batch comparison measures one complete calculation per run. Coroutine calculation time adds up its chunk work, while wall time also includes the frames spent yielding. C# async wall time includes waiting for its worker task; Unity Jobs includes scheduling and completing the job. Use the same machine and project settings for all runs. The first Burst run in the Editor can include compilation overhead, so repeat the comparison before presenting results.

These scenes do not create spheres or simulate gravity. They isolate batch scheduling: every strategy performs the same made-up calculation over the same values. For each value, it multiplies by `1.00001`, adds `0.00002`, and subtracts `1` whenever the result reaches `1`. For example, `0.5` becomes `0.500025`, then `0.500050`. This calculation only creates computer work; it does not describe a real-world process.

| Mode | Simple explanation | Complexity |
| --- | --- | --- |
| Single threaded | One loop processes every number on Unity's main thread. | O(N × I) work |
| C# async | One `Task` processes the numbers on a worker thread; Unity reads the result after it finishes. | O(N × I) work |
| C# parallel | Persistent C# worker threads split the numbers into ranges. | O(N × I) total work |
| Unity Jobs | `IJobParallelFor` processes native data in parallel without Burst compilation. | O(N × I) total work |
| Unity coroutine | A loop processes a small chunk, yields, then continues on a later frame. | O(N × I) total work |
| Unity Jobs with Burst | The same job is marked with `[BurstCompile]` and compiled by Burst when available. | O(N × I) total work |

Here, `N` is the number of values and `I` is the number of iterations per value. Every strategy does O(N × I) work and stores O(N) values. Parallel modes can finish sooner when enough work is available, but scheduling has a cost.

Choose **Tools → Batch Processing → Create Example Scenes** to regenerate the six scenes and their Build Settings entries. Each runtime script contains its own copy of the short calculation so you can read one strategy at a time without following shared calculation helpers.

## Open the gravity example

The gravity project is in [`Assets/Examples/GravitySimulation`](Assets/Examples/GravitySimulation). Every processing strategy has a dedicated 2D scene and a dedicated 3D scene. The scenes are grouped under `Scenes/2D` and `Scenes/3D`, with one controller and no runtime UI. Use the controller Inspector to set ball count, gravity, softening, and gravity scale. Each scene already selects its own dimension.

| Processing strategy | 2D scene | 3D scene |
| --- | --- | --- |
| Single threaded | [`Gravity_SingleThreaded.unity`](Assets/Examples/GravitySimulation/Scenes/2D/Gravity_SingleThreaded.unity) | [`Gravity_SingleThreaded.unity`](Assets/Examples/GravitySimulation/Scenes/3D/Gravity_SingleThreaded.unity) |
| C# async | [`Gravity_CSharpAsync.unity`](Assets/Examples/GravitySimulation/Scenes/2D/Gravity_CSharpAsync.unity) | [`Gravity_CSharpAsync.unity`](Assets/Examples/GravitySimulation/Scenes/3D/Gravity_CSharpAsync.unity) |
| C# parallel | [`Gravity_CSharpParallel.unity`](Assets/Examples/GravitySimulation/Scenes/2D/Gravity_CSharpParallel.unity) | [`Gravity_CSharpParallel.unity`](Assets/Examples/GravitySimulation/Scenes/3D/Gravity_CSharpParallel.unity) |
| Unity Jobs | [`Gravity_UnityJobs.unity`](Assets/Examples/GravitySimulation/Scenes/2D/Gravity_UnityJobs.unity) | [`Gravity_UnityJobs.unity`](Assets/Examples/GravitySimulation/Scenes/3D/Gravity_UnityJobs.unity) |
| Unity coroutine | [`Gravity_UnityCoroutine.unity`](Assets/Examples/GravitySimulation/Scenes/2D/Gravity_UnityCoroutine.unity) | [`Gravity_UnityCoroutine.unity`](Assets/Examples/GravitySimulation/Scenes/3D/Gravity_UnityCoroutine.unity) |
| Unity Jobs with Burst | [`Gravity_UnityJobsBurst.unity`](Assets/Examples/GravitySimulation/Scenes/2D/Gravity_UnityJobsBurst.unity) | [`Gravity_UnityJobsBurst.unity`](Assets/Examples/GravitySimulation/Scenes/3D/Gravity_UnityJobsBurst.unity) |

The 12 individual gravity scenes, two gravity comparison launchers, their 12 equal-size generated benchmark copies, six batch example scenes, and the batch comparison launcher are in **Build Settings** after running both scene-generation menu items. Use **Tools → Gravity Simulation → Regenerate Example Scenes** and **Tools → Gravity Simulation → Create Comparison Scenes** for gravity, plus **Tools → Batch Processing → Create Example Scenes** for batch processing.

### Live 2D and 3D comparison dashboard

The comparison launcher scenes run each implemented strategy one at a time and stream its profiler readings to a browser page on the same PC. Unity still renders and simulates the balls; the browser is a live monitor for progress, charts, and the results table.

1. In Unity, open [`Scenes/2D/Gravity_Benchmark.unity`](Assets/Examples/GravitySimulation/Scenes/2D/Gravity_Benchmark.unity) or [`Scenes/3D/Gravity_Benchmark.unity`](Assets/Examples/GravitySimulation/Scenes/3D/Gravity_Benchmark.unity), then press **Play**.
2. The runner starts a loopback-only web server and opens the dashboard in your default browser. If the browser does not open automatically, visit [http://127.0.0.1:8765](http://127.0.0.1:8765).
3. The runner loads each example scene in order. The dashboard updates while each scene is warming up and being measured. Stop Play mode when the comparison finishes.

Unity serves the page and its live JSON endpoint from `127.0.0.1:8765`; the server binds only to the local PC. No result or simulation data is sent to a public service. The page source is under [`Presentation/index.html`](Presentation/index.html).

The comparison runner makes generated copies of the example scenes with **128 balls in every run**, so the editable teaching scenes are left unchanged. It uses a two-second warm-up and an eight-second measured pass per strategy by default. Change these fields on the `Gravity Benchmark Runner` object in a comparison scene to adjust the run. Use the same settings when repeating a comparison.

The gravity examples allocate their arrays and worker resources once during scene setup. The measured physics steps reuse those buffers: they do not clone arrays or create a new task, parallel loop delegate, or coroutine iterator every step. The async and parallel gravity examples keep workers alive for the scene, and the coroutine keeps one iterator alive. Batch scenes run once when opened, with input arrays created before the calculation starts; the coroutine yields between chunks. Scene setup, Unity physics, Editor code, and the live dashboard can still allocate; use a Development Build and Unity's GC Alloc profiler data when checking allocations from the target application.

The live chart shows the current strategy marker, main-thread CPU time, and frame time. The completed-run chart compares average marker time per measured frame or P95 frame time. The profiler recorder sums marker samples within each frame. C# async uses its worker-thread calculation marker, while Unity coroutine marks each yielded chunk, so coroutine work is spread across multiple frames and its marker value is not the full calculation time. Use marker timings to compare measured work per frame and frame timing for overall responsiveness. The Editor and physics engine add shared overhead. For a formal performance claim, use a Development Build on the same machine and compare repeated runs.

#### Run the comparison headless

For repeatable command-line runs without a graphics window or browser dashboard, close the Unity Editor and run the PowerShell helper from the project folder. It builds a Windows x64 Development player (so `ProfilerRecorder` can read strategy markers) and then runs the same six-scene comparison with Unity's `-batchmode -nographics` options:

```powershell
.\Tools\Run-HeadlessGravityBenchmark.ps1 `
  -UnityEditorPath "C:\Program Files\Unity\Hub\Editor\6000.5.1f1\Editor\Unity.exe" `
  -Dimension 2D `
  -WarmupSeconds 2 `
  -MeasurementSeconds 8
```

Use `-Dimension 3D` for the 3D scenes. The JSON and CSV reports are written under `Build/Headless/` by default; pass `-OutputPath "C:\path\report.json"` to choose another JSON location (the CSV uses the same name). `Build/Headless/` is ignored by Git. The player still runs Unity's physics, gravity scripts, and profiler recorders; it skips rendering, browser serving, and dashboard chart updates. Headless measurement samples use preallocated buffers so sample collection does not grow a list during the measured pass. This removes dashboard overhead, but Unity startup, scene loading, physics, and the OS can still allocate or affect timings. For a GUI-free command-line run use a built player, not Editor Play mode.

Choose **Tools → Gravity Simulation → Create Comparison Scenes** to regenerate both launchers and their equal-size benchmark copies after changing scene setup.

Each gravity strategy has one self-contained implementation script in its own assembly under `Assets/Examples/GravitySimulation/Scripts`. Setup, the gravity formula, physics-body creation, and force application are repeated in every script on purpose. This keeps the code for each strategy in one place, so beginners can read and present one script without navigating a shared base class or helper library. The repetition is intentional; the scheduling difference is the main thing to compare.

In the 2D scenes, each ball is a circle sprite with a dynamic `Rigidbody2D` and a non-trigger `CircleCollider2D` on the same GameObject. These scenes use a top-down orthographic camera and Unity 2D physics. All six strategies share the circle sprite asset at [`Resources/GravityBall.png`](Assets/Examples/GravitySimulation/Resources/GravityBall.png). In the 3D scenes, each ball is a sphere mesh with a dynamic `Rigidbody` and a non-trigger `SphereCollider`; those scenes use Unity 3D physics. Built-in world gravity is disabled because the selected strategy calculates custom softened inverse-square gravity and applies it through `AddForce`; 2D converts acceleration to a mass-scaled force because `ForceMode2D` has no acceleration option.

### Gravity formula

For target body *i*, every other body *j* adds this acceleration:

```text
r_ij = position_j - position_i
a_i += G * mass_j * r_ij / (|r_ij|² + softening²)^(3/2)
```

This is Newton's inverse-square gravity written as a vector acceleration. Dividing the force by the target mass cancels `mass_i`, leaving the source mass `mass_j`. The `_softening` setting keeps the calculation finite when two centers get very close. In the 2D scenes the z component of `r_ij` is set to zero; the 3D scenes use all three components. Each gravity script includes this formula in comments next to its calculation loop.

The initial positions and velocities are deterministic. This is a visual workload example, not a high-precision orbital solver. Async and coroutine scenes keep applying the last completed gravity calculation while the next calculation is in flight or split into chunks.

### Algorithm complexity

Let `N` be the number of spheres. For each target sphere, the gravity calculation checks every other sphere, for about `N × (N - 1)` pairs. That is **O(N^2) work per physics step** for every strategy. The arrays holding body state and calculated acceleration use **O(N) space**.

| Strategy | Total gravity work | How execution is scheduled |
| --- | --- | --- |
| Single threaded | O(N^2) | All pair checks run on the main thread. |
| C# async | O(N^2) | The same single-worker calculation runs on a .NET thread-pool thread. |
| C# parallel | O(N^2) | Target bodies are split across workers; ideal compute time can approach O(N^2 / P), where `P` is the number of workers, plus scheduling overhead. |
| Unity Jobs | O(N^2) | Target bodies are split into jobs; total work remains O(N^2), with job and data-copy overhead. |
| Unity coroutine | O(N^2) | The same pair checks are chunked across frames; this changes responsiveness, not total work. |
| Unity Jobs with Burst | O(N^2) | The same jobs are Burst-compiled; compilation can lower the constant cost, not the algorithmic complexity. |

## Profiling

1. Open **Window → Analysis → Profiler** before running the example.
2. Use the same workload settings for each mode. For gravity, reset the scene between captures. Each batch scene starts with the same values when you press Play.
3. Warm up each mode first. Discard the first Burst measurement in Editor Play mode because it may include JIT compilation.
4. Compare frame time and main-thread cost. The gravity strategies expose `GravitySimulation.*` profiler markers. The async worker calculation is marked `GravitySimulation.CSharpAsync.Worker`; coroutine chunks are marked `GravitySimulation.UnityCoroutine.Chunk`. Unity's Rigidbody integration and collision solving still run on the main thread and are shared by every mode, so use the markers to compare the processing strategies and total frame time to see the overall cost.
5. Small workloads may be slower with parallel strategies because scheduling has a cost. Results depend on hardware, Editor load, Unity version, and Burst configuration.

Each batch component shows **compute time** for the measured calculation and **wall time** from start to completion. Async wall time includes starting and waiting for its Task; parallel time includes waiting for all worker threads; Jobs time includes scheduling and completing the job. Coroutine compute time adds up chunk work, while wall time also includes the frames spent yielding.

## Project layout

```text
Assets/Examples/
├── BatchProcessing/
│   ├── Editor/       Scene creation command
│   ├── Scenes/       One scene per strategy and a comparison launcher
│   └── Scripts/      One self-contained script per strategy
└── GravitySimulation/
    ├── Editor/       Gravity mode scene generator
    ├── Scenes/
    │   ├── 2D/       Example scenes, equal-size benchmark copies, and a launcher
    │   └── 3D/       Example scenes, equal-size benchmark copies, and a launcher
    └── Scripts/      Standalone strategy scripts and the comparison runner
Presentation/         Local browser dashboard for live profiler readings
```

Unity requires each controller to be a `MonoBehaviour` with lifecycle callbacks such as `Awake` and `FixedUpdate`. The jobs examples also need a small job struct with an `Execute` method, and the coroutine example needs an iterator. These are Unity's entry points; the gravity calculation itself stays directly in the strategy script.

The batch examples have one script per strategy under [`Assets/Examples/BatchProcessing/Scripts`](Assets/Examples/BatchProcessing/Scripts). Each file contains its own calculation instead of calling a shared helper. Unity still requires lifecycle callbacks such as `Start`, and the Jobs API requires `Execute`; these are Unity entry points, not shared calculation modules.

Burst is a package dependency but is not enabled project-wide. The plain Jobs mode remains a baseline; the Burst job opts into compilation with `[BurstCompile]`.

## Optional Unity CLI integration

The project includes Unity's `com.unity.pipeline` package for the Unity CLI editor connection. To install Unity's official skills and register the local MCP server in Codex on another machine:

```powershell
codex plugin marketplace add Unity-Technologies/unity-agent-plugin
codex plugin add unity@unity-agent-plugin
unity skill install codex
unity pipeline install
unity mcp configure codex --project-path .
```

Restart Codex after installing the plugin. The MCP connection is available while the project is open in Unity Editor with the Pipeline package resolved.
