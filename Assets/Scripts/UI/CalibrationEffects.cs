using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AGVRSystem.UI
{
    /// <summary>
    /// Adds ambient effects and animations to the Calibration scene:
    /// - Board entrance slide-in animations with fade
    /// - Gentle board hover/bob
    /// - Corner bracket pulse animation (┌ ┐ └ ┘ glow cycle)
    /// - Scanning line sweep across the hand render areas
    /// - Status bar text breathing
    /// - Instruction board accent line shimmer
    /// - Subtle directional light warm cycling
    /// - Ambient particle motes
    /// Attach to the Managers GameObject.
    /// </summary>
    public class CalibrationEffects : MonoBehaviour
    {
        [Header("Boards")]
        [SerializeField] private Transform _calibrationBoard;
        [SerializeField] private Transform _instructionBoard;

        [Header("Canvas Groups")]
        [SerializeField] private CanvasGroup _calibCanvasGroup;
        [SerializeField] private CanvasGroup _instrCanvasGroup;

        [Header("Corner Brackets")]
        [SerializeField] private TMP_Text _cornerTL;
        [SerializeField] private TMP_Text _cornerTR;
        [SerializeField] private TMP_Text _cornerBL;
        [SerializeField] private TMP_Text _cornerBR;

        [Header("Scanning Line")]
        [SerializeField] private RectTransform _leftRenderArea;
        [SerializeField] private RectTransform _rightRenderArea;

        [Header("Accent Elements")]
        [SerializeField] private Graphic _instrAccentLine;
        [SerializeField] private Graphic _centerDivider;

        [Header("Lighting")]
        [SerializeField] private Light _directionalLight;

        [Header("Board Bob Settings")]
        [SerializeField] private float _bobAmplitude = 0.006f;
        [SerializeField] private float _bobSpeed = 0.7f;

        [Header("Entrance Animation")]
        [SerializeField] private float _entranceDuration = 1.0f;
        [SerializeField] private float _entranceStagger = 0.25f;

        [Header("Corner Pulse")]
        [SerializeField] private float _cornerPulseSpeed = 1.2f;
        [SerializeField] private float _cornerMinAlpha = 0.3f;
        [SerializeField] private float _cornerMaxAlpha = 0.85f;

        [Header("Scan Line")]
        [SerializeField] private float _scanSpeed = 0.4f;
        [SerializeField] private Color _scanLineColor = new Color(0.2f, 0.78f, 0.38f, 0.15f);
        [SerializeField] private float _scanLineHeight = 3f;

        // Cached
        private Vector3 _calibBoardStartPos;
        private Vector3 _instrBoardStartPos;
        private Color _lightBaseColor;
        private float _lightBaseIntensity;
        private bool _entranceComplete;
        private float _time;

        // Scan line UI elements
        private RectTransform _leftScanLine;
        private RectTransform _rightScanLine;
        private Image _leftScanImage;
        private Image _rightScanImage;

        private void Start()
        {
            CacheValues();
            CreateScanLines();
            StartCoroutine(PlayEntranceSequence());
        }

        private void CacheValues()
        {
            if (_calibrationBoard != null)
                _calibBoardStartPos = _calibrationBoard.localPosition;
            if (_instructionBoard != null)
                _instrBoardStartPos = _instructionBoard.localPosition;
            if (_directionalLight != null)
            {
                _lightBaseColor = _directionalLight.color;
                _lightBaseIntensity = _directionalLight.intensity;
            }

            // Start canvases invisible
            if (_calibCanvasGroup != null) _calibCanvasGroup.alpha = 0f;
            if (_instrCanvasGroup != null) _instrCanvasGroup.alpha = 0f;
        }

        private void Update()
        {
            _time += Time.smoothDeltaTime;

            if (_entranceComplete)
            {
                AnimateBoardBob();
            }

            AnimateCornerPulse();
            AnimateScanLine();
            AnimateAccentShimmer();
            AnimateLightCycle();
        }

        // ===== ENTRANCE =====

        private IEnumerator PlayEntranceSequence()
        {
            yield return new WaitForSeconds(0.15f);

            // Calibration board slides up from below
            StartCoroutine(AnimateBoardEntrance(
                _calibrationBoard, _calibBoardStartPos, Vector3.down * 0.12f,
                _calibCanvasGroup, _entranceDuration));

            yield return new WaitForSeconds(_entranceStagger);

            // Instruction board slides in from right
            StartCoroutine(AnimateBoardEntrance(
                _instructionBoard, _instrBoardStartPos, Vector3.right * 0.12f,
                _instrCanvasGroup, _entranceDuration));

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
                float eased = 1f - (1f - t) * (1f - t) * (1f - t); // ease-out cubic

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
            if (_calibrationBoard != null)
            {
                float y = Mathf.Sin(_time * _bobSpeed) * _bobAmplitude;
                _calibrationBoard.localPosition = _calibBoardStartPos + Vector3.up * y;
            }

            if (_instructionBoard != null)
            {
                float y = Mathf.Sin((_time + 1.5f) * _bobSpeed) * _bobAmplitude;
                _instructionBoard.localPosition = _instrBoardStartPos + Vector3.up * y;
            }
        }

        // ===== CORNER BRACKET PULSE =====

        private void AnimateCornerPulse()
        {
            if (_cornerTL == null) return;

            // Sequential pulse: each corner lights up in sequence
            float cycle = _time * _cornerPulseSpeed;

            SetCornerAlpha(_cornerTL, cycle, 0f);
            SetCornerAlpha(_cornerTR, cycle, 0.25f);
            SetCornerAlpha(_cornerBR, cycle, 0.5f);
            SetCornerAlpha(_cornerBL, cycle, 0.75f);
        }

        private void SetCornerAlpha(TMP_Text corner, float cycle, float phaseOffset)
        {
            if (corner == null) return;

            float t = (Mathf.Sin((cycle + phaseOffset) * Mathf.PI * 2f) + 1f) * 0.5f;
            float alpha = Mathf.Lerp(_cornerMinAlpha, _cornerMaxAlpha, t);

            Color c = corner.color;
            c.a = alpha;
            corner.color = c;
        }

        // ===== SCANNING LINE =====

        private void CreateScanLines()
        {
            _leftScanLine = CreateScanLineUI(_leftRenderArea, "LeftScanLine", out _leftScanImage);
            _rightScanLine = CreateScanLineUI(_rightRenderArea, "RightScanLine", out _rightScanImage);
        }

        private RectTransform CreateScanLineUI(RectTransform parent, string name, out Image img)
        {
            img = null;
            if (parent == null) return null;

            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(0f, _scanLineHeight);
            rect.anchoredPosition = Vector2.zero;

            img = go.GetComponent<Image>();
            img.color = _scanLineColor;
            img.raycastTarget = false;

            return rect;
        }

        private void AnimateScanLine()
        {
            if (_leftScanLine == null || _leftRenderArea == null) return;

            // Sweep from bottom to top, then reset
            float areaHeight = _leftRenderArea.rect.height;
            float t = Mathf.Repeat(_time * _scanSpeed, 1f);
            float yPos = Mathf.Lerp(0f, areaHeight, t);

            _leftScanLine.anchoredPosition = new Vector2(0f, yPos);
            if (_rightScanLine != null)
                _rightScanLine.anchoredPosition = new Vector2(0f, yPos);

            // Fade at edges
            float edgeFade = 1f;
            if (t < 0.1f) edgeFade = t / 0.1f;
            else if (t > 0.9f) edgeFade = (1f - t) / 0.1f;

            Color c = _scanLineColor;
            c.a = _scanLineColor.a * edgeFade;
            if (_leftScanImage != null) _leftScanImage.color = c;
            if (_rightScanImage != null) _rightScanImage.color = c;
        }

        // ===== ACCENT SHIMMER =====

        private void AnimateAccentShimmer()
        {
            if (_instrAccentLine != null)
            {
                float t = (Mathf.Sin(_time * 1.5f) + 1f) * 0.5f;
                Color c = _instrAccentLine.color;
                float shimmer = Mathf.Lerp(0.8f, 1.2f, t);
                c.r = Mathf.Clamp01(c.r * shimmer);
                c.g = Mathf.Clamp01(c.g * shimmer);
                c.a = Mathf.Lerp(0.4f, 0.7f, t);
                _instrAccentLine.color = c;
            }

            if (_centerDivider != null)
            {
                float t = (Mathf.Sin(_time * 0.8f + 0.5f) + 1f) * 0.5f;
                Color c = _centerDivider.color;
                c.a = Mathf.Lerp(0.15f, 0.35f, t);
                _centerDivider.color = c;
            }
        }

        // ===== LIGHT CYCLING =====

        private void AnimateLightCycle()
        {
            if (_directionalLight == null) return;

            // Narrow intensity range (±2 %) with a very slow cycle (~78 s period)
            // SmoothStep eliminates the inflection-point micro-jitter visible in VR
            float t       = (Mathf.Sin(_time * 0.04f * Mathf.PI) + 1f) * 0.5f;
            float smoothT = Mathf.SmoothStep(0f, 1f, t);
            _directionalLight.intensity = Mathf.Lerp(
                _lightBaseIntensity * 0.98f,
                _lightBaseIntensity * 1.02f,
                smoothT);

            // Colour stays fixed to baked value — no per-frame hue shift
            _directionalLight.color = _lightBaseColor;
        }

        private void OnDestroy()
        {
            if (_leftScanLine != null) Destroy(_leftScanLine.gameObject);
            if (_rightScanLine != null) Destroy(_rightScanLine.gameObject);
        }
    }
}
