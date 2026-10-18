# PSO simulation

Particle Swarm Optimization (PSO) is the optimization workload for this folder. Each particle is a candidate point `(x, y)`. The swarm updates particle positions and velocities over repeated iterations to minimize the 2D Sphere function:

```text
score = x² + y²
best x = 0, best y = 0, best score = 0
```

- Example scene: `Scenes/PSO_Simulation.unity`
- Processing comparison scenes: `Scenes/PSO_*.unity`
- Scripts: `Scripts/` (`BatchParticleSwarmExample` demonstrates the algorithm; `PSOProcessingExample` profiles it using each processing method)
- Editor menu: **Tools → PSO Simulation → Open Example Scene**
- Work: **O(P × I)** objective evaluations for `P` particles and `I` iterations; storage is **O(P)**.
- Resources: none are required; particles and input arrays are created by the example.

Open `Assets/WebDashboard/Scenes/Optimization_Benchmark.unity` to run PSO and Random Search through six Unity methods each. Select the **Particle Swarm (PSO)** tab to compare PSO processing runtimes. This project compares processing methods within the same workload; it does not rank the optimization algorithms by solution quality.
