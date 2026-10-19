using Unity.Collections;
using Unity.Jobs;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Beginner example: Unity Jobs: an IJobParallelFor calculates different target bodies using NativeArray data, without Burst.
/// The setup and gravity formula are kept in this file so this strategy can be read on its own.
/// </summary>
public sealed class Gravity3DUnityJobsGravityExample : MonoBehaviour
{
    #region Settings and reusable data
    [Header("Beginner settings")]
    [FormerlySerializedAs("_bodyCount"), SerializeField, Min(1)] private int _ballCount = 128; // Number of spheres in this example.
    [SerializeField, Min(0f)] private float _gravity = 1.15f; // Gravitational constant G.
    [SerializeField, Min(0.001f)] private float _softening = 0.2f; // Softens the force when bodies get very close.
    [SerializeField, Min(0f)] private float _gravityScale = 1f; // Makes the force easier to tune in the Inspector.
    [SerializeField] private bool _is2D; // Set by the scene: true for a flat XY simulation.

    private Rigidbody[] _bodies3D;
    private Rigidbody2D[] _bodies2D;
    private Vector3[] _positions; // Current positions copied from the physics bodies.
    private float[] _masses; // Mass of each body, used as the gravity source strength.
    private Vector3[] _accelerations; // Results from the most recent gravity calculation.
    private readonly ProfilerMarker _gravityMarker = new ProfilerMarker("GravitySimulation.UnityJobs");
    private NativeArray<Vector3> _nativePositions;
    private NativeArray<float> _nativeMasses;
    private NativeArray<Vector3> _nativeAccelerations;
    #endregion

    #region Example implementation
    // Awake runs once when the scene starts: create the arrays, ball visuals, and physics bodies.
    private void Awake()
    {
        // Arrays keep each body's position, mass, and calculated gravity easy to find.
        // Read the local dashboard value before creating the balls and their reusable arrays.
        _ballCount = BenchmarkRunSettings.GetGravityBallCount(_ballCount);
        _bodies3D = new Rigidbody[_ballCount];
        _bodies2D = new Rigidbody2D[_ballCount];
        _positions = new Vector3[_ballCount];
        _masses = new float[_ballCount];
        _accelerations = new Vector3[_ballCount];
        // Resources lets every 2D ball reuse one circle sprite asset.
        Sprite ballSprite = BenchmarkRunSettings.IsCalculationOnlyGravityBenchmark ? null : Resources.Load<Sprite>("GravityBall");
        // Jobs read NativeArrays, so allocate native input and output buffers once.
        _nativePositions = new NativeArray<Vector3>(_ballCount, Allocator.Persistent);
        _nativeMasses = new NativeArray<float>(_ballCount, Allocator.Persistent);
        _nativeAccelerations = new NativeArray<Vector3>(_ballCount, Allocator.Persistent);
        // A fixed seed makes every scene start with the same bodies.
        uint randomState = 0xC0FFEEu;
        for (int i = 0; i < _ballCount; i++)
        {
            // The golden-angle spiral spreads bodies instead of placing them in a straight row.
            float y = 1f - (i / (float)Mathf.Max(1, _ballCount - 1)) * 2f;
            float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float angle = i * 2.39996323f;
            Vector3 direction;
            if (_is2D)
                direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            else
                direction = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);

            // Vary each body's distance from the center while keeping scene startup repeatable.
            randomState = randomState * 1664525u + 1013904223u;
            float randomRadius = (randomState & 0x00FFFFFFu) / 16777216f;
            Vector3 position = direction * (2f + randomRadius * 4.5f);
            Vector3 tangent;
            if (_is2D)
                tangent = new Vector3(-direction.y, direction.x, 0f);
            else
                tangent = Vector3.Cross(direction, Vector3.up).normalized;
            if (tangent.sqrMagnitude < 0.01f)
                tangent = Vector3.right;

            // Give bodies a few different masses so the source-mass term is visible in the formula.
            float mass = 0.7f + (i % 5) * 0.15f;
            _positions[i] = position;
            _masses[i] = mass;

            // Use a flat circle sprite in the 2D scenes. All balls share this imported sprite.
            float ballDiameter = Mathf.Clamp(0.2f * Mathf.Pow(128f / _ballCount, 0.22f), 0.08f, 0.25f);
            Color ballColor = Color.HSVToRGB((i * 0.6180339f) % 1f, 0.68f, 1f);

            if (_is2D)
            {
                // The sprite, circle collider, and Rigidbody2D all belong to this 2D ball.
                GameObject ball = new GameObject("Gravity Ball " + i.ToString("D3"));
                ball.transform.position = position;
                ball.transform.localScale = Vector3.one * ballDiameter;

                if (!BenchmarkRunSettings.IsCalculationOnlyGravityBenchmark)
                {
                    SpriteRenderer spriteRenderer = ball.AddComponent<SpriteRenderer>();
                    spriteRenderer.sprite = ballSprite;
                    spriteRenderer.color = ballColor;
                }

                CircleCollider2D circle = ball.AddComponent<CircleCollider2D>();
                circle.radius = 0.5f;
                circle.isTrigger = false;
                Rigidbody2D body = ball.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Dynamic;
                body.mass = mass;
                body.gravityScale = 0f; // The script calculates gravity itself.
                body.linearVelocity = new Vector2(-direction.y, direction.x) * 0.18f;
                body.constraints = RigidbodyConstraints2D.FreezeRotation;
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                body.interpolation = RigidbodyInterpolation2D.Interpolate;
                _bodies2D[i] = body;
            }
            else
            {
                // The 3D scene uses a lit sphere mesh with Unity's 3D physics components.
                GameObject sphere;
                if (BenchmarkRunSettings.IsCalculationOnlyGravityBenchmark)
                {
                    sphere = new GameObject();
                    sphere.AddComponent<SphereCollider>();
                }
                else
                {
                    sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    BenchmarkRunSettings.SetSharedBallColor(sphere.GetComponent<Renderer>(), ballColor);
                }
                sphere.name = "Gravity Ball " + i.ToString("D3");
                sphere.transform.position = position;
                sphere.transform.localScale = Vector3.one * ballDiameter;

                Rigidbody body = sphere.AddComponent<Rigidbody>();
                body.mass = mass;
                body.useGravity = false; // The script calculates gravity itself.
                body.linearVelocity = tangent.normalized * 0.18f;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                _bodies3D[i] = body;
            }
        }
    }
    // Unity calls FixedUpdate once per physics step.
    private void FixedUpdate()
    {
        // Read positions after the previous physics step, then calculate and apply the next forces.
        for (int i = 0; i < _ballCount; i++)
        {
            if (_is2D)
            {
                Vector2 position = _bodies2D[i].position;
                _positions[i] = new Vector3(position.x, position.y, 0f);
            }
            else
            {
                _positions[i] = _bodies3D[i].position;
            }
        }

        using (_gravityMarker.Auto())
        {
            // Copy managed arrays into native memory before scheduling the job.
            for (int i = 0; i < _ballCount; i++)
            {
                _nativePositions[i] = _positions[i];
                _nativeMasses[i] = _masses[i];
            }

            var gravityJob = new GravityJob
            {
                Positions = _nativePositions,
                Masses = _nativeMasses,
                Accelerations = _nativeAccelerations,
                Gravity = _gravity * _gravityScale,
                SofteningSquared = _softening * _softening,
                Is2D = _is2D
            };
            // Each job iteration calculates one body. Complete waits before reading its results.
            gravityJob.Schedule(_ballCount, 32).Complete();

            for (int i = 0; i < _ballCount; i++)
                _accelerations[i] = _nativeAccelerations[i];
        }
        // AddForce leaves integration and collision resolution to Unity's physics engine.
        if (BenchmarkRunSettings.IsCalculationOnlyGravityBenchmark)
            return;
        for (int i = 0; i < _ballCount; i++)
        {
            if (_is2D)
                _bodies2D[i].AddForce(
                    new Vector2(_accelerations[i].x, _accelerations[i].y) * _bodies2D[i].mass,
                    ForceMode2D.Force);
            else
                _bodies3D[i].AddForce(_accelerations[i], ForceMode.Acceleration);
        }
    }
    private void OnDestroy()
    {
        // Persistent native memory must be released when this scene object is destroyed.
        if (_nativePositions.IsCreated) _nativePositions.Dispose();
        if (_nativeMasses.IsCreated) _nativeMasses.Dispose();
        if (_nativeAccelerations.IsCreated) _nativeAccelerations.Dispose();
    }

    private struct GravityJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Vector3> Positions;
        [ReadOnly] public NativeArray<float> Masses;
        [WriteOnly] public NativeArray<Vector3> Accelerations;
        public float Gravity;
        public float SofteningSquared;
        public bool Is2D;

        public void Execute(int index)
        {
            // This job iteration calculates the acceleration for one target body.
            // GRAVITY FORMULA (one source body j acting on target body i):
            // force_ij = G * mass_i * mass_j * offset / |offset|^3
            // deltaAcceleration_i = G * mass_j * offset / (dot(offset, offset) + epsilon^2)^(3/2)
            // offset = position_j - position_i; G = gravity * gravityScale; epsilon = softening.
            // The target mass cancels when force is divided by mass_i. In 2D, offset.z is zero.
            // Complexity per physics step: O(N^2) gravity work and O(N) storage, where N is ball count.
            Vector3 acceleration = Vector3.zero;
            for (int other = 0; other < Positions.Length; other++)
            {
                if (other == index)
                    continue;

                Vector3 offset = Positions[other] - Positions[index];
                if (Is2D)
                    offset.z = 0f;
                float inverseDistance = 1f /
                    Mathf.Sqrt(offset.sqrMagnitude + SofteningSquared);
                acceleration += offset * (Gravity * Masses[other] *
                    inverseDistance * inverseDistance * inverseDistance);
            }
            Accelerations[index] = acceleration;
        }
    }
    #endregion
}
