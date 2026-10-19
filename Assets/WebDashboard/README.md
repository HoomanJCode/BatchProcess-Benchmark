# Local real-time Web Dashboard

The dashboard displays Unity profiler measurements in a browser on this PC. It binds to `127.0.0.1:8765`; it is not published on the public internet.

- Page: `index.html`
- Unity streaming and comparison runner: `Scripts/RealtimeBenchmarkDashboard.cs`
- Scene and player generation: `Editor/`
- Full comparison launcher: `Scenes/All_Examples_Benchmark.unity`
- Optimization comparison launcher: `Scenes/Optimization_Benchmark.unity`
- Gravity and Simple Iteration launchers live in their example folders.

Run `All_Examples_Benchmark` in Unity to measure Gravity 2D, Gravity 3D, Simple Iteration, and Random Search in sequence. The page updates live during each workload and saves its completed results in browser local storage. Each comparison tab contains its Unity processing methods. Simple Iteration and Random Search each have five Unity methods; C# async splits the workload across worker tasks and awaits them.

The full benchmark scene starts the local page automatically. If needed, open `http://127.0.0.1:8765/` manually. Use the Gravity, Simple Iteration, or Random Search tabs to compare runtimes. Gravity has separate 2D and 3D subtabs. The optimization launcher compares Random Search across its processing methods.

The comparison panel has editable settings for each example. Select Gravity, Simple Iteration, or Random Search, set its workload size and measured runs, then choose **Run this comparison again**. Gravity also uses the selected 2D or 3D tab. The settings are saved in this browser, then sent only to Unity on `127.0.0.1`; every strategy receives the same workload settings. Random Search accepts evaluations per trial.

After a completed run, the live timeline is replaced by a short summary of the fastest method, speedup over the single-threaded baseline, median method time, and slowest method. Runtime bars use a logarithmic scale so methods remain visible when one is much slower; the labels show actual measured time. The page uses a light theme and links to the project repository in its footer.

Batch and optimization comparisons run one full, unmeasured warm-up pass for each method before collecting the configured measured runs. This helps keep first-use costs such as worker startup and Burst compilation out of the reported averages. Simple Iteration uses a shared batch-size setting in each method scene (default 1,000; set all scenes to the same value). Its C# async method starts worker tasks for disjoint batches and awaits them, so batches may run concurrently. Random Search processes trials in order; C# async splits each trial's independent candidate evaluations into bounded Task.Run chunks, matching the work units used by the other parallel methods. Compute time includes task creation, scheduling, and completion; wall time also includes the Unity continuation. For end-to-end comparison, select Wall P95 in the chart. Small workloads can still run faster on one thread because parallel scheduling has overhead.

Keep Unity in Play mode while you use the rerun controls. The local server stays active after the current comparison finishes so the browser can request another example or settings combination. Press Unity's Play button to stop the runner when you are done.

The headless gravity helper also serves this page from the standalone player at `http://127.0.0.1:8765/` after its first report is written. It keeps the player alive to serve results and accept rerun requests; stop the `GravityBenchmark.exe` process in Task Manager when finished. Other command-line helpers that do not request dashboard hosting still exit after writing their reports.
