using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using AGVRSystem.Data;

namespace AGVRSystem.UI
{
    /// <summary>
    /// Displays post-session results on the report board:
    /// - Stat cards (duration, accuracy, grip) with icons
    /// - Per-exercise accuracy bar chart
    /// - Improvement indicator comparing to previous session
    /// - Exercise breakdown table (name / reps / accuracy / time)
    /// All text is clamped within the board via RectMask2D and overflow modes.
    /// Chart building is deferred one frame so Unity layout has been calculated.
    /// </summary>
    public class SessionSummaryUI : MonoBehaviour
    {
        [Header("Stat Cards")]
        [SerializeField] private TMP_Text _durationText;
        [SerializeField] private TMP_Text _accuracyText;
        [SerializeField] private TMP_Text _gripText;

        [Header("Stat Values (large)")]
        [SerializeField] private TMP_Text _durationValue;
        [SerializeField] private TMP_Text _accuracyValue;
        [SerializeField] private TMP_Text _gripValue;

        [Header("Stat Icons")]
        [SerializeField] private TMP_Text _durationIcon;
        [SerializeField] private TMP_Text _accuracyIcon;
        [SerializeField] private TMP_Text _gripIcon;

        [Header("Chart")]
        [SerializeField] private RectTransform _chartArea;
        [SerializeField] private TMP_Text _chartTitle;

        [Header("Improvement")]
        [SerializeField] private TMP_Text _improvementText;
        [SerializeField] private Image _improvementBG;

        [Header("Exercise Table")]
        [SerializeField] private Transform _exerciseTableParent;
        [SerializeField] private GameObject _exerciseRowPrefab;
        [SerializeField] private TMP_Text _tableHeader;

        [Header("Button")]
        [SerializeField] private Button _newSessionButton;

        [Header("Chart Colors")]
        [SerializeField] private Color _barColorHigh = new Color(0.35f, 0.7f, 0.5f, 0.9f);
        [SerializeField] private Color _barColorMid = new Color(0.85f, 0.72f, 0.35f, 0.9f);
        [SerializeField] private Color _barColorLow = new Color(0.8f, 0.35f, 0.3f, 0.9f);
        [SerializeField] private Color _barBgColor = new Color(0.12f, 0.13f, 0.17f, 0.5f);

        private const string CalibrationSceneName = "Calibration";
        private const float BarMaxHeight   = 75f;
        private const float MaxBarWidth    = 45f;
        private const float MinBarWidth    = 24f;
        private const float BarSpacing     = 6f;
        private const float LabelFontSize  = 8f;
        private const float ValueFontSize  = 7f;
        private const int   MaxChartBars   = 8;
        private const int   MaxTableRows   = 5;
        private const float TableRowHeight = 22f;
        private const float TableFontSize  = 9f;

        // Pending data passed to ShowSummary before layout is ready
        private SessionData _pendingData;
        private bool _layoutReady;

        private List<GameObject> _chartBars = new List<GameObject>();

        private void Awake()
        {
            if (_newSessionButton != null)
                _newSessionButton.onClick.AddListener(OnNewSession);

            AutoFindStatCardReferences();
            EnsureOverflowProtection();
        }

        private IEnumerator Start()
        {
            // Wait two frames so Unity finishes the layout pass and
            // rect dimensions are valid before we build the chart
            yield return null;
            yield return null;

            _layoutReady = true;

            if (_pendingData != null)
            {
                RenderSummary(_pendingData);
                _pendingData = null;
            }
        }

        /// <summary>
        /// Auto-discovers stat card TMP_Text references by traversing the hierarchy
        /// when Inspector references are not wired.
        /// </summary>
        private void AutoFindStatCardReferences()
        {
            AutoFindText(ref _durationValue, "DurationCard", "Value");
            AutoFindText(ref _durationText, "DurationCard", "Label");
            AutoFindText(ref _durationIcon, "DurationCard", "Icon");

            AutoFindText(ref _accuracyValue, "AccuracyCard", "Value");
            AutoFindText(ref _accuracyText, "AccuracyCard", "Label");
            AutoFindText(ref _accuracyIcon, "AccuracyCard", "Icon");

            AutoFindText(ref _gripValue, "GripCard", "Value");
            AutoFindText(ref _gripText, "GripCard", "Label");
            AutoFindText(ref _gripIcon, "GripCard", "Icon");
        }

        private void AutoFindText(ref TMP_Text field, string parentName, string childName)
        {
            if (field != null) return;

            Transform parent = FindChildByName(transform, parentName);
            if (parent == null) return;

            Transform child = parent.Find(childName);
            if (child != null)
                field = child.GetComponent<TMP_Text>();
        }

