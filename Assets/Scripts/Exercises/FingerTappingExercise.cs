using UnityEngine;

namespace AGVRSystem.Exercises
{
    /// <summary>
    /// Detect index-thumb taps via OVRHand pinch strength (>= 0.85).
    /// 20 taps target, 0.3s cooldown. Supports both hands.
    /// Uses GetFingerPinchStrength instead of OVRSkeleton.Bones so detection
    /// works reliably after scene transitions where the skeleton may not re-initialize.
    /// </summary>
    public class FingerTappingExercise : BaseExercise
    {
        private const float PinchOnThreshold = 0.85f;
        private const float PinchOffThreshold = 0.35f;
        private const float TapCooldown = 0.3f;
        private const int DefaultTargetTaps = 20;

        private float _cooldownTimer;
        private int _tapCount;
        private bool _wasPinching;

        public override void StartExercise()
        {
            ResetBase();
            TargetReps = DefaultTargetTaps;
            _cooldownTimer = 0f;
            _tapCount = 0;
            _wasPinching = false;
        }

        public override void StopExercise()
        {
            IsActive = false;
        }

        public override float Evaluate()
        {
            return _tapCount / (float)Mathf.Max(1, TargetReps);
        }

        private void Update()
        {
            if (!IsActive)
                return;

            _cooldownTimer -= Time.deltaTime;

            var manager = HandTrackingManager.Instance;
            if (manager == null)
                return;

            float pinchStrength = GetBestIndexPinch(manager);

            // Require pinch to drop below the off-threshold before a new tap registers,
            // preventing a sustained pinch from counting as multiple taps.
            if (_wasPinching)
            {
                if (pinchStrength < PinchOffThreshold)
                    _wasPinching = false;
                return;
            }

            if (pinchStrength >= PinchOnThreshold && _cooldownTimer <= 0f)
            {
                _tapCount++;
                _cooldownTimer = TapCooldown;
                _wasPinching = true;
                RegisterRep(pinchStrength);
            }
        }

        /// <summary>
        /// Returns the strongest index-finger pinch strength across both hands.
        /// </summary>
        private float GetBestIndexPinch(HandTrackingManager manager)
        {
            float left = 0f;
            float right = 0f;

            if (manager.LeftHand != null && manager.IsLeftTracked)
                left = manager.LeftHand.GetFingerPinchStrength(OVRHand.HandFinger.Index);

            if (manager.RightHand != null && manager.IsRightTracked)
                right = manager.RightHand.GetFingerPinchStrength(OVRHand.HandFinger.Index);

            return Mathf.Max(left, right);
        }
    }
}
