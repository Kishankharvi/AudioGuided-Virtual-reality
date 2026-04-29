using UnityEngine;

namespace AGVRSystem.UI
{
    /// <summary>
    /// Defines a color theme for the application UI.
    /// Each theme is a family of related colors used across all panels.
    /// </summary>
    [CreateAssetMenu(fileName = "NewTheme", menuName = "AGVRSystem/Theme Data")]
    public class ThemeData : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string _themeName = "Default";
        [SerializeField] private Color _accentPrimary = new Color(0.2f, 0.85f, 0.4f, 1f);

        [Header("Panel Colors")]
        [SerializeField] private Color _panelBackground = new Color(0.06f, 0.16f, 0.10f, 0.82f);
        [SerializeField] private Color _panelBorder = new Color(0.20f, 0.38f, 0.18f, 0.55f);
        [SerializeField] private Color _innerPanel = new Color(0.04f, 0.12f, 0.07f, 0.75f);

        [Header("Text Colors")]
        [SerializeField] private Color _titleColor = new Color(0.92f, 0.95f, 0.85f, 1f);
        [SerializeField] private Color _labelColor = new Color(0.50f, 0.68f, 0.42f, 1f);
        [SerializeField] private Color _valueColor = new Color(0.92f, 0.95f, 0.85f, 1f);

        [Header("Button Colors")]
        [SerializeField] private Color _buttonNormal = new Color(0.18f, 0.52f, 0.28f, 1f);
        [SerializeField] private Color _buttonHighlight = new Color(0.22f, 0.60f, 0.32f, 1f);
        [SerializeField] private Color _buttonPressed = new Color(0.14f, 0.42f, 0.22f, 1f);
        [SerializeField] private Color _buttonText = new Color(0.95f, 0.98f, 0.90f, 1f);

        [Header("Gradient (Title)")]
        [SerializeField] private Color _gradientTopLeft = new Color(0.92f, 0.95f, 0.85f, 1f);
        [SerializeField] private Color _gradientTopRight = new Color(0.85f, 0.92f, 0.78f, 1f);
        [SerializeField] private Color _gradientBottomLeft = new Color(0.60f, 0.80f, 0.50f, 1f);
        [SerializeField] private Color _gradientBottomRight = new Color(0.50f, 0.72f, 0.40f, 1f);

        /// <summary>Theme display name.</summary>
        public string ThemeName => _themeName;
        public Color AccentPrimary => _accentPrimary;
        public Color PanelBackground => _panelBackground;
        public Color PanelBorder => _panelBorder;
        public Color InnerPanel => _innerPanel;
        public Color TitleColor => _titleColor;
        public Color LabelColor => _labelColor;
        public Color ValueColor => _valueColor;
        public Color ButtonNormal => _buttonNormal;
        public Color ButtonHighlight => _buttonHighlight;
        public Color ButtonPressed => _buttonPressed;
        public Color ButtonText => _buttonText;
        public Color GradientTopLeft => _gradientTopLeft;
        public Color GradientTopRight => _gradientTopRight;
        public Color GradientBottomLeft => _gradientBottomLeft;
        public Color GradientBottomRight => _gradientBottomRight;
    }
}
