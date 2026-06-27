using TMPro;
using UnityEngine;

namespace SulfurDamageNumbersMod
{
    internal sealed class DamageNumberView : IDamageNumberView
    {
        private const int WorldFadeSteps = 10;
        private const float WorldOutlineOffsetFactor = 0.02f;

        private static readonly Vector2[] OutlineDirections =
        {
            new Vector2(-1f, -1f),
            new Vector2(-1f,  1f),
            new Vector2( 1f, -1f),
            new Vector2( 1f,  1f)
        };

        private readonly GameObject _gameObject;
        private readonly Transform _transform;
        private readonly TextMeshPro[] _outlineTexts;
        private readonly TextMeshPro _text;

        private Vector3 _basePosition;
        private Vector3 _jitter;
        private float _lifetime;
        private float _floatSpeed;
        private Color _baseColor;
        private int _lastFadeStep = -1;

        public bool IsActive { get; private set; }
        public float StartTime { get; private set; }
        public Vector3 CurrentWorldPosition { get { return _transform.position; } }
        public Material FontSharedMaterial { get { return _text.fontSharedMaterial; } }

        public DamageNumberView(Transform parent, Material sharedMaterial)
        {
            _gameObject = new GameObject("SULFUR Damage Number World");
            _gameObject.transform.SetParent(parent, false);
            _transform = _gameObject.transform;

            // The game's TMP material does not reliably display SDF outlines. Keep every
            // glyph at the exact same size and form the border from four small diagonal
            // offsets. This avoids the enlarged-black-copy appearance from 1.0.11.
            _outlineTexts = new TextMeshPro[OutlineDirections.Length];
            for (int i = 0; i < _outlineTexts.Length; i++)
                _outlineTexts[i] = CreateTextLayer("Outline" + i, 0);

            _text = CreateTextLayer("Face", 1);

            if (sharedMaterial != null)
                SetSharedMaterial(sharedMaterial);
            else if (_text.fontSharedMaterial != null)
                SetOutlineSharedMaterial(_text.fontSharedMaterial);

            Stop();
        }

        private TextMeshPro CreateTextLayer(string name, int sortingOrder)
        {
            GameObject layer = new GameObject(name, typeof(TextMeshPro));
            layer.transform.SetParent(_transform, false);

            TextMeshPro text = layer.GetComponent<TextMeshPro>();
            text.alignment = TextAlignmentOptions.Center;
            text.fontStyle = FontStyles.Bold;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.sortingOrder = sortingOrder;
            return text;
        }

        public void SetSharedMaterial(Material sharedMaterial)
        {
            if (sharedMaterial == null)
                return;

            _text.fontSharedMaterial = sharedMaterial;
            SetOutlineSharedMaterial(sharedMaterial);
        }

        private void SetOutlineSharedMaterial(Material sharedMaterial)
        {
            for (int i = 0; i < _outlineTexts.Length; i++)
                _outlineTexts[i].fontSharedMaterial = sharedMaterial;
        }

        public void Play(string value, Vector3 position, Vector3 jitter, float lifetime, float floatSpeed, float worldScale, float fontSize, Color color)
        {
            _basePosition = position;
            _jitter = jitter;
            StartTime = Time.time;
            _lifetime = Mathf.Max(0.05f, lifetime);
            _floatSpeed = floatSpeed;
            _baseColor = color;
            _lastFadeStep = WorldFadeSteps;

            _transform.position = position + jitter;
            _transform.localScale = Vector3.one * worldScale;

            color.a = 1f;
            _text.text = value;
            _text.fontSize = fontSize;
            _text.color = color;

            float outlineOffset = Mathf.Max(0.05f, fontSize * WorldOutlineOffsetFactor);
            for (int i = 0; i < _outlineTexts.Length; i++)
            {
                TextMeshPro outline = _outlineTexts[i];
                Vector2 direction = OutlineDirections[i];
                outline.transform.localPosition = new Vector3(
                    direction.x * outlineOffset,
                    direction.y * outlineOffset,
                    0f);
                outline.text = value;
                outline.fontSize = fontSize;
                outline.color = Color.black;
                outline.enabled = true;
            }

            _text.enabled = true;
            _gameObject.SetActive(true);
            IsActive = true;
        }

        public void Tick(Camera camera, float now)
        {
            if (!IsActive) return;

            float age = now - StartTime;
            if (age >= _lifetime)
            {
                Stop();
                return;
            }

            _transform.position = _basePosition + _jitter + Vector3.up * (age * _floatSpeed);

            if (camera != null)
            {
                Vector3 direction = _transform.position - camera.transform.position;
                if (direction.sqrMagnitude > 0.0001f)
                    _transform.rotation = Quaternion.LookRotation(direction, camera.transform.up);
            }

            // World-space TMP stores alpha in vertex colors. Quantizing the fade avoids
            // rebuilding all text meshes on every rendered frame.
            float remaining = Mathf.Clamp01(1f - age / _lifetime);
            int fadeStep = Mathf.Clamp(Mathf.CeilToInt(remaining * WorldFadeSteps), 0, WorldFadeSteps);
            if (fadeStep != _lastFadeStep)
            {
                _lastFadeStep = fadeStep;
                float alpha = fadeStep / (float)WorldFadeSteps;

                Color faceColor = _baseColor;
                faceColor.a = alpha;
                _text.color = faceColor;

                Color outlineColor = Color.black;
                outlineColor.a = alpha;
                for (int i = 0; i < _outlineTexts.Length; i++)
                    _outlineTexts[i].color = outlineColor;
            }
        }

        public void Stop()
        {
            IsActive = false;
            if (_text != null)
                _text.enabled = false;

            if (_outlineTexts != null)
            {
                for (int i = 0; i < _outlineTexts.Length; i++)
                {
                    if (_outlineTexts[i] != null)
                        _outlineTexts[i].enabled = false;
                }
            }

            if (_gameObject != null)
                _gameObject.SetActive(false);
        }
    }
}
