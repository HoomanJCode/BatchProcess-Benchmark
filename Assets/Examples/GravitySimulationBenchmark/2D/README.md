# Gravity simulation benchmark (2D)

- Launcher scene: `Scenes/Gravity_Benchmark.unity`
- Per-method scenes: `Scenes/Benchmark/`
- Dimension-specific components: `Scripts/`
- Shared calculation and strategy code: `../Scripts/`

The benchmark uses the same 2D softened gravity calculation as the visual example, with z distance ignored. It measures array calculations without per-ball GameObjects or Unity physics components.
