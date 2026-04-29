using System.Collections.Generic;
using UnityEngine;

namespace AGVRSystem.Exercises
{
    /// <summary>
    /// Spread all fingers and hold for 2 seconds. Includes bilateral symmetry score.
    /// Uses OVRSkeleton bone angles when available; falls back to OVRHand pinch strengths
    /// when the skeleton is not initialized (e.g. after scene transitions).
    ///
    /// Anatomical finger spread ranges (wrist to MCP angle between adjacent fingers):
    ///   Index-Middle  : 8-15 deg relaxed, 15-25 deg max
    ///   Middle-Ring   : 6-12 deg relaxed, 10-18 deg max  (smallest range!)
    ///   Ring-Pinky    : 8-15 deg relaxed, 15-25 deg max
    ///   Thumb-Index   : 25-45 deg relaxed, 40-70 deg max
    ///
    /// Per-pair thresholds account for anatomical differences.
    /// </summary>
    public class FingerSpreadingExercise : BaseExercise
    {
        [Header("Per-Pair Thresholds (degrees)")]
        [Tooltip("Minimum angle between Index and Middle MCP rays.")]
        [SerializeField] private float _indexMiddleThreshold = 10f;

        [Tooltip("Minimum angle between Middle and Ring MCP rays (smallest anatomical range).")]
        [SerializeField] private float _middleRingThreshold = 7f;

        [Tooltip("Minimum angle between Ring and Pinky MCP rays.")]
        [SerializeField] private float _ringPinkyThreshold = 8f;

        [Tooltip("Minimum angle between Thumb and Index MCP rays.")]
        [SerializeField] private float _thumbIndexThreshold = 20f;

        [Header("Detection Settings")]
        [Tooltip("Grace period (seconds) before resetting hold timer on brief tracking jitter.")]
        [SerializeField] private float _jitterGracePeriod = 0.3f;

        [Tooltip("How many of the 4 finger pairs must pass for spread to count (1-4).")]
        [SerializeField, Range(1, 4)] private int _minPairsRequired = 3;

        private const float PinchSpreadThreshold = 0.3f;
        private const float HoldDuration = 2f;
        private const int DefaultTargetReps = 8;
        private const float SymmetryDenominator = 90f;
        private const int SpreadPairCount = 4;
        private const float DiagnosticInterval = 1.5f;
        private const float MinBoneVectorLengthSqr = 0.0001f;

        private static readonly OVRHand.HandFinger[] SpreadFingers =
        {
            OVRHand.HandFinger.Index,
            OVRHand.HandFinger.Middle,
            OVRHand.HandFinger.Ring,
            OVRHand.HandFinger.Pinky
        };

        private float _holdTimer;
        private float _failTimer;
        private float _diagTimer;
        private readonly List<float> _symmetryScores = new List<float>();

        // Current spread angles for HUD display (updated every frame)
        private float[] _currentLeftSpreads;
        private float[] _currentRightSpreads;

        /// <summary>Current hold progress (0-1) for HUD display.</summary>
        public float HoldProgress => IsActive ? Mathf.Clamp01(_holdTimer / HoldDuration) : 0f;

        /// <summary>
        /// Current spread angles (degrees) for display: [Index-Middle, Middle-Ring, Ring-Pinky, Thumb-Index].
        /// Returns the best available hand data. Null if no data available.
        /// </summary>
        public float[] CurrentSpreadAngles => _currentLeftSpreads ?? _currentRightSpreads;

        /// <summary>Left hand spread angles, or null if unavailable.</summary>
        public float[] LeftSpreadAngles => _currentLeftSpreads;

        /// <summary>Right hand spread angles, or null if unavailable.</summary>
        public float[] RightSpreadAngles => _currentRightSpreads;

        /// <summary>
        /// Returns a formatted string of current spread angles for HUD instruction text.
        /// Example: "IM: 12.3  MR: 8.1  RP: 10.5  TI: 35.2"
        /// </summary>
        public string GetSpreadAngleDisplay()
        {
            float[] angles = CurrentSpreadAngles;
            if (angles == null || angles.Length < SpreadPairCount)
                return "Spread your fingers...";

            return $"IM: {angles[0]:F1}\u00b0  MR: {angles[1]:F1}\u00b0  RP: {angles[2]:F1}\u00b0  TI: {angles[3]:F1}\u00b0";
        }

