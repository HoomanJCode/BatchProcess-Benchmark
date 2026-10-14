# Local real-time Web Dashboard

The dashboard displays Unity profiler measurements in a browser on this PC. It binds to `127.0.0.1:8765`; it is not published on the public internet.

- Page: `index.html`
- Unity streaming and comparison runner: `Scripts/RealtimeBenchmarkDashboard.cs`
- Scene and player generation: `Editor/`
- Optimization comparison launcher: `Scenes/Optimization_Benchmark.unity`
- Gravity and Simple Iteration launchers live in their example folders.

Run each comparison launcher in Unity. The page updates live while the scene runs, then saves the completed result in browser local storage so you can compare the five examples: Gravity 2D, Gravity 3D, Simple Iteration, PSO, and Random Search. Each workload uses the same six methods: single-threaded, C# async, C# parallel, Unity Jobs, Unity coroutine, and Unity Jobs with Burst.

Start Unity's Play mode in the corresponding benchmark scene. The scene starts the local page automatically. If needed, open `http://127.0.0.1:8765/` manually. Use the workload tabs and the 2D/3D or PSO/Random Search subtabs to compare runtimes within each workload.