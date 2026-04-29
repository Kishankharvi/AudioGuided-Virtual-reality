using UnityEngine;

namespace AGVRSystem.Exercises
{
    /// <summary>
    /// Pinch-and-Hold with a small virtual sphere.
    ///
    /// The patient must bring their thumb-tip and index-tip together around a small
    /// sphere and sustain the contact for the hold duration. Detection combines:
    ///   1. OVRSkeleton bone-distance: thumb-to-index Euclidean distance below threshold
    ///   2. OVRHand optical pinch strength confirmation
    ///   3. Proximity: fingertip midpoint must be within reach of the sphere centre
    ///
    /// The sphere responds visually: it shifts from cool blue (idle) to warm orange
    /// (held) and scales inward proportionally to convey pinch pressure. On a
    /// successful hold the sphere briefly pulses back to full size as feedback.
    ///
    /// Bilateral: either hand may hold; the hand whose fingertip midpoint is
    /// closest to the sphere wins each frame if both qualify simultaneously.
    /// DifficultyMultiplier scales the required pinch strength so the exercise
    /// adapts with the shared BaseExercise rolling-window algorithm.
    ///
    /// Attach to a GameObject in the RehabSession scene that also has a small sphere
    /// child GameObject assigned to _sphereTransform.
    /// </summary>
    public class PinchHoldSphereExercise : BaseExercise
    {
        [Header("Sphere Reference")]
        [Tooltip("Transform of the small sphere the patient must pinch.")]
        [SerializeField] private Transform _sphereTransform;
        [SerializeField] private float _sphereDisplayRadius = 0.015f; // visual size hint

        [Header("Detection Thresholds")]
        [Tooltip("Maximum thumb-tip to index-tip distance (m) to count as pinching.")]
        [SerializeField] private float _pinchDistanceThreshold = 0.035f;
        [Tooltip("Minimum OVRHand pinch strength (Index) for optical confirmation.")]
        [SerializeField] private float _pinchStrengthThreshold = 0.65f;
        [Tooltip("Max distance (m) from fingertip midpoint to sphere centre.")]
        [SerializeField] private float _proximityThreshold = 0.06f;

        [Header("Hold Settings")]
        [SerializeField] private float _holdDuration  = 2.0f;
        private const int   DefaultTargetReps          = 8;
        private const float DiagnosticInterval         = 2f;

        [Header("Sphere Visuals")]
        [SerializeField] private Color _idleColor  = new Color(0.55f, 0.82f, 1.00f, 1f);
        [SerializeField] private Color _holdColor  = new Color(1.00f, 0.55f, 0.20f, 1f);
        [SerializeField] private Color _doneColor  = new Color(0.35f, 0.90f, 0.45f, 1f);
        [SerializeField] private float _maxSqueezeScale = 0.78f; // fraction of original

        // ── Runtime state ──────────────────────────────────────────────────────

        private float    _holdTimer;
        private bool     _isHolding;
        private string   _activeHandLabel = "";
        private float    _diagTimer;

        private Renderer _sphereRenderer;
        private Vector3  _originalSphereScale;
        private float    _pulseTimer; // brief expand after rep
        private const float PulseDuration = 0.35f;
        private bool    _pulsing;

        // ── Public properties ──────────────────────────────────────────────────

        /// <summary>Hold progress (0-1) for HUD display.</summary>
        public float HoldProgress =>
            _isHolding ? Mathf.Clamp01(_holdTimer / _holdDuration) : 0f;

        /// <summary>Which hand is currently holding ("Left"/"Right"/"").</summary>
        public string ActiveHandLabel => _activeHandLabel;

        // ── BaseExercise interface ─────────────────────────────────────────────

        public override void StartExercise()
        {
            ResetBase();
            TargetReps       = DefaultTargetReps;
            _holdTimer       = 0f;
            _isHolding       = false;
            _activeHandLabel = "";
            _diagTimer       = 0f;
            _pulsing         = false;

            if (_sphereTransform != null)
            {
                _sphereRenderer      = _sphereTransform.GetComponent<Renderer>();
                _originalSphereScale = _sphereTransform.localScale;
                ApplySphereColor(_idleColor);
            }

            Debug.Log("[PinchHoldSphereExercise] Started — " +
                $"pinchDist={_pinchDistanceThreshold * 100f:F1} cm  " +
                $"strength≥{_pinchStrengthThreshold:F2}  " +
                $"proximity={_proximityThreshold * 100f:F1} cm  " +
                $"hold={_holdDuration}s  reps={DefaultTargetReps}  bilateral=true");
        }

        public override void StopExercise()
        {
            IsActive = false;
            RestoreSphereScale();
        }

        public override float Evaluate()
        {
            return CurrentReps / (float)Mathf.Max(1, TargetReps);
        }

        // ── Update ─────────────────────────────────────────────────────────────

