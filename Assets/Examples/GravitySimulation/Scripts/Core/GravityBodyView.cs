using UnityEngine;

/// <summary>
/// The view-side module for one simulated body. It owns rendering and exposes its
/// force calculation for the Unity modular-main-thread comparison mode.
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
    private int _stateIndex;

    public void Initialize(BodyState initialState, Color color, SimulationDimension dimension, Sprite sprite)
    {
        if (dimension == SimulationDimension.TwoD)
        {
            _spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
            _spriteRenderer.sprite = sprite != null ? sprite : GetDefaultSprite();
            _spriteRenderer.color = color;
            _spriteRenderer.sortingOrder = 1;
        }
        else
        {
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

        transform.position = initialState.Position;
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

    internal void SetStateIndex(int index)
    {
        _stateIndex = index;
    }

    /// <summary>Called only by the modular mode; ordinary modes use shared math loops.</summary>
    public Vector3 CalculateAcceleration(BodyState[] states, float gravity, float softening, bool is2D)
    {
        return GravityMath.Acceleration(states, _stateIndex, gravity, softening, is2D);
    }

    public void ApplyState(BodyState state, SimulationDimension dimension)
    {
        Vector3 position = state.Position;
        if (dimension == SimulationDimension.TwoD)
            position.z = 0f;
        transform.position = position;
    }
}
