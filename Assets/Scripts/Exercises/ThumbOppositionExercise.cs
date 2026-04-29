using UnityEngine;

namespace AGVRSystem.Exercises
{
    /// <summary>
    /// Touch thumb to each finger in sequence: Index -> Middle -> Ring -> Pinky.
    /// Each touch requires pinch strength above a per-finger threshold.
    /// The gap timer only runs AFTER the previous finger is released, giving
    /// the user enough time to move to the next finger.
    ///
    /// Bilateral support: both hands are tracked simultaneously. Either hand
    /// completing the pinch on the current target finger advances the sequence.
    /// The active hand label indicates which hand is performing.
    ///
    /// Designed for rehabilitation:
    ///   - Per-finger thresholds (pinky is weaker than index)
    ///   - Generous gap timing (gap only counts after release)
    ///   - Visual feedback via CurrentFingerName/SequenceProgress
    ///   - Diagnostic logging for on-device debugging
    /// </summary>
    public class ThumbOppositionExercise : BaseExercise
    {
        [Header("Thresholds (per finger)")]
        [Tooltip("Pinch threshold for Index finger (easiest).")]
        [SerializeField] private float _indexThreshold = 0.7f;

        [Tooltip("Pinch threshold for Middle finger.")]
        [SerializeField] private float _middleThreshold = 0.65f;

        [Tooltip("Pinch threshold for Ring finger (often coupled with pinky).")]
        [SerializeField] private float _ringThreshold = 0.55f;

        [Tooltip("Pinch threshold for Pinky finger (weakest).")]
        [SerializeField] private float _pinkyThreshold = 0.50f;

        [Header("Timing")]
        [Tooltip("Max seconds between releasing one finger and touching the next.")]
        [SerializeField] private float _maxGap = 2.0f;

        [Tooltip("Pinch must drop below this fraction of the threshold to count as released.")]
        [SerializeField] private float _releaseFraction = 0.5f;

        private const int DefaultTargetReps = 6;
        private const int SequenceLength = 4;
        private const float DiagnosticInterval = 1.5f;

        private static readonly OVRHand.HandFinger[] Sequence =
        {
            OVRHand.HandFinger.Index,
            OVRHand.HandFinger.Middle,
            OVRHand.HandFinger.Ring,
            OVRHand.HandFinger.Pinky
        };

        private static readonly string[] FingerNames = { "Index", "Middle", "Ring", "Pinky" };

        private int _currentFingerIndex;
        private float _gapTimer;
        private float _totalGapTime;
        private bool _waitingForRelease;
        private float _diagTimer;
        private int _sequenceResets;

        // Bilateral tracking
        private string _activeHandLabel = "";
        private float _leftPinchStrength;
        private float _rightPinchStrength;

        /// <summary>Name of the finger the user should touch next.</summary>
        public string CurrentFingerName => FingerNames[_currentFingerIndex];

        /// <summary>Sequence progress (0-1) for the current rep: 0/4, 1/4, 2/4, 3/4.</summary>
        public float SequenceProgress => _currentFingerIndex / (float)SequenceLength;

        /// <summary>Hold progress: how far through the current sequence (0-1).</summary>
        public float HoldProgress => SequenceProgress;

        /// <summary>Label of the hand that last performed a touch ("Left" / "Right" / "").</summary>
        public string ActiveHandLabel => _activeHandLabel;

        /// <summary>Current left hand pinch strength for the target finger (0-1).</summary>
        public float LeftPinchStrength => _leftPinchStrength;

        /// <summary>Current right hand pinch strength for the target finger (0-1).</summary>
        public float RightPinchStrength => _rightPinchStrength;

        public override void StartExercise()
        {
            ResetBase();
            TargetReps = DefaultTargetReps;
            ResetSequence();
            _sequenceResets = 0;
            _diagTimer = 0f;
            _activeHandLabel = "";
            _leftPinchStrength = 0f;
            _rightPinchStrength = 0f;

            Debug.Log($"[ThumbOppositionExercise] Started — thresholds: " +
                $"I={_indexThreshold} M={_middleThreshold} R={_ringThreshold} P={_pinkyThreshold} " +
                $"maxGap={_maxGap}s reps={DefaultTargetReps} bilateral=true");
        }

        public override void StopExercise()
        {
            IsActive = false;
        }

        public override float Evaluate()
        {
            return CurrentReps / (float)Mathf.Max(1, TargetReps);
        }

