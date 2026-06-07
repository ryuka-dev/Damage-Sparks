using TMPro;
using UnityEngine;

namespace SulfurDamageNumbersMod
{
    internal sealed class DamageNumberView : MonoBehaviour
    {
        private TextMeshPro _text;
        private Vector3 _basePosition;
        private Vector3 _jitter;
        private float _startTime;
        private float _lifetime;
        private float _floatSpeed;
        private Color _baseColor;

        public void Init(string value, Vector3 position, Vector3 jitter, float lifetime, float floatSpeed, float worldScale, float fontSize, Color color)
        {
            _basePosition = position;
            _jitter = jitter;
            _startTime = Time.time;
            _lifetime = Mathf.Max(0.05f, lifetime);
            _floatSpeed = floatSpeed;
            _baseColor = color;

            transform.position = position + jitter;
            transform.localScale = Vector3.one * worldScale;

            _text = gameObject.AddComponent<TextMeshPro>();
            _text.text = value;
            _text.fontSize = fontSize;
            _text.alignment = TextAlignmentOptions.Center;
            _text.textWrappingMode = TextWrappingModes.NoWrap;
            _text.color = _baseColor;
        }

        private void LateUpdate()
        {
            float age = Time.time - _startTime;
            if (age >= _lifetime)
            {
                Destroy(gameObject);
                return;
            }

            transform.position = _basePosition + _jitter + Vector3.up * (age * _floatSpeed);

            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 dir = transform.position - cam.transform.position;
                if (dir.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(dir, cam.transform.up);
            }

            float alpha = Mathf.Clamp01(1f - age / _lifetime);
            Color c = _baseColor;
            c.a = alpha;
            _text.color = c;
        }
    }
}
