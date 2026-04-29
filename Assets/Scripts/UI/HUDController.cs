using System.Collections;
using UnityEngine;
using TMPro;

namespace AGVRSystem.UI
{
    /// <summary>
    /// Fixed world-space HUD for the RehabSession scene.
    /// Stays in a single position — does NOT follow the camera.
    /// Dark-themed rounded panels:
    /// - Top bar: Session Time | Exercise | Reps | Confidence Badge
    /// - Center: Left Grip Panel | Exercise Info + Avg Strength | Right Grip Panel
    /// - Bottom: Hold Progress Bar | Accuracy Bar
    /// Supports bilateral hand labels and finger spread angle overlay.
    /// </summary>
    public class HUDController : MonoBehaviour
    {
        [Header("Canvas")]
        [SerializeField] private Canvas _hudCanvas;

        [Header("Top Bar")]
        [SerializeField] private TMP_Text _sessionTimeLabel;
        [SerializeField] private TMP_Text _sessionTimeValue;
        [SerializeField] private TMP_Text _exerciseLabel;
        [SerializeField] private TMP_Text _exerciseValue;
        [SerializeField] private TMP_Text _repsLabel;
        [SerializeField] private TMP_Text _repsValue;
        [SerializeField] private ConfidenceBadge _confidenceBadge;

        [Header("Center Panels")]
        [SerializeField] private GripPanel _leftGripPanel;
        [SerializeField] private GripPanel _rightGripPanel;
        [SerializeField] private TMP_Text _exerciseTitleText;
        [SerializeField] private TMP_Text _exerciseInstructionText;

        [Header("Average Strength")]
        [SerializeField] private TMP_Text _averageStrengthLabel;
        [SerializeField] private TMP_Text _averageStrengthValue;
        [SerializeField] private ProgressBar _averageStrengthBar;

        [Header("Bottom Bars")]
        [SerializeField] private ProgressBar _holdProgressBar;
        [SerializeField] private TMP_Text _holdProgressLabel;
        [SerializeField] private ProgressBar _accuracyBar;
        [SerializeField] private TMP_Text _accuracyLabel;
        [SerializeField] private TMP_Text _accuracyPercentText;

        [Header("Feedback")]
        [SerializeField] private TMP_Text _feedbackText;
        [SerializeField] private GameObject _trackingLostPanel;

        [Header("Bilateral Hand Label")]
        [SerializeField] private TMP_Text _activeHandText;

        [Header("Spread Angle Overlay")]
        [SerializeField] private FingerSpreadAngleOverlay _spreadAngleOverlay;

        private const float FeedbackFlashDuration = 1.5f;
        private const float StrengthMaxValue = 100f;

        private Coroutine _feedbackCoroutine;

        private void Start()
        {
            if (_hudCanvas != null)
            {
                _hudCanvas.renderMode = RenderMode.WorldSpace;
            }

            if (_feedbackText != null)
            {
                _feedbackText.text = string.Empty;
            }

            // Auto-find GripPanels by name if not wired in Inspector.
            if (_leftGripPanel == null)
            {
                var leftGO = FindChildByName(transform, "LeftGripPanel");
                if (leftGO != null)
                {
                    _leftGripPanel = leftGO.GetComponent<GripPanel>();
                    if (_leftGripPanel == null)
                        _leftGripPanel = leftGO.gameObject.AddComponent<GripPanel>();
                    Debug.Log("[HUDController] Auto-found LeftGripPanel.");
                }
            }

            if (_rightGripPanel == null)
            {
                var rightGO = FindChildByName(transform, "RightGripPanel");
                if (rightGO != null)
                {
                    _rightGripPanel = rightGO.GetComponent<GripPanel>();
                    if (_rightGripPanel == null)
                        _rightGripPanel = rightGO.gameObject.AddComponent<GripPanel>();
                    Debug.Log("[HUDController] Auto-found RightGripPanel.");
                }
            }

            // Auto-find TMP_Text fields by name if not wired
            AutoFindText(ref _sessionTimeValue, "SessionTimeValue");
            AutoFindText(ref _exerciseValue, "ExerciseValue");
            AutoFindText(ref _repsValue, "RepsValue");
            AutoFindText(ref _exerciseTitleText, "ExerciseTitleText");
            AutoFindText(ref _exerciseInstructionText, "ExerciseInstructionText");
            AutoFindText(ref _accuracyPercentText, "AccuracyPercent");
            AutoFindText(ref _feedbackText, "FeedbackText");
            AutoFindText(ref _activeHandText, "ActiveHandText");

            // Auto-find ProgressBars by parent name
            AutoFindProgressBar(ref _holdProgressBar, "HoldProgressBarBG");
            AutoFindProgressBar(ref _accuracyBar, "AccuracyBarBG");

            // Auto-find spread overlay
            if (_spreadAngleOverlay == null)
                _spreadAngleOverlay = GetComponentInChildren<FingerSpreadAngleOverlay>(true);

            if (_averageStrengthLabel != null)
            {
                _averageStrengthLabel.text = "Avg Strength";
            }

            ShowTrackingLost(false);
            SetSpreadOverlayVisible(false);
        }

        /// <summary>
        /// Searches children recursively for a GameObject with the given name.
        /// </summary>
        private static Transform FindChildByName(Transform parent, string name)
        {
            if (parent == null) return null;

            foreach (Transform child in parent)
            {
                if (child.name == name)
                    return child;

                Transform found = FindChildByName(child, name);
                if (found != null)
                    return found;
            }

            return null;
        }

        private void AutoFindText(ref TMP_Text field, string goName)
        {
            if (field != null) return;

            var go = FindChildByName(transform, goName);
            if (go != null)
                field = go.GetComponent<TMP_Text>();
        }

