# Gravity simulation (3D)

This example uses Unity's 3D scene and physics components. Each ball is a sphere with a dynamic `Rigidbody` and `SphereCollider` and moves in XYZ space.

- Teaching scenes: `Scenes/Gravity_*.unity`
- Strategy scripts: `Scripts/`
- Ball collisions use the shared [`Resources/GravityBallPhysicsMaterial.physicMaterial`](Resources/GravityBallPhysicsMaterial.physicMaterial) asset. Edit its friction and bounciness values to tune the 3D balls.
- Dimension-specific scene menu: **Tools → Gravity 3D → Regenerate Scenes**

Every strategy script is self-contained. The force uses softened Newtonian gravity:

```text
r_ij = position_j - position_i
acceleration_i += G * mass_j * r_ij / (|r_ij|² + softening²)^(3/2)
```

The work per physics step is **O(N²)** for `N` balls. Positions, masses, and acceleration buffers use **O(N)** memory. The scene disables built-in world gravity because each strategy calculates and applies the attraction itself.

The C# async strategy splits target balls into a bounded set of `Task.Run` worker chunks and waits for them before applying forces. Its profiler marker covers task creation, scheduling, and completion. Each worker checks every source ball sequentially. The C# parallel strategy uses `Parallel.For` over target balls; its inner source-ball loop is sequential. Both strategies keep Unity physics reads and force application on the main thread. The async gravity calculation still waits inside `FixedUpdate`, so its calculation is parallel but does not leave the physics step running asynchronously.

The array-only processing benchmark is a separate example at `../../GravitySimulationBenchmark/3D/`.
