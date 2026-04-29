using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AGVRSystem.UI
{
    /// <summary>
    /// Adds ambient effects and animations to the MainMenu scene:
    /// - Board hover/bob animations
    /// - UI entrance slide-in animations
    /// - Button pulse glow
    /// - Accent line shimmer
    /// - Subtle directional light warm cycling
    /// Attach to the Managers GameObject.
    /// </summary>
    public class MainMenuEffects : MonoBehaviour
    {
        [Header("Boards")]
        [SerializeField] private Transform _aboutBoard;
        [SerializeField] private Transform _sessionBoard;

        [Header("UI Elements")]
        [SerializeField] private CanvasGroup _aboutCanvasGroup;
        [SerializeField] private CanvasGroup _sessionCanvasGroup;
        [SerializeField] private Graphic _startButtonBG;
        [SerializeField] private Graphic _accentLine;

        [Header("Lighting")]
        [SerializeField] private Light _directionalLight;

        [Header("Board Bob Settings")]
        [SerializeField] private float _bobAmplitude = 0.008f;
        [SerializeField] private float _bobSpeed = 0.8f;
        [SerializeField] private float _bobPhaseOffset = 1.2f;

        [Header("Entrance Animation")]
        [SerializeField] private float _entranceDuration = 1.2f;
        [SerializeField] private float _entranceDelay = 0.3f;
        [SerializeField] private float _slideDistance = 0.15f;

        [Header("Button Pulse")]
        [SerializeField] private float _pulseSpeed = 1.5f;
        [SerializeField] private float _pulseMinAlpha = 0.7f;
        [SerializeField] private float _pulseMaxAlpha = 1.0f;

        [Header("Light Cycling")]
        [SerializeField] private float _lightCycleSpeed = 0.04f;   // very slow — ~80 s period
        [SerializeField] private float _lightIntensityMin = 0.97f; // narrow range to avoid flicker
        [SerializeField] private float _lightIntensityMax = 1.03f;

        // Cached original positions
        private Vector3 _aboutBoardStartPos;
        private Vector3 _sessionBoardStartPos;
        private Color _startButtonBaseColor;
        private Color _accentLineBaseColor;
        private Color _lightBaseColor;
        private float _lightBaseIntensity;

        // State
        private bool _entranceComplete;
        private float _timeAccumulator;

        private void Start()
        {
            CacheOriginalValues();
            StartCoroutine(PlayEntranceSequence());
        }

        private void CacheOriginalValues()
        {
            if (_aboutBoard != null)
                _aboutBoardStartPos = _aboutBoard.localPosition;

            if (_sessionBoard != null)
                _sessionBoardStartPos = _sessionBoard.localPosition;

            if (_startButtonBG != null)
                _startButtonBaseColor = _startButtonBG.color;

            if (_accentLine != null)
                _accentLineBaseColor = _accentLine.color;

            if (_directionalLight != null)
            {
                _lightBaseColor = _directionalLight.color;
                _lightBaseIntensity = _directionalLight.intensity;
            }

            // Start canvases invisible for entrance animation
            if (_aboutCanvasGroup != null)
            {
                _aboutCanvasGroup.alpha = 0f;
            }

            if (_sessionCanvasGroup != null)
            {
                _sessionCanvasGroup.alpha = 0f;
            }
        }

        private void Update()
        {
            // Use smoothDeltaTime so sudden frame spikes don't cause a visible intensity jump
            _timeAccumulator += Time.smoothDeltaTime;

            AnimateBoardBob();
            AnimateButtonPulse();
            AnimateAccentShimmer();
            AnimateLightCycle();
        }

        // ===== BOARD BOB =====

        private void AnimateBoardBob()
        {
            if (!_entranceComplete)
                return;

            if (_aboutBoard != null)
            {
                float yOffset = Mathf.Sin(_timeAccumulator * _bobSpeed) * _bobAmplitude;
                _aboutBoard.localPosition = _aboutBoardStartPos + Vector3.up * yOffset;
            }

            if (_sessionBoard != null)
            {
                float yOffset = Mathf.Sin((_timeAccumulator + _bobPhaseOffset) * _bobSpeed) * _bobAmplitude;
                _sessionBoard.localPosition = _sessionBoardStartPos + Vector3.up * yOffset;
            }
        }

        // ===== BUTTON PULSE =====

        private void AnimateButtonPulse()
        {
            if (_startButtonBG == null || !_entranceComplete)
                return;

            float t = (Mathf.Sin(_timeAccumulator * _pulseSpeed * Mathf.PI) + 1f) * 0.5f;
            float alpha = Mathf.Lerp(_pulseMinAlpha, _pulseMaxAlpha, t);

            Color c = _startButtonBaseColor;
            // Slightly brighten the color on pulse peak
            float brightness = Mathf.Lerp(1f, 1.25f, t);
            c.r = Mathf.Clamp01(_startButtonBaseColor.r * brightness);
            c.g = Mathf.Clamp01(_startButtonBaseColor.g * brightness);
            c.b = Mathf.Clamp01(_startButtonBaseColor.b * brightness);
            c.a = alpha;
            _startButtonBG.color = c;
        }

        // ===== ACCENT LINE SHIMMER =====

        private void AnimateAccentShimmer()
        {
            if (_accentLine == null || !_entranceComplete)
                return;

            // Gentle hue shift shimmer
            float t = (Mathf.Sin(_timeAccumulator * 1.2f) + 1f) * 0.5f;
            Color c = _accentLineBaseColor;
            float shimmer = Mathf.Lerp(0.85f, 1.15f, t);
            c.r = Mathf.Clamp01(_accentLineBaseColor.r * shimmer);
            c.g = Mathf.Clamp01(_accentLineBaseColor.g * shimmer);
            c.b = Mathf.Clamp01(_accentLineBaseColor.b * shimmer);
            _accentLine.color = c;
        }

        // ===== LIGHT CYCLING =====

        private void AnimateLightCycle()
        {
            if (_directionalLight == null)
                return;

            // Very slow, smooth intensity breathing — no colour manipulation to prevent flicker
            float t = (Mathf.Sin(_timeAccumulator * _lightCycleSpeed * Mathf.PI) + 1f) * 0.5f;
            // SmoothStep removes the inflection-point micro-stutters visible in VR headsets
            float smoothT = Mathf.SmoothStep(0f, 1f, t);
            _directionalLight.intensity = Mathf.Lerp(_lightIntensityMin, _lightIntensityMax, smoothT);

            // Keep light colour locked to baked value — never shift hue per-frame
            _directionalLight.color = _lightBaseColor;
        }

        // ===== ENTRANCE SEQUENCE =====

        private IEnumerator PlayEntranceSequence()
        {
            // Brief initial delay
            yield return new WaitForSeconds(0.2f);

            // Slide in AboutBoard from left + fade canvas
            StartCoroutine(AnimateBoardEntrance(
                _aboutBoard, _aboutBoardStartPos, Vector3.left * _slideDistance,
                _aboutCanvasGroup, _entranceDuration));

            // Staggered delay for SessionBoard
            yield return new WaitForSeconds(_entranceDelay);

            // Slide in SessionBoard from right + fade canvas
            StartCoroutine(AnimateBoardEntrance(
                _sessionBoard, _sessionBoardStartPos, Vector3.right * _slideDistance,
                _sessionCanvasGroup, _entranceDuration));

            // Wait for all to finish
            yield return new WaitForSeconds(_entranceDuration);

            _entranceComplete = true;
        }

        private IEnumerator AnimateBoardEntrance(
            Transform board, Vector3 targetPos, Vector3 offsetDir,
            CanvasGroup canvasGroup, float duration)
        {
            if (board == null)
                yield break;

            Vector3 startPos = targetPos + offsetDir;
            board.localPosition = startPos;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                // Ease-out cubic
                float eased = 1f - (1f - t) * (1f - t) * (1f - t);

                board.localPosition = Vector3.Lerp(startPos, targetPos, eased);

                if (canvasGroup != null)
                {
                    canvasGroup.alpha = eased;
                }

                yield return null;
            }

            board.localPosition = targetPos;
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
            }
        }
    }
}
