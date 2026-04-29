using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AGVRSystem
{
    /// <summary>
    /// Singleton manager wrapping OVRHand + OVRSkeleton for both hands.
    /// Provides tracking state events and grip strength calculation.
    /// Auto-discovers hand references when they become null (e.g. after scene transitions
    /// where the OVRCameraRig is recreated in the new scene).
    /// </summary>
    public class HandTrackingManager : MonoBehaviour
    {
        public static HandTrackingManager Instance { get; private set; }

        [SerializeField] private OVRHand _leftHand;
        [SerializeField] private OVRHand _rightHand;
        [SerializeField] private OVRSkeleton _leftSkeleton;
        [SerializeField] private OVRSkeleton _rightSkeleton;

        public OVRHand LeftHand => _leftHand;
        public OVRHand RightHand => _rightHand;
        public OVRSkeleton LeftSkeleton => _leftSkeleton;
        public OVRSkeleton RightSkeleton => _rightSkeleton;

        public bool IsLeftTracked { get; private set; }
        public bool IsRightTracked { get; private set; }

        /// <summary>
        /// Fired when either hand loses tracking.
        /// </summary>
        public event Action OnTrackingLost;

        /// <summary>
        /// Fired when tracking is restored after being lost.
        /// </summary>
        public event Action OnTrackingRestored;

        private bool _wasLeftTracked;
        private bool _wasRightTracked;

        private const string LeftHandVisualName = "LeftHandVisual";
        private const string RightHandVisualName = "RightHandVisual";

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Destroy ONLY this component — NOT the gameObject.
                // HandTrackingManager lives on the shared /Managers root which also
                // parents SessionManager, ExerciseCoordinator, HandJointVisualizer, etc.
                // Destroying the gameObject would wipe out the entire scene hierarchy.
                Debug.Log($"[HandTrackingManager] Duplicate singleton on '{gameObject.name}' — removing component only.");
                Destroy(this);
                return;
            }

            Instance = this;
            // No DontDestroyOnLoad — each scene has its own OVRCameraRig and hand
            // visuals so references need rebinding on every scene load anyway.
            // The Instance is set per-scene; OnDestroy clears it so the next
            // scene's HTM can take over cleanly.
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // After a scene transition, the OVRCameraRig is recreated
            // and our serialized references point to destroyed objects.
            // Re-discover the hand components in the new scene.
            RebindHandReferences();
        }

        private void Update()
        {
            // Safety: if references were destroyed mid-frame, try to rebind
            if (_leftHand == null || _rightHand == null)
            {
                RebindHandReferences();
            }

            UpdateTrackingState();
        }

        /// <summary>
        /// Finds OVRHand and OVRSkeleton components in the current scene.
        /// Tries named hand visuals first, then falls back to any OVRHand in the scene.
        /// </summary>
        private void RebindHandReferences()
        {
            if (_leftHand == null)
            {
                GameObject leftVisual = GameObject.Find(LeftHandVisualName);
                if (leftVisual != null)
                {
                    _leftHand = leftVisual.GetComponent<OVRHand>();
                    _leftSkeleton = leftVisual.GetComponent<OVRSkeleton>();
                }
            }

            if (_rightHand == null)
            {
                GameObject rightVisual = GameObject.Find(RightHandVisualName);
                if (rightVisual != null)
                {
                    _rightHand = rightVisual.GetComponent<OVRHand>();
                    _rightSkeleton = rightVisual.GetComponent<OVRSkeleton>();
                }
            }

            // Fallback: search for any OVRHand in the scene if named visuals not found.
            // Matches both legacy HandLeft/HandRight and XRHandLeft/XRHandRight skeleton types.
            if (_leftHand == null || _rightHand == null)
            {
                var allHands = FindObjectsByType<OVRHand>(FindObjectsSortMode.None);
                foreach (var hand in allHands)
                {
                    if (hand == null) continue;

                    var skeleton = hand.GetComponent<OVRSkeleton>();
                    if (skeleton == null) continue;

                    var skeletonType = skeleton.GetSkeletonType();
                    bool isLeft = skeletonType == OVRSkeleton.SkeletonType.HandLeft
                               || skeletonType == OVRSkeleton.SkeletonType.XRHandLeft;
                    bool isRight = skeletonType == OVRSkeleton.SkeletonType.HandRight
                                || skeletonType == OVRSkeleton.SkeletonType.XRHandRight;

                    if (isLeft && _leftHand == null)
                    {
                        _leftHand = hand;
                        _leftSkeleton = skeleton;
                        Debug.Log($"[HandTrackingManager] Fallback: bound left hand from {hand.gameObject.name}.");
                    }
                    else if (isRight && _rightHand == null)
                    {
                        _rightHand = hand;
                        _rightSkeleton = skeleton;
                        Debug.Log($"[HandTrackingManager] Fallback: bound right hand from {hand.gameObject.name}.");
                    }
                }
            }
        }

        private void UpdateTrackingState()
        {
            // Accept any tracking confidence for grab interaction — Low confidence
            // still provides usable pinch data. Only truly untracked hands (IsTracked=false)
            // should block interaction.
            bool leftTracked = _leftHand != null && _leftHand.IsTracked;
            bool rightTracked = _rightHand != null && _rightHand.IsTracked;

            IsLeftTracked = leftTracked;
            IsRightTracked = rightTracked;

            bool wasAnyTracked = _wasLeftTracked || _wasRightTracked;
            bool isAnyTracked = leftTracked || rightTracked;

            if (wasAnyTracked && !isAnyTracked)
            {
                OnTrackingLost?.Invoke();
            }
            else if (!wasAnyTracked && isAnyTracked)
            {
                OnTrackingRestored?.Invoke();
            }

            _wasLeftTracked = leftTracked;
            _wasRightTracked = rightTracked;
        }

        /// <summary>
        /// Calculates grip strength as the average pinch strength of Index, Middle, Ring, Pinky fingers (0-100).
        /// Note: pinch-based — measures thumb-to-fingertip distance, not actual finger curl.
        /// Prefer GetFingerCurlGripStrength for accurate grip detection.
        /// </summary>
        public float GetHandGripStrength(OVRHand hand)
        {
            if (hand == null || !hand.IsTracked)
                return 0f;

            float sum = 0f;
            sum += hand.GetFingerPinchStrength(OVRHand.HandFinger.Index);
            sum += hand.GetFingerPinchStrength(OVRHand.HandFinger.Middle);
            sum += hand.GetFingerPinchStrength(OVRHand.HandFinger.Ring);
            sum += hand.GetFingerPinchStrength(OVRHand.HandFinger.Pinky);

            const int fingerCount = 4;
            const float strengthScale = 100f;
            return (sum / fingerCount) * strengthScale;
        }

        /// <summary>
        /// Computes grip strength from actual finger curl/flexion angles (0-100).
        /// Measures how much each finger is curled (MCP → PIP joint angle),
        /// which accurately represents squeezing/gripping regardless of thumb position.
        /// </summary>
        public float GetFingerCurlGripStrength(OVRHand hand, OVRSkeleton skeleton)
        {
            if (hand == null || !hand.IsTracked)
                return 0f;

            if (skeleton == null || !skeleton.IsInitialized ||
                skeleton.Bones == null || skeleton.Bones.Count == 0)
            {
                // Fallback to pinch-based when skeleton unavailable
                return GetHandGripStrength(hand);
            }

            // Measure flexion at PIP joints (proximal interphalangeal)
            // A fully extended finger has ~0° flexion at PIP; fully curled ~90-110°
            float totalCurl = 0f;
            int validFingers = 0;

            // Index: MCP(6) → PIP(7) → DIP(8)
            totalCurl += GetBoneFlexion(skeleton, BoneIndex_Index1, BoneIndex_Index2, BoneIndex_Index3, ref validFingers);
            // Middle: MCP(9) → PIP(10) → DIP(11)
            totalCurl += GetBoneFlexion(skeleton, BoneIndex_Middle1, BoneIndex_Middle2, BoneIndex_Middle3, ref validFingers);
            // Ring: MCP(12) → PIP(13) → DIP(14)
            totalCurl += GetBoneFlexion(skeleton, BoneIndex_Ring1, BoneIndex_Ring2, BoneIndex_Ring3, ref validFingers);
            // Pinky: metacarpal(15) → MCP(16) → PIP(17)
            totalCurl += GetBoneFlexion(skeleton, BoneIndex_Pinky0, BoneIndex_Pinky1, BoneIndex_Pinky2, ref validFingers);

            if (validFingers == 0)
                return GetHandGripStrength(hand);

            float avgCurl = totalCurl / validFingers;
            // Normalize: 0° = no grip, 90° = full grip → map to 0-100
            return Mathf.Clamp(avgCurl / MaxCurlAngle * GripStrengthScale, 0f, GripStrengthScale);
        }

        /// <summary>
        /// Returns the skeleton for the given hand (left or right).
        /// </summary>
        public OVRSkeleton GetSkeletonForHand(OVRHand hand)
        {
            if (hand == _leftHand) return _leftSkeleton;
            if (hand == _rightHand) return _rightSkeleton;
            return null;
        }

        private float GetBoneFlexion(OVRSkeleton skeleton, int proximalIdx, int intermediateIdx, int distalIdx, ref int validCount)
        {
            int boneCount = skeleton.Bones.Count;
            if (proximalIdx >= boneCount || intermediateIdx >= boneCount || distalIdx >= boneCount)
                return 0f;

            var proxBone = skeleton.Bones[proximalIdx];
            var interBone = skeleton.Bones[intermediateIdx];
            var distBone = skeleton.Bones[distalIdx];

            if (proxBone?.Transform == null || interBone?.Transform == null || distBone?.Transform == null)
                return 0f;

            Vector3 v1 = interBone.Transform.position - proxBone.Transform.position;
            Vector3 v2 = distBone.Transform.position - interBone.Transform.position;

            if (v1.sqrMagnitude < MinBoneLengthSqr || v2.sqrMagnitude < MinBoneLengthSqr)
                return 0f;

            validCount++;
            return Vector3.Angle(v1, v2);
        }

        // Bone indices (OVRSkeleton.BoneId enum values)
        private const int BoneIndex_Index1 = 6;
        private const int BoneIndex_Index2 = 7;
        private const int BoneIndex_Index3 = 8;
        private const int BoneIndex_Middle1 = 9;
        private const int BoneIndex_Middle2 = 10;
        private const int BoneIndex_Middle3 = 11;
        private const int BoneIndex_Ring1 = 12;
        private const int BoneIndex_Ring2 = 13;
        private const int BoneIndex_Ring3 = 14;
        private const int BoneIndex_Pinky0 = 15;
        private const int BoneIndex_Pinky1 = 16;
        private const int BoneIndex_Pinky2 = 17;
        private const float MaxCurlAngle = 90f;
        private const float GripStrengthScale = 100f;
        private const float MinBoneLengthSqr = 0.00001f;
    }
}
