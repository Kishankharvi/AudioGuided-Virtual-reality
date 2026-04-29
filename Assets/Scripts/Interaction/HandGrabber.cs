using System;
using System.Collections.Generic;
using UnityEngine;

namespace AGVRSystem.Interaction
{
    /// <summary>
    /// Hand-tracking-based grabber with accurate finger-aware grab detection.
    /// Uses multiple grab strategies:
    ///   - Pinch grab: thumb + index fingertips close around a small object
    ///   - Palm grab: full hand grip wrapping around a larger object
    ///
    /// Tracks whether the grab was initiated by pinch or grip and uses the
    /// SAME signal type for release detection, preventing "sticky" objects.
    ///
    /// Attach to each hand visual (LeftHandVisual / RightHandVisual).
    /// </summary>
    public class HandGrabber : MonoBehaviour
    {
        [Header("Grab Detection")]
        [Tooltip("Radius for detecting nearby grabbable objects (meters).")]
        [SerializeField] private float _grabRadius = 0.06f;

        [Tooltip("Pinch strength threshold to start a grab (0-1).")]
        [SerializeField] private float _pinchThreshold = 0.75f;

        [Tooltip("Pinch release threshold — lower than grab to add hysteresis.")]
        [SerializeField] private float _pinchReleaseThreshold = 0.4f;

        [Tooltip("Minimum grip strength (average of all finger pinches) for palm grabs.")]
        [SerializeField] private float _gripThreshold = 0.6f;

        [Tooltip("Palm grip release threshold.")]
        [SerializeField] private float _gripReleaseThreshold = 0.35f;

        [Tooltip("If hand moves further than this from the grab point, force release (meters).")]
        [SerializeField] private float _maxGrabDistance = 0.15f;

        [SerializeField] private LayerMask _grabLayerMask = ~0;

        [Header("Tracking")]
        [Tooltip("How tightly the object follows the hand. 1.0 = instant snap, 0.5 = smooth.")]
        [SerializeField, Range(0.3f, 1f)] private float _followTightness = 0.85f;

        [Tooltip("Maximum speed the grabbed object can move (m/s). Prevents teleporting on tracking glitches.")]
        [SerializeField] private float _maxFollowSpeed = 8f;

        [Header("Debug")]
        [SerializeField] private bool _showDebugSphere;
        [SerializeField] private float _diagnosticInterval = 2f;

        /// <summary>Fired when a grab starts. Passes the grabbed Rigidbody.</summary>
        public event Action<Rigidbody> OnGrabStarted;

        /// <summary>Fired when a grab ends. Passes the released Rigidbody.</summary>
        public event Action<Rigidbody> OnGrabEnded;

        /// <summary>The currently grabbed object, or null.</summary>
        public Rigidbody GrabbedObject => _grabbedObject;

        /// <summary>Whether the hand is currently grabbing an object.</summary>
        public bool IsGrabbing => _grabbedObject != null;

        /// <summary>Current pinch strength (0-1) of the index finger.</summary>
        public float PinchStrength { get; private set; }

        /// <summary>Whether the associated OVRHand is currently tracked.</summary>
        public bool IsHandTracked => _hand != null && _hand.IsTracked;

        private OVRHand _hand;
        private OVRSkeleton _skeleton;
        private Rigidbody _grabbedObject;

        // Bone transforms cached from skeleton
        private Transform _thumbTip;
        private Transform _indexTip;
        private Transform _middleTip;
        private Transform _ringTip;
        private Transform _pinkyTip;
        private Transform _wristBone;

        // Grab state
        private Vector3 _grabOffset;
        private Quaternion _grabRotationOffset;
        private Vector3 _pinchCenter;
        private Vector3 _palmCenter;
        private Vector3 _activeGrabCenter;

        /// <summary>How the current grab was initiated — determines which signal to check for release.</summary>
        private enum GrabType { None, Pinch, Grip }
        private GrabType _activeGrabType = GrabType.None;

        // Original rigidbody settings
        private bool _origKinematic;
        private bool _origGravity;

        // Velocity tracking for throw on release
        private Vector3 _prevGrabPos;
        private const int VelocityFrames = 5;
        private readonly Vector3[] _velocityBuffer = new Vector3[VelocityFrames];
        private int _velocityIndex;

        private readonly Collider[] _overlapResults = new Collider[16];

        // Retry for late-initializing OVRHand
        private float _handSearchTimer;
        private const float HandSearchInterval = 0.25f;

        // Diagnostics
        private float _diagnosticTimer;
        private bool _loggedReady;
        private bool _bonesCached;

        private void Awake()
        {
            FindHandReferences();
        }

