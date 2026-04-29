using UnityEngine;
using TMPro;

namespace AGVRSystem.UI
{
    /// <summary>
    /// Panel showing overall grip % and per-finger progress bars (Index, Middle, Ring, Pinky).
    /// Matches the reference design with rounded dark panel background.
    /// Auto-discovers ProgressBar and TMP_Text children by name convention if not wired.
    /// </summary>
    public class GripPanel : MonoBehaviour
    {
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _percentageText;
        [SerializeField] private ProgressBar _indexBar;
        [SerializeField] private ProgressBar _middleBar;
        [SerializeField] private ProgressBar _ringBar;
        [SerializeField] private ProgressBar _pinkyBar;

        private const float StrengthScale = 100f;

        private void Start()
        {
            // Auto-find progress bars and text by name convention
            // E.g., LeftGripPanel has children: LeftGripTitle, LeftGripPercent,
            // LeftIndexBarBG (with ProgressBar), LeftMiddleBarBG, etc.
            AutoFindProgressBar(ref _indexBar, "Index");
            AutoFindProgressBar(ref _middleBar, "Middle");
            AutoFindProgressBar(ref _ringBar, "Ring");
            AutoFindProgressBar(ref _pinkyBar, "Pinky");

            if (_percentageText == null)
            {
                AutoFindText(ref _percentageText, "Percent");
            }
            if (_titleText == null)
            {
                AutoFindText(ref _titleText, "Title");
            }
        }

        /// <summary>
        /// Updates the panel with grip data from an OVRHand.
        /// </summary>
        public void UpdateGrip(OVRHand hand, float overallGrip)
        {
            if (_percentageText != null)
            {
                _percentageText.text = $"{overallGrip:F0}%";
            }

            if (hand == null || !hand.IsTracked)
            {
                SetAllBars(0f);
                return;
            }

            float index = hand.GetFingerPinchStrength(OVRHand.HandFinger.Index);
            float middle = hand.GetFingerPinchStrength(OVRHand.HandFinger.Middle);
            float ring = hand.GetFingerPinchStrength(OVRHand.HandFinger.Ring);
            float pinky = hand.GetFingerPinchStrength(OVRHand.HandFinger.Pinky);

            if (_indexBar != null) _indexBar.SetValue(index);
            if (_middleBar != null) _middleBar.SetValue(middle);
            if (_ringBar != null) _ringBar.SetValue(ring);
            if (_pinkyBar != null) _pinkyBar.SetValue(pinky);
        }

        private void SetAllBars(float value)
        {
            if (_indexBar != null) _indexBar.SetValueImmediate(value);
            if (_middleBar != null) _middleBar.SetValueImmediate(value);
            if (_ringBar != null) _ringBar.SetValueImmediate(value);
            if (_pinkyBar != null) _pinkyBar.SetValueImmediate(value);
        }

        /// <summary>
        /// Searches children for a ProgressBar whose parent name contains the finger name.
        /// E.g., "Index" matches "LeftIndexBarBG" or "RightIndexBarBG".
        /// </summary>
        private void AutoFindProgressBar(ref ProgressBar field, string fingerName)
        {
            if (field != null) return;

            foreach (Transform child in transform)
            {
                if (child.name.Contains(fingerName) && child.name.Contains("Bar"))
                {
                    field = child.GetComponent<ProgressBar>();
                    if (field != null)
                    {
                        Debug.Log($"[GripPanel] Auto-found {fingerName} ProgressBar on {child.name}.");
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Searches children for a TMP_Text whose name contains the keyword.
        /// </summary>
        private void AutoFindText(ref TMP_Text field, string keyword)
        {
            if (field != null) return;

            foreach (Transform child in transform)
            {
                if (child.name.Contains(keyword))
                {
                    field = child.GetComponent<TMP_Text>();
                    if (field != null) return;
                }
            }
        }
    }
}
