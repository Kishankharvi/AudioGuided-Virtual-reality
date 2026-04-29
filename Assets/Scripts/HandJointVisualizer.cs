// Old commented-out implementation removed — active version below.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

namespace AGVRSystem
{
    /// <summary>
    /// Renders glowing joint spheres, skeleton bone lines, and live angle labels
    /// on tracked hands. Joints pulse and shift color from green (relaxed) to
    /// orange (bent) based on flexion angle.
    /// </summary>
    public class HandJointVisualizer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private OVRSkeleton _leftSkeleton;
        [SerializeField] private OVRSkeleton _rightSkeleton;

        [Header("Joint Markers")]
        [SerializeField] private float _jointRadius = 0.005f;
        [SerializeField] private float _tipRadius = 0.004f;
        [SerializeField] private float _wristRadius = 0.007f;

        [Header("Bone Lines")]
        [SerializeField] private float _lineWidth = 0.002f;

        [Header("Angle Labels")]
        [SerializeField] private float _angleLabelSize = 0.009f;
        [SerializeField] private float _angleLabelOffset = 0.019f;

        [Header("Effects")]
        [SerializeField] private float _pulseSpeed = 3f;
        [SerializeField] private float _pulseIntensity = 0.4f;
        [SerializeField] private float _glowIntensity = 2.5f;

        [Header("Colors")]
        [SerializeField] private Color _relaxedColor = new Color(0.2f, 0.85f, 0.4f, 1f);
        [SerializeField] private Color _bentColor = new Color(0.95f, 0.55f, 0.1f, 1f);
        [SerializeField] private Color _tipColor = new Color(0.4f, 0.7f, 1f, 1f);
        [SerializeField] private Color _lineColor = new Color(0.3f, 0.8f, 0.5f, 0.6f);
        [SerializeField] private Color _angleTextColor = new Color(1f, 1f, 0.85f, 1f);

        [Header("Smoothing")]
        [Tooltip("Enable One Euro Filter smoothing. OFF by default because OVR SDK already filters tracking data.")]
        [SerializeField] private bool _useSmoothing = false;
        [Tooltip("Minimum cutoff frequency for One Euro Filter. Higher = less smoothing, more responsive.")]
        [SerializeField] private float _oneEuroMinCutoff = 15.0f;
        [Tooltip("Speed coefficient for One Euro Filter. Higher = faster movements get less smoothing.")]
        [SerializeField] private float _oneEuroBeta = 1.5f;

        // ─── Constants ────────────────────────────────────────────────────────

        private const int MaxBones = 24;
        private const float MaxFlexionAngle = 120f;
        private const float AngleArcRadius = 0.012f;
        private const int ArcSegments = 12;

        private static readonly int[,] BoneConnections =
        {
            // Thumb chain
            { 0, 2 },  { 2, 3 },  { 3, 4 },  { 4, 5 },  { 5, 19 },
            // Index chain
            { 0, 6 },  { 6, 7 },  { 7, 8 },  { 8, 20 },
            // Middle chain
            { 0, 9 },  { 9, 10 }, { 10, 11 }, { 11, 21 },
            // Ring chain
            { 0, 12 }, { 12, 13 },{ 13, 14 }, { 14, 22 },
            // Pinky chain
            { 0, 15 }, { 15, 16 },{ 16, 17 }, { 17, 18 }, { 18, 23 },
        };

        private static readonly int[,] AngleJoints =
        {
            { 2, 3, 4 },
            { 3, 4, 5 },
            { 6, 7, 8 },
            { 9, 10, 11 },
            { 12, 13, 14 },
            { 15, 16, 17 },
            { 16, 17, 18 },
        };

        // Actual fingertip bone IDs: ThumbTip=19, IndexTip=20, MiddleTip=21, RingTip=22, PinkyTip=23
        private static readonly HashSet<int> TipBones = new HashSet<int> { 19, 20, 21, 22, 23 };

        // ─── Nested struct ────────────────────────────────────────────────────

        private struct HandVisualData
        {
            public GameObject Root;
            public GameObject[] JointSpheres;
            public LineRenderer[] BoneLines;
            public TextMeshPro[] AngleLabels;
            public LineRenderer[] AngleArcs;
            public bool WasInitialized;
        }

        // ─── Private fields ───────────────────────────────────────────────────

