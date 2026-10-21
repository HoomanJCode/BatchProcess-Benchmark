# Gravity simulation (2D)

This example uses Unity's 2D scene and physics components. The balls are circle sprites with `Rigidbody2D` and `CircleCollider2D`; they move in the XY plane.

- Teaching scenes: `Scenes/Gravity_*.unity`
- Strategy scripts: `Scripts/`
- Sprite resource: `Resources/GravityBall.png`
- Dimension-specific scene menu: **Tools → Gravity 2D → Regenerate Scenes**

Every strategy script is self-contained. Each ball skips itself, sums contributions from the other `N - 1` balls, then averages those contributions:

```text
r_ij = position_j - position_i
averageAcceleration_i = Σ[j ≠ i] G * mass_j * r_ij / (|r_ij|² + softening²)^(3/2) / max(1, N - 1)
```

The work per physics step is **O(N²)** for `N` balls. Positions, masses, and acceleration buffers use **O(N)** memory. The 2D scripts use `Rigidbody2D.AddForce` and set the z component of each direction to zero.

The C# async strategy splits target balls into a bounded set of `Task.Run` worker chunks and waits for them before applying forces. Its profiler marker covers task creation, scheduling, and completion. Each worker checks every source ball sequentially. The C# parallel strategy uses `Parallel.For` over target balls; its inner source-ball loop is sequential. Both strategies keep Unity physics reads and force application on the main thread. The async gravity calculation still waits inside `FixedUpdate`, so its calculation is parallel but does not leave the physics step running asynchronously.

The array-only processing benchmark is a separate example at `../../GravitySimulationBenchmark/2D/`.
