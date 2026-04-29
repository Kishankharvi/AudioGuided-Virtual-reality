using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using AGVRSystem.Interaction;

namespace AGVRSystem.Exercises
{
    /// <summary>
    /// User grabs an object and holds for 3 seconds. Includes a grace period
    /// so brief tracking glitches don't reset the entire hold timer.
    /// Supports both XRI (XRGrabInteractable) and OVR hand tracking (HandGrabber).
    /// Uses curl-based grip strength for accurate detection of actual finger curl,
    /// not just pinch (thumb-to-fingertip distance).
    /// </summary>
    public class GripHoldExercise : BaseExercise
    {
        [Header("XRI Grab (optional)")]
        [SerializeField] private XRGrabInteractable _grabbable;

        [Header("Hand Tracking Grab")]
        [Tooltip("If null, ANY grabbed exercise object counts. If set, only this specific object counts.")]
        [SerializeField] private Rigidbody _targetRigidbody;

        [Tooltip("Accept any exercise object grab (when target is not specified).")]
        [SerializeField] private bool _acceptAnyObject = true;

        [Header("Detection Settings")]
        [Tooltip("Seconds of non-grip before the hold timer fully resets. Prevents tracking glitch resets.")]
        [SerializeField] private float _holdGracePeriod = 0.5f;

        [Tooltip("Minimum curl-based grip strength (0-1) required to count as holding.")]
        [SerializeField] private float _minGripStrength = 0.3f;

        private const float HoldDuration = 3f;
        private const int DefaultTargetReps = 5;
        private const float DiagnosticInterval = 2f;

        private int _successfulGrabs;
        private int _attempts;
        private float _grabTimer;
        private float _releaseTimer;
        private bool _isHolding;
        private float _diagTimer;

        // Track which grabber is holding the target for per-hand grip monitoring
        private HandGrabber _activeGrabber;
        private HandGrabber[] _handGrabbers;

        /// <summary>
        /// Current hold progress normalized to 0-1 for HUD display.
        /// </summary>
        public float HoldProgress => _isHolding ? Mathf.Clamp01(_grabTimer / (HoldDuration * DifficultyMultiplier)) : 0f;

        /// <summary>
        /// Current real-time grip strength of the grabbing hand (0-1).
        /// </summary>
        public float CurrentGripStrength { get; private set; }

        public override void StartExercise()
        {
            ResetBase();
            TargetReps = DefaultTargetReps;
            _successfulGrabs = 0;
            _attempts = 0;
            _grabTimer = 0f;
            _releaseTimer = 0f;
            _isHolding = false;
            _diagTimer = 0f;

            if (_grabbable != null)
            {
                _grabbable.selectEntered.AddListener(OnGrabbed);
                _grabbable.selectExited.AddListener(OnReleased);
            }

            // Auto-find target rigidbody (only if single-target mode)
            if (!_acceptAnyObject)
            {
                if (_targetRigidbody == null && _grabbable != null)
                    _targetRigidbody = _grabbable.GetComponent<Rigidbody>();

                if (_targetRigidbody == null)
                {
                    var ctrl = FindAnyObjectByType<ExerciseObjectController>();
                    if (ctrl != null)
                    {
                        _targetRigidbody = ctrl.GetComponent<Rigidbody>();
                        if (_targetRigidbody != null)
                            Debug.Log($"[GripHoldExercise] Auto-found target Rigidbody on '{ctrl.gameObject.name}'.");
                    }
                }
            }

            if (_targetRigidbody == null && !_acceptAnyObject)
                Debug.LogWarning("[GripHoldExercise] No target Rigidbody found. " +
                    "Set _acceptAnyObject=true or assign _targetRigidbody.");

            _handGrabbers = FindObjectsByType<HandGrabber>(FindObjectsSortMode.None);
            foreach (var grabber in _handGrabbers)
            {
                grabber.OnGrabStarted += OnHandGrabStarted;
                grabber.OnGrabEnded += OnHandGrabEnded;
            }

            Debug.Log($"[GripHoldExercise] Started. Found {_handGrabbers.Length} HandGrabbers, " +
                $"acceptAny={_acceptAnyObject} target={(_targetRigidbody != null ? _targetRigidbody.name : "any")} " +
                $"grace={_holdGracePeriod}s minGrip={_minGripStrength}");
        }

        public override void StopExercise()
        {
            IsActive = false;
            _isHolding = false;

            if (_grabbable != null)
            {
                _grabbable.selectEntered.RemoveListener(OnGrabbed);
                _grabbable.selectExited.RemoveListener(OnReleased);
            }

            UnsubscribeHandGrabbers();
        }

        public override float Evaluate()
        {
            return _successfulGrabs / Mathf.Max(1f, _attempts);
        }

