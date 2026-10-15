# Simple iteration (simple batch example)

This is the small, repeatable workload used to explain processing methods. Each scene starts with the same number values, performs the same calculation, and reports a checksum plus compute and wall time.

For each value, every iteration multiplies by `1.00001`, adds `0.00002`, and subtracts `1` if the result reaches `1`. The calculation is intentionally artificial; it represents CPU work, not a physical process.

- Scenes: `Scenes/SimpleIteration_*.unity`
- Scripts: `Scripts/` (one readable script per processing method)
- Editor menu: **Tools → Simple Iteration → Open Comparison Scene**
- Comparison scene: `Scenes/SimpleIteration_Benchmark.unity`
- Complexity: **O(N × I)** work and **O(N)** stored values (`N` values, `I` iterations per value)
- Resources: none are needed; the example creates its input values in code.

The Unity comparison launcher runs one unmeasured warm-up pass and then repeated measured runs for each of the seven C# methods. Every scene has a **Batch Size** setting, defaulting to 1,000 items. With 100,000 items this creates 100 batches; set it to 10,000 to create 10. Keep the value equal in every scene when comparing methods.

**C# async** processes all batches sequentially in one worker task. **C# async tasks** starts one task per batch and awaits all of them. Its task creation and scheduling costs are included in the measurement; those task objects are necessary for this method. Task scheduling has a cost, so the best batch size depends on the workload and PC. For a headless run, see `Tools/Run-HeadlessBatchBenchmark.ps1` in the project root.

## Python comparison

`Tools/PythonBatchBenchmark/benchmark.py` runs outside Unity and measures Python single-threaded loops, one Python thread task per batch, one Python process task per batch, and NumPy vectorized operations when NumPy is installed. It uses the same item count, iteration count, batch size, and starting-value formula as these scenes. The defaults are 100,000 items, 100 iterations, batches of 1,000, and five measured runs after one warm-up. Change them with `--items`, `--iterations`, `--batch-size`, and `--runs` when matching different Unity scene settings.

Install NumPy and run the script from the project root:

```powershell
py -m pip install -r Tools/PythonBatchBenchmark/requirements.txt
py Tools/PythonBatchBenchmark/benchmark.py
```

Then select **Simple Iteration** in the local dashboard and load `Library/PythonBatchBenchmark/results.json` with **Load Python timings**. The import is kept in this browser's local storage. The dashboard checks that the Python and latest Unity results have matching settings before combining their rows. Reimport the report after a new Python run to replace the old Python timings.

The Python process pool is true multi-process work but includes task scheduling, inter-process transfer of each batch, and copying results back. The Python thread pool starts tasks but CPU-bound Python code normally remains limited by the Global Interpreter Lock. NumPy performs vector operations in native code and reuses its arrays during the calculation. Python's built-in numbers use 64-bit floats and the Unity example stores 32-bit floats, so their checksums can differ slightly. Python has no rendered frame, so its total elapsed calculation time fills both compute and wall-time columns; compare compute averages for the main runtime comparison.
