# Random Search optimization

Random Search generates candidate points `(x, y)` and keeps the best one found. It minimizes the same 2D Sphere function as PSO:

```text
score = x² + y²
best x = 0, best y = 0, best score = 0
```

- Example scene: `Scenes/RandomSearch_Optimization.unity`
- Processing comparison scenes: `Scenes/RandomSearch_*.unity`
- Scripts: `Scripts/` (`BatchRandomSearchExample` demonstrates the algorithm; `RandomSearchProcessingExample` profiles it using each processing method)
- Editor menu: **Tools → Random Search → Open Example Scene**
- Work: **O(E)** objective evaluations for `E` candidates; storage is **O(1)** for the current best result plus reusable input buffers.
- Resources: none are required; candidates and input arrays are created by the example.

Open `Assets/WebDashboard/Scenes/Optimization_Benchmark.unity` to run Random Search and PSO through all six processing methods. Select the **Random Search** tab to compare its processing runtimes. The algorithms are workloads here; the table compares processing methods, not algorithm quality.