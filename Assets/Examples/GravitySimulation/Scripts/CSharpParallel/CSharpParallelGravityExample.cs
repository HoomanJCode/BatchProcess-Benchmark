using System.Threading.Tasks;
using Unity.Profiling;
using UnityEngine;

/// <summary>
/// Beginner example: C# Parallel.For: worker threads calculate different target bodies at the same time.
/// The setup and gravity formula are kept in this file so this strategy can be read on its own.
/// </summary>
public sealed class CSharpParallelGravityExample : MonoBehaviour
{
    [Header("Beginner settings")]
    [SerializeField, Min(1)] private int _bodyCount = 128; // Number of spheres in this example.
    [SerializeField, Min(0f)] private float _gravity = 1.15f; // Gravitational constant G.
    [SerializeField, Min(0.001f)] private float _softening = 0.2f; // Softens the force when bodies get very close.
    [SerializeField, Min(0f)] private float _gravityScale = 1f; // Makes the force easier to tune in the Inspector.
    [SerializeField] private bool _is2D; // Set by the scene: true for a flat XY simulation.

    private Rigidbody[] _bodies3D;
    private Rigidbody2D[] _bodies2D;
    private Vector3[] _positions; // Current positions copied from the physics bodies.
    private float[] _masses; // Mass of each body, used as the gravity source strength.
    private Vector3[] _accelerations; // Results from the most recent gravity calculation.
    private readonly ProfilerMarker _gravityMarker = new ProfilerMarker("GravitySimulation.CSharpParallel");

    // Awake runs once when the scene starts: create the arrays, spheres, and physics bodies.
    private void Awake()
    {
        // Arrays keep each body's position, mass, and calculated gravity easy to find.
        _bodyCount = Mathf.Max(1, _bodyCount);
        _bodies3D = new Rigidbody[_bodyCount];
        _bodies2D = new Rigidbody2D[_bodyCount];
        _positions = new Vector3[_bodyCount];
        _masses = new float[_bodyCount];
        _accelerations = new Vector3[_bodyCount];
        // A fixed seed makes every scene start with the same bodies.
        uint randomState = 0xC0FFEEu;
        for (int i = 0; i < _bodyCount; i++)
        {
            // The golden-angle spiral spreads bodies instead of placing them in a straight row.
            float y = 1f - (i / (float)Mathf.Max(1, _bodyCount - 1)) * 2f;
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

            // Every visible sphere has a collider and a dynamic physics body.
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Gravity Body " + i.ToString("D3");
            sphere.transform.position = position;
            float sphereScale = Mathf.Clamp(0.2f * Mathf.Pow(128f / _bodyCount, 0.22f), 0.08f, 0.25f);
            sphere.transform.localScale = Vector3.one * sphereScale;
            sphere.GetComponent<Renderer>().material.color =
                Color.HSVToRGB((i * 0.6180339f) % 1f, 0.68f, 1f);

            if (_is2D)
            {
                // Keep the sphere mesh on a child so its disabled 3D collider can stay separate
                // from the 2D collider and Rigidbody2D on the parent physics object.
                sphere.GetComponent<SphereCollider>().enabled = false;
                GameObject physicsBody = new GameObject(sphere.name + " Physics");
                physicsBody.transform.position = position;
                sphere.transform.SetParent(physicsBody.transform);
                sphere.transform.localPosition = Vector3.zero;

                CircleCollider2D circle = physicsBody.AddComponent<CircleCollider2D>();
                circle.radius = sphereScale * 0.5f;
                circle.isTrigger = false;
                Rigidbody2D body = physicsBody.AddComponent<Rigidbody2D>();
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
        for (int i = 0; i < _bodyCount; i++)
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
            // Each worker owns one output index. All workers only read the shared input arrays.
            Parallel.For(0, _bodyCount, i =>
            {
                // GRAVITY FORMULA (one source body j acting on target body i):
                // force_ij = G * mass_i * mass_j * offset / |offset|^3
                // deltaAcceleration_i = G * mass_j * offset / (dot(offset, offset) + epsilon^2)^(3/2)
                // offset = position_j - position_i; G = gravity * gravityScale; epsilon = softening.
                // The target mass cancels when force is divided by mass_i. In 2D, offset.z is zero.
                Vector3 acceleration = Vector3.zero;
                for (int other = 0; other < _bodyCount; other++)
                {
                    if (other == i)
                        continue;

                    Vector3 offset = _positions[other] - _positions[i];
                    if (_is2D)
                        offset.z = 0f;
                    float inverseDistance = 1f /
                        Mathf.Sqrt(offset.sqrMagnitude + _softening * _softening);
                    acceleration += offset * (_gravity * _gravityScale * _masses[other] *
                        inverseDistance * inverseDistance * inverseDistance);
                }
                _accelerations[i] = acceleration;
            });
        }
        // AddForce applies the calculated acceleration; Unity integrates motion and resolves collisions.
        for (int i = 0; i < _bodyCount; i++)
        {
            if (_is2D)
                _bodies2D[i].AddForce(
                    new Vector2(_accelerations[i].x, _accelerations[i].y) * _bodies2D[i].mass,
                    ForceMode2D.Force);
            else
                _bodies3D[i].AddForce(_accelerations[i], ForceMode.Acceleration);
        }
    }
}