        private HandVisualData _leftVisual;
        private HandVisualData _rightVisual;
        private Material _jointMaterial;
        private Camera _mainCamera;

        private Vector3[] _leftSmoothed;
        private Vector3[] _rightSmoothed;
        private Vector3[] _leftPrevRaw;
        private Vector3[] _rightPrevRaw;
        private float[]   _leftDxFilter;
        private float[]   _rightDxFilter;

        // ─── Lifecycle ────────────────────────────────────────────────────────

        private void Awake()
        {
            CreateJointMaterial();
            RefreshCamera();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private bool _initialSceneHandled;

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Skip the INITIAL scene load — Inspector references are still valid.
            // Only clear and re-discover on SUBSEQUENT scene loads (transitions).
            if (!_initialSceneHandled)
            {
                _initialSceneHandled = true;
                Debug.Log("[HandJointVisualizer] Initial scene load — keeping Inspector skeleton refs.");
                RefreshCamera();
                return;
            }

            Debug.Log("[HandJointVisualizer] Scene transition detected — re-discovering skeletons.");
            _leftSkeleton  = null;
            _rightSkeleton = null;

            CleanupHand(ref _leftVisual);
            CleanupHand(ref _rightVisual);

            _leftSmoothed  = null;  _rightSmoothed  = null;
            _leftPrevRaw   = null;  _rightPrevRaw   = null;
            _leftDxFilter  = null;  _rightDxFilter  = null;

            RefreshCamera();
            TryDiscoverSkeletons();
        }

        private void OnDestroy()
        {
            CleanupHand(ref _leftVisual);
            CleanupHand(ref _rightVisual);
            if (_jointMaterial != null)
                Destroy(_jointMaterial);
        }

        private void Update()
        {
            if (_mainCamera == null)
                RefreshCamera();

            // Detect stale skeleton references (destroyed during scene transition)
            if (_leftSkeleton != null && _leftSkeleton.gameObject == null)
                _leftSkeleton = null;
            if (_rightSkeleton != null && _rightSkeleton.gameObject == null)
                _rightSkeleton = null;

            if (_leftSkeleton == null || _rightSkeleton == null)
                TryDiscoverSkeletons();

            UpdateHand(_leftSkeleton,  ref _leftVisual,
                       ref _leftSmoothed,  ref _leftPrevRaw,  ref _leftDxFilter,
                       "LeftJoints");

            UpdateHand(_rightSkeleton, ref _rightVisual,
                       ref _rightSmoothed, ref _rightPrevRaw, ref _rightDxFilter,
                       "RightJoints");
        }

        // ─── Camera ───────────────────────────────────────────────────────────

        private void RefreshCamera()
        {
            _mainCamera = Camera.main;
            if (_mainCamera == null)
            {
                var ovrRig = FindAnyObjectByType<OVRCameraRig>();
                if (ovrRig != null)
                    _mainCamera = ovrRig.centerEyeAnchor.GetComponent<Camera>();
            }
        }

        // ─── Skeleton discovery ───────────────────────────────────────────────

        private void TryDiscoverSkeletons()
        {
            // Strategy 1: HandTrackingManager (persists across scenes)
            if (HandTrackingManager.Instance != null)
            {
                var lsk = HandTrackingManager.Instance.LeftSkeleton;
                var rsk = HandTrackingManager.Instance.RightSkeleton;

                // Bind even if not yet initialized — UpdateHand guards on IsInitialized.
                // This ensures we're ready to render as soon as tracking data flows.
                if (_leftSkeleton == null && lsk != null)
                {
                    _leftSkeleton = lsk;
                    Debug.Log($"[HandJointVisualizer] Bound left skeleton via HTM (initialized={lsk.IsInitialized}).");
                }
                if (_rightSkeleton == null && rsk != null)
                {
                    _rightSkeleton = rsk;
                    Debug.Log($"[HandJointVisualizer] Bound right skeleton via HTM (initialized={rsk.IsInitialized}).");
                }
            }

            // Strategy 2: Direct scene search fallback
            if (_leftSkeleton == null || _rightSkeleton == null)
            {
                var skeletons = FindObjectsByType<OVRSkeleton>(FindObjectsSortMode.None);
                foreach (var sk in skeletons)
                {
                    if (sk == null) continue;
                    var type = sk.GetSkeletonType();
                    bool isLeft  = type == OVRSkeleton.SkeletonType.HandLeft
                                || type == OVRSkeleton.SkeletonType.XRHandLeft;
                    bool isRight = type == OVRSkeleton.SkeletonType.HandRight
                                || type == OVRSkeleton.SkeletonType.XRHandRight;

                    if (isLeft && _leftSkeleton == null)
                    {
                        _leftSkeleton = sk;
                        Debug.Log($"[HandJointVisualizer] Bound left skeleton via search: {sk.gameObject.name}");
                    }
                    else if (isRight && _rightSkeleton == null)
                    {
                        _rightSkeleton = sk;
                        Debug.Log($"[HandJointVisualizer] Bound right skeleton via search: {sk.gameObject.name}");
                    }
                }
            }
        }

