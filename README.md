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

| Mode | Simple explanation |
| --- | --- |
| Single threaded | One loop processes every number on Unity's main thread. |
| C# async | A `Task` processes the numbers on a worker thread; Unity reads the result after it finishes. |
| C# parallel | `Parallel.For` splits numbers across .NET thread-pool workers. |
| Unity Jobs | `IJobParallelFor` processes native data in parallel without Burst compilation. |
| Unity coroutine | A loop processes a small chunk, yields, then continues on a later frame. |
| Unity Jobs with Burst | The same job is marked with `[BurstCompile]` and compiled by Burst when available. |

Choose **Tools → Batch Processing → Create Example Scene** to regenerate the batch scene and its Build Settings entry.

## Open the gravity example

The gravity project is in [`Assets/Examples/GravitySimulation`](Assets/Examples/GravitySimulation). Each scene is a standalone example with one controller and no runtime UI. The starter scene opens in 2D with sprite bodies; each strategy scene opens in 3D with sphere bodies. Use the controller Inspector to set body count, gravity, softening, gravity scale, and dimension.

| Scene | Starts with |
| --- | --- |
| [`SampleScene.unity`](Assets/Examples/GravitySimulation/Scenes/SampleScene.unity) | Single threaded, 2D sprites |
| [`Gravity_SingleThreaded.unity`](Assets/Examples/GravitySimulation/Scenes/Gravity_SingleThreaded.unity) | Single threaded |
| [`Gravity_CSharpAsync.unity`](Assets/Examples/GravitySimulation/Scenes/Gravity_CSharpAsync.unity) | C# async |
| [`Gravity_CSharpParallel.unity`](Assets/Examples/GravitySimulation/Scenes/Gravity_CSharpParallel.unity) | C# parallel |
| [`Gravity_UnityJobs.unity`](Assets/Examples/GravitySimulation/Scenes/Gravity_UnityJobs.unity) | Unity Jobs |
| [`Gravity_UnityCoroutine.unity`](Assets/Examples/GravitySimulation/Scenes/Gravity_UnityCoroutine.unity) | Unity coroutine |
| [`Gravity_UnityJobsBurst.unity`](Assets/Examples/GravitySimulation/Scenes/Gravity_UnityJobsBurst.unity) | Unity Jobs with Burst |

All seven gravity scenes and the batch scene are in **Build Settings**. Regenerate the gravity scenes with **Tools → Gravity Simulation → Regenerate Example Scenes**.

Each gravity strategy has one implementation script in its own assembly under `Assets/Examples/GravitySimulation/Scripts`. Those assemblies reference `GravitySimulation.Core` for the shared gravity formula and common scene setup.

In 2D mode, bodies use `SpriteRenderer`, a dynamic `Rigidbody2D`, and a non-trigger `CircleCollider2D`; a circle sprite is generated when no body sprite is assigned. In 3D mode, sphere bodies use a dynamic `Rigidbody` and a non-trigger `SphereCollider`. Unity's 2D or 3D physics engine integrates movement and resolves sphere contacts. Built-in world gravity is disabled because the selected strategy calculates custom softened inverse-square gravity and applies it through `Rigidbody.AddForce`; 2D converts acceleration to an equivalent mass-scaled force because `ForceMode2D` has no acceleration option.

The initial positions and velocities are deterministic. This is a visual workload example, not a high-precision orbital solver. Async and coroutine scenes keep applying the last completed gravity calculation while the next calculation is in flight or split into chunks. At `N` bodies, each calculation performs roughly `N × (N - 1)` pair evaluations, so the workload grows quadratically.

## Profiling

1. Open **Window → Analysis → Profiler** before running the example.
2. Use the same workload settings for each mode. For gravity, reset the scene between captures; the batch scene recreates its same starting values for each run.
3. Warm up each mode first. Discard the first Burst measurement in Editor Play mode because it may include JIT compilation.
4. Compare frame time and main-thread cost. The gravity strategies expose `GravitySimulation.*` profiler markers around gravity calculation. Unity's Rigidbody integration and collision solving still run on the main thread and are shared by every mode, so use the markers to compare the processing strategies and total frame time to see the overall cost.
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
    ├── Scenes/       Starter scene and strategy scenes
    └── Scripts/      Shared gravity core and one script per strategy
```

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