        private void Update()
        {
            if (!IsActive)
                return;

            if (_isHolding)
            {
                // Monitor continuous grip strength using curl-based detection
                float grip = GetActiveHandGripStrength();
                CurrentGripStrength = grip;

                bool gripSufficient = grip >= _minGripStrength;

                if (gripSufficient)
                {
                    _releaseTimer = 0f;
                    _grabTimer += Time.deltaTime;

                    if (_grabTimer >= HoldDuration * DifficultyMultiplier)
                    {
                        _successfulGrabs++;
                        _isHolding = false;
                        _activeGrabber = null;
                        float accuracy = _successfulGrabs / (float)Mathf.Max(1, _attempts);
                        RegisterRep(accuracy);
                        Debug.Log($"[GripHoldExercise] REP COMPLETE! grip={grip:F2} " +
                            $"success={_successfulGrabs}/{_attempts}");
                    }
                }
                else
                {
                    // Grace period: don't immediately reset on brief grip loss
                    _releaseTimer += Time.deltaTime;
                    if (_releaseTimer >= _holdGracePeriod)
                    {
                        // Grip was lost for too long — reset hold
                        _isHolding = false;
                        _grabTimer = 0f;
                        _releaseTimer = 0f;
                        _activeGrabber = null;
                        Debug.Log($"[GripHoldExercise] Hold lost after grace period. grip={grip:F2}");
                    }
                    // During grace period, don't advance the timer but don't reset either
                }
            }

            // Diagnostics
            _diagTimer += Time.deltaTime;
            if (_diagTimer >= DiagnosticInterval)
            {
                _diagTimer = 0f;
                Debug.Log($"[GripHoldExercise] DIAG: holding={_isHolding} grip={CurrentGripStrength:F2} " +
                    $"timer={_grabTimer:F1}/{HoldDuration * DifficultyMultiplier:F1}s " +
                    $"release={_releaseTimer:F2}s reps={CurrentReps}/{TargetReps} " +
                    $"grabber={(_activeGrabber != null ? _activeGrabber.name : "none")}");
            }
        }

        /// <summary>
        /// Gets curl-based grip strength from the specific hand holding the target.
        /// Returns 0-1 range.
        /// </summary>
        private float GetActiveHandGripStrength()
        {
            var manager = HandTrackingManager.Instance;
            if (manager == null)
                return 0.5f;

            if (_activeGrabber != null)
            {
                bool isLeft = _activeGrabber.name.Contains("Left", System.StringComparison.OrdinalIgnoreCase);
                OVRHand hand = isLeft ? manager.LeftHand : manager.RightHand;
                OVRSkeleton skeleton = isLeft ? manager.LeftSkeleton : manager.RightSkeleton;

                if (hand != null && hand.IsTracked)
                {
                    return manager.GetFingerCurlGripStrength(hand, skeleton) / 100f;
                }
            }

            // Fallback: check both hands
            float best = 0f;
            if (manager.LeftHand != null && manager.IsLeftTracked)
                best = Mathf.Max(best, manager.GetFingerCurlGripStrength(
                    manager.LeftHand, manager.LeftSkeleton) / 100f);
            if (manager.RightHand != null && manager.IsRightTracked)
                best = Mathf.Max(best, manager.GetFingerCurlGripStrength(
                    manager.RightHand, manager.RightSkeleton) / 100f);

            return best;
        }

        // --- XRI events ---
        private void OnGrabbed(SelectEnterEventArgs args)
        {
            if (!IsActive) return;
            BeginHold();
        }

        private void OnReleased(SelectExitEventArgs args)
        {
            if (!IsActive) return;
            EndHold();
        }

        // --- HandGrabber events ---
        private void OnHandGrabStarted(Rigidbody grabbedRb)
        {
            if (!IsActive) return;

            // Accept any object if _acceptAnyObject is true, otherwise check target
            bool isTarget = _acceptAnyObject
                || _targetRigidbody == null
                || grabbedRb == _targetRigidbody;
            if (!isTarget) return;

            // Track the actual grabbed rigidbody for grip strength monitoring
            if (_targetRigidbody == null || _acceptAnyObject)
                _targetRigidbody = grabbedRb;

            // Track which grabber grabbed the target
            if (_handGrabbers != null)
            {
                foreach (var grabber in _handGrabbers)
                {
                    if (grabber != null && grabber.GrabbedObject == grabbedRb)
                    {
                        _activeGrabber = grabber;
                        break;
                    }
                }
            }

            BeginHold();
        }

        private void OnHandGrabEnded(Rigidbody releasedRb)
        {
            if (!IsActive) return;
            if (_targetRigidbody != null && releasedRb != _targetRigidbody) return;
            EndHold();
        }

        private void BeginHold()
        {
            if (_isHolding) return;
            _isHolding = true;
            _grabTimer = 0f;
            _releaseTimer = 0f;
            _attempts++;
            Debug.Log($"[GripHoldExercise] Hold started. Attempt #{_attempts}");
        }

        private void EndHold()
        {
            // Don't immediately end — let the Update grace period handle it
            // This prevents instant release on brief tracking loss
            if (_holdGracePeriod > 0f && _isHolding)
            {
                // The Update loop will handle the grace period
                return;
            }

            _isHolding = false;
            _grabTimer = 0f;
            _releaseTimer = 0f;
            _activeGrabber = null;
        }

        private void UnsubscribeHandGrabbers()
        {
            if (_handGrabbers == null) return;

            foreach (var grabber in _handGrabbers)
            {
                if (grabber != null)
                {
                    grabber.OnGrabStarted -= OnHandGrabStarted;
                    grabber.OnGrabEnded -= OnHandGrabEnded;
                }
            }
        }

        private void OnDestroy()
        {
            if (_grabbable != null)
            {
                _grabbable.selectEntered.RemoveListener(OnGrabbed);
                _grabbable.selectExited.RemoveListener(OnReleased);
            }

            UnsubscribeHandGrabbers();
        }
    }
}
