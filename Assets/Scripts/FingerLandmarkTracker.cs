using UnityEngine;

namespace AGVRSystem
{
    /// <summary>
    /// Tracks 21 bone positions per hand every FixedUpdate.
    /// Provides flexion angles, spread measurements, and accuracy scoring.
    /// </summary>
    public class FingerLandmarkTracker : MonoBehaviour
    {
        private const int LandmarkCount = 21;

        private readonly Vector3[] _leftLandmarks = new Vector3[LandmarkCount];
        private readonly Vector3[] _rightLandmarks = new Vector3[LandmarkCount];

        private void FixedUpdate()
        {
            var manager = HandTrackingManager.Instance;
            if (manager == null)
                return;

            CacheLandmarks(manager.LeftSkeleton, _leftLandmarks);
            CacheLandmarks(manager.RightSkeleton, _rightLandmarks);
        }

        private void CacheLandmarks(OVRSkeleton skeleton, Vector3[] landmarks)
        {
            if (skeleton == null || !skeleton.IsInitialized || skeleton.Bones == null)
                return;

            int count = Mathf.Min(skeleton.Bones.Count, LandmarkCount);
            for (int i = 0; i < count; i++)
            {
                if (skeleton.Bones[i] != null && skeleton.Bones[i].Transform != null)
                {
                    landmarks[i] = skeleton.Bones[i].Transform.position;
                }
            }
        }

        /// <summary>
        /// Returns cached 21-element position array for the given skeleton.
        /// </summary>
        public Vector3[] GetLandmarks(OVRSkeleton skeleton)
        {
            var manager = HandTrackingManager.Instance;
            if (manager == null)
                return _leftLandmarks;

            return skeleton == manager.LeftSkeleton ? _leftLandmarks : _rightLandmarks;
        }

        /// <summary>
        /// Computes flexion angle between three bone joints.
        /// </summary>
        public float ComputeFlexion(
            OVRSkeleton skeleton,
            OVRSkeleton.BoneId proximal,
            OVRSkeleton.BoneId intermediate,
            OVRSkeleton.BoneId distal)
        {
            if (skeleton == null || !skeleton.IsInitialized)
                return 0f;

            Vector3[] landmarks = GetLandmarks(skeleton);
            int proxIdx = (int)proximal;
            int interIdx = (int)intermediate;
            int distIdx = (int)distal;

            if (proxIdx >= LandmarkCount || interIdx >= LandmarkCount || distIdx >= LandmarkCount)
                return 0f;

            Vector3 v1 = landmarks[interIdx] - landmarks[proxIdx];
            Vector3 v2 = landmarks[distIdx] - landmarks[interIdx];

            if (v1.sqrMagnitude < Mathf.Epsilon || v2.sqrMagnitude < Mathf.Epsilon)
                return 0f;

            return Vector3.Angle(v1, v2);
        }

        /// <summary>
        /// Computes spread angles between adjacent finger MCP joints.
        /// Returns float[4]: Index-Middle, Middle-Ring, Ring-Pinky, Thumb-Index.
        /// </summary>
        public float[] ComputeSpread(OVRSkeleton skeleton)
        {
            float[] spreads = new float[4];

            if (skeleton == null || !skeleton.IsInitialized)
                return spreads;

            Vector3[] landmarks = GetLandmarks(skeleton);

            // BoneId values for MCP joints (Meta XR SDK v83)
            int wristIdx = (int)OVRSkeleton.BoneId.Hand_WristRoot;
            int thumbMcpIdx = (int)OVRSkeleton.BoneId.Hand_Thumb0;
            int indexMcpIdx = (int)OVRSkeleton.BoneId.Hand_Index1;
            int middleMcpIdx = (int)OVRSkeleton.BoneId.Hand_Middle1;
            int ringMcpIdx = (int)OVRSkeleton.BoneId.Hand_Ring1;
            int pinkyMcpIdx = (int)OVRSkeleton.BoneId.Hand_Pinky0;

            if (wristIdx >= LandmarkCount || pinkyMcpIdx >= LandmarkCount)
                return spreads;

            Vector3 wristPos = landmarks[wristIdx];
            Vector3 toIndex = landmarks[indexMcpIdx] - wristPos;
            Vector3 toMiddle = landmarks[middleMcpIdx] - wristPos;
            Vector3 toRing = landmarks[ringMcpIdx] - wristPos;
            Vector3 toPinky = landmarks[pinkyMcpIdx] - wristPos;
            Vector3 toThumb = landmarks[thumbMcpIdx] - wristPos;

            spreads[0] = Vector3.Angle(toIndex, toMiddle);
            spreads[1] = Vector3.Angle(toMiddle, toRing);
            spreads[2] = Vector3.Angle(toRing, toPinky);
            spreads[3] = Vector3.Angle(toThumb, toIndex);

            return spreads;
        }

        /// <summary>
        /// Returns accuracy score based on how close measured value is to target within tolerance.
        /// </summary>
        public float GetAccuracy(float measuredValue, float targetValue, float tolerance)
        {
            if (tolerance <= 0f)
                return 0f;

            return Mathf.Clamp01(1f - Mathf.Abs(measuredValue - targetValue) / tolerance);
        }
    }
}