        public override void StartExercise()
        {
            ResetBase();
            TargetReps = DefaultTargetReps;
            _holdTimer = 0f;
            _failTimer = 0f;
            _diagTimer = 0f;
            _symmetryScores.Clear();
            Debug.Log($"[FingerSpreadingExercise] Started — thresholds: " +
                $"IM={_indexMiddleThreshold}° MR={_middleRingThreshold}° " +
                $"RP={_ringPinkyThreshold}° TI={_thumbIndexThreshold}° " +
                $"minPairs={_minPairsRequired} grace={_jitterGracePeriod}s reps={DefaultTargetReps}");
        }

        public override void StopExercise()
        {
            IsActive = false;
        }

        public override float Evaluate()
        {
            if (_symmetryScores.Count == 0)
                return 0f;

            float sum = 0f;
            for (int i = 0; i < _symmetryScores.Count; i++)
                sum += _symmetryScores[i];

            return sum / _symmetryScores.Count;
        }

        private void Update()
        {
            if (!IsActive)
                return;

            var manager = HandTrackingManager.Instance;
            if (manager == null)
                return;

            // Prefer skeleton-based detection (direct bone positions).
            // Fall back to pinch-based detection only when no skeleton is available.
            if (IsSkeletonUsable(manager))
                UpdateWithSkeleton(manager);
            else
                UpdateWithPinch(manager);
        }

        /// <summary>
        /// Returns true when at least one skeleton has an initialized bone list.
        /// </summary>
        private bool IsSkeletonUsable(HandTrackingManager manager)
        {
            return IsSkeletonReady(manager.LeftSkeleton)
                || IsSkeletonReady(manager.RightSkeleton);
        }

        private bool IsSkeletonReady(OVRSkeleton skeleton)
        {
            return skeleton != null
                && skeleton.IsInitialized
                && skeleton.Bones != null
                && skeleton.Bones.Count > 0;
        }

        // ── Path A: skeleton-based (most accurate) ───────────────────────────

        private void UpdateWithSkeleton(HandTrackingManager manager)
        {
            bool leftReady = IsSkeletonReady(manager.LeftSkeleton);
            bool rightReady = IsSkeletonReady(manager.RightSkeleton);

            if (!leftReady && !rightReady)
            {
                ApplyFailure();
                return;
            }

            // Compute spreads directly from bone positions (no caching).
            // Returns null if bone data is invalid (positions at origin, etc.)
            float[] leftSpreads = leftReady ? ComputeSpreadDirect(manager.LeftSkeleton) : null;
            float[] rightSpreads = rightReady ? ComputeSpreadDirect(manager.RightSkeleton) : null;

            // Cache for HUD display
            _currentLeftSpreads = leftSpreads;
            _currentRightSpreads = rightSpreads;

            // If both skeletons are "ready" but returned null data, fall to pinch path
            if (leftSpreads == null && rightSpreads == null)
            {
                UpdateWithPinch(manager);
                return;
            }

            bool leftPass = leftSpreads == null || CheckSpreadPass(leftSpreads);
            bool rightPass = rightSpreads == null || CheckSpreadPass(rightSpreads);
            bool hasAnyData = leftSpreads != null || rightSpreads != null;
            bool allPass = hasAnyData && leftPass && rightPass;

            // Diagnostic logging
            LogDiagnostics(leftSpreads, rightSpreads, leftPass, rightPass);

            if (allPass)
            {
                _failTimer = 0f;
                _holdTimer += Time.deltaTime;

                if (_holdTimer >= HoldDuration)
                {
                    float leftAvg = leftSpreads != null ? Average(leftSpreads) : 0f;
                    float rightAvg = rightSpreads != null ? Average(rightSpreads) : 0f;
                    float symmetry = (leftSpreads != null && rightSpreads != null)
                        ? Mathf.Clamp01(1f - Mathf.Abs(leftAvg - rightAvg) / SymmetryDenominator)
                        : 0.8f;

                    _symmetryScores.Add(symmetry);
                    RegisterRep(symmetry);
                    _holdTimer = 0f;

                    string ls = FormatSpreads(leftSpreads);
                    string rs = FormatSpreads(rightSpreads);
                    Debug.Log($"[FingerSpreadingExercise] REP COMPLETE! L={ls} R={rs} sym={symmetry:F2}");
                }
            }
            else
            {
                ApplyFailure();
            }
        }