        private void Update()
        {
            if (!IsActive) return;

            AnimatePulse();

            var manager = HandTrackingManager.Instance;
            if (manager == null || _sphereTransform == null)
            {
                if (_isHolding) BreakHold();
                return;
            }

            bool leftOk  = manager.LeftHand  != null && manager.IsLeftTracked
                        && manager.LeftSkeleton  != null;
            bool rightOk = manager.RightHand != null && manager.IsRightTracked
                        && manager.RightSkeleton != null;

            if (!leftOk && !rightOk)
            {
                if (_isHolding) BreakHold();
                return;
            }

            float leftDist  = float.MaxValue;
            float rightDist = float.MaxValue;
            bool  leftOk2   = leftOk  && IsPinchingNearSphere(
                manager.LeftHand,  manager.LeftSkeleton,  out leftDist);
            bool  rightOk2  = rightOk && IsPinchingNearSphere(
                manager.RightHand, manager.RightSkeleton, out rightDist);

            bool   anyPinching;
            string label;

            if (leftOk2 && rightOk2)
            {
                anyPinching = true;
                label       = leftDist <= rightDist ? "Left" : "Right";
            }
            else if (leftOk2)  { anyPinching = true;  label = "Left";  }
            else if (rightOk2) { anyPinching = true;  label = "Right"; }
            else               { anyPinching = false; label = "";      }

            if (anyPinching)
            {
                if (!_isHolding)
                {
                    _isHolding       = true;
                    _holdTimer       = 0f;
                    _activeHandLabel = label;
                    ApplySphereColor(_holdColor);
                    Debug.Log($"[PinchHoldSphereExercise] Hold STARTED ({label})");
                }

                _holdTimer += Time.deltaTime;

                // Squeeze the sphere inward as hold progresses
                float t = Mathf.Clamp01(_holdTimer / _holdDuration);
                float scaleF = Mathf.Lerp(1f, _maxSqueezeScale, t);
                if (_sphereTransform != null)
                    _sphereTransform.localScale = _originalSphereScale * scaleF;

                if (_holdTimer >= _holdDuration)
                {
                    RegisterRep(1f); // sustained hold = perfect accuracy
                    _isHolding  = false;
                    _holdTimer  = 0f;
                    ApplySphereColor(_doneColor);
                    StartPulse();
                    Debug.Log($"[PinchHoldSphereExercise] REP! " +
                        $"reps={CurrentReps}/{TargetReps}  hand={_activeHandLabel}");
                    _activeHandLabel = "";
                }
            }
            else if (_isHolding)
            {
                BreakHold();
            }

            // Diagnostic logging
            _diagTimer += Time.deltaTime;
            if (_diagTimer >= DiagnosticInterval)
            {
                _diagTimer = 0f;
                Debug.Log($"[PinchHoldSphereExercise] DIAG: " +
                    $"holding={_isHolding}  timer={_holdTimer:F1}/{_holdDuration}s  " +
                    $"reps={CurrentReps}/{TargetReps}  hand={_activeHandLabel}");
            }
        }

        // ── Hold helpers ───────────────────────────────────────────────────────

        private void BreakHold()
        {
            _isHolding       = false;
            _holdTimer       = 0f;
            _activeHandLabel = "";
            ApplySphereColor(_idleColor);
            RestoreSphereScale();
            Debug.Log("[PinchHoldSphereExercise] Hold BROKEN — pinch lost or hand moved away.");
        }

        // ── Pinch-near-sphere detection ────────────────────────────────────────

        /// <summary>
        /// Returns true when <paramref name="hand"/> is performing a valid pinch
        /// close enough to the sphere. Uses both bone distance and optical
        /// pinch-strength for redundant, robust detection.
        /// </summary>
        private bool IsPinchingNearSphere(
            OVRHand hand, OVRSkeleton skeleton, out float distToSphere)
        {
            distToSphere = float.MaxValue;

            Transform thumbTip = FindBone(skeleton, OVRSkeleton.BoneId.Hand_ThumbTip);
            Transform indexTip = FindBone(skeleton, OVRSkeleton.BoneId.Hand_IndexTip);

            if (thumbTip == null || indexTip == null) return false;

            // Midpoint between thumb and index tip = pinch contact point
            Vector3 midpoint  = (thumbTip.position + indexTip.position) * 0.5f;
            distToSphere      = Vector3.Distance(midpoint, _sphereTransform.position);

            float fingerDist   = Vector3.Distance(thumbTip.position, indexTip.position);
            float optStrength  = hand.GetFingerPinchStrength(OVRHand.HandFinger.Index);

            // Scale required strength by difficulty; clamp so it never exceeds 1
            float requiredStr  = Mathf.Clamp(
                _pinchStrengthThreshold * DifficultyMultiplier, 0.3f, 0.95f);

            return fingerDist  <= _pinchDistanceThreshold
                && optStrength >= requiredStr
                && distToSphere <= _proximityThreshold;
        }

        // ── Sphere visual helpers ──────────────────────────────────────────────

        private void ApplySphereColor(Color color)
        {
            if (_sphereRenderer != null)
                _sphereRenderer.material.color = color;
        }

        private void RestoreSphereScale()
        {
            if (_sphereTransform != null && _originalSphereScale != Vector3.zero)
                _sphereTransform.localScale = _originalSphereScale;
        }

        private void StartPulse()
        {
            _pulsing     = true;
            _pulseTimer  = 0f;
            RestoreSphereScale();
        }

        private void AnimatePulse()
        {
            if (!_pulsing || _sphereTransform == null) return;

            _pulseTimer += Time.deltaTime;
            float t     = _pulseTimer / PulseDuration;

            if (t >= 1f)
            {
                _pulsing = false;
                RestoreSphereScale();
                ApplySphereColor(_idleColor);
                return;
            }

            // Quick expand-then-return (overshoot and settle)
            float scale = 1f + Mathf.Sin(t * Mathf.PI) * 0.25f;
            _sphereTransform.localScale = _originalSphereScale * scale;
        }

        // ── Bone lookup ────────────────────────────────────────────────────────

        private static Transform FindBone(OVRSkeleton skeleton,
                                          OVRSkeleton.BoneId id)
        {
            if (skeleton.Bones == null) return null;
            foreach (var bone in skeleton.Bones)
                if (bone != null && bone.Transform != null && bone.Id == id)
                    return bone.Transform;
            return null;
        }

        private void OnDestroy()
        {
            RestoreSphereScale();
        }
    }
}
