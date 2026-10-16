using System.Collections;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Beginner example: Unity coroutine: a coroutine calculates a small group of target bodies each frame.
/// The setup and gravity formula are kept in this file so this strategy can be read on its own.
/// </summary>
public sealed class Gravity3DUnityCoroutineGravityExample : MonoBehaviour
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
    private Vector3[] _positionSnapshot; // Reusable input snapshot for the active calculation.
    private float[] _masses; // Mass of each body, used as the gravity source strength.
    private Vector3[] _accelerations; // Results from the most recent gravity calculation.
    private Vector3[] _nextAccelerations; // Reusable output buffer for the active calculation.
    private readonly ProfilerMarker _gravityMarker = new ProfilerMarker("GravitySimulation.UnityCoroutine");
    private const int BodiesPerFrame = 16;
    private readonly ProfilerMarker _loopStartMarker =
        new ProfilerMarker("GravitySimulation.UnityCoroutine.LoopStart");
    private readonly ProfilerMarker _chunkMarker =
        new ProfilerMarker("GravitySimulation.UnityCoroutine.Chunk");
    private bool _calculationRequested;
    private bool _calculationRunning;
    private float _stepGravity;
    private float _stepSofteningSquared;
    private bool _stepIs2D;
    private Coroutine _workerCoroutine;
    #endregion

    #region Example implementation
    // Awake runs once when the scene starts: create the arrays, ball visuals, and physics bodies.
    private void Awake()
    {
        // Arrays keep each body's position, mass, and calculated gravity easy to find.
        // Read the local dashboard value before creating the balls and their reusable arrays.
        _ballCount = Mathf.Max(1, PlayerPrefs.GetInt("SimulationLab.Balls", _ballCount));
        _bodies3D = new Rigidbody[_ballCount];
        _bodies2D = new Rigidbody2D[_ballCount];
        _positions = new Vector3[_ballCount];
        _positionSnapshot = new Vector3[_ballCount];
        _masses = new float[_ballCount];
        _accelerations = new Vector3[_ballCount];
        _nextAccelerations = new Vector3[_ballCount];
        // Resources lets every 2D ball reuse one circle sprite asset.
        Sprite ballSprite = Resources.Load<Sprite>("GravityBall");
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

                SpriteRenderer spriteRenderer = ball.AddComponent<SpriteRenderer>();
                spriteRenderer.sprite = ballSprite;
                spriteRenderer.color = ballColor;

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
                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = "Gravity Ball " + i.ToString("D3");
                sphere.transform.position = position;
                sphere.transform.localScale = Vector3.one * ballDiameter;
                sphere.GetComponent<Renderer>().material.color = ballColor;

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

    private void Start()
    {
        // Start one coroutine and keep yielding from it; do not create an iterator per physics step.
        _workerCoroutine = StartCoroutine(CalculateGravityInChunks());
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

        // Keep applying the last complete result while this step is split across frames.
        using (_gravityMarker.Auto())
        {
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

        // Copy into reusable storage once. The coroutine reads this snapshot until all chunks finish.
        if (!_calculationRequested && !_calculationRunning)
        {
            System.Array.Copy(_positions, _positionSnapshot, _ballCount);
            _stepGravity = _gravity * _gravityScale;
            _stepSofteningSquared = _softening * _softening;
            _stepIs2D = _is2D;
            _calculationRequested = true;
        }
        return;
    }

    private IEnumerator CalculateGravityInChunks()
    {
        while (true)
        {
            if (!_calculationRequested)
            {
                yield return null;
                continue;
            }

            _calculationRequested = false;
            // Mark the first frame so the benchmark can sum this loop's later chunks.
            using (_loopStartMarker.Auto())
                _calculationRunning = true;
            for (int start = 0; start < _ballCount; start += BodiesPerFrame)
            {
                int end = Mathf.Min(start + BodiesPerFrame, _ballCount);
                // Calculate up to BodiesPerFrame target bodies, then yield for the next frame.
                using (_chunkMarker.Auto())
                {
                    for (int i = start; i < end; i++)
                    {
                        // GRAVITY FORMULA (one source body j acting on target body i):
                        // force_ij = G * mass_i * mass_j * offset / |offset|^3
                        // deltaAcceleration_i = G * mass_j * offset / (dot(offset, offset) + epsilon^2)^(3/2)
                        // offset = position_j - position_i; G = gravity * gravityScale; epsilon = softening.
                        // The target mass cancels when force is divided by mass_i. In 2D, offset.z is zero.
                        // Complexity per physics step: O(N^2) gravity work and O(N) storage, where N is ball count.
                        Vector3 acceleration = Vector3.zero;
                        for (int other = 0; other < _ballCount; other++)
                        {
                            if (other == i)
                                continue;
                            Vector3 offset = _positionSnapshot[other] - _positionSnapshot[i];
                            if (_stepIs2D)
                                offset.z = 0f;
                            float inverseDistance = 1f /
                                Mathf.Sqrt(offset.sqrMagnitude + _stepSofteningSquared);
                            acceleration += offset * (_stepGravity * _masses[other] *
                                inverseDistance * inverseDistance * inverseDistance);
                        }
                        _nextAccelerations[i] = acceleration;
                    }
                }

                if (end < _ballCount)
                    yield return null;
            }

            // Publish the completed result by swapping buffers; no new result array is created.
            Vector3[] previousResults = _accelerations;
            _accelerations = _nextAccelerations;
            _nextAccelerations = previousResults;
            _calculationRunning = false;
        }
    }
    #endregion
}
