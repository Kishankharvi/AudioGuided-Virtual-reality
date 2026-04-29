using UnityEngine;
using UnityEngine.UI;

namespace AGVRSystem.UI
{
    /// <summary>
    /// Simple fill-based progress bar using two RoundedImages (background + fill).
    /// Supports smooth animated fill and color gradients.
    /// </summary>
    public class ProgressBar : MonoBehaviour
    {
        [SerializeField] private RoundedImage _background;
        [SerializeField] private RectTransform _fillArea;
        [SerializeField] private RoundedImage _fill;
        [SerializeField] private Color _fillColor = new Color(0.25f, 0.65f, 0.35f, 1f);
        [SerializeField] private Color _backgroundColor = new Color(0.15f, 0.18f, 0.25f, 1f);
        [SerializeField] private float _smoothSpeed = 8f;

        private float _targetValue;
        private float _currentValue;

        private void Start()
        {
            if (_background != null)
            {
                _background.color = _backgroundColor;
            }

            if (_fill != null)
            {
                _fill.color = _fillColor;
            }
        }

        private void Update()
        {
            if (Mathf.Approximately(_currentValue, _targetValue))
                return;

            _currentValue = Mathf.Lerp(_currentValue, _targetValue, Time.deltaTime * _smoothSpeed);

            if (Mathf.Abs(_currentValue - _targetValue) < 0.001f)
            {
                _currentValue = _targetValue;
            }

            ApplyFill();
        }

        /// <summary>
        /// Sets the fill value (0-1). Animates smoothly.
        /// </summary>
        public void SetValue(float value01)
        {
            _targetValue = Mathf.Clamp01(value01);
        }

        /// <summary>
        /// Sets the fill value immediately without animation.
        /// </summary>
        public void SetValueImmediate(float value01)
        {
            _targetValue = Mathf.Clamp01(value01);
            _currentValue = _targetValue;
            ApplyFill();
        }

        /// <summary>
        /// Updates the fill color at runtime.
        /// </summary>
        public void SetFillColor(Color newColor)
        {
            _fillColor = newColor;
            if (_fill != null)
            {
                _fill.color = _fillColor;
            }
        }

        private void ApplyFill()
        {
            if (_fillArea == null)
                return;

            _fillArea.anchorMin = Vector2.zero;
            _fillArea.anchorMax = new Vector2(_currentValue, 1f);
            _fillArea.offsetMin = Vector2.zero;
            _fillArea.offsetMax = Vector2.zero;
        }
    }
}