        private void AutoFindProgressBar(ref ProgressBar field, string parentGoName)
        {
            if (field != null) return;

            var go = FindChildByName(transform, parentGoName);
            if (go != null)
                field = go.GetComponent<ProgressBar>();
        }

        private void Update()
        {
            UpdateConfidenceBadge();
        }

        /// <summary>
        /// Updates all HUD elements with current exercise state.
        /// </summary>
        public void UpdateHUD(
            float timer,
            string exerciseName,
            string exerciseInstruction,
            int exerciseNumber,
            int reps,
            int targetReps,
            float accuracy,
            float holdProgress)
        {
            if (_sessionTimeValue != null)
            {
                int minutes = (int)(timer / 60f);
                int seconds = (int)(timer % 60f);
                _sessionTimeValue.text = $"{minutes:D2}:{seconds:D2}";
            }

            if (_exerciseValue != null)
            {
                _exerciseValue.text = exerciseName;
            }

            if (_repsValue != null)
            {
                _repsValue.text = $"{reps} / {targetReps}";
            }

            if (_exerciseTitleText != null)
            {
                _exerciseTitleText.text = $"Exercise {exerciseNumber} \u2014 {exerciseName}";
            }

            if (_exerciseInstructionText != null)
            {
                _exerciseInstructionText.text = exerciseInstruction;
            }

            if (_holdProgressBar != null)
            {
                _holdProgressBar.SetValue(holdProgress);
            }

            if (_accuracyBar != null)
            {
                _accuracyBar.SetValue(accuracy / StrengthMaxValue);
            }

            if (_accuracyPercentText != null)
            {
                _accuracyPercentText.text = $"{accuracy:F0}%";
            }
        }

        /// <summary>
        /// Updates the active hand label display (e.g., "Left", "Right", or "Both").
        /// </summary>
        public void UpdateActiveHandLabel(string handLabel)
        {
            if (_activeHandText != null)
            {
                _activeHandText.text = string.IsNullOrEmpty(handLabel) ? "" : $"Active: {handLabel}";
            }
        }

        /// <summary>
        /// Shows or hides the finger spread angle overlay.
        /// </summary>
        public void SetSpreadOverlayVisible(bool visible)
        {
            if (_spreadAngleOverlay != null)
            {
                _spreadAngleOverlay.SetVisible(visible);
            }
        }

        /// <summary>
        /// Updates grip panels with per-finger data and average strength display.
        /// </summary>
        public void UpdateGripPanels(OVRHand leftHand, float leftGrip,
                                     OVRHand rightHand, float rightGrip)
        {
            _leftGripPanel?.UpdateGrip(leftHand, leftGrip);
            _rightGripPanel?.UpdateGrip(rightHand, rightGrip);

            bool leftTracked  = leftHand  != null && leftHand.IsTracked;
            bool rightTracked = rightHand != null && rightHand.IsTracked;
            UpdateAverageStrength(leftGrip, leftTracked, rightGrip, rightTracked);
        }

        /// <summary>
        /// Computes and displays the average grip strength from tracked hands.
        /// </summary>
        private void UpdateAverageStrength(float leftGrip, bool leftTracked,
                                           float rightGrip, bool rightTracked)
        {
            float average;
            if (leftTracked && rightTracked)
                average = (leftGrip + rightGrip) / 2f;
            else if (leftTracked)
                average = leftGrip;
            else if (rightTracked)
                average = rightGrip;
            else
                average = 0f;

            if (_averageStrengthValue != null)
                _averageStrengthValue.text = $"{average:F0}%";

            _averageStrengthBar?.SetValue(average / StrengthMaxValue);
        }


        /// <summary>
        /// Shows a temporary feedback message that fades out.
        /// </summary>
        public void ShowFeedback(string message)
        {
            if (_feedbackText == null)
                return;

            if (_feedbackCoroutine != null)
            {
                StopCoroutine(_feedbackCoroutine);
            }

            _feedbackCoroutine = StartCoroutine(FeedbackCoroutine(message));
        }

        /// <summary>
        /// Shows or hides the "TRACKING LOST" overlay panel.
        /// </summary>
        public void ShowTrackingLost(bool show)
        {
            if (_trackingLostPanel != null)
            {
                _trackingLostPanel.SetActive(show);
            }
        }

        private void UpdateConfidenceBadge()
        {
            if (_confidenceBadge == null)
                return;

            var manager = HandTrackingManager.Instance;
            if (manager == null)
            {
                _confidenceBadge.SetLost();
                return;
            }

            bool leftHigh = manager.LeftHand != null
                && manager.LeftHand.IsTracked
                && manager.LeftHand.HandConfidence == OVRHand.TrackingConfidence.High;

            bool rightHigh = manager.RightHand != null
                && manager.RightHand.IsTracked
                && manager.RightHand.HandConfidence == OVRHand.TrackingConfidence.High;

            if (leftHigh && rightHigh)
            {
                _confidenceBadge.SetHigh();
            }
            else if (manager.IsLeftTracked || manager.IsRightTracked)
            {
                _confidenceBadge.SetMedium();
            }
            else
            {
                _confidenceBadge.SetLost();
            }
        }

        private IEnumerator FeedbackCoroutine(string message)
        {
            _feedbackText.text = message;
            _feedbackText.alpha = 1f;

            float elapsed = 0f;
            while (elapsed < FeedbackFlashDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / FeedbackFlashDuration;
                _feedbackText.alpha = 1f - t;
                yield return null;
            }

            _feedbackText.text = string.Empty;
            _feedbackText.alpha = 1f;
            _feedbackCoroutine = null;
        }
    }
}
