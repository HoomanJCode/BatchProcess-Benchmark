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

`Tools/PythonBatchBenchmark/setup_and_run.py` runs outside Unity and measures Python single-threaded loops, one Python thread task per batch, one Python process task per batch, and NumPy vectorized operations. When you press **Play** on `SimpleIteration_Benchmark` or `All_Examples_Benchmark`, Unity runs this Python comparison automatically after its seven C# methods. The headless batch build uses the same automatic flow. It passes the Unity item count, iteration count, batch size, and run count to Python.

If Python has no NumPy installed, the setup script creates an isolated environment in the ignored `Library/PythonBatchBenchmark/venv` folder and installs it there. Python 3 must be available as `py` or `python` on `PATH`. To run the Python comparison separately:

```powershell
py -3 Tools/PythonBatchBenchmark/setup_and_run.py
```

The report is written to `Library/PythonBatchBenchmark/results.json`. Unity reads it automatically and sends all eleven method rows to the dashboard. The browser saves the completed comparison in local storage.

The Python process pool is true multi-process work but includes task scheduling, inter-process transfer of each batch, and copying results back. The Python thread pool starts tasks but CPU-bound Python code normally remains limited by the Global Interpreter Lock. NumPy performs vector operations in native code and reuses its arrays during the calculation. Python's built-in numbers use 64-bit floats and the Unity example stores 32-bit floats, so their checksums can differ slightly. Python has no rendered frame, so its total elapsed calculation time fills both compute and wall-time columns; compare compute averages for the main runtime comparison.
