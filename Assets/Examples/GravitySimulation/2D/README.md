# Gravity simulation (2D)

This example uses Unity's 2D scene and physics components. The balls are circle sprites with `Rigidbody2D` and `CircleCollider2D`; they move in the XY plane.

- Teaching scenes: `Scenes/Gravity_*.unity`
- Equal-size profiling scenes: `Scenes/Benchmark/`
- Strategy scripts: `Scripts/`
- Sprite resource: `Resources/GravityBall.png`
- Dimension-specific scene menu: **Tools → Gravity 2D → Regenerate Scenes**

Every strategy script is self-contained. The force uses softened Newtonian gravity:

```text
r_ij = position_j - position_i
acceleration_i += G * mass_j * r_ij / (|r_ij|² + softening²)^(3/2)
```

The work per physics step is **O(N²)** for `N` balls. Positions, masses, and acceleration buffers use **O(N)** memory. The 2D scripts use `Rigidbody2D.AddForce` and set the z component of each direction to zero.

Use `Scenes/Gravity_Benchmark.unity` to run all six processing methods in sequence and compare them in the local Web Dashboard.