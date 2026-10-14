# Unity Batch Processing and Gravity Demos

This Unity project contains parallel-processing examples. The batch scene compares processing strategies over a list of numbers. The gravity scenes show bodies attracting each other in either a 2D plane or 3D space.

## Requirements

- Unity **6000.5.1f1** (see `ProjectSettings/ProjectVersion.txt`)
- Packages listed in `Packages/manifest.json`. Unity Package Manager installs them when the project opens.

## Run the batch example

1. Open the project in Unity Hub.
2. Open [`Assets/Examples/BatchProcessing/Scenes/BatchProcessing.unity`](Assets/Examples/BatchProcessing/Scenes/BatchProcessing.unity).
3. In the `Batch Processing App` Inspector, choose the processing mode and workload size. The scene runs the selected mode on Play when **Run On Start** is enabled. You can also run it from the component's **Run Batch** context menu.
4. Compare modes using the reported checksum and the matching `BatchProcessing.*` marker in **Window → Analysis → Profiler**. Each run starts with the same deterministic values. Burst can produce small floating-point checksum differences while running the same calculation.

The batch scene has no runtime UI. Its mode, item count, iteration count, and run-on-start option are Inspector fields; the last checksum and timings are shown on the component. Async work is marked `BatchProcessing.CSharpAsync.Worker`; coroutine chunks use the selected mode marker across frames.

The sample calculation repeats a small multiply/add operation for each number. It is intentionally simple so it is easy to explain: the calculation stays the same while the scheduling changes.

This scene does not create spheres or simulate gravity. It isolates batch scheduling: every mode receives the same deterministic `float` array and performs the same calculation. Use the checksum to compare results, allowing for small floating-point differences in Burst, then compare timing and responsiveness in the Profiler. The gravity scene is the visual physics demonstration.

| Mode | Simple explanation | Complexity |
| --- | --- | --- |
| Single threaded | One loop processes every number on Unity's main thread. | O(N × I) work |
| C# async | A `Task` processes the numbers on a worker thread; Unity reads the result after it finishes. | O(N × I) work |
| C# parallel | `Parallel.For` splits numbers across .NET thread-pool workers. | O(N × I) total work |
| Unity Jobs | `IJobParallelFor` processes native data in parallel without Burst compilation. | O(N × I) total work |
| Unity coroutine | A loop processes a small chunk, yields, then continues on a later frame. | O(N × I) total work |
| Unity Jobs with Burst | The same job is marked with `[BurstCompile]` and compiled by Burst when available. | O(N × I) total work |

Here, `N` is the number of values and `I` is the number of iterations per value. Every mode performs the same calculation, so its total work stays O(N × I). Parallel modes can reduce elapsed time when enough work is available, but scheduling costs matter. The input array uses O(N) space; Unity Jobs also creates a temporary native copy that uses O(N) extra space.

Choose **Tools → Batch Processing → Create Example Scene** to regenerate the batch scene and its Build Settings entry.

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

All 12 gravity scenes and the batch scene are in **Build Settings**. Regenerate them with **Tools → Gravity Simulation → Regenerate Example Scenes**.

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
2. Use the same workload settings for each mode. For gravity, reset the scene between captures; the batch scene recreates its same starting values for each run.
3. Warm up each mode first. Discard the first Burst measurement in Editor Play mode because it may include JIT compilation.
4. Compare frame time and main-thread cost. The gravity strategies expose `GravitySimulation.*` profiler markers. The async worker calculation is marked `GravitySimulation.CSharpAsync.Worker`; coroutine chunks are marked `GravitySimulation.UnityCoroutine.Chunk`. Unity's Rigidbody integration and collision solving still run on the main thread and are shared by every mode, so use the markers to compare the processing strategies and total frame time to see the overall cost.
5. Small workloads may be slower with parallel strategies because scheduling has a cost. Results depend on hardware, Editor load, Unity version, and Burst configuration.

The batch component reports **compute time** for work within that mode (including job setup and copying for Unity Jobs), and **wall time** from start to completion. For async, wall time includes the wait until the worker finishes. For coroutines, wall time includes the frames spent yielding while compute time sums only the chunk work.

## Project layout

```text
Assets/Examples/
├── BatchProcessing/
│   ├── Editor/       Scene creation command
│   ├── Scenes/       Standalone comparison scene
│   └── Scripts/      One script containing the calculation and processing modes
└── GravitySimulation/
    ├── Editor/       Gravity mode scene generator
    ├── Scenes/
    │   ├── 2D/       One scene per processing strategy
    │   └── 3D/       One scene per processing strategy
    └── Scripts/      One standalone, commented controller script per strategy
```

Unity requires each controller to be a `MonoBehaviour` with lifecycle callbacks such as `Awake` and `FixedUpdate`. The jobs examples also need a small job struct with an `Execute` method, and the coroutine example needs an iterator. These are Unity's entry points; the gravity calculation itself stays directly in the strategy script.

The batch example's settings, processing modes, jobs, and calculation are all in [`BatchProcessingApp.cs`](Assets/Examples/BatchProcessing/Scripts/BatchProcessingApp.cs).

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