        /// <summary>
        /// Applies failure with jitter grace period — brief threshold violations
        /// don't immediately reset the hold timer.
        /// </summary>
        private void ApplyFailure()
        {
            _failTimer += Time.deltaTime;
            if (_failTimer >= _jitterGracePeriod)
            {
                _holdTimer = 0f;
            }
        }

        /// <summary>
        /// Computes spread angles using DIRECT bone positions from the skeleton.
        /// Returns null if bone data is invalid (zero-length vectors, missing bones).
        /// Returns float[4]: Index-Middle, Middle-Ring, Ring-Pinky, Thumb-Index.
        /// </summary>
        private float[] ComputeSpreadDirect(OVRSkeleton skeleton)
        {
            if (skeleton == null || !skeleton.IsInitialized || skeleton.Bones == null)
                return null;

            int wristIdx = (int)OVRSkeleton.BoneId.Hand_WristRoot;
            int thumbIdx = (int)OVRSkeleton.BoneId.Hand_Thumb0;
            int indexIdx = (int)OVRSkeleton.BoneId.Hand_Index1;
            int middleIdx = (int)OVRSkeleton.BoneId.Hand_Middle1;
            int ringIdx = (int)OVRSkeleton.BoneId.Hand_Ring1;
            int pinkyIdx = (int)OVRSkeleton.BoneId.Hand_Pinky0;

            int boneCount = skeleton.Bones.Count;
            if (wristIdx >= boneCount || pinkyIdx >= boneCount)
                return null;

            Vector3 wristPos = GetBonePosition(skeleton, wristIdx);
            Vector3 thumbPos = GetBonePosition(skeleton, thumbIdx);
            Vector3 indexPos = GetBonePosition(skeleton, indexIdx);
            Vector3 middlePos = GetBonePosition(skeleton, middleIdx);
            Vector3 ringPos = GetBonePosition(skeleton, ringIdx);
            Vector3 pinkyPos = GetBonePosition(skeleton, pinkyIdx);

            Vector3 toThumb = thumbPos - wristPos;
            Vector3 toIndex = indexPos - wristPos;
            Vector3 toMiddle = middlePos - wristPos;
            Vector3 toRing = ringPos - wristPos;
            Vector3 toPinky = pinkyPos - wristPos;

            // Validate that bone positions are real (not all at origin/same point)
            if (toIndex.sqrMagnitude < MinBoneVectorLengthSqr
                || toMiddle.sqrMagnitude < MinBoneVectorLengthSqr
                || toRing.sqrMagnitude < MinBoneVectorLengthSqr
                || toPinky.sqrMagnitude < MinBoneVectorLengthSqr)
            {
                return null; // Return null (not zeros) so caller knows data is invalid
            }

            return new float[]
            {
                Vector3.Angle(toIndex, toMiddle),   // [0] Index-Middle
                Vector3.Angle(toMiddle, toRing),     // [1] Middle-Ring
                Vector3.Angle(toRing, toPinky),      // [2] Ring-Pinky
                Vector3.Angle(toThumb, toIndex)      // [3] Thumb-Index
            };
        }

        private Vector3 GetBonePosition(OVRSkeleton skeleton, int boneIndex)
        {
            var bone = skeleton.Bones[boneIndex];
            return (bone != null && bone.Transform != null)
                ? bone.Transform.position
                : Vector3.zero;
        }

        /// <summary>
        /// Checks if spread angles meet per-pair thresholds.
        /// Uses _minPairsRequired for flexibility (default 3 of 4 pairs must pass).
        /// </summary>
        private bool CheckSpreadPass(float[] spreads)
        {
            if (spreads == null || spreads.Length < SpreadPairCount)
                return false;

            int passingPairs = 0;

            if (spreads[0] >= _indexMiddleThreshold) passingPairs++;
            if (spreads[1] >= _middleRingThreshold) passingPairs++;
            if (spreads[2] >= _ringPinkyThreshold) passingPairs++;
            if (spreads[3] >= _thumbIndexThreshold) passingPairs++;

            return passingPairs >= _minPairsRequired;
        }

