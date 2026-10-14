using UnityEngine;

/// <summary>
/// Renders one simulated body as a sprite or sphere.
/// </summary>
public sealed class GravityBodyView : MonoBehaviour
{
    private static Material _sharedMaterial;
    private static Sprite _defaultSprite;
    private static Texture2D _defaultSpriteTexture;
    private static readonly int ColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int StandardColorId = Shader.PropertyToID("_Color");
    private MaterialPropertyBlock _propertyBlock;
    private Renderer _renderer;
    private SpriteRenderer _spriteRenderer;
    private Rigidbody _rigidbody;
    private Rigidbody2D _rigidbody2D;

    public void Initialize(BodyState initialState, Color color, SimulationDimension dimension, Sprite sprite)
    {
        // Set the initial pose before adding a Rigidbody so physics starts from the correct position.
        transform.position = initialState.Position;

        if (dimension == SimulationDimension.TwoD)
        {
            // The 2D physics world needs both a 2D collider and a dynamic 2D body.
            CircleCollider2D circle = GetComponent<CircleCollider2D>();
            if (circle == null) circle = gameObject.AddComponent<CircleCollider2D>();
            circle.isTrigger = false;

            _rigidbody2D = GetComponent<Rigidbody2D>();
            if (_rigidbody2D == null) _rigidbody2D = gameObject.AddComponent<Rigidbody2D>();
            _rigidbody2D.bodyType = RigidbodyType2D.Dynamic;
            _rigidbody2D.mass = initialState.Mass;
            _rigidbody2D.gravityScale = 0f;
            _rigidbody2D.linearVelocity = initialState.Velocity;
            _rigidbody2D.constraints = RigidbodyConstraints2D.FreezeRotation;
            _rigidbody2D.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            _rigidbody2D.interpolation = RigidbodyInterpolation2D.Interpolate;

            _spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
            _spriteRenderer.sprite = sprite != null ? sprite : GetDefaultSprite();
            _spriteRenderer.color = color;
            _spriteRenderer.sortingOrder = 1;
        }
        else
        {
            // Keep the sphere collider from the primitive and let PhysX resolve contacts.
            SphereCollider sphere = GetComponent<SphereCollider>();
            if (sphere == null) sphere = gameObject.AddComponent<SphereCollider>();
            sphere.isTrigger = false;

            _rigidbody = GetComponent<Rigidbody>();
            if (_rigidbody == null) _rigidbody = gameObject.AddComponent<Rigidbody>();
            _rigidbody.mass = initialState.Mass;
            _rigidbody.useGravity = false;
            _rigidbody.linearVelocity = initialState.Velocity;
            _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

            _renderer = GetComponent<Renderer>();
            if (_sharedMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                if (shader != null)
                    _sharedMaterial = new Material(shader)
                    {
                        name = "Gravity Body Shared Material",
                        hideFlags = HideFlags.HideAndDontSave
                    };
            }
            if (_renderer != null && _sharedMaterial != null)
                _renderer.sharedMaterial = _sharedMaterial;

            _propertyBlock = new MaterialPropertyBlock();
            if (_renderer != null)
            {
                _renderer.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(ColorId, color);
                _propertyBlock.SetColor(StandardColorId, color);
                _renderer.SetPropertyBlock(_propertyBlock);
            }
        }
    }

    /// <summary>Reads physics-owned motion state for the next gravity calculation.</summary>
    public BodyState ReadState(float mass, SimulationDimension dimension)
    {
        if (dimension == SimulationDimension.TwoD)
        {
            Vector2 position = _rigidbody2D.position;
            Vector2 velocity = _rigidbody2D.linearVelocity;
            return new BodyState(new Vector3(position.x, position.y, 0f),
                new Vector3(velocity.x, velocity.y, 0f), mass);
        }

        return new BodyState(_rigidbody.position, _rigidbody.linearVelocity, mass);
    }

    /// <summary>Applies gravity as acceleration so body mass does not change the orbit calculation.</summary>
    public void ApplyGravityAcceleration(Vector3 acceleration, SimulationDimension dimension)
    {
        if (dimension == SimulationDimension.TwoD)
            // Legacy 2D physics has no acceleration force mode, so convert acceleration to force.
            _rigidbody2D.AddForce(acceleration * _rigidbody2D.mass, ForceMode2D.Force);
        else
            _rigidbody.AddForce(acceleration, ForceMode.Acceleration);
    }

    private static Sprite GetDefaultSprite()
    {
        if (_defaultSprite != null)
            return _defaultSprite;

        const int size = 64;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size - 0.5f;
                float dy = (y + 0.5f) / size - 0.5f;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01((0.48f - distance) * size + 0.5f) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        }

        _defaultSpriteTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Default Gravity Body Sprite Texture",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        _defaultSpriteTexture.SetPixels32(pixels);
        _defaultSpriteTexture.Apply(false, true);
        _defaultSprite = Sprite.Create(_defaultSpriteTexture, new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f), size);
        _defaultSprite.name = "Default Gravity Body Sprite";
        _defaultSprite.hideFlags = HideFlags.HideAndDontSave;
        return _defaultSprite;
    }

}