        private void Update()
        {
            if (!IsActive)
                return;

            var manager = HandTrackingManager.Instance;
            if (manager == null)
                return;

            // Check both hands
            bool leftOk = manager.LeftHand != null && manager.IsLeftTracked;
            bool rightOk = manager.RightHand != null && manager.IsRightTracked;

            if (!leftOk && !rightOk)
                return;

            OVRHand.HandFinger targetFinger = Sequence[_currentFingerIndex];
            float threshold = GetThresholdForFinger(_currentFingerIndex);
            float releaseThreshold = threshold * _releaseFraction;

            // Get pinch from both hands
            _leftPinchStrength = leftOk ? manager.LeftHand.GetFingerPinchStrength(targetFinger) : 0f;
            _rightPinchStrength = rightOk ? manager.RightHand.GetFingerPinchStrength(targetFinger) : 0f;

            // Pick best signal
            float bestPinch;
            string bestLabel;

            if (_leftPinchStrength >= _rightPinchStrength && leftOk)
            {
                bestPinch = _leftPinchStrength;
                bestLabel = "Left";
            }
            else if (rightOk)
            {
                bestPinch = _rightPinchStrength;
                bestLabel = "Right";
            }
            else
            {
                bestPinch = _leftPinchStrength;
                bestLabel = "Left";
            }

            // Phase 1: Waiting for the user to RELEASE the previous finger
            if (_waitingForRelease)
            {
                if (bestPinch < releaseThreshold)
                {
                    _waitingForRelease = false;
                    _gapTimer = 0f;

                    Debug.Log($"[ThumbOppositionExercise] {FingerNames[Mathf.Max(0, _currentFingerIndex - 1)]} " +
                        $"released -> waiting for {FingerNames[_currentFingerIndex]} " +
                        $"(pinch={bestPinch:F2} < {releaseThreshold:F2})");
                }
                return;
            }

            // Phase 2: Waiting for the user to TOUCH the next finger
            if (_currentFingerIndex > 0)
            {
                _gapTimer += Time.deltaTime;
                if (_gapTimer > _maxGap)
                {
                    _sequenceResets++;
                    Debug.Log($"[ThumbOppositionExercise] Sequence RESET — gap {_gapTimer:F1}s > {_maxGap}s " +
                        $"waiting for {FingerNames[_currentFingerIndex]} (resets={_sequenceResets})");
                    ResetSequence();
                    return;
                }
            }

            // Phase 3: Detect the pinch on the target finger
            if (bestPinch >= threshold)
            {
                _totalGapTime += _gapTimer;
                string fingerName = FingerNames[_currentFingerIndex];
                _activeHandLabel = bestLabel;
                _currentFingerIndex++;
                _gapTimer = 0f;
                _waitingForRelease = true;

                Debug.Log($"[ThumbOppositionExercise] TOUCH {fingerName} ({bestLabel})! pinch={bestPinch:F2} " +
                    $"(threshold={threshold:F2}) seq={_currentFingerIndex}/{SequenceLength}");

                if (_currentFingerIndex >= SequenceLength)
                {
                    float maxPossibleGap = _maxGap * SequenceLength;
                    float accuracy = 1f - Mathf.Clamp01(_totalGapTime / maxPossibleGap);
                    RegisterRep(accuracy);
                    Debug.Log($"[ThumbOppositionExercise] REP COMPLETE! accuracy={accuracy:F2} " +
                        $"totalGap={_totalGapTime:F2}s reps={CurrentReps}/{TargetReps}");
                    ResetSequence();
                }
            }

            // Diagnostics
            _diagTimer += Time.deltaTime;
            if (_diagTimer >= DiagnosticInterval)
            {
                _diagTimer = 0f;
                Debug.Log($"[ThumbOppositionExercise] DIAG: target={FingerNames[_currentFingerIndex]} " +
                    $"L={_leftPinchStrength:F2} R={_rightPinchStrength:F2} threshold={threshold:F2} " +
                    $"gap={_gapTimer:F1}/{_maxGap}s released={!_waitingForRelease} " +
                    $"seq={_currentFingerIndex}/{SequenceLength} reps={CurrentReps}/{TargetReps} " +
                    $"resets={_sequenceResets} hand={_activeHandLabel}");
            }
        }

        /// <summary>
        /// Returns the pinch threshold for the given finger index.
        /// Later fingers (ring, pinky) have lower thresholds since they're weaker.
        /// </summary>
        private float GetThresholdForFinger(int fingerIndex)
        {
            return fingerIndex switch
            {
                0 => _indexThreshold * DifficultyMultiplier,
                1 => _middleThreshold * DifficultyMultiplier,
                2 => _ringThreshold * DifficultyMultiplier,
                3 => _pinkyThreshold * DifficultyMultiplier,
                _ => _indexThreshold * DifficultyMultiplier
            };
        }

        private void ResetSequence()
        {
            _currentFingerIndex = 0;
            _gapTimer = 0f;
            _totalGapTime = 0f;
            _waitingForRelease = false;
            _activeHandLabel = "";
        }
    }
}
