using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AGVRSystem.Exercises;

namespace AGVRSystem.UI
{
    /// <summary>
    /// Draws a procedural hand diagram on UI with real-time angle arcs between fingers.
    /// Renders both left and right hand visualizations side-by-side with live
    /// spread angle labels sourced from FingerSpreadingExercise or OVRSkeleton data.
    /// Shows finger ray lines from wrist to each fingertip with colored arc indicators
    /// and degree labels between adjacent finger pairs.
    /// </summary>
    public class FingerSpreadAngleOverlay : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("Container RectTransform for the left hand diagram.")]
        [SerializeField] private RectTransform _leftHandArea;

        [Tooltip("Container RectTransform for the right hand diagram.")]
        [SerializeField] private RectTransform _rightHandArea;

        [Header("Hand Source")]
        [SerializeField] private OVRSkeleton _leftSkeleton;
        [SerializeField] private OVRSkeleton _rightSkeleton;
        [SerializeField] private OVRHand _leftHand;
        [SerializeField] private OVRHand _rightHand;

        [Header("Exercise Reference")]
        [Tooltip("Optional: reads thresholds to color angles green/red.")]
        [SerializeField] private FingerSpreadingExercise _spreadExercise;

        [Header("Appearance")]
        [SerializeField] private Color _fingerRayColor = new Color(0.5f, 0.6f, 0.7f, 0.5f);
        [SerializeField] private Color _anglePassColor = new Color(0.3f, 0.85f, 0.45f, 1f);
        [SerializeField] private Color _angleFailColor = new Color(0.85f, 0.4f, 0.3f, 0.8f);
        [SerializeField] private Color _anglePendingColor = new Color(0.7f, 0.7f, 0.7f, 0.6f);
        [SerializeField] private float _rayLineWidth = 2f;
        [SerializeField] private float _arcLineWidth = 1.5f;
        [SerializeField] private float _projectionScale = 1200f;
        [SerializeField] private float _verticalOffset = -30f;
        [SerializeField] private float _arcRadius = 40f;
        [SerializeField] private int _arcSegments = 12;

        [Header("Label")]
        [SerializeField] private float _labelFontSize = 11f;
        [SerializeField] private float _labelOffset = 18f;

        [Header("Smoothing")]
        [SerializeField] private float _smoothing = 8f;

        // Bone indices for finger MCP joints + thumb base + wrist
        private const int WristIdx = 0;
        private const int ThumbIdx = 2;  // Hand_Thumb0
        private const int IndexIdx = 6;  // Hand_Index1
        private const int MiddleIdx = 9;  // Hand_Middle1
        private const int RingIdx = 12;   // Hand_Ring1
        private const int PinkyIdx = 15;  // Hand_Pinky0
        private const int MaxBones = 24;

        private const int AnglePairCount = 4;
        private const float DiscoveryInterval = 0.5f;
        private const float MaxDiscoveryTime = 15f;

        /// <summary>Finger pair names for display.</summary>
        private static readonly string[] PairLabels = { "I-M", "M-R", "R-P", "T-I" };

        /// <summary>Default thresholds when no exercise reference is available.</summary>
        private static readonly float[] DefaultThresholds = { 10f, 7f, 8f, 20f };

        /// <summary>Finger base bone indices in order: Thumb, Index, Middle, Ring, Pinky.</summary>
        private static readonly int[] FingerBases = { ThumbIdx, IndexIdx, MiddleIdx, RingIdx, PinkyIdx };

        private HandDiagramData _leftDiagram;
        private HandDiagramData _rightDiagram;

        private float _discoveryTimer;
        private float _discoveryElapsed;

        private struct HandDiagramData
        {
            public GameObject Root;
            public RectTransform[] FingerRays;       // 5 finger ray lines
            public Image[] FingerRayImages;
            public RectTransform[] ArcLines;          // 4 arc line containers
            public RectTransform[][] ArcSegments;     // segments per arc
            public Image[][] ArcSegmentImages;
            public TMP_Text[] AngleLabels;            // 4 angle text labels
            public RectTransform WristDot;
            public RectTransform[] FingerDots;        // 5 finger tip dots
            public Vector2[] SmoothedPositions;       // smoothed bone positions
            public bool IsInitialized;
        }

        private void Update()
        {
            TryDiscoverReferences();

            UpdateHandDiagram(_leftSkeleton, _leftHand, _leftHandArea,
                ref _leftDiagram, "LeftSpreadDiagram", false);
            UpdateHandDiagram(_rightSkeleton, _rightHand, _rightHandArea,
                ref _rightDiagram, "RightSpreadDiagram", true);
        }

        /// <summary>
        /// Enables or disables the overlay visibility.
        /// </summary>
        public void SetVisible(bool visible)
        {
            if (_leftDiagram.Root != null)
                _leftDiagram.Root.SetActive(visible);
            if (_rightDiagram.Root != null)
                _rightDiagram.Root.SetActive(visible);

            enabled = visible;
        }

        private void TryDiscoverReferences()
        {
            if (_discoveryElapsed >= MaxDiscoveryTime) return;
            if (_leftSkeleton != null && _rightSkeleton != null &&
                _leftHand != null && _rightHand != null) return;

            _discoveryTimer += Time.deltaTime;
            _discoveryElapsed += Time.deltaTime;
            if (_discoveryTimer < DiscoveryInterval) return;
            _discoveryTimer = 0f;

            if (HandTrackingManager.Instance != null)
            {
                if (_leftSkeleton == null) _leftSkeleton = HandTrackingManager.Instance.LeftSkeleton;
                if (_rightSkeleton == null) _rightSkeleton = HandTrackingManager.Instance.RightSkeleton;
                if (_leftHand == null) _leftHand = HandTrackingManager.Instance.LeftHand;
                if (_rightHand == null) _rightHand = HandTrackingManager.Instance.RightHand;
            }

            if (_spreadExercise == null)
                _spreadExercise = FindAnyObjectByType<FingerSpreadingExercise>();
        }

        private void UpdateHandDiagram(
            OVRSkeleton skeleton, OVRHand hand, RectTransform area,
            ref HandDiagramData diagram, string rootName, bool mirrorX)
        {
            if (area == null) return;

            bool isTracked = skeleton != null
                && skeleton.IsInitialized
                && skeleton.Bones != null
                && skeleton.Bones.Count > 0
                && hand != null
                && hand.IsTracked;

            if (!isTracked)
            {
                if (diagram.Root != null)
                    SetDiagramAlpha(ref diagram, 0.15f);
                return;
            }

            int boneCount = Mathf.Min(skeleton.Bones.Count, MaxBones);

            if (!diagram.IsInitialized)
                InitializeDiagram(area, ref diagram, rootName);

            SetDiagramAlpha(ref diagram, 1f);

            // Project key bone positions to 2D
            Vector2[] projected = ProjectKeyBones(skeleton, boneCount, mirrorX);

            // Smooth
            if (diagram.SmoothedPositions == null || diagram.SmoothedPositions.Length != projected.Length)
            {
                diagram.SmoothedPositions = (Vector2[])projected.Clone();
            }
            else
            {
                float alpha = 1f - Mathf.Exp(-_smoothing * Time.deltaTime);
                for (int i = 0; i < projected.Length; i++)
                    diagram.SmoothedPositions[i] = Vector2.Lerp(diagram.SmoothedPositions[i], projected[i], alpha);
            }

            Vector2 wristPos = diagram.SmoothedPositions[0];
            // Positions: [wrist, thumb, index, middle, ring, pinky]

            // Update wrist dot
            if (diagram.WristDot != null)
                diagram.WristDot.anchoredPosition = wristPos;

            // Update finger rays and dots
            for (int i = 0; i < FingerBases.Length; i++)
            {
                Vector2 fingerPos = diagram.SmoothedPositions[i + 1]; // +1 because [0] is wrist

                if (diagram.FingerRays[i] != null)
                    UpdateLine(diagram.FingerRays[i], wristPos, fingerPos);

                if (diagram.FingerDots[i] != null)
                    diagram.FingerDots[i].anchoredPosition = fingerPos;
            }

            // Compute and display angles between adjacent finger pairs
            float[] angles = ComputeSpreadAngles(skeleton);
            float[] thresholds = GetCurrentThresholds();

            // Pairs: [Index-Middle, Middle-Ring, Ring-Pinky, Thumb-Index]
            // Smoothed positions: [wrist(0), thumb(1), index(2), middle(3), ring(4), pinky(5)]
            int[][] pairIndices = {
                new[] { 2, 3 }, // Index-Middle
                new[] { 3, 4 }, // Middle-Ring
                new[] { 4, 5 }, // Ring-Pinky
                new[] { 1, 2 }, // Thumb-Index
            };

            for (int p = 0; p < AnglePairCount; p++)
            {
                int fromFinger = pairIndices[p][0];
                int toFinger = pairIndices[p][1];

                Vector2 dir1 = (diagram.SmoothedPositions[fromFinger] - wristPos).normalized;
                Vector2 dir2 = (diagram.SmoothedPositions[toFinger] - wristPos).normalized;

                float angle = angles != null ? angles[p] : 0f;
                bool passes = angle >= thresholds[p];

                Color arcColor = angles != null
                    ? (passes ? _anglePassColor : _angleFailColor)
                    : _anglePendingColor;

                // Update arc segments
                UpdateArc(diagram.ArcSegments[p], diagram.ArcSegmentImages[p],
                    wristPos, dir1, dir2, arcColor);

                // Update angle label
                if (diagram.AngleLabels[p] != null)
                {
                    Vector2 midDir = (dir1 + dir2).normalized;
                    diagram.AngleLabels[p].rectTransform.anchoredPosition =
                        wristPos + midDir * (_arcRadius + _labelOffset);
                    diagram.AngleLabels[p].text = angles != null
                        ? $"{PairLabels[p]}\n{angle:F1}\u00b0"
                        : PairLabels[p];
                    diagram.AngleLabels[p].color = arcColor;
                }
            }
        }

        /// <summary>
        /// Projects wrist + 5 finger base positions to 2D canvas coords.
        /// Returns float[6]: [wrist, thumb, index, middle, ring, pinky].
        /// </summary>
        private Vector2[] ProjectKeyBones(OVRSkeleton skeleton, int boneCount, bool mirrorX)
        {
            Vector2[] result = new Vector2[FingerBases.Length + 1]; // +1 for wrist

            Vector3 wristWorld = GetBoneWorldPos(skeleton, WristIdx, boneCount);
            result[0] = Vector2.zero; // wrist at center

            for (int i = 0; i < FingerBases.Length; i++)
            {
                Vector3 fingerWorld = GetBoneWorldPos(skeleton, FingerBases[i], boneCount);
                Vector3 offset = fingerWorld - wristWorld;

                float x = Vector3.Dot(offset, Vector3.right) * _projectionScale;
                float y = Vector3.Dot(offset, Vector3.up) * _projectionScale;

                if (mirrorX) x = -x;

                result[i + 1] = new Vector2(x, y + _verticalOffset);
            }

            return result;
        }

        private Vector3 GetBoneWorldPos(OVRSkeleton skeleton, int boneIndex, int boneCount)
        {
            if (boneIndex >= boneCount) return Vector3.zero;
            var bone = skeleton.Bones[boneIndex];
            return (bone != null && bone.Transform != null) ? bone.Transform.position : Vector3.zero;
        }

        /// <summary>
        /// Computes spread angles directly from skeleton bone positions.
        /// Returns float[4]: [Index-Middle, Middle-Ring, Ring-Pinky, Thumb-Index].
        /// </summary>
        private float[] ComputeSpreadAngles(OVRSkeleton skeleton)
        {
            if (skeleton == null || !skeleton.IsInitialized ||
                skeleton.Bones == null || skeleton.Bones.Count == 0)
                return null;

            int boneCount = skeleton.Bones.Count;
            Vector3 wrist = GetBoneWorldPos(skeleton, WristIdx, boneCount);
            Vector3 thumb = GetBoneWorldPos(skeleton, ThumbIdx, boneCount);
            Vector3 index = GetBoneWorldPos(skeleton, IndexIdx, boneCount);
            Vector3 middle = GetBoneWorldPos(skeleton, MiddleIdx, boneCount);
            Vector3 ring = GetBoneWorldPos(skeleton, RingIdx, boneCount);
            Vector3 pinky = GetBoneWorldPos(skeleton, PinkyIdx, boneCount);

            const float minSqr = 0.0001f;
            Vector3 toThumb = thumb - wrist;
            Vector3 toIndex = index - wrist;
            Vector3 toMiddle = middle - wrist;
            Vector3 toRing = ring - wrist;
            Vector3 toPinky = pinky - wrist;

            if (toIndex.sqrMagnitude < minSqr || toMiddle.sqrMagnitude < minSqr ||
                toRing.sqrMagnitude < minSqr || toPinky.sqrMagnitude < minSqr)
                return null;

            return new[]
            {
                Vector3.Angle(toIndex, toMiddle),
                Vector3.Angle(toMiddle, toRing),
                Vector3.Angle(toRing, toPinky),
                Vector3.Angle(toThumb, toIndex)
            };
        }

        private float[] GetCurrentThresholds()
        {
            // If we have a linked exercise, extract its thresholds via reflection-safe serialized fields
            // For now, use defaults
            return DefaultThresholds;
        }

        private void InitializeDiagram(RectTransform area, ref HandDiagramData data, string rootName)
        {
            var rootGO = new GameObject(rootName, typeof(RectTransform));
            rootGO.transform.SetParent(area, false);
            var rootRect = rootGO.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.sizeDelta = Vector2.zero;
            rootRect.anchoredPosition = Vector2.zero;

            data.Root = rootGO;

            // Finger rays (5 lines from wrist to each finger)
            data.FingerRays = new RectTransform[FingerBases.Length];
            data.FingerRayImages = new Image[FingerBases.Length];
            for (int i = 0; i < FingerBases.Length; i++)
            {
                CreateLine(rootRect, $"FingerRay_{i}", _fingerRayColor, _rayLineWidth,
                    out data.FingerRays[i], out data.FingerRayImages[i]);
            }

            // Angle arcs (4 arcs between adjacent finger pairs)
            data.ArcLines = new RectTransform[AnglePairCount];
            data.ArcSegments = new RectTransform[AnglePairCount][];
            data.ArcSegmentImages = new Image[AnglePairCount][];
            for (int p = 0; p < AnglePairCount; p++)
            {
                var arcContainer = new GameObject($"Arc_{p}", typeof(RectTransform));
                arcContainer.transform.SetParent(rootRect, false);
                var arcRect = arcContainer.GetComponent<RectTransform>();
                arcRect.anchorMin = new Vector2(0.5f, 0.5f);
                arcRect.anchorMax = new Vector2(0.5f, 0.5f);
                arcRect.sizeDelta = Vector2.zero;
                data.ArcLines[p] = arcRect;

                data.ArcSegments[p] = new RectTransform[_arcSegments];
                data.ArcSegmentImages[p] = new Image[_arcSegments];
                for (int s = 0; s < _arcSegments; s++)
                {
                    CreateLine(arcRect, $"Seg_{s}", _anglePendingColor, _arcLineWidth,
                        out data.ArcSegments[p][s], out data.ArcSegmentImages[p][s]);
                }
            }

            // Angle labels (4 text labels)
            data.AngleLabels = new TMP_Text[AnglePairCount];
            for (int p = 0; p < AnglePairCount; p++)
            {
                var labelGO = new GameObject($"AngleLabel_{p}", typeof(RectTransform), typeof(TextMeshProUGUI));
                labelGO.transform.SetParent(rootRect, false);

                var labelRect = labelGO.GetComponent<RectTransform>();
                labelRect.anchorMin = new Vector2(0.5f, 0.5f);
                labelRect.anchorMax = new Vector2(0.5f, 0.5f);
                labelRect.pivot = new Vector2(0.5f, 0.5f);
                labelRect.sizeDelta = new Vector2(50f, 30f);

                var tmp = labelGO.GetComponent<TextMeshProUGUI>();
                tmp.fontSize = _labelFontSize;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = _anglePendingColor;
                tmp.raycastTarget = false;
                tmp.enableWordWrapping = false;

                data.AngleLabels[p] = tmp;
            }

            // Wrist dot
            data.WristDot = CreateDot(rootRect, "WristDot", 10f, _fingerRayColor);

            // Finger dots (5 dots at finger bases)
            data.FingerDots = new RectTransform[FingerBases.Length];
            for (int i = 0; i < FingerBases.Length; i++)
            {
                data.FingerDots[i] = CreateDot(rootRect, $"FingerDot_{i}", 7f,
                    new Color(1f, 1f, 1f, 0.8f));
            }

            data.SmoothedPositions = null;
            data.IsInitialized = true;
        }

        private RectTransform CreateDot(RectTransform parent, string name, float size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);

            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;

            return rect;
        }

        private void CreateLine(RectTransform parent, string name, Color color, float width,
            out RectTransform lineRect, out Image lineImage)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            lineRect = go.GetComponent<RectTransform>();
            lineRect.anchorMin = new Vector2(0.5f, 0.5f);
            lineRect.anchorMax = new Vector2(0.5f, 0.5f);
            lineRect.pivot = new Vector2(0f, 0.5f);
            lineRect.sizeDelta = new Vector2(0f, width);

            lineImage = go.GetComponent<Image>();
            lineImage.color = color;
            lineImage.raycastTarget = false;
        }

        private void UpdateLine(RectTransform lineRect, Vector2 from, Vector2 to)
        {
            if (lineRect == null) return;
            Vector2 diff = to - from;
            lineRect.anchoredPosition = from;
            lineRect.sizeDelta = new Vector2(diff.magnitude, lineRect.sizeDelta.y);
            lineRect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg);
        }

        /// <summary>
        /// Draws an arc between two 2D directions using line segments.
        /// </summary>
        private void UpdateArc(
            RectTransform[] segments, Image[] segmentImages,
            Vector2 center, Vector2 dir1, Vector2 dir2, Color color)
        {
            if (segments == null) return;

            float angle1 = Mathf.Atan2(dir1.y, dir1.x);
            float angle2 = Mathf.Atan2(dir2.y, dir2.x);

            // Ensure we go from angle1 to angle2 in the shorter direction
            float delta = Mathf.DeltaAngle(angle1 * Mathf.Rad2Deg, angle2 * Mathf.Rad2Deg) * Mathf.Deg2Rad;

            for (int s = 0; s < segments.Length; s++)
            {
                if (segments[s] == null) continue;

                float t0 = s / (float)segments.Length;
                float t1 = (s + 1f) / segments.Length;

                float a0 = angle1 + delta * t0;
                float a1 = angle1 + delta * t1;

                Vector2 p0 = center + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * _arcRadius;
                Vector2 p1 = center + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * _arcRadius;

                UpdateLine(segments[s], p0, p1);

                if (segmentImages[s] != null)
                    segmentImages[s].color = color;
            }
        }

        private void SetDiagramAlpha(ref HandDiagramData data, float alpha)
        {
            if (data.AngleLabels != null)
            {
                for (int i = 0; i < data.AngleLabels.Length; i++)
                {
                    if (data.AngleLabels[i] != null)
                    {
                        Color c = data.AngleLabels[i].color;
                        c.a = Mathf.Min(c.a, alpha);
                        data.AngleLabels[i].color = c;
                    }
                }
            }

            if (data.FingerRayImages != null)
            {
                for (int i = 0; i < data.FingerRayImages.Length; i++)
                {
                    if (data.FingerRayImages[i] != null)
                    {
                        Color c = data.FingerRayImages[i].color;
                        c.a = Mathf.Min(c.a, alpha);
                        data.FingerRayImages[i].color = c;
                    }
                }
            }
        }

        private void OnDestroy()
        {
            if (_leftDiagram.Root != null) Destroy(_leftDiagram.Root);
            if (_rightDiagram.Root != null) Destroy(_rightDiagram.Root);
            _leftDiagram = default;
            _rightDiagram = default;
        }
    }
}