        private static Transform FindChildByName(Transform root, string name)
        {
            if (root == null) return null;
            foreach (Transform child in root)
            {
                if (child.name == name) return child;
                Transform found = FindChildByName(child, name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// Populates all summary fields with the completed session data.
        /// If the layout pass has not yet run the render is deferred to Start().
        /// </summary>
        public void ShowSummary(SessionData data)
        {
            if (data == null)
            {
                Debug.LogWarning("[SessionSummaryUI] No session data to display.");
                return;
            }

            if (!_layoutReady)
            {
                // Store and wait — Start() coroutine will call RenderSummary after layout
                _pendingData = data;
                return;
            }

            RenderSummary(data);
        }

        private void RenderSummary(SessionData data)
        {
            PopulateStatCards(data);
            BuildChart(data);
            PopulateExerciseTable(data);
            CalculateImprovement(data);
        }

        private void PopulateStatCards(SessionData data)
        {
            int minutes = (int)(data.totalDuration / 60f);
            int seconds = (int)(data.totalDuration % 60f);

            // Labels
            if (_durationText != null) _durationText.text  = "DURATION";
            if (_accuracyText != null) _accuracyText.text  = "ACCURACY";
            if (_gripText     != null) _gripText.text      = "AVG GRIP";

            // Values — show 0 for unrecorded grip rather than "0.0%"
            if (_durationValue != null) _durationValue.text = $"{minutes:D2}:{seconds:D2}";
            if (_accuracyValue != null) _accuracyValue.text = $"{data.overallAccuracy:F1}%";
            if (_gripValue     != null)
            {
                float grip = data.averageGripStrength;
                _gripValue.text = grip > 0.01f ? $"{grip:F1}%" : "--";
            }

            // Short word icons — visible even without an icon font
            if (_durationIcon != null) _durationIcon.text = "TIME";
            if (_accuracyIcon != null) _accuracyIcon.text = "ACC";
            if (_gripIcon     != null) _gripIcon.text     = "GRIP";
        }

        private void BuildChart(SessionData data)
        {
            if (_chartArea == null || data.exercises == null || data.exercises.Count == 0)
                return;

            // Clear old bars
            foreach (var bar in _chartBars)
            {
                if (bar != null) Destroy(bar);
            }
            _chartBars.Clear();

            if (_chartTitle != null)
                _chartTitle.text = "EXERCISE ACCURACY";

            // Clamp to max bars to prevent overflow
            int barCount = Mathf.Min(data.exercises.Count, MaxChartBars);

            // rect.width is valid here because ShowSummary defers until after layout
            float chartWidth     = _chartArea.rect.width;
            if (chartWidth <= 10f) chartWidth = 360f; // safe fallback
            float availableWidth = chartWidth - 24f;  // side padding
            float barWidth       = Mathf.Clamp(
                (availableWidth - (barCount - 1) * BarSpacing) / Mathf.Max(1, barCount),
                MinBarWidth, MaxBarWidth);

            float totalWidth = barCount * (barWidth + BarSpacing) - BarSpacing;
            float startX = -totalWidth * 0.5f;

            for (int i = 0; i < barCount; i++)
            {
                var ex = data.exercises[i];
                float accuracy = Mathf.Clamp01(ex.accuracy / 100f);
                float xPos = startX + i * (barWidth + BarSpacing) + barWidth * 0.5f;

                // Bar background
                CreateBarElement(_chartArea, $"BarBG_{i}",
                    new Vector2(xPos, BarMaxHeight * 0.5f),
                    new Vector2(barWidth, BarMaxHeight),
                    _barBgColor);

                // Bar fill
                float barHeight = Mathf.Max(accuracy * BarMaxHeight, 2f);
                Color barColor = accuracy >= 0.8f ? _barColorHigh :
                                 accuracy >= 0.5f ? _barColorMid : _barColorLow;

                CreateBarElement(_chartArea, $"BarFill_{i}",
                    new Vector2(xPos, barHeight * 0.5f),
                    new Vector2(barWidth - 4f, barHeight),
                    barColor);

                // Value label on top of bar
                CreateTextElement(_chartArea, $"BarVal_{i}",
                    new Vector2(xPos, barHeight + 6f),
                    new Vector2(barWidth + 8f, 12f),
                    $"{ex.accuracy:F0}%", ValueFontSize,
                    barColor, TextAlignmentOptions.Center);

                // Exercise name label below
                string shortName = ShortenName(ex.exerciseName);
                CreateTextElement(_chartArea, $"BarLabel_{i}",
                    new Vector2(xPos, -8f),
                    new Vector2(barWidth + 12f, 16f),
                    shortName, LabelFontSize,
                    new Color(0.6f, 0.6f, 0.65f, 1f), TextAlignmentOptions.Center);
            }
        }

        private void CreateBarElement(RectTransform parent, string name,
            Vector2 position, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;

            _chartBars.Add(go);
        }

        private void CreateTextElement(RectTransform parent, string name,
            Vector2 position, Vector2 size, string text, float fontSize,
            Color color, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.fontStyle = FontStyles.Bold;
            tmp.raycastTarget = false;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Ellipsis;

            _chartBars.Add(go);
        }

        private void CalculateImprovement(SessionData data)
        {
            if (_improvementText == null) return;

            float prevAccuracy    = PlayerPrefs.GetFloat("LastSessionAccuracy", -1f);
            float currentAccuracy = data.overallAccuracy; // stored as 0-100

            // Persist current session for the next comparison
            PlayerPrefs.SetFloat("LastSessionAccuracy", currentAccuracy);
            PlayerPrefs.Save();

            if (prevAccuracy < 0f)
            {
                _improvementText.text = "First session — great start!";
                if (_improvementBG != null)
                    _improvementBG.color = new Color(0.15f, 0.35f, 0.55f, 0.25f);
                return;
            }

            float delta = currentAccuracy - prevAccuracy;

            // Use 1 % as the threshold to distinguish meaningful change from noise
            if (delta > 1f)
            {
                _improvementText.text = $"+{delta:F1}% improvement from last session";
                if (_improvementBG != null)
                    _improvementBG.color = new Color(0.20f, 0.50f, 0.35f, 0.25f);
            }
            else if (delta < -1f)
            {
                _improvementText.text = $"{delta:F1}% from last session — keep practicing!";
                if (_improvementBG != null)
                    _improvementBG.color = new Color(0.60f, 0.45f, 0.20f, 0.25f);
            }
            else
            {
                _improvementText.text = "Consistent performance — well done!";
                if (_improvementBG != null)
                    _improvementBG.color = new Color(0.25f, 0.35f, 0.50f, 0.25f);
            }
        }

        private void PopulateExerciseTable(SessionData data)
        {
            if (_exerciseTableParent == null)
                return;

            // Clear existing rows
            for (int i = _exerciseTableParent.childCount - 1; i >= 0; i--)
            {
                Destroy(_exerciseTableParent.GetChild(i).gameObject);
            }

            if (_tableHeader != null)
                _tableHeader.text = "EXERCISE BREAKDOWN  —  Exercise  |  Reps  |  Acc%  |  Time";

            if (data.exercises == null || data.exercises.Count == 0)
                return;

            // Add RectMask2D to the table parent to clip any overflow
            var tableRect = _exerciseTableParent.GetComponent<RectTransform>();
            if (tableRect != null && _exerciseTableParent.GetComponent<RectMask2D>() == null)
            {
                _exerciseTableParent.gameObject.AddComponent<RectMask2D>();
            }

            int rowCount = Mathf.Min(data.exercises.Count, MaxTableRows);

            for (int i = 0; i < rowCount; i++)
            {
                var metrics = data.exercises[i];
                CreateCompactTableRow(tableRect, i, metrics);
            }

            // Show "+N more" if there are extra exercises beyond the limit
            if (data.exercises.Count > MaxTableRows)
            {
                int extra = data.exercises.Count - MaxTableRows;
                CreateCompactOverflowRow(tableRect, rowCount, extra);
            }
        }

        /// <summary>
        /// Creates a single compact exercise breakdown row: "Name  Reps  Acc%  Time"
        /// all on one line within a fixed 22px height.
        /// </summary>
        private void CreateCompactTableRow(RectTransform parent, int rowIndex, ExerciseMetrics metrics)
        {
            float yPos = -(rowIndex * TableRowHeight);

            // Row background (alternating subtle tint)
            var rowBG = new GameObject($"Row_{rowIndex}_BG", typeof(RectTransform), typeof(Image));
            rowBG.transform.SetParent(parent, false);
            var bgRect = rowBG.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0f, 1f);
            bgRect.anchorMax = new Vector2(1f, 1f);
            bgRect.pivot = new Vector2(0.5f, 1f);
            bgRect.anchoredPosition = new Vector2(0f, yPos);
            bgRect.sizeDelta = new Vector2(0f, TableRowHeight);
            var bgImg = rowBG.GetComponent<Image>();
            bgImg.color = rowIndex % 2 == 0
                ? new Color(0.08f, 0.09f, 0.12f, 0.3f)
                : new Color(0.10f, 0.11f, 0.14f, 0.2f);
            bgImg.raycastTarget = false;
            _chartBars.Add(rowBG);

            // Exercise name (left-aligned, 45% width)
            CreateTableCell(parent, $"Row_{rowIndex}_Name",
                new Vector2(0f, 1f), new Vector2(0.45f, 1f),
                new Vector2(0f, yPos), new Vector2(0f, TableRowHeight),
                ShortenName(metrics.exerciseName), TextAlignmentOptions.Left,
                new Color(0.78f, 0.8f, 0.84f, 1f));

            // Reps (center, 18% width)
            CreateTableCell(parent, $"Row_{rowIndex}_Reps",
                new Vector2(0.45f, 1f), new Vector2(0.63f, 1f),
                new Vector2(0f, yPos), new Vector2(0f, TableRowHeight),
                $"{metrics.repsCompleted}/{metrics.targetReps}", TextAlignmentOptions.Center,
                new Color(0.6f, 0.62f, 0.66f, 1f));

            // Accuracy (center, 20% width, colored)
            float acc = metrics.accuracy;
            Color accColor = acc >= 80f ? _barColorHigh :
                             acc >= 50f ? _barColorMid : _barColorLow;
            CreateTableCell(parent, $"Row_{rowIndex}_Acc",
                new Vector2(0.63f, 1f), new Vector2(0.82f, 1f),
                new Vector2(0f, yPos), new Vector2(0f, TableRowHeight),
                $"{acc:F0}%", TextAlignmentOptions.Center, accColor);

            // Duration (right-aligned, 18% width)
            CreateTableCell(parent, $"Row_{rowIndex}_Time",
                new Vector2(0.82f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, yPos), new Vector2(0f, TableRowHeight),
                $"{metrics.duration:F0}s", TextAlignmentOptions.Right,
                new Color(0.55f, 0.56f, 0.6f, 1f));
        }

        private void CreateTableCell(RectTransform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax,
            Vector2 position, Vector2 sizeDelta,
            string text, TextAlignmentOptions alignment, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = sizeDelta;

            // Inset left/right padding via offsetMin/offsetMax
            rect.offsetMin = new Vector2(rect.offsetMin.x + 4f, rect.offsetMin.y);
            rect.offsetMax = new Vector2(rect.offsetMax.x - 4f, rect.offsetMax.y);

            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = TableFontSize;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.fontStyle = FontStyles.Normal;
            tmp.raycastTarget = false;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.enableAutoSizing = false;
            tmp.margin = new Vector4(4f, 0f, 4f, 0f);

            _chartBars.Add(go);
        }

        private void CreateCompactOverflowRow(RectTransform parent, int rowIndex, int extraCount)
        {
            float yPos = -(rowIndex * TableRowHeight);
            CreateTableCell(parent, "Row_Overflow",
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, yPos), new Vector2(0f, TableRowHeight),
                $"+{extraCount} more exercises", TextAlignmentOptions.Center,
                new Color(0.5f, 0.52f, 0.55f, 0.6f));
        }

        /// <summary>
        /// Sets text with overflow protection to prevent text rendering outside the board.
        /// </summary>
        private void SetOverflowSafe(TMP_Text text, string value)
        {
            if (text == null) return;
            text.text = value;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.enableAutoSizing = false;
        }

        /// <summary>
        /// Adds RectMask2D to the canvas to clip any text or UI elements that overflow the board.
        /// </summary>
        private void EnsureOverflowProtection()
        {
            // Add RectMask2D to the canvas parent if it doesn't exist
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                var canvasRect = canvas.GetComponent<RectTransform>();
                if (canvasRect != null && canvasRect.GetComponent<RectMask2D>() == null)
                {
                    canvasRect.gameObject.AddComponent<RectMask2D>();
                    Debug.Log("[SessionSummaryUI] Added RectMask2D for overflow protection.");
                }
            }

            // Also add mask to chart area
            if (_chartArea != null && _chartArea.GetComponent<RectMask2D>() == null)
            {
                _chartArea.gameObject.AddComponent<RectMask2D>();
            }
        }

        private string ShortenName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            if (name.Contains("Grip"))            return "Grip Hold";
            if (name.Contains("PinchHoldSphere")) return "Pinch Sphere";
            if (name.Contains("Tap"))             return "Tapping";
            if (name.Contains("Pinch"))           return "Pinching";
            if (name.Contains("Spread"))          return "Spreading";
            if (name.Contains("Thumb"))           return "Thumb Opp";
            return name.Length > 12 ? name.Substring(0, 12) : name;
        }

        private void OnNewSession()
        {
            SceneManager.LoadScene(CalibrationSceneName);
        }

        private void OnDestroy()
        {
            if (_newSessionButton != null)
            {
                _newSessionButton.onClick.RemoveListener(OnNewSession);
            }

            foreach (var bar in _chartBars)
            {
                if (bar != null) Destroy(bar);
            }
            _chartBars.Clear();
        }
    }
}
