using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AGVRSystem.UI
{
    /// <summary>
    /// Waits for both hands at High confidence for a configurable duration.
    /// Shows left/right hand images, status pills with text, and a status bar
    /// matching a calibration canvas design with position-detected indicators.
    /// </summary>
    public class CalibrationUI : MonoBehaviour
    {
        [Header("Confidence Indicators")]
        [SerializeField] private Graphic _leftConfidenceIndicator;
        [SerializeField] private Graphic _rightConfidenceIndicator;

        [Header("Status Pills")]
        [SerializeField] private Graphic _leftStatusPill;
        [SerializeField] private Graphic _rightStatusPill;
        [SerializeField] private TMP_Text _leftStatusText;
        [SerializeField] private TMP_Text _rightStatusText;

        [Header("Status Bar")]
        [SerializeField] private TMP_Text _statusText;

        [Header("Calibration Timing")]
        [Tooltip("Seconds both hands must hold High confidence before calibration completes. Default: 6.")]
        [SerializeField] private float _confirmationDuration = 6f;

        private const string RehabSessionScene = "RehabSession";

        private static readonly Color GreenColor = new Color(0.2f, 0.75f, 0.35f, 1f);
        private static readonly Color BlueColor = new Color(0.22f, 0.55f, 0.88f, 1f);
        private static readonly Color YellowColor = new Color(0.9f, 0.85f, 0.15f, 1f);
        private static readonly Color RedColor = new Color(0.9f, 0.2f, 0.2f, 1f);

        private float _highConfidenceTimer;
        private bool _calibrationDone;

        /// <summary>
        /// Fired when calibration succeeds (both hands tracked at High confidence for required duration).
        /// </summary>
        public event Action OnCalibrationComplete;

        private float _lastCountdownBeepTime;
        private bool _ttsCalibrationStarted;

        private void Start()
        {
            // Fade in on scene start
            if (SceneTransitionManager.Instance != null)
            {
                SceneTransitionManager.Instance.FadeIn();
            }

            // TTS calibration intro
            if (Audio.TTSVoiceGuide.Instance != null)
            {
                Audio.TTSVoiceGuide.Instance.SpeakCalibrationStart();
            }
        }

        private void Update()
        {
            if (_calibrationDone)
                return;

            var manager = HandTrackingManager.Instance;
            if (manager == null)
            {
                SetLeftState(RedColor, "Not Detected");
                SetRightState(RedColor, "Not Detected");
                ResetTimer("Waiting for hand tracking...");
                return;
            }

            bool leftHigh = manager.LeftHand != null
                && manager.LeftHand.IsTracked
                && manager.LeftHand.HandConfidence == OVRHand.TrackingConfidence.High;

            bool rightHigh = manager.RightHand != null
                && manager.RightHand.IsTracked
                && manager.RightHand.HandConfidence == OVRHand.TrackingConfidence.High;

            bool leftTracked = manager.IsLeftTracked;
            bool rightTracked = manager.IsRightTracked;

            // Update left hand state
            if (leftHigh)
            {
                SetLeftState(GreenColor, "Position Detected");
            }
            else if (leftTracked)
            {
                SetLeftState(YellowColor, "Low Confidence");
            }
            else
            {
                SetLeftState(RedColor, "Not Detected");
            }

            // Update right hand state
            if (rightHigh)
            {
                SetRightState(BlueColor, "Position Detected");
            }
            else if (rightTracked)
            {
                SetRightState(YellowColor, "Low Confidence");
            }
            else
            {
                SetRightState(RedColor, "Not Detected");
            }

            // Calibration progress
            if (leftHigh && rightHigh)
            {
                _highConfidenceTimer += Time.deltaTime;

                float remaining = _confirmationDuration - _highConfidenceTimer;
                if (_statusText != null)
                {
                    _statusText.text = $"Calibrating... hold steady {remaining:F1}s";
                }

                // TTS progress at halfway
                if (!_ttsCalibrationStarted && _highConfidenceTimer > _confirmationDuration * 0.5f)
                {
                    _ttsCalibrationStarted = true;
                    if (Audio.TTSVoiceGuide.Instance != null)
                    {
                        Audio.TTSVoiceGuide.Instance.SpeakCalibrationProgress();
                    }
                }

                // Countdown beeps each second
                float elapsed = _highConfidenceTimer;
                if (elapsed - _lastCountdownBeepTime >= 1f && Audio.UIAudioFeedback.Instance != null)
                {
                    _lastCountdownBeepTime = elapsed;
                    if (remaining <= 1f)
                    {
                        Audio.UIAudioFeedback.Instance.PlayCountdownFinal();
                    }
                    else
                    {
                        Audio.UIAudioFeedback.Instance.PlayCountdownBeep();
                    }
                }

                if (_highConfidenceTimer >= _confirmationDuration)
                {
                    _calibrationDone = true;

                    if (_statusText != null)
                    {
                        _statusText.text = "Calibration Complete! Starting session...";
                    }

                    // Audio feedback for calibration success
                    if (Audio.UIAudioFeedback.Instance != null)
                    {
                        Audio.UIAudioFeedback.Instance.PlaySuccess();
                    }

                    if (Audio.TTSVoiceGuide.Instance != null)
                    {
                        Audio.TTSVoiceGuide.Instance.SpeakCalibrationComplete();
                    }

                    OnCalibrationComplete?.Invoke();

                    if (SceneTransitionManager.Instance != null)
                    {
                        Audio.UIAudioFeedback.Instance?.PlayTransition();
                        SceneTransitionManager.Instance.TransitionToScene(RehabSessionScene);
                    }
                }
            }
            else if (leftTracked || rightTracked)
            {
                ResetTimer("Hold both hands steady in view...");
            }
            else
            {
                ResetTimer("Show both hands to begin calibration");
            }
        }

        private void SetLeftState(Color pillColor, string text)
        {
            if (_leftConfidenceIndicator != null)
            {
                _leftConfidenceIndicator.color = pillColor;
            }

            if (_leftStatusPill != null)
            {
                _leftStatusPill.color = pillColor;
            }

            if (_leftStatusText != null)
            {
                _leftStatusText.text = text;
            }
        }

        private void SetRightState(Color pillColor, string text)
        {
            if (_rightConfidenceIndicator != null)
            {
                _rightConfidenceIndicator.color = pillColor;
            }

            if (_rightStatusPill != null)
            {
                _rightStatusPill.color = pillColor;
            }

            if (_rightStatusText != null)
            {
                _rightStatusText.text = text;
            }
        }

        private void ResetTimer(string message)
        {
            _highConfidenceTimer = 0f;

            if (_statusText != null)
            {
                _statusText.text = message;
            }
        }
    }
}