        private void Update()
        {
            // Retry finding OVRHand if not yet available
            if (_hand == null)
            {
                _handSearchTimer += Time.deltaTime;
                if (_handSearchTimer >= HandSearchInterval)
                {
                    _handSearchTimer = 0f;
                    FindHandReferences();
                }
                if (_grabbedObject != null) ReleaseObject();
                LogDiag("Waiting for OVRHand", false, 0f, 0f, 0);
                return;
            }

            if (!_hand.IsTracked)
            {
                if (_grabbedObject != null) ReleaseObject();
                LogDiag("Not tracked", false, 0f, 0f, 0);
                return;
            }

            if (!_loggedReady)
            {
                _loggedReady = true;
                Debug.Log($"[HandGrabber] {name}: Hand TRACKED and ready.");
            }

            CacheBoneTransforms();
            UpdateGrabCenters();

            PinchStrength = _hand.GetFingerPinchStrength(OVRHand.HandFinger.Index);
            float pinchStrength = GetPinchStrength();
            float gripStrength = GetPalmGripStrength();

            if (_grabbedObject == null)
            {
                // --- Try to START a grab ---
                // Pinch has priority (more precise intent signal)
                if (pinchStrength >= _pinchThreshold)
                {
                    TryGrab(GrabType.Pinch);
                }
                else if (gripStrength >= _gripThreshold)
                {
                    TryGrab(GrabType.Grip);
                }
            }
            else
            {
                // --- Check for RELEASE using the same signal that initiated the grab ---
                bool wantRelease = ShouldRelease(pinchStrength, gripStrength);

                if (wantRelease)
                {
                    ReleaseObject();
                }
                else
                {
                    MoveGrabbedObject();
                }
            }

            // Diagnostics
            int nearby = Physics.OverlapSphereNonAlloc(
                _activeGrabCenter, _grabRadius, _overlapResults, _grabLayerMask);
            LogDiag("Active", _grabbedObject != null, pinchStrength, gripStrength, nearby);
        }

        /// <summary>
        /// Determines if the grabbed object should be released based on the
        /// grab type that initiated the hold. This prevents curl-based grip
        /// values from keeping objects stuck when pinch was the grab trigger.
        /// Also force-releases if fingers move too far from the object.
        /// </summary>
        private bool ShouldRelease(float pinchStrength, float gripStrength)
        {
            // Safety: force release if fingertips are far from the object
            if (_grabbedObject != null && _bonesCached)
            {
                float distToObj = Vector3.Distance(_activeGrabCenter, _grabbedObject.position);
                if (distToObj > _maxGrabDistance)
                    return true;
            }

            switch (_activeGrabType)
            {
                case GrabType.Pinch:
                    // Release when pinch drops below pinch release threshold
                    return pinchStrength < _pinchReleaseThreshold;

                case GrabType.Grip:
                    // Release when palm grip drops below grip release threshold
                    return gripStrength < _gripReleaseThreshold;

                default:
                    return true;
            }
        }

        /// <summary>
        /// Returns the strongest pinch signal across index, middle, ring, pinky.
        /// This is the OVR "thumb touches fingertip" measurement.
        /// </summary>
        private float GetPinchStrength()
        {
            if (_hand == null) return 0f;

            float best = _hand.GetFingerPinchStrength(OVRHand.HandFinger.Index);
            best = Mathf.Max(best, _hand.GetFingerPinchStrength(OVRHand.HandFinger.Middle));
            best = Mathf.Max(best, _hand.GetFingerPinchStrength(OVRHand.HandFinger.Ring));
            best = Mathf.Max(best, _hand.GetFingerPinchStrength(OVRHand.HandFinger.Pinky));
            return best;
        }

        /// <summary>
        /// Returns palm grip strength: average pinch across all 4 fingers.
        /// Only counts as a "grip" when multiple fingers are engaged simultaneously.
        /// Uses raw OVR pinch values (not curl-based) for consistent release behavior.
        /// </summary>
        private float GetPalmGripStrength()
        {
            if (_hand == null) return 0f;

            float idx = _hand.GetFingerPinchStrength(OVRHand.HandFinger.Index);
            float mid = _hand.GetFingerPinchStrength(OVRHand.HandFinger.Middle);
            float rng = _hand.GetFingerPinchStrength(OVRHand.HandFinger.Ring);
            float pnk = _hand.GetFingerPinchStrength(OVRHand.HandFinger.Pinky);

            // Require at least 3 fingers engaged above a minimum to count as palm grip
            const float minFingerEngagement = 0.3f;
            int engaged = 0;
            if (idx > minFingerEngagement) engaged++;
            if (mid > minFingerEngagement) engaged++;
            if (rng > minFingerEngagement) engaged++;
            if (pnk > minFingerEngagement) engaged++;

            if (engaged < 3) return 0f;

            return (idx + mid + rng + pnk) / 4f;
        }

