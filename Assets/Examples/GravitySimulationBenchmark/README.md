# Gravity simulation benchmark

This standalone example compares the gravity calculation strategies in 2D and 3D. It uses preallocated arrays and does not create per-ball GameObjects, renderers, colliders, or rigidbodies during the measured workload.

- 2D scenes and scripts: `2D/`
- 3D scenes and scripts: `3D/`
- Shared calculation setup and method strategies: `Scripts/`
- Scene generation: `Editor/GravityBenchmarkSceneGeneration.cs`

Each dimension has a `Gravity_Benchmark.unity` launcher scene and one scene per strategy under `Scenes/Benchmark/`. The benchmark scenes are generated from the corresponding visual examples in `GravitySimulation`, so both examples use the same gravity inputs while measuring only the calculation workload.

The five strategies are single threaded, C# async with bounded `Task.Run` workers, C# parallel with `Parallel.For`, Unity Jobs, and Unity Jobs with Burst.
