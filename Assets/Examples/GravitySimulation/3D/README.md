# Gravity simulation (3D)

This example uses Unity's 3D scene and physics components. Each ball is a sphere with a dynamic `Rigidbody` and `SphereCollider` and moves in XYZ space.

- Teaching scenes: `Scenes/Gravity_*.unity`
- Equal-size profiling scenes: `Scenes/Benchmark/`
- Strategy scripts: `Scripts/`
- Dimension-specific scene menu: **Tools → Gravity 3D → Regenerate Scenes**

Every strategy script is self-contained. The force uses softened Newtonian gravity:

```text
r_ij = position_j - position_i
acceleration_i += G * mass_j * r_ij / (|r_ij|² + softening²)^(3/2)
```

The work per physics step is **O(N²)** for `N` balls. Positions, masses, and acceleration buffers use **O(N)** memory. The scene disables built-in world gravity because each strategy calculates and applies the attraction itself.

The C# async strategy splits target balls into a bounded set of `Task.Run` worker chunks and waits for them before applying forces. Its profiler marker covers task creation, scheduling, and completion. Each worker checks every source ball sequentially. The C# parallel strategy uses `Parallel.For` over target balls; its inner source-ball loop is sequential. Both strategies keep Unity physics reads and force application on the main thread. The async gravity calculation still waits inside `FixedUpdate`, so its calculation is parallel but does not leave the physics step running asynchronously.

Use `Scenes/Gravity_Benchmark.unity` to run the five Unity processing methods followed by Python single-thread, threads, processes, and NumPy. Python evaluates the same gravity formula for a fixed deterministic 3D layout; it does not run Unity physics or have Unity frame timings. The results appear beside the Unity methods in the 3D dashboard tab.
