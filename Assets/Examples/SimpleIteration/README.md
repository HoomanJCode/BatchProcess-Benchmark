# Simple iteration (simple batch example)

This is the small, repeatable workload used to explain processing methods. Each scene starts with the same number values, performs the same calculation, and reports a checksum plus compute and wall time.

For each value, every iteration multiplies by `1.00001`, adds `0.00002`, and subtracts `1` if the result reaches `1`. The calculation is intentionally artificial; it represents CPU work, not a physical process.

- Scenes: `Scenes/SimpleIteration_*.unity`
- Scripts: `Scripts/` (one readable script per processing method)
- Editor menu: **Tools → Simple Iteration → Open Comparison Scene**
- Comparison scene: `Scenes/SimpleIteration_Benchmark.unity`
- Complexity: **O(N × I)** work and **O(N)** stored values (`N` values, `I` iterations per value)
- Resources: none are needed; the example creates its input values in code.

The comparison launcher runs one unmeasured warm-up pass and then repeated measured runs for each of the seven methods. Every scene has a **Batch Size** setting, defaulting to 1,000 items. With 100,000 items this creates 100 batches; set it to 10,000 to create 10. Keep the value equal in every scene when comparing methods.

**C# async** processes all batches sequentially in one worker task. **C# async tasks (batched)** starts one task per batch and awaits all of them. Its task creation and scheduling costs are included in the measurement; those task objects are necessary for this method. Task scheduling has a cost, so the best batch size depends on the workload and PC. For a headless run, see `Tools/Run-HeadlessBatchBenchmark.ps1` in the project root.
