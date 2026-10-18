# Local real-time Web Dashboard

The dashboard displays Unity profiler measurements in a browser on this PC. It binds to `127.0.0.1:8765`; it is not published on the public internet.

- Page: `index.html`
- Unity streaming and comparison runner: `Scripts/RealtimeBenchmarkDashboard.cs`
- Scene and player generation: `Editor/`
- Full comparison launcher: `Scenes/All_Examples_Benchmark.unity`
- Optimization comparison launcher: `Scenes/Optimization_Benchmark.unity`
- Gravity and Simple Iteration launchers live in their example folders.

Run `All_Examples_Benchmark` in Unity to measure all five workloads in sequence: Gravity 2D, Gravity 3D, Simple Iteration, PSO, and Random Search. The page updates live during each workload and saves its completed results in browser local storage. Every comparison tab contains its Unity methods plus Python single-thread, threads, processes, and NumPy methods. Simple Iteration has six Unity methods; C# async splits the workload across worker tasks and awaits them.

The full benchmark scene starts the local page automatically. If needed, open `http://127.0.0.1:8765/` manually. Use the Gravity, Simple Iteration, PSO Simulation, or Random Search tabs to compare runtimes. Gravity has separate 2D and 3D subtabs. The optimization launcher runs both PSO and Random Search; their results appear in their respective dashboard tabs.

The comparison panel has editable settings for each example. Select Gravity, Simple Iteration, PSO, or Random Search, set its workload size and measured runs, then choose **Run this comparison again**. Gravity also uses the selected 2D or 3D tab. The settings are saved in this browser, then sent only to Unity on `127.0.0.1`; every strategy receives the same workload settings. Random Search accepts evaluations per trial, and PSO has its own particle and iteration settings. Unity and Python results are compared using the matching settings.

After a completed run, the live timeline is replaced by a short summary of the fastest method, speedup over the single-threaded baseline, median method time, and slowest method. Runtime bars use a logarithmic scale so methods remain visible when one is much slower; the labels show actual measured time. The page uses a light theme and links to the project repository in its footer.

Each workload starts the standalone Python benchmark after its Unity methods finish. Gravity passes the ball count and 2D/3D dimension. Simple Iteration passes the same item count, iteration count, batch size, and run count. PSO and Random Search pass the algorithm, deterministic seed, workload size, and trial count. The four Python rows are added to the matching dashboard results; PSO and Random Search stay in their separate tabs. The browser saves each combined comparison in local storage.

On first use, the Python setup script checks for NumPy. If it is missing, it creates `Library/PythonBatchBenchmark/venv` and installs NumPy into that ignored project-local environment. Python 3 must be installed and available as `py` or `python` on `PATH`. The methods are Python single threaded, Python threads, Python processes, and NumPy vectorized operations. Process-pool times include task scheduling and data transfer. Python does not have Unity frames: gravity measures a fixed-layout force calculation, while Unity also steps physics and resolves collisions. Compare compute times for calculation cost; wall/frame measurements include different runtime work. The Unity and Python implementations use the same deterministic workload, with small numerical differences possible from float precision.

Batch and optimization comparisons run one full, unmeasured warm-up pass for each method before collecting the configured measured runs. This helps keep first-use costs such as worker startup and Burst compilation out of the reported averages. Simple Iteration uses a shared batch-size setting in each method scene (default 1,000; set all scenes to the same value). Its C# async method starts worker tasks for disjoint batches and awaits them, so batches may run concurrently. PSO and Random Search assign independent trials to worker tasks; a single-trial run splits independent work into chunks where possible. These methods include task creation, scheduling, and completion in compute time; wall time also includes the Unity continuation. Coroutine compute time sums active chunks and reductions, excluding frame waits; wall time includes those waits. For end-to-end comparison, select Wall P95 in the chart. Small workloads can still run faster on one thread because parallel scheduling has overhead.

Keep Unity in Play mode while you use the rerun controls. The local server stays active after the current comparison finishes so the browser can request another example or settings combination. Press Unity's Play button to stop the runner when you are done. Standalone headless runs are unaffected.
