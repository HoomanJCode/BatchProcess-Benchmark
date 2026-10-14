# Local real-time Web Dashboard

The dashboard displays Unity profiler measurements in a browser on this PC. It binds to `127.0.0.1:8765`; it is not published on the public internet.

- Page: `index.html`
- Unity streaming and comparison runner: `Scripts/RealtimeBenchmarkDashboard.cs`
- Scene and player generation: `Editor/`
- Full comparison launcher: `Scenes/All_Examples_Benchmark.unity`
- Optimization comparison launcher: `Scenes/Optimization_Benchmark.unity`
- Gravity and Simple Iteration launchers live in their example folders.

Run `All_Examples_Benchmark` in Unity to measure all five examples in sequence: Gravity 2D, Gravity 3D, Simple Iteration, PSO, and Random Search. The page updates live during each workload and saves its completed results in browser local storage. Each example has its own overview card or dimension view and comparison tab. Gravity and optimization use six processing methods. Simple Iteration adds a seventh method, C# async tasks (batched), to compare one task per batch with single-worker async and the other strategies.

The full benchmark scene starts the local page automatically. If needed, open `http://127.0.0.1:8765/` manually. Use the Gravity, Simple Iteration, PSO Simulation, or Random Search tabs to compare runtimes. Gravity has separate 2D and 3D subtabs. The optimization launcher runs both PSO and Random Search; their results appear in their respective dashboard tabs.

The dashboard overview lists the workload settings used by each example: balls for gravity, items and loop iterations for Simple Iteration, and particles, iterations, evaluations, and trials for optimization. After a completed run, the live timeline is replaced by a short summary of the fastest method, speedup over the single-threaded baseline, median method time, and slowest method. Runtime bars are proportional to measured time, so shorter bars are faster. The page uses a light theme and links to the project repository in its footer.

Batch and optimization comparisons run one full, unmeasured warm-up pass for each method before collecting the configured measured runs. This helps keep first-use costs such as worker startup and Burst compilation out of the reported averages. Simple Iteration uses a shared batch-size setting in each method scene (default 1,000; set all scenes to the same value). Its batched C# Tasks method creates one task per batch and includes the task creation and scheduling cost in its measurement. C# async uses one worker task for all batches; it does not split the calculation across cores. Small workloads can still run faster on a single thread because parallel scheduling has overhead.

When a comparison finishes in the Unity Editor, the runner publishes the completed measurements, waits briefly for the local browser to save them, and then exits Play mode. Standalone headless runs are unaffected.