        // ─── Materials ────────────────────────────────────────────────────────

        private void CreateJointMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Sprites/Default")
                         ?? Shader.Find("Standard");

            if (shader == null)
            {
                Debug.LogError("[HandJointVisualizer] No usable shader found. " +
                    "Add 'Universal Render Pipeline/Lit' to Always Included Shaders.");
                return;
            }

            _jointMaterial = new Material(shader);
            _jointMaterial.SetColor("_BaseColor", _relaxedColor);
            _jointMaterial.SetFloat("_Smoothness", 0.85f);
            _jointMaterial.SetFloat("_Metallic", 0.1f);
            _jointMaterial.EnableKeyword("_EMISSION");
            _jointMaterial.SetColor("_EmissionColor", _relaxedColor * _glowIntensity);
        }

        private Material CreateLineMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Sprites/Default")
                         ?? Shader.Find("Unlit/Color");

            if (shader == null)
            {
                Debug.LogWarning("[HandJointVisualizer] Line shader not found.");
                return null;
            }

            var mat = new Material(shader);
            mat.SetColor("_BaseColor", color);
            return mat;
        }

        private void ConfigureLine(LineRenderer lr, Color color, float width)
        {
            lr.useWorldSpace     = true;
            lr.startWidth        = width;
            lr.endWidth          = width;
            lr.startColor        = color;
            lr.endColor          = color;
            lr.numCornerVertices = 4;
            lr.numCapVertices    = 4;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows    = false;

            Material lineMat = CreateLineMaterial(color);
            if (lineMat != null)
                lr.material = lineMat;
        }

        // ─── Per-hand update ──────────────────────────────────────────────────

        private float _nextLogTime;
        private const float LogInterval = 5f;

        private void UpdateHand(
            OVRSkeleton skeleton,
            ref HandVisualData visual,
            ref Vector3[] smoothed,
            ref Vector3[] prevRaw,
            ref float[]   dxFilter,
            string rootName)
        {
            if (skeleton == null || !skeleton.IsInitialized || skeleton.Bones == null)
            {
                if (visual.Root != null)
                    visual.Root.SetActive(false);

                // Periodic diagnostic logging
                if (Time.time >= _nextLogTime)
                {
                    _nextLogTime = Time.time + LogInterval;
                    string reason = skeleton == null ? "skeleton null"
                        : !skeleton.IsInitialized ? $"not initialized (GO={skeleton.gameObject.name})"
                        : "bones null";
                    Debug.Log($"[HandJointVisualizer] {rootName} waiting: {reason}");
                }
                return;
            }

            int boneCount = skeleton.Bones.Count;
            if (boneCount == 0)
                return;

            if (!visual.WasInitialized)
                InitializeHandVisuals(skeleton, ref visual, rootName, boneCount);

            if (visual.Root != null && !visual.Root.activeSelf)
                visual.Root.SetActive(true);

            UpdateJointPositions(skeleton, ref visual, boneCount,
                                 ref smoothed, ref prevRaw, ref dxFilter);
            UpdateBoneLines(ref visual, boneCount, ref smoothed);
            UpdateAngleLabelsFromSmoothed(ref visual, boneCount, ref smoothed);
            UpdateAngleArcsFromSmoothed(ref visual, boneCount, ref smoothed);
        }

        // ─── Initialization ───────────────────────────────────────────────────

        private void InitializeHandVisuals(
            OVRSkeleton skeleton,
            ref HandVisualData visual,
            string rootName,
            int boneCount)
        {
            visual.Root = new GameObject(rootName);
            visual.Root.transform.SetParent(transform, false);

            // Joint spheres
            visual.JointSpheres = new GameObject[boneCount];
            for (int i = 0; i < boneCount && i < MaxBones; i++)
            {
                float radius = GetJointRadius(i);
                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = $"Joint_{i}";
                sphere.transform.SetParent(visual.Root.transform, false);
                sphere.transform.localScale = Vector3.one * radius * 2f;

                Collider col = sphere.GetComponent<Collider>();
                if (col != null) Destroy(col);

                Renderer rend = sphere.GetComponent<Renderer>();
                if (rend != null && _jointMaterial != null)
                {
                    rend.material = new Material(_jointMaterial);
                    rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    rend.receiveShadows = false;
                }

                visual.JointSpheres[i] = sphere;
            }

            // Bone lines
            int connectionCount = BoneConnections.GetLength(0);
            visual.BoneLines = new LineRenderer[connectionCount];
            for (int i = 0; i < connectionCount; i++)
            {
                GameObject lineObj = new GameObject($"BoneLine_{i}");
                lineObj.transform.SetParent(visual.Root.transform, false);
                LineRenderer lr = lineObj.AddComponent<LineRenderer>();
                ConfigureLine(lr, _lineColor, _lineWidth);
                lr.positionCount = 2;
                visual.BoneLines[i] = lr;
            }

            // Angle labels and arcs
            int angleCount = AngleJoints.GetLength(0);
            visual.AngleLabels = new TextMeshPro[angleCount];
            visual.AngleArcs   = new LineRenderer[angleCount];

            for (int i = 0; i < angleCount; i++)
            {
                // Label
                GameObject labelObj = new GameObject($"AngleLabel_{i}");
                labelObj.transform.SetParent(visual.Root.transform, false);
                TextMeshPro tmp = labelObj.AddComponent<TextMeshPro>();
                tmp.fontSize = 1.2f;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = _angleTextColor;
                tmp.textWrappingMode = TextWrappingModes.NoWrap;
                tmp.overflowMode = TextOverflowModes.Overflow;

                RectTransform rt = labelObj.GetComponent<RectTransform>();
                if (rt != null) rt.sizeDelta = new Vector2(0.05f, 0.02f);

                labelObj.transform.localScale = Vector3.one * _angleLabelSize;
                visual.AngleLabels[i] = tmp;

                // Arc
                GameObject arcObj = new GameObject($"AngleArc_{i}");
                arcObj.transform.SetParent(visual.Root.transform, false);
                LineRenderer arc = arcObj.AddComponent<LineRenderer>();
                ConfigureLine(arc, _relaxedColor, _lineWidth * 0.8f);
                arc.positionCount = ArcSegments + 1;
                arc.loop = false;
                visual.AngleArcs[i] = arc;
            }

            visual.WasInitialized = true;
        }

        // ─── Joint positions + smoothing ──────────────────────────────────────

        private void UpdateJointPositions(
            OVRSkeleton skeleton,
            ref HandVisualData visual,
            int boneCount,
            ref Vector3[] smoothed,
            ref Vector3[] prevRaw,
            ref float[]   dxFilter)
        {
            if (smoothed == null || smoothed.Length != boneCount)
            {
                smoothed  = new Vector3[boneCount];
                prevRaw   = new Vector3[boneCount];
                dxFilter  = new float[boneCount];

                for (int i = 0; i < boneCount; i++)
                {
                    OVRBone bone = skeleton.Bones[i];
                    Vector3 start = (bone?.Transform != null) ? bone.Transform.position : Vector3.zero;
                    smoothed[i] = start;
                    prevRaw[i]  = start;
                }
            }

            float dt   = Time.deltaTime;
            float time = Time.time;

            for (int i = 0; i < boneCount && i < visual.JointSpheres.Length; i++)
            {
                if (visual.JointSpheres[i] == null) continue;

                OVRBone bone = skeleton.Bones[i];
                if (bone == null || bone.Transform == null) continue;

                Vector3 rawPos = bone.Transform.position;

                if (_useSmoothing)
                {
                    // One Euro Filter for optional smoothing
                    float rawSpeed = (dt > Mathf.Epsilon)
                        ? (rawPos - prevRaw[i]).magnitude / dt
                        : 0f;

                    float alphaD = OneEuroAlpha(dt, 1.0f);
                    dxFilter[i] = alphaD * rawSpeed + (1f - alphaD) * dxFilter[i];

                    float adaptiveCutoff = _oneEuroMinCutoff + _oneEuroBeta * dxFilter[i];
                    float alpha = OneEuroAlpha(dt, adaptiveCutoff);

                    smoothed[i] = Vector3.Lerp(smoothed[i], rawPos, alpha);
                }
                else
                {
                    // Use raw bone positions directly — OVR SDK already filters
                    smoothed[i] = rawPos;
                }

                prevRaw[i] = rawPos;

                visual.JointSpheres[i].transform.position = smoothed[i];

                float flexion = GetJointFlexion(smoothed, i, boneCount);
                float t = Mathf.Clamp01(flexion / MaxFlexionAngle);

                Color jointColor = TipBones.Contains(i)
                    ? Color.Lerp(_tipColor, _bentColor, t)
                    : Color.Lerp(_relaxedColor, _bentColor, t);

                float pulse = 1f;
                if (t > 0.2f)
                    pulse = 1f + Mathf.Sin(time * _pulseSpeed + i) * _pulseIntensity * t;

                float radius = GetJointRadius(i);
                visual.JointSpheres[i].transform.localScale = Vector3.one * radius * 2f * pulse;

                Renderer rend = visual.JointSpheres[i].GetComponent<Renderer>();
                if (rend != null && rend.material != null)
                {
                    rend.material.SetColor("_BaseColor", jointColor);
                    rend.material.SetColor("_EmissionColor", jointColor * _glowIntensity * pulse);
                }
            }
        }

        private static float OneEuroAlpha(float dt, float cutoff)
        {
            float tau = 1f / (2f * Mathf.PI * cutoff);
            return 1f / (1f + tau / Mathf.Max(dt, Mathf.Epsilon));
        }

        // ─── Bone lines ───────────────────────────────────────────────────────

        private void UpdateBoneLines(
            ref HandVisualData visual,
            int boneCount,
            ref Vector3[] smoothed)
        {
            if (smoothed == null) return;

            int connectionCount = BoneConnections.GetLength(0);
            for (int i = 0; i < connectionCount; i++)
            {
                if (visual.BoneLines[i] == null) continue;

                int fromIdx = BoneConnections[i, 0];
                int toIdx   = BoneConnections[i, 1];

                if (fromIdx >= boneCount || toIdx >= boneCount ||
                    fromIdx >= smoothed.Length || toIdx >= smoothed.Length)
                {
                    visual.BoneLines[i].enabled = false;
                    continue;
                }

                visual.BoneLines[i].enabled = true;
                visual.BoneLines[i].SetPosition(0, smoothed[fromIdx]);
                visual.BoneLines[i].SetPosition(1, smoothed[toIdx]);
            }
        }

        // ─── Angle labels (using same positions as spheres/lines) ──────────

        private void UpdateAngleLabelsFromSmoothed(
            ref HandVisualData visual,
            int boneCount,
            ref Vector3[] positions)
        {
            if (positions == null) return;

            int angleCount = AngleJoints.GetLength(0);
            for (int i = 0; i < angleCount; i++)
            {
                if (visual.AngleLabels[i] == null) continue;

                int proxIdx  = AngleJoints[i, 0];
                int pivotIdx = AngleJoints[i, 1];
                int distIdx  = AngleJoints[i, 2];

                if (proxIdx >= boneCount || pivotIdx >= boneCount || distIdx >= boneCount
                    || proxIdx >= positions.Length || pivotIdx >= positions.Length || distIdx >= positions.Length)
                {
                    visual.AngleLabels[i].gameObject.SetActive(false);
                    continue;
                }

                Vector3 proxPos  = positions[proxIdx];
                Vector3 pivotPos = positions[pivotIdx];
                Vector3 distPos  = positions[distIdx];

                Vector3 v1 = proxPos - pivotPos;
                Vector3 v2 = distPos - pivotPos;

                if (v1.sqrMagnitude < Mathf.Epsilon || v2.sqrMagnitude < Mathf.Epsilon)
                {
                    visual.AngleLabels[i].gameObject.SetActive(false);
                    continue;
                }

                float angle = Vector3.Angle(v1, v2);
                visual.AngleLabels[i].gameObject.SetActive(true);

                Vector3 midDir = (v1.normalized + v2.normalized).normalized;
                if (midDir.sqrMagnitude < Mathf.Epsilon) midDir = Vector3.up;

                visual.AngleLabels[i].transform.position =
                    pivotPos + midDir * _angleLabelOffset;

                if (_mainCamera != null)
                {
                    visual.AngleLabels[i].transform.rotation = Quaternion.LookRotation(
                        visual.AngleLabels[i].transform.position - _mainCamera.transform.position);
                }

                float t = Mathf.Clamp01(angle / MaxFlexionAngle);
                visual.AngleLabels[i].color = Color.Lerp(
                    new Color(0.7f, 1f, 0.8f, 1f),
                    new Color(1f, 0.8f, 0.4f, 1f), t);
                visual.AngleLabels[i].text = $"{angle:F0}\u00B0";
            }
        }

        // ─── Angle arcs (using same positions as spheres/lines) ──────────

        private void UpdateAngleArcsFromSmoothed(
            ref HandVisualData visual,
            int boneCount,
            ref Vector3[] positions)
        {
            if (positions == null) return;

            int angleCount = AngleJoints.GetLength(0);
            for (int i = 0; i < angleCount; i++)
            {
                if (visual.AngleArcs[i] == null) continue;

                int proxIdx  = AngleJoints[i, 0];
                int pivotIdx = AngleJoints[i, 1];
                int distIdx  = AngleJoints[i, 2];

                if (proxIdx >= boneCount || pivotIdx >= boneCount || distIdx >= boneCount
                    || proxIdx >= positions.Length || pivotIdx >= positions.Length || distIdx >= positions.Length)
                {
                    visual.AngleArcs[i].enabled = false;
                    continue;
                }

                Vector3 pivotPos = positions[pivotIdx];
                Vector3 v1 = (positions[proxIdx] - pivotPos).normalized;
                Vector3 v2 = (positions[distIdx] - pivotPos).normalized;

                if (v1.sqrMagnitude < Mathf.Epsilon || v2.sqrMagnitude < Mathf.Epsilon)
                {
                    visual.AngleArcs[i].enabled = false;
                    continue;
                }

                visual.AngleArcs[i].enabled = true;

                float angle = Vector3.Angle(v1, v2);
                float t     = Mathf.Clamp01(angle / MaxFlexionAngle);

                Vector3 cross = Vector3.Cross(v1, v2);
                if (cross.sqrMagnitude < Mathf.Epsilon) cross = Vector3.up;

                for (int s = 0; s <= ArcSegments; s++)
                {
                    float frac    = (float)s / ArcSegments;
                    Quaternion rot = Quaternion.AngleAxis(angle * frac, cross.normalized);
                    visual.AngleArcs[i].SetPosition(s, pivotPos + rot * v1 * AngleArcRadius);
                }

                Color arcCol = Color.Lerp(_relaxedColor, _bentColor, t);
                arcCol.a = 0.7f;
                visual.AngleArcs[i].startColor = arcCol;
                visual.AngleArcs[i].endColor   = arcCol;
            }
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        private float GetJointFlexion(Vector3[] positions, int boneIdx, int boneCount)
        {
            if (positions == null) return 0f;

            int angleCount = AngleJoints.GetLength(0);
            for (int i = 0; i < angleCount; i++)
            {
                if (AngleJoints[i, 1] != boneIdx) continue;

                int proxIdx = AngleJoints[i, 0];
                int distIdx = AngleJoints[i, 2];

                if (proxIdx >= boneCount || distIdx >= boneCount
                    || proxIdx >= positions.Length || distIdx >= positions.Length
                    || boneIdx >= positions.Length) continue;

                Vector3 v1 = positions[proxIdx]  - positions[boneIdx];
                Vector3 v2 = positions[distIdx]  - positions[boneIdx];

                if (v1.sqrMagnitude > Mathf.Epsilon && v2.sqrMagnitude > Mathf.Epsilon)
                    return Vector3.Angle(v1, v2);
            }

            return 0f;
        }

        private float GetJointRadius(int boneIndex)
        {
            if (boneIndex == 0)           return _wristRadius;
            if (TipBones.Contains(boneIndex)) return _tipRadius;
            return _jointRadius;
        }

        private void CleanupHand(ref HandVisualData visual)
        {
            if (visual.Root != null)
                Destroy(visual.Root);

            visual = default; // resets WasInitialized to false, nulls all arrays
        }
    }
}