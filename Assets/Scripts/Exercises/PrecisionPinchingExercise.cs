using UnityEngine;

namespace AGVRSystem.Exercises
{
    /// <summary>
    /// Precision pinch exercise — cycles through all fingers (Index, Middle, Ring, Pinky).
    /// For each finger, hold a pinch (thumb-to-fingertip) >= threshold for 1.5 seconds.
    /// Each successful hold counts as one rep. The exercise cycles through fingers in order,
    /// advancing to the next finger after each successful rep.
    ///
    /// Bilateral support: both hands are tracked simultaneously. Either hand completing
    /// a pinch on the target finger counts as progress. The active hand label indicates
    /// which hand is currently performing the pinch.
    ///
    /// This trains fine motor control and per-finger dexterity:
    ///   - Index: easiest, highest precision
    ///   - Middle: moderate difficulty
    ///   - Ring: harder, often coupled with pinky
    ///   - Pinky: hardest, weakest finger
    /// </summary>
    public class PrecisionPinchingExercise : BaseExercise
    {
        [Header("Pinch Settings")]
        [Tooltip("Minimum pinch strength required for the target finger.")]
        [SerializeField] private float _pinchThreshold = 0.8f;

        [Tooltip("Maximum pinch allowed on OTHER fingers to ensure isolation.")]
        [SerializeField] private float _isolationThreshold = 0.5f;

        [Tooltip("Whether to require finger isolation (only target finger pinching).")]
        [SerializeField] private bool _requireIsolation = false;

        private const float HoldDuration = 1.5f;
        private const int DefaultTargetReps = 12;
        private const int FingerCount = 4;
        private const float DiagnosticInterval = 2f;

        private static readonly OVRHand.HandFinger[] AllFingers =
        {
            OVRHand.HandFinger.Index,
            OVRHand.HandFinger.Middle,
            OVRHand.HandFinger.Ring,
            OVRHand.HandFinger.Pinky
        };

        private static readonly string[] FingerNames = { "Index", "Middle", "Ring", "Pinky" };

        private float _pinchTimer;
        private bool _isPinching;
        private int _failedAttempts;
        private float _pinchStrengthAccumulator;
        private int _pinchSampleCount;
        private int _currentFingerIdx;
        private float _diagTimer;

        // Bilateral tracking state
        private string _activeHandLabel = "";
        private float _leftPinchStrength;
        private float _rightPinchStrength;

        /// <summary>Current pinch hold progress normalized to 0-1 for HUD display.</summary>
        public float HoldProgress => _isPinching ? Mathf.Clamp01(_pinchTimer / HoldDuration) : 0f;

        /// <summary>Name of the finger currently being trained.</summary>
        public string CurrentFingerName => FingerNames[_currentFingerIdx % FingerCount];

        /// <summary>Which finger (0-3) is the current target.</summary>
        public int CurrentFingerIndex => _currentFingerIdx % FingerCount;

        /// <summary>The OVRHand.HandFinger enum for the current target.</summary>
        public OVRHand.HandFinger CurrentTargetFinger => AllFingers[_currentFingerIdx % FingerCount];

        /// <summary>Label of the hand currently performing the pinch ("Left" / "Right" / "").</summary>
        public string ActiveHandLabel => _activeHandLabel;

        /// <summary>Current left hand pinch strength for the target finger (0-1).</summary>
        public float LeftPinchStrength => _leftPinchStrength;

        /// <summary>Current right hand pinch strength for the target finger (0-1).</summary>
        public float RightPinchStrength => _rightPinchStrength;

        public override void StartExercise()
        {
            ResetBase();
            TargetReps = DefaultTargetReps;
            _pinchTimer = 0f;
            _isPinching = false;
            _failedAttempts = 0;
            _pinchStrengthAccumulator = 0f;
            _pinchSampleCount = 0;
            _currentFingerIdx = 0;
            _diagTimer = 0f;
            _activeHandLabel = "";
            _leftPinchStrength = 0f;
            _rightPinchStrength = 0f;

            Debug.Log($"[PrecisionPinchingExercise] Started — threshold={_pinchThreshold} " +
                $"isolation={_requireIsolation} ({_isolationThreshold}) reps={DefaultTargetReps} " +
                $"first finger={FingerNames[0]} bilateral=true");
        }

        public override void StopExercise()
        {
            IsActive = false;
        }

        public override float Evaluate()
        {
            return CurrentReps / Mathf.Max(1f, CurrentReps + _failedAttempts);
        }

