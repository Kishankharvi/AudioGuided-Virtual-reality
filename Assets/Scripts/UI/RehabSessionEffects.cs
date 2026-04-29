using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AGVRSystem.UI
{
    /// <summary>
    /// Adds ambient effects and animations to the RehabSession scene:
    /// - Board entrance slide-in animations with fade
    /// - Gentle board hover/bob
    /// - HUD accent bar pulse
    /// - Exercise info accent bar shimmer
    /// - Hold progress bar glow when active
    /// - Feedback text pop animation
    /// - Directional light warm cycling
    /// - Table spotlight
    /// Attach to the Managers GameObject.
    /// </summary>
    public class RehabSessionEffects : MonoBehaviour
    {
        [Header("Boards")]
        [SerializeField] private Transform _hudBoard;
        [SerializeField] private Transform _infoBoard;
        [SerializeField] private Transform _reportBoard;

        [Header("Canvas Groups")]
        [SerializeField] private CanvasGroup _hudCanvasGroup;
        [SerializeField] private CanvasGroup _infoCanvasGroup;

        [Header("Accent Elements")]
        [SerializeField] private Graphic _infoAccentBar;
        [SerializeField] private Graphic _reportAccent;
        [SerializeField] private Graphic _holdProgressFill;

        [Header("Feedback")]
        [SerializeField] private TMP_Text _feedbackText;

        [Header("Lighting")]
        [SerializeField] private Light _directionalLight;

        [Header("Board Bob Settings")]
        [SerializeField] private float _bobAmplitude = 0.005f;
        [SerializeField] private float _bobSpeed = 0.6f;

        [Header("Entrance Animation")]
        [SerializeField] private float _entranceDuration = 1.0f;
        [SerializeField] private float _entranceStagger = 0.2f;

        // Cached
        private Vector3 _hudStartPos;
        private Vector3 _infoStartPos;
        private Vector3 _reportStartPos;
        private Color _lightBaseColor;
        private float _lightBaseIntensity;
        private bool _entranceComplete;
        private float _time;

        // Feedback pop
        private float _feedbackPopTimer;
        private string _lastFeedbackText = "";

        private void Start()
        {
            CacheValues();
            StartCoroutine(PlayEntranceSequence());
        }

        private void CacheValues()
        {
            if (_hudBoard != null) _hudStartPos = _hudBoard.localPosition;
            if (_infoBoard != null) _infoStartPos = _infoBoard.localPosition;
            if (_reportBoard != null) _reportStartPos = _reportBoard.localPosition;
            if (_directionalLight != null)
            {
                _lightBaseColor = _directionalLight.color;
                _lightBaseIntensity = _directionalLight.intensity;
            }

            if (_hudCanvasGroup != null) _hudCanvasGroup.alpha = 0f;
            if (_infoCanvasGroup != null) _infoCanvasGroup.alpha = 0f;
        }

        private void Update()
        {
            _time += Time.smoothDeltaTime;

            if (_entranceComplete)
            {
                AnimateBoardBob();
            }

            AnimateAccentShimmer();
            AnimateProgressGlow();
            AnimateFeedbackPop();
            AnimateLightCycle();
        }

        // ===== ENTRANCE =====

        private IEnumerator PlayEntranceSequence()
        {
            yield return new WaitForSeconds(0.2f);

            // HUD slides down from above
            StartCoroutine(AnimateBoardEntrance(
                _hudBoard, _hudStartPos, Vector3.up * 0.1f,
                _hudCanvasGroup, _entranceDuration));

            yield return new WaitForSeconds(_entranceStagger);

            // Info board slides in from right
            StartCoroutine(AnimateBoardEntrance(
                _infoBoard, _infoStartPos, Vector3.right * 0.12f,
                _infoCanvasGroup, _entranceDuration));

            yield return new WaitForSeconds(_entranceDuration);
            _entranceComplete = true;
        }

        private IEnumerator AnimateBoardEntrance(
            Transform board, Vector3 targetPos, Vector3 offset,
            CanvasGroup cg, float duration)
        {
            if (board == null) yield break;

            Vector3 startPos = targetPos + offset;
            board.localPosition = startPos;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float eased = 1f - (1f - t) * (1f - t) * (1f - t);

                board.localPosition = Vector3.Lerp(startPos, targetPos, eased);
                if (cg != null) cg.alpha = eased;
                yield return null;
            }

            board.localPosition = targetPos;
            if (cg != null) cg.alpha = 1f;
        }

        // ===== BOARD BOB =====

        private void AnimateBoardBob()
        {
            if (_hudBoard != null)
            {
                float y = Mathf.Sin(_time * _bobSpeed) * _bobAmplitude;
                _hudBoard.localPosition = _hudStartPos + Vector3.up * y;
            }

            if (_infoBoard != null)
            {
                float y = Mathf.Sin((_time + 1.8f) * _bobSpeed) * _bobAmplitude;
                _infoBoard.localPosition = _infoStartPos + Vector3.up * y;
            }
        }

        // ===== ACCENT SHIMMER =====

        private void AnimateAccentShimmer()
        {
            if (_infoAccentBar != null)
            {
                float t = (Mathf.Sin(_time * 1.2f) + 1f) * 0.5f;
                Color c = _infoAccentBar.color;
                c.a = Mathf.Lerp(0.5f, 0.9f, t);
                _infoAccentBar.color = c;
            }

            if (_reportAccent != null)
            {
                float t = (Mathf.Sin(_time * 0.9f + 0.7f) + 1f) * 0.5f;
                Color c = _reportAccent.color;
                c.a = Mathf.Lerp(0.3f, 0.7f, t);
                _reportAccent.color = c;
            }
        }

        // ===== PROGRESS GLOW =====

        private void AnimateProgressGlow()
        {
            if (_holdProgressFill == null) return;

            Color c = _holdProgressFill.color;
            float pulse = (Mathf.Sin(_time * 3f) + 1f) * 0.5f;
            float brightness = Mathf.Lerp(0.85f, 1.15f, pulse);
            c.r = Mathf.Clamp01(c.r * brightness);
            c.g = Mathf.Clamp01(c.g * brightness);
            c.b = Mathf.Clamp01(c.b * brightness);
            _holdProgressFill.color = c;
        }

        // ===== FEEDBACK POP =====

        private void AnimateFeedbackPop()
        {
            if (_feedbackText == null) return;

            string currentText = _feedbackText.text;
            if (currentText != _lastFeedbackText && !string.IsNullOrEmpty(currentText))
            {
                _lastFeedbackText = currentText;
                _feedbackPopTimer = 0.3f;
                _feedbackText.transform.localScale = Vector3.one * 1.15f;
            }

            if (_feedbackPopTimer > 0f)
            {
                _feedbackPopTimer -= Time.deltaTime;
                float t = Mathf.Clamp01(_feedbackPopTimer / 0.3f);
                float scale = Mathf.Lerp(1f, 1.15f, t);
                _feedbackText.transform.localScale = Vector3.one * scale;
            }
        }

        // ===== LIGHT CYCLING =====

        private void AnimateLightCycle()
        {
            if (_directionalLight == null) return;

            // Very slow, narrow breathing — period ~80 s, ±3 % intensity
            // SmoothStep removes inflection-point micro-stutters in VR
            float t       = (Mathf.Sin(_time * 0.04f * Mathf.PI) + 1f) * 0.5f;
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            _directionalLight.intensity = Mathf.Lerp(
                _lightBaseIntensity * 0.97f,
                _lightBaseIntensity * 1.03f,
                smoothT);

            // Keep colour stable — never shift hue or ambient per-frame (primary flicker source)
            _directionalLight.color = _lightBaseColor;

            // RenderSettings.ambientEquatorColor intentionally NOT changed here;
            // per-frame ambient changes cause visible flicker in VR due to indirect
            // lighting recalculation on every frame.
        }
    }
}