        /// <summary>
        /// Caches bone transforms from the skeleton for efficient per-frame access.
        /// </summary>
        private void CacheBoneTransforms()
        {
            if (_bonesCached) return;
            if (_skeleton == null || !_skeleton.IsInitialized || _skeleton.Bones == null) return;

            foreach (var bone in _skeleton.Bones)
            {
                if (bone == null || bone.Transform == null) continue;
                switch (bone.Id)
                {
                    case OVRSkeleton.BoneId.Hand_ThumbTip:  _thumbTip  = bone.Transform; break;
                    case OVRSkeleton.BoneId.Hand_IndexTip:  _indexTip  = bone.Transform; break;
                    case OVRSkeleton.BoneId.Hand_MiddleTip: _middleTip = bone.Transform; break;
                    case OVRSkeleton.BoneId.Hand_RingTip:   _ringTip   = bone.Transform; break;
                    case OVRSkeleton.BoneId.Hand_PinkyTip:  _pinkyTip  = bone.Transform; break;
                    case OVRSkeleton.BoneId.Hand_WristRoot:  _wristBone = bone.Transform; break;
                }
            }

            _bonesCached = _thumbTip != null && _indexTip != null;
            if (_bonesCached)
                Debug.Log($"[HandGrabber] {name}: Bones cached successfully.");
        }

        /// <summary>
        /// Computes both the pinch center (thumb+index midpoint) and the palm center
        /// (average of all fingertips). The active grab center is chosen based on
        /// the active grab type.
        /// </summary>
        private void UpdateGrabCenters()
        {
            if (!_bonesCached)
            {
                _pinchCenter = transform.position;
                _palmCenter = transform.position;
                _activeGrabCenter = transform.position;
                return;
            }

            // Pinch center: midpoint of thumb and index tips
            _pinchCenter = (_thumbTip.position + _indexTip.position) * 0.5f;

            // Palm center: weighted average of all available fingertips
            int count = 0;
            Vector3 sum = Vector3.zero;

            void AddTip(Transform tip) { if (tip != null) { sum += tip.position; count++; } }
            AddTip(_thumbTip);
            AddTip(_indexTip);
            AddTip(_middleTip);
            AddTip(_ringTip);
            AddTip(_pinkyTip);

            _palmCenter = count > 0 ? sum / count : transform.position;

            // Use grab-type-appropriate center
            if (_activeGrabType == GrabType.Grip)
                _activeGrabCenter = Vector3.Lerp(_pinchCenter, _palmCenter, 0.6f);
            else
                _activeGrabCenter = _pinchCenter;
        }

        private void TryGrab(GrabType grabType)
        {
            int hitCount = Physics.OverlapSphereNonAlloc(
                _activeGrabCenter, _grabRadius, _overlapResults, _grabLayerMask);

            Rigidbody closestRb = null;
            float closestDist = float.MaxValue;

            for (int i = 0; i < hitCount; i++)
            {
                Rigidbody rb = _overlapResults[i].attachedRigidbody;
                if (rb == null) continue;

                Vector3 closestPoint = _overlapResults[i].ClosestPoint(_activeGrabCenter);
                float dist = Vector3.Distance(_activeGrabCenter, closestPoint);

                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestRb = rb;
                }
            }

            if (closestRb != null)
                GrabObject(closestRb, grabType);
        }

        private void GrabObject(Rigidbody rb, GrabType grabType)
        {
            _grabbedObject = rb;
            _activeGrabType = grabType;
            _origKinematic = rb.isKinematic;
            _origGravity = rb.useGravity;

            rb.isKinematic = true;
            rb.useGravity = false;

            _grabOffset = rb.position - _activeGrabCenter;
            _grabRotationOffset = Quaternion.Inverse(GetGrabRotation()) * rb.rotation;

            // Initialize velocity tracking
            _prevGrabPos = rb.position;
            _velocityIndex = 0;
            for (int i = 0; i < VelocityFrames; i++)
                _velocityBuffer[i] = Vector3.zero;

            Debug.Log($"[HandGrabber] Grabbed: {rb.name} via {grabType} (offset={_grabOffset.magnitude:F3}m)");
            OnGrabStarted?.Invoke(rb);
        }

        private void ReleaseObject()
        {
            if (_grabbedObject == null) return;

            Rigidbody released = _grabbedObject;
            released.isKinematic = _origKinematic;
            released.useGravity = _origGravity;

            // Apply averaged velocity for natural throw
            Vector3 avgVel = Vector3.zero;
            for (int i = 0; i < VelocityFrames; i++)
                avgVel += _velocityBuffer[i];
            avgVel /= VelocityFrames;
            released.linearVelocity = avgVel;
            released.angularVelocity = Vector3.zero;

            _grabbedObject = null;
            _activeGrabType = GrabType.None;

            Debug.Log($"[HandGrabber] Released: {released.name} (vel={avgVel.magnitude:F2}m/s)");
            OnGrabEnded?.Invoke(released);
        }

