using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AGVRSystem.UI
{
    /// <summary>
    /// Renders a live 2D projection of OVRSkeleton hand data onto a UI canvas.
    /// Creates joint dots and skeleton bone lines as UI elements, updating
    /// positions every frame from real hand tracking data.
    /// Supports independent left/right hand rendering with configurable colors.
    /// </summary>
    public class CanvasHandRenderer : MonoBehaviour
    {
        [Header("Hand Skeleton Sources")]
        [SerializeField] private OVRSkeleton _leftSkeleton;
        [SerializeField] private OVRSkeleton _rightSkeleton;
        [SerializeField] private OVRHand _leftHand;
        [SerializeField] private OVRHand _rightHand;

        [Header("Canvas Targets")]
        [Tooltip("RectTransform area where the left hand will be rendered")]
        [SerializeField] private RectTransform _leftHandArea;
        [Tooltip("RectTransform area where the right hand will be rendered")]
        [SerializeField] private RectTransform _rightHandArea;

        [Header("Joint Appearance")]
        [SerializeField] private float _jointDotSize = 8f;
        [SerializeField] private float _tipDotSize = 10f;
        [SerializeField] private float _wristDotSize = 12f;
        [SerializeField] private float _boneLineWidth = 2f;

        [Header("Colors")]
        [SerializeField] private Color _leftJointColor = new Color(0.2f, 0.78f, 0.38f, 1f);
        [SerializeField] private Color _leftBoneColor = new Color(0.2f, 0.78f, 0.38f, 0.6f);
        [SerializeField] private Color _rightJointColor = new Color(0.25f, 0.55f, 0.90f, 1f);
        [SerializeField] private Color _rightBoneColor = new Color(0.25f, 0.55f, 0.90f, 0.6f);
        [SerializeField] private Color _tipColor = new Color(1f, 1f, 1f, 0.95f);

        [Header("Animation")]
        [SerializeField] private float _pulseSpeed = 3f;
        [SerializeField] private float _pulseAmount = 0.3f;
        [Tooltip("Exponential smoothing factor: higher = more responsive, lower = smoother. Safe range 1-20.")]
        [SerializeField] private float _smoothing = 8f;

        [Header("Projection")]
        [Tooltip("Scale multiplier for mapping 3D hand size to canvas pixels")]
        [SerializeField] private float _projectionScale = 1200f;
        [Tooltip("Vertical offset to center the hand in the render area")]
        [SerializeField] private float _verticalOffset = -30f;

        private const int MaxBones = 24;

        // Auto-discovery retry
        private float _discoveryTimer;
        private const float DiscoveryInterval = 0.5f;
        private const float MaxDiscoveryTime = 15f;
        private float _discoveryElapsed;

        /// <summary>
        /// Bone connection pairs (from, to) defining the skeleton wireframe.
        /// Matches HandJointVisualizer bone indices.
        /// </summary>
        private static readonly int[,] BoneConnections =
        {
            { 0, 2 },   // Wrist -> Thumb0
            { 2, 3 },   // Thumb0 -> Thumb1
            { 3, 4 },   // Thumb1 -> Thumb2
            { 4, 5 },   // Thumb2 -> Thumb3 (ThumbTip)
            { 0, 6 },   // Wrist -> Index1
            { 6, 7 },   // Index1 -> Index2
            { 7, 8 },   // Index2 -> Index3 (IndexTip)
            { 0, 9 },   // Wrist -> Middle1
            { 9, 10 },  // Middle1 -> Middle2
            { 10, 11 }, // Middle2 -> Middle3 (MiddleTip)
            { 0, 12 },  // Wrist -> Ring1
            { 12, 13 }, // Ring1 -> Ring2
            { 13, 14 }, // Ring2 -> Ring3 (RingTip)
            { 0, 15 },  // Wrist -> Pinky0
            { 15, 16 }, // Pinky0 -> Pinky1
            { 16, 17 }, // Pinky1 -> Pinky2
            { 17, 18 }, // Pinky2 -> Pinky3 (PinkyTip)
        };

        /// <summary>
        /// Knuckle bridge connections (palm line across finger bases).
        /// </summary>
        private static readonly int[,] KnuckleConnections =
        {
            { 6, 9 },   // Index1 -> Middle1
            { 9, 12 },  // Middle1 -> Ring1
            { 12, 15 }, // Ring1 -> Pinky0
        };

        /// <summary>Tip bone indices for special styling.</summary>
        private static readonly HashSet<int> TipBones = new HashSet<int> { 5, 8, 11, 14, 18 };

        private CanvasHandData _leftData;
        private CanvasHandData _rightData;

        private struct CanvasHandData
        {
            public GameObject Root;
            public RectTransform[] JointDots;
            public Image[] JointImages;
            public RectTransform[] BoneLines;
            public Image[] BoneLineImages;
            public RectTransform[] KnuckleLines;
            public Image[] KnuckleLineImages;
            public Vector2[] SmoothedPositions;
            public bool IsInitialized;
        }

        private void Update()
        {
            // Auto-discover OVR hand references when not wired in Inspector
            if (_leftSkeleton == null || _rightSkeleton == null ||
                _leftHand == null || _rightHand == null)
            {
                TryDiscoverHandReferences();
            }

            RenderHand(_leftSkeleton, _leftHand, _leftHandArea,
                ref _leftData, "LeftCanvasHand",
                _leftJointColor, _leftBoneColor, false);

            RenderHand(_rightSkeleton, _rightHand, _rightHandArea,
                ref _rightData, "RightCanvasHand",
                _rightJointColor, _rightBoneColor, true);
        }

        /// <summary>
        /// Periodically searches for OVRSkeleton/OVRHand components.
        /// Tries HandTrackingManager first, then falls back to direct scene search.
        /// </summary>
        private void TryDiscoverHandReferences()
        {
            if (_discoveryElapsed >= MaxDiscoveryTime) return;

            _discoveryTimer += Time.deltaTime;
            _discoveryElapsed += Time.deltaTime;
            if (_discoveryTimer < DiscoveryInterval) return;
            _discoveryTimer = 0f;

            // Strategy 1: From HandTrackingManager singleton
            if (HandTrackingManager.Instance != null)
            {
                if (_leftSkeleton == null) _leftSkeleton = HandTrackingManager.Instance.LeftSkeleton;
                if (_rightSkeleton == null) _rightSkeleton = HandTrackingManager.Instance.RightSkeleton;
                if (_leftHand == null) _leftHand = HandTrackingManager.Instance.LeftHand;
                if (_rightHand == null) _rightHand = HandTrackingManager.Instance.RightHand;
            }

            // Strategy 2: Direct scene search by skeleton type
            if (_leftSkeleton == null || _rightSkeleton == null)
            {
                var allSkeletons = FindObjectsByType<OVRSkeleton>(FindObjectsSortMode.None);
                foreach (var sk in allSkeletons)
                {
                    if (sk == null) continue;
                    var type = sk.GetSkeletonType();
                    if (type == OVRSkeleton.SkeletonType.HandLeft && _leftSkeleton == null)
                    {
                        _leftSkeleton = sk;
                        _leftHand = sk.GetComponent<OVRHand>();
                        Debug.Log($"[CanvasHandRenderer] Auto-discovered left skeleton on {sk.gameObject.name}.");
                    }
                    else if (type == OVRSkeleton.SkeletonType.HandRight && _rightSkeleton == null)
                    {
                        _rightSkeleton = sk;
                        _rightHand = sk.GetComponent<OVRHand>();
                        Debug.Log($"[CanvasHandRenderer] Auto-discovered right skeleton on {sk.gameObject.name}.");
                    }
                }
            }
        }

        private void OnDestroy()
        {
            CleanupHand(ref _leftData);
            CleanupHand(ref _rightData);
        }

        private void RenderHand(
            OVRSkeleton skeleton, OVRHand hand, RectTransform area,
            ref CanvasHandData data, string rootName,
            Color jointColor, Color boneColor, bool mirrorX)
        {
            if (area == null)
                return;

            bool isTracked = skeleton != null
                && skeleton.IsInitialized
                && skeleton.Bones != null
                && skeleton.Bones.Count > 0
                && hand != null
                && hand.IsTracked;

            if (!isTracked)
            {
                if (data.Root != null)
                {
                    SetGroupAlpha(data, 0.15f);
                }
                return;
            }

            int boneCount = Mathf.Min(skeleton.Bones.Count, MaxBones);

            if (!data.IsInitialized)
            {
                InitializeHandUI(area, ref data, rootName, boneCount, jointColor, boneColor);
            }

            SetGroupAlpha(data, 1f);

            // Project 3D bone positions to 2D canvas coordinates
            Vector2[] positions = ProjectBones(skeleton, area, boneCount, mirrorX);

            // Smooth positions
            if (data.SmoothedPositions == null || data.SmoothedPositions.Length != boneCount)
            {
                data.SmoothedPositions = new Vector2[boneCount];
                for (int i = 0; i < boneCount; i++)
                {
                    data.SmoothedPositions[i] = positions[i];
                }
            }
            else
            {
                // Frame-rate-independent exponential smooth:
                // alpha = 1 - exp(-smoothing * dt) keeps behaviour consistent regardless of fps.
                float alpha = 1f - Mathf.Exp(-_smoothing * Time.deltaTime);
                for (int i = 0; i < boneCount; i++)
                {
                    data.SmoothedPositions[i] = Vector2.Lerp(
                        data.SmoothedPositions[i], positions[i], alpha);
                }
            }

            // Update joint dot positions
            float time = Time.time;
            for (int i = 0; i < boneCount && i < data.JointDots.Length; i++)
            {
                if (data.JointDots[i] == null)
                    continue;

                data.JointDots[i].anchoredPosition = data.SmoothedPositions[i];

                // Pulse animation
                float pulse = 1f + Mathf.Sin(time * _pulseSpeed + i * 0.5f) * _pulseAmount;
                float baseSize = GetDotSize(i);
                float size = baseSize * pulse;
                data.JointDots[i].sizeDelta = new Vector2(size, size);

                // Color based on type
                if (data.JointImages[i] != null)
                {
                    Color c = TipBones.Contains(i) ? _tipColor : jointColor;
                    c.a = 0.9f + Mathf.Sin(time * _pulseSpeed + i) * 0.1f;
                    data.JointImages[i].color = c;
                }
            }

            // Update bone lines
            int connCount = BoneConnections.GetLength(0);
            for (int i = 0; i < connCount && i < data.BoneLines.Length; i++)
            {
                int from = BoneConnections[i, 0];
                int to = BoneConnections[i, 1];

                if (from >= boneCount || to >= boneCount)
                    continue;

                UpdateLine(data.BoneLines[i], data.SmoothedPositions[from], data.SmoothedPositions[to]);
            }

            // Update knuckle lines
            int knuckleCount = KnuckleConnections.GetLength(0);
            for (int i = 0; i < knuckleCount && i < data.KnuckleLines.Length; i++)
            {
                int from = KnuckleConnections[i, 0];
                int to = KnuckleConnections[i, 1];

                if (from >= boneCount || to >= boneCount)
                    continue;

                UpdateLine(data.KnuckleLines[i], data.SmoothedPositions[from], data.SmoothedPositions[to]);
            }
        }

        private Vector2[] ProjectBones(OVRSkeleton skeleton, RectTransform area, int boneCount, bool mirrorX)
        {
            var positions = new Vector2[boneCount];
            var bones = skeleton.Bones;

            // Use wrist as reference point
            Vector3 wristWorld = bones[0].Transform.position;

            // Fixed "front view" projection — always shows hands upright
            // with fingers pointing UP in the UI, regardless of actual hand orientation.
            // X axis = world right (lateral), Y axis = world up (vertical).
            // This matches the silhouette orientation (hand held up, palm forward).
            Vector3 projRight = Vector3.right;
            Vector3 projUp = Vector3.up;

            for (int i = 0; i < boneCount; i++)
            {
                if (bones[i] == null || bones[i].Transform == null)
                {
                    positions[i] = Vector2.zero;
                    continue;
                }

                Vector3 worldPos = bones[i].Transform.position;
                Vector3 offset = worldPos - wristWorld;

                // Project onto fixed front-facing plane
                float x = Vector3.Dot(offset, projRight) * _projectionScale;
                float y = Vector3.Dot(offset, projUp) * _projectionScale;

                if (mirrorX)
                {
                    x = -x;
                }

                // Center in area
                positions[i] = new Vector2(x, y + _verticalOffset);
            }

            return positions;
        }

        private void InitializeHandUI(
            RectTransform area, ref CanvasHandData data, string rootName,
            int boneCount, Color jointColor, Color boneColor)
        {
            // Root container
            var rootGO = new GameObject(rootName, typeof(RectTransform));
            rootGO.transform.SetParent(area, false);
            var rootRect = rootGO.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.sizeDelta = Vector2.zero;
            rootRect.anchoredPosition = Vector2.zero;

            data.Root = rootGO;

            // Create bone lines first (render behind joints)
            int connCount = BoneConnections.GetLength(0);
            data.BoneLines = new RectTransform[connCount];
            data.BoneLineImages = new Image[connCount];
            for (int i = 0; i < connCount; i++)
            {
                CreateLine(rootRect, $"Bone_{i}", boneColor, _boneLineWidth,
                    out data.BoneLines[i], out data.BoneLineImages[i]);
            }

            // Create knuckle lines
            int knuckleCount = KnuckleConnections.GetLength(0);
            data.KnuckleLines = new RectTransform[knuckleCount];
            data.KnuckleLineImages = new Image[knuckleCount];
            for (int i = 0; i < knuckleCount; i++)
            {
                Color knuckleColor = boneColor;
                knuckleColor.a *= 0.5f;
                CreateLine(rootRect, $"Knuckle_{i}", knuckleColor, _boneLineWidth * 0.7f,
                    out data.KnuckleLines[i], out data.KnuckleLineImages[i]);
            }

            // Create joint dots
            data.JointDots = new RectTransform[boneCount];
            data.JointImages = new Image[boneCount];
            for (int i = 0; i < boneCount; i++)
            {
                float size = GetDotSize(i);
                Color c = TipBones.Contains(i) ? _tipColor : jointColor;

                var dotGO = new GameObject($"Joint_{i}", typeof(RectTransform), typeof(Image));
                dotGO.transform.SetParent(rootRect, false);

                var dotRect = dotGO.GetComponent<RectTransform>();
                dotRect.anchorMin = new Vector2(0.5f, 0.5f);
                dotRect.anchorMax = new Vector2(0.5f, 0.5f);
                dotRect.pivot = new Vector2(0.5f, 0.5f);
                dotRect.sizeDelta = new Vector2(size, size);

                var img = dotGO.GetComponent<Image>();
                img.color = c;
                img.raycastTarget = false;

                data.JointDots[i] = dotRect;
                data.JointImages[i] = img;
            }

            data.SmoothedPositions = null;
            data.IsInitialized = true;
        }

        private void CreateLine(RectTransform parent, string name, Color color, float width,
            out RectTransform lineRect, out Image lineImage)
        {
            var lineGO = new GameObject(name, typeof(RectTransform), typeof(Image));
            lineGO.transform.SetParent(parent, false);

            lineRect = lineGO.GetComponent<RectTransform>();
            lineRect.anchorMin = new Vector2(0.5f, 0.5f);
            lineRect.anchorMax = new Vector2(0.5f, 0.5f);
            lineRect.pivot = new Vector2(0f, 0.5f);
            lineRect.sizeDelta = new Vector2(0f, width);

            lineImage = lineGO.GetComponent<Image>();
            lineImage.color = color;
            lineImage.raycastTarget = false;
        }

        private void UpdateLine(RectTransform lineRect, Vector2 from, Vector2 to)
        {
            if (lineRect == null)
                return;

            Vector2 diff = to - from;
            float length = diff.magnitude;
            float angle = Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg;

            lineRect.anchoredPosition = from;
            lineRect.sizeDelta = new Vector2(length, lineRect.sizeDelta.y);
            lineRect.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        private float GetDotSize(int boneIndex)
        {
            if (boneIndex == 0)
                return _wristDotSize;

            if (TipBones.Contains(boneIndex))
                return _tipDotSize;

            return _jointDotSize;
        }

        private void SetGroupAlpha(CanvasHandData data, float alpha)
        {
            if (data.JointImages != null)
            {
                for (int i = 0; i < data.JointImages.Length; i++)
                {
                    if (data.JointImages[i] != null)
                    {
                        Color c = data.JointImages[i].color;
                        c.a = Mathf.Min(c.a, alpha);
                        data.JointImages[i].color = c;
                    }
                }
            }

            if (data.BoneLineImages != null)
            {
                for (int i = 0; i < data.BoneLineImages.Length; i++)
                {
                    if (data.BoneLineImages[i] != null)
                    {
                        Color c = data.BoneLineImages[i].color;
                        c.a = Mathf.Min(c.a, alpha);
                        data.BoneLineImages[i].color = c;
                    }
                }
            }

            if (data.KnuckleLineImages != null)
            {
                for (int i = 0; i < data.KnuckleLineImages.Length; i++)
                {
                    if (data.KnuckleLineImages[i] != null)
                    {
                        Color c = data.KnuckleLineImages[i].color;
                        c.a = Mathf.Min(c.a, alpha);
                        data.KnuckleLineImages[i].color = c;
                    }
                }
            }
        }

        private void CleanupHand(ref CanvasHandData data)
        {
            if (data.Root != null)
            {
                Destroy(data.Root);
            }

            data = default;
        }
    }
}
