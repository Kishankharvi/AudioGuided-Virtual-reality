using UnityEngine;
using TMPro;

namespace AGVRSystem.UI
{
    /// <summary>
    /// Green pill-shaped badge that shows tracking confidence state.
    /// Updates color: green = High, yellow = Medium, red = Lost.
    /// </summary>
    public class ConfidenceBadge : MonoBehaviour
    {
        [SerializeField] private RoundedImage _background;
        [SerializeField] private TMP_Text _label;

        private static readonly Color HighColor = new Color(0.2f, 0.75f, 0.35f, 1f);
        private static readonly Color MediumColor = new Color(0.85f, 0.75f, 0.1f, 1f);
        private static readonly Color LostColor = new Color(0.85f, 0.2f, 0.2f, 1f);

        private static readonly Color HighBorderColor = new Color(0.25f, 0.85f, 0.4f, 0.6f);
        private static readonly Color MediumBorderColor = new Color(0.95f, 0.85f, 0.15f, 0.6f);
        private static readonly Color LostBorderColor = new Color(0.95f, 0.25f, 0.25f, 0.6f);

        /// <summary>
        /// Sets badge to High confidence (green).
        /// </summary>
        public void SetHigh()
        {
            Apply(HighColor, HighBorderColor, "High confidence");
        }

        /// <summary>
        /// Sets badge to Medium confidence (yellow).
        /// </summary>
        public void SetMedium()
        {
            Apply(MediumColor, MediumBorderColor, "Low confidence");
        }

        /// <summary>
        /// Sets badge to Lost state (red).
        /// </summary>
        public void SetLost()
        {
            Apply(LostColor, LostBorderColor, "Tracking lost");
        }

        private void Apply(Color bgColor, Color borderColor, string text)
        {
            if (_background != null)
            {
                _background.color = bgColor;
                _background.BorderColor = borderColor;
            }

            if (_label != null)
            {
                _label.text = text;
            }
        }
    }
}