        // ── Path B: pinch-based fallback ─────────────────────────────────────

        private void UpdateWithPinch(HandTrackingManager manager)
        {
            bool leftTracked = manager.LeftHand != null && manager.IsLeftTracked;
            bool rightTracked = manager.RightHand != null && manager.IsRightTracked;

            if (!leftTracked && !rightTracked)
            {
                ApplyFailure();
                return;
            }

            float[] leftPinch = SamplePinch(manager.LeftHand, leftTracked);
            float[] rightPinch = SamplePinch(manager.RightHand, rightTracked);

            // For spread detection: all fingers should have LOW pinch strength
            // (fingers away from thumb = not pinching)
            bool leftSpread = leftTracked && AllBelow(leftPinch, PinchSpreadThreshold);
            bool rightSpread = rightTracked && AllBelow(rightPinch, PinchSpreadThreshold);

            bool spreadDetected = (leftTracked && rightTracked)
                ? (leftSpread && rightSpread)
                : (leftSpread || rightSpread);

            if (spreadDetected)
            {
                _failTimer = 0f;
                _holdTimer += Time.deltaTime;

                if (_holdTimer >= HoldDuration)
                {
                    float symmetry = PinchSymmetry(leftPinch, rightPinch, leftTracked, rightTracked);
                    _symmetryScores.Add(symmetry);
                    RegisterRep(symmetry);
                    _holdTimer = 0f;
                    Debug.Log($"[FingerSpreadingExercise] REP (pinch path)! sym={symmetry:F2}");
                }
            }
            else
            {
                ApplyFailure();
            }
        }

        /// <summary>
        /// Samples index/middle/ring/pinky pinch strength for one hand.
        /// </summary>
        private float[] SamplePinch(OVRHand hand, bool tracked)
        {
            float[] values = new float[SpreadFingers.Length];
            if (!tracked || hand == null)
                return values;

            for (int i = 0; i < SpreadFingers.Length; i++)
                values[i] = hand.GetFingerPinchStrength(SpreadFingers[i]);

            return values;
        }

        /// <summary>
        /// Computes bilateral symmetry from pinch profiles (1 = identical, 0 = opposite).
        /// </summary>
        private float PinchSymmetry(float[] left, float[] right, bool leftTracked, bool rightTracked)
        {
            if (!leftTracked || !rightTracked)
                return 0.8f;

            float diffSum = 0f;
            for (int i = 0; i < SpreadFingers.Length; i++)
                diffSum += Mathf.Abs(left[i] - right[i]);

            return Mathf.Clamp01(1f - diffSum / SpreadFingers.Length);
        }

        private bool AllBelow(float[] values, float threshold)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] >= threshold)
                    return false;
            }
            return true;
        }

        private float Average(float[] values)
        {
            float sum = 0f;
            for (int i = 0; i < values.Length; i++)
                sum += values[i];
            return sum / Mathf.Max(1f, values.Length);
        }

        private string FormatSpreads(float[] spreads)
        {
            if (spreads == null) return "N/A";
            return $"[IM={spreads[0]:F1} MR={spreads[1]:F1} RP={spreads[2]:F1} TI={spreads[3]:F1}]";
        }

        private void LogDiagnostics(float[] leftSpreads, float[] rightSpreads, bool leftPass, bool rightPass)
        {
            _diagTimer += Time.deltaTime;
            if (_diagTimer < DiagnosticInterval) return;
            _diagTimer = 0f;

            string ls = FormatSpreads(leftSpreads);
            string rs = FormatSpreads(rightSpreads);
            Debug.Log($"[FingerSpreadingExercise] DIAG: L={ls} pass={leftPass} | R={rs} pass={rightPass} | " +
                $"hold={_holdTimer:F1}/{HoldDuration}s fail={_failTimer:F2}s rep={CurrentReps}/{TargetReps}");
        }
    }
}
