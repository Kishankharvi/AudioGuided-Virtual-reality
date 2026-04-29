using UnityEngine;
using UnityEngine.UI;

namespace AGVRSystem.UI
{
    /// <summary>
    /// Builds a stylized semi-transparent hand silhouette from UI primitives at runtime.
    /// Used as a background guide image behind live hand tracking projections.
    /// Attach to a RectTransform where the silhouette should appear.
    /// </summary>
    public class HandSilhouetteBuilder : MonoBehaviour
    {
        [Header("Hand Configuration")]
        [SerializeField] private bool _isRightHand;

        [Header("Appearance")]
        [SerializeField] private Color _silhouetteColor = new Color(0.3f, 0.3f, 0.35f, 0.12f);
        [SerializeField] private Color _outlineColor = new Color(0.4f, 0.4f, 0.45f, 0.18f);
        [SerializeField] private float _outlineWidth = 1.5f;
        [SerializeField] private float _handScale = 1f;

        private const float PalmWidth = 100f;
        private const float PalmHeight = 110f;
        private const float PalmCornerRadius = 22f;

        /// <summary>Finger definitions: offsetX, offsetY, width, height, rotation, cornerRadius.</summary>
        private static readonly float[][] FingerDefs =
        {
            // Thumb (angled outward)
            new[] { -55f, -10f, 22f, 65f, 25f, 8f },
            // Index
            new[] { -32f, 70f, 18f, 72f, 0f, 7f },
            // Middle
            new[] { -8f, 78f, 19f, 80f, 0f, 7f },
            // Ring
            new[] { 16f, 72f, 18f, 70f, 0f, 7f },
            // Pinky
            new[] { 38f, 60f, 16f, 58f, 0f, 6f },
        };

        private void Awake()
        {
            BuildSilhouette();
        }

        private void BuildSilhouette()
        {
            var parentRect = GetComponent<RectTransform>();
            if (parentRect == null)
                return;

            // Container for the silhouette
            var container = new GameObject("HandSilhouette", typeof(RectTransform));
            container.transform.SetParent(transform, false);

            var containerRect = container.GetComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0.5f, 0.5f);
            containerRect.anchorMax = new Vector2(0.5f, 0.5f);
            containerRect.pivot = new Vector2(0.5f, 0.5f);
            containerRect.sizeDelta = new Vector2(PalmWidth * 2f, PalmHeight * 2f);
            containerRect.anchoredPosition = new Vector2(0f, -15f);
            containerRect.localScale = new Vector3(
                _isRightHand ? -_handScale : _handScale,
                _handScale, 1f);

            // Palm (rounded rectangle)
            CreateRoundedRect(containerRect, "Palm",
                Vector2.zero, new Vector2(PalmWidth, PalmHeight),
                0f, PalmCornerRadius);

            // Fingers
            for (int i = 0; i < FingerDefs.Length; i++)
            {
                float[] def = FingerDefs[i];
                CreateRoundedRect(containerRect, $"Finger_{i}",
                    new Vector2(def[0], def[1]),
                    new Vector2(def[2], def[3]),
                    def[4], def[5]);
            }

            // Wrist stub
            CreateRoundedRect(containerRect, "Wrist",
                new Vector2(0f, -65f),
                new Vector2(70f, 30f),
                0f, 10f);
        }

        private void CreateRoundedRect(RectTransform parent, string name,
            Vector2 position, Vector2 size, float rotation, float cornerRadius)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            rect.localRotation = Quaternion.Euler(0f, 0f, rotation);

            var roundedImage = go.AddComponent<RoundedImage>();
            roundedImage.color = _silhouetteColor;
            roundedImage.raycastTarget = false;
            roundedImage.CornerRadius = cornerRadius;

            if (_outlineWidth > 0f)
            {
                roundedImage.HasBorder = true;
                roundedImage.BorderWidth = _outlineWidth;
                roundedImage.BorderColor = _outlineColor;
            }
        }
    }
}
