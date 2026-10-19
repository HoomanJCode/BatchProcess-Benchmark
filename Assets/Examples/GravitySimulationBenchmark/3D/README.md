# Gravity simulation benchmark (3D)

- Launcher scene: `Scenes/Gravity_Benchmark.unity`
- Per-method scenes: `Scenes/Benchmark/`
- Dimension-specific components: `Scripts/`
- Shared calculation and strategy code: `../Scripts/`

The benchmark uses the same 3D softened gravity calculation as the visual example. It measures array calculations without per-ball GameObjects or Unity physics components.
