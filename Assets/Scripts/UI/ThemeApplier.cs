using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AGVRSystem.UI
{
    /// <summary>
    /// Applies the active ThemeData colors to tagged UI elements on this GameObject.
    /// Attach to any UI element that should respond to theme changes.
    /// </summary>
    public class ThemeApplier : MonoBehaviour
    {
        /// <summary>Which theme color property to apply to this element.</summary>
        public enum ThemeRole
        {
            PanelBackground,
            PanelBorder,
            InnerPanel,
            TitleText,
            LabelText,
            ValueText,
            ButtonNormal,
            ButtonText,
            AccentPrimary,
            TitleGradient
        }

        [SerializeField] private ThemeRole _role = ThemeRole.PanelBackground;

        private Graphic _graphic;
        private TMP_Text _tmpText;
        private RoundedImage _roundedImage;
        private Button _button;

        private void Awake()
        {
            _graphic = GetComponent<Graphic>();
            _tmpText = GetComponent<TMP_Text>();
            _roundedImage = GetComponent<RoundedImage>();
            _button = GetComponent<Button>();
        }

        private void OnEnable()
        {
            if (ThemeManager.Instance != null)
            {
                ThemeManager.Instance.OnThemeChanged += ApplyTheme;
                ApplyTheme(ThemeManager.Instance.CurrentTheme);
            }
        }

        private void OnDisable()
        {
            if (ThemeManager.Instance != null)
            {
                ThemeManager.Instance.OnThemeChanged -= ApplyTheme;
            }
        }

        /// <summary>
        /// Applies the given theme colors based on this element's assigned role.
        /// </summary>
        public void ApplyTheme(ThemeData theme)
        {
            if (theme == null)
                return;

            switch (_role)
            {
                case ThemeRole.PanelBackground:
                    SetColor(theme.PanelBackground);
                    SetBorder(theme.PanelBorder);
                    break;

                case ThemeRole.PanelBorder:
                    SetColor(theme.PanelBorder);
                    break;

                case ThemeRole.InnerPanel:
                    SetColor(theme.InnerPanel);
                    SetBorder(theme.PanelBorder);
                    break;

                case ThemeRole.TitleText:
                    SetColor(theme.TitleColor);
                    break;

                case ThemeRole.LabelText:
                    SetColor(theme.LabelColor);
                    break;

                case ThemeRole.ValueText:
                    SetColor(theme.ValueColor);
                    break;

                case ThemeRole.ButtonNormal:
                    SetColor(theme.ButtonNormal);
                    SetBorder(theme.AccentPrimary);
                    ApplyButtonColors(theme);
                    break;

                case ThemeRole.ButtonText:
                    SetColor(theme.ButtonText);
                    break;

                case ThemeRole.AccentPrimary:
                    SetColor(theme.AccentPrimary);
                    break;

                case ThemeRole.TitleGradient:
                    ApplyGradient(theme);
                    break;
            }
        }

        private void SetColor(Color color)
        {
            if (_graphic != null)
            {
                _graphic.color = color;
            }
        }

        private void SetBorder(Color borderColor)
        {
            if (_roundedImage != null && _roundedImage.HasBorder)
            {
                _roundedImage.BorderColor = borderColor;
            }
        }

        private void ApplyButtonColors(ThemeData theme)
        {
            if (_button != null)
            {
                ColorBlock cb = _button.colors;
                cb.normalColor = theme.ButtonNormal;
                cb.highlightedColor = theme.ButtonHighlight;
                cb.pressedColor = theme.ButtonPressed;
                cb.selectedColor = theme.ButtonHighlight;
                _button.colors = cb;
            }
        }

        private void ApplyGradient(ThemeData theme)
        {
            if (_tmpText != null)
            {
                _tmpText.enableVertexGradient = true;
                _tmpText.colorGradient = new VertexGradient(
                    theme.GradientTopLeft,
                    theme.GradientTopRight,
                    theme.GradientBottomLeft,
                    theme.GradientBottomRight
                );
            }
        }
    }
}