        private void MoveGrabbedObject()
        {
            if (_grabbedObject == null) return;

            Vector3 targetPos = _activeGrabCenter + GetGrabRotation() * _grabOffset;
            Quaternion targetRot = GetGrabRotation() * _grabRotationOffset;

            // Clamp max movement speed to prevent teleporting on tracking glitches
            Vector3 delta = targetPos - _grabbedObject.position;
            float maxDelta = _maxFollowSpeed * Time.deltaTime;
            if (delta.sqrMagnitude > maxDelta * maxDelta)
                delta = delta.normalized * maxDelta;

            Vector3 newPos = Vector3.Lerp(
                _grabbedObject.position, _grabbedObject.position + delta, _followTightness);

            _grabbedObject.MovePosition(newPos);
            _grabbedObject.MoveRotation(Quaternion.Slerp(
                _grabbedObject.rotation, targetRot, _followTightness));

            // Track velocity for throw-on-release
            float dt = Mathf.Max(Time.deltaTime, 0.001f);
            _velocityBuffer[_velocityIndex % VelocityFrames] = (newPos - _prevGrabPos) / dt;
            _velocityIndex++;
            _prevGrabPos = newPos;
        }

        /// <summary>
        /// Returns the grab orientation from the wrist bone, falling back to transform.
        /// </summary>
        private Quaternion GetGrabRotation()
        {
            return _wristBone != null ? _wristBone.rotation : transform.rotation;
        }

        /// <summary>
        /// Searches the hierarchy for OVRHand and OVRSkeleton components.
        /// </summary>
        private void FindHandReferences()
        {
            _hand = GetComponentInParent<OVRHand>(true)
                 ?? GetComponentInChildren<OVRHand>(true);

            _skeleton = GetComponentInParent<OVRSkeleton>(true)
                     ?? GetComponentInChildren<OVRSkeleton>(true);

            if (_hand == null)
            {
                var allHands = FindObjectsByType<OVRHand>(FindObjectsSortMode.None);
                bool wantLeft = name.Contains("Left", StringComparison.OrdinalIgnoreCase);

                foreach (var h in allHands)
                {
                    if (h == null) continue;
                    var sk = h.GetComponent<OVRSkeleton>();
                    if (sk == null) continue;

                    bool isLeft = sk.GetSkeletonType() == OVRSkeleton.SkeletonType.HandLeft
                               || sk.GetSkeletonType() == OVRSkeleton.SkeletonType.XRHandLeft;
                    if (wantLeft == isLeft)
                    {
                        _hand = h;
                        _skeleton = sk;
                        break;
                    }
                }

                if (_hand == null && allHands.Length > 0)
                {
                    _hand = allHands[0];
                    _skeleton = allHands[0].GetComponent<OVRSkeleton>();
                }
            }

            if (_hand != null)
                Debug.Log($"[HandGrabber] Found OVRHand on {_hand.gameObject.name} (from {name}).");
        }

        private void LogDiag(string state, bool isGrabbing, float pinch, float grip, int nearbyObjects)
        {
            if (_diagnosticInterval <= 0f) return;
            _diagnosticTimer += Time.deltaTime;
            if (_diagnosticTimer < _diagnosticInterval) return;
            _diagnosticTimer = 0f;

            string skelState = _skeleton != null
                ? (_skeleton.IsInitialized ? $"Init({_skeleton.Bones?.Count ?? 0})" : "NOT Init")
                : "null";

            Debug.Log($"[HandGrabber:{name}] {state} | " +
                $"Skel={skelState} | Bones={(_bonesCached ? "cached" : "pending")} | " +
                $"Pinch={pinch:F2} Grip={grip:F2} | " +
                $"GrabType={_activeGrabType} | " +
                $"Grab={(_grabbedObject != null ? _grabbedObject.name : "none")} | " +
                $"Nearby={nearbyObjects} | Center={_activeGrabCenter}");
        }

        private void OnDrawGizmosSelected()
        {
            if (!_showDebugSphere) return;

            Gizmos.color = _grabbedObject != null
                ? new Color(0f, 1f, 0f, 0.3f)
                : new Color(1f, 1f, 0f, 0.3f);

            Vector3 center = Application.isPlaying ? _activeGrabCenter : transform.position;
            Gizmos.DrawWireSphere(center, _grabRadius);

            if (Application.isPlaying && _bonesCached)
            {
                Gizmos.color = new Color(0.3f, 0.5f, 1f, 0.5f);
                Gizmos.DrawWireSphere(_pinchCenter, 0.015f);
                Gizmos.color = new Color(1f, 0.3f, 0.8f, 0.5f);
                Gizmos.DrawWireSphere(_palmCenter, 0.02f);
            }
        }

        private void OnDisable()
        {
            if (_grabbedObject != null) ReleaseObject();
        }
    }
}