        private void Update()
        {
            if (!IsActive)
                return;

            var manager = HandTrackingManager.Instance;
            if (manager == null)
                return;

            OVRHand.HandFinger targetFinger = AllFingers[_currentFingerIdx % FingerCount];
            float adjustedThreshold = _pinchThreshold * Mathf.Clamp(DifficultyMultiplier, 0.5f, 1f);

            // Check both hands independently
            bool leftOk = manager.LeftHand != null && manager.IsLeftTracked;
            bool rightOk = manager.RightHand != null && manager.IsRightTracked;

            _leftPinchStrength = leftOk ? manager.LeftHand.GetFingerPinchStrength(targetFinger) : 0f;
            _rightPinchStrength = rightOk ? manager.RightHand.GetFingerPinchStrength(targetFinger) : 0f;

            if (!leftOk && !rightOk)
                return;

            // Pick the hand with the stronger pinch signal
            float bestPinch;
            OVRHand bestHand;
            string bestLabel;

            if (_leftPinchStrength >= _rightPinchStrength && leftOk)
            {
                bestPinch = _leftPinchStrength;
                bestHand = manager.LeftHand;
                bestLabel = "Left";
            }
            else if (rightOk)
            {
                bestPinch = _rightPinchStrength;
                bestHand = manager.RightHand;
                bestLabel = "Right";
            }
            else
            {
                bestPinch = _leftPinchStrength;
                bestHand = manager.LeftHand;
                bestLabel = "Left";
            }

            bool pinchMet = bestPinch >= adjustedThreshold;

            // Optionally check that OTHER fingers are NOT pinching (isolation)
            bool isolationPassed = true;
            if (_requireIsolation && pinchMet)
            {
                for (int i = 0; i < FingerCount; i++)
                {
                    if (i == _currentFingerIdx % FingerCount) continue;
                    float otherPinch = bestHand.GetFingerPinchStrength(AllFingers[i]);
                    if (otherPinch > _isolationThreshold)
                    {
                        isolationPassed = false;
                        break;
                    }
                }
            }

            if (pinchMet && isolationPassed)
            {
                if (!_isPinching)
                {
                    _isPinching = true;
                    _pinchTimer = 0f;
                    _pinchStrengthAccumulator = 0f;
                    _pinchSampleCount = 0;
                    _activeHandLabel = bestLabel;
                    Debug.Log($"[PrecisionPinchingExercise] {FingerNames[_currentFingerIdx % FingerCount]} " +
                        $"pinch started on {bestLabel} hand (strength={bestPinch:F2})");
                }

                _pinchTimer += Time.deltaTime;
                _pinchStrengthAccumulator += bestPinch;
                _pinchSampleCount++;

                if (_pinchTimer >= HoldDuration)
                {
                    float avgStrength = _pinchStrengthAccumulator / Mathf.Max(1, _pinchSampleCount);
                    RegisterRep(avgStrength);
                    _isPinching = false;
                    _pinchTimer = 0f;

                    string fingerName = FingerNames[_currentFingerIdx % FingerCount];
                    _currentFingerIdx++;
                    string nextFinger = FingerNames[_currentFingerIdx % FingerCount];

                    Debug.Log($"[PrecisionPinchingExercise] REP! {fingerName} ({_activeHandLabel}) " +
                        $"avg={avgStrength:F2} rep={CurrentReps}/{TargetReps} -> next={nextFinger}");
                    _activeHandLabel = "";
                }
            }
            else if (_isPinching)
            {
                _failedAttempts++;
                _isPinching = false;
                _pinchTimer = 0f;

                string reason = !pinchMet ? "strength dropped" : "isolation failed";
                Debug.Log($"[PrecisionPinchingExercise] {FingerNames[_currentFingerIdx % FingerCount]} " +
                    $"pinch LOST ({reason}) — failed attempts: {_failedAttempts}");
                _activeHandLabel = "";
            }

            // Diagnostics
            _diagTimer += Time.deltaTime;
            if (_diagTimer >= DiagnosticInterval)
            {
                _diagTimer = 0f;
                string fingerName = FingerNames[_currentFingerIdx % FingerCount];
                Debug.Log($"[PrecisionPinchingExercise] DIAG: target={fingerName} " +
                    $"L={_leftPinchStrength:F2} R={_rightPinchStrength:F2} threshold={adjustedThreshold:F2} " +
                    $"holding={_isPinching} timer={_pinchTimer:F1}/{HoldDuration}s " +
                    $"reps={CurrentReps}/{TargetReps} fails={_failedAttempts} hand={_activeHandLabel}");
            }
        }
    }
}
