using System;
using System.Collections.Generic;
using UnityEngine;
using AGVRSystem.Data;
using AGVRSystem.Audio;
using AGVRSystem.Exercises;
using AGVRSystem.UI;
using UnityEngine.SceneManagement; 

namespace AGVRSystem
{
    /// <summary>
    /// Sequences all 6 exercises in order, aggregates metrics, fires session completion.
    /// Integrates with audio systems for voice guidance and exercise cues.
    /// Drives the HUD each frame with exercise state and grip data.
    /// </summary>
    public class ExerciseCoordinator : MonoBehaviour
    {
        [SerializeField] private BaseExercise[] _exercises;
        [SerializeField] private ExerciseAudioCues _audioCues;
        [SerializeField] private HUDController _hudController;
        [SerializeField] private ExerciseObjectController[] _exerciseObjects;

        private int _currentIndex;
        private SessionData _sessionData;
        private readonly List<ExerciseMetrics> _metrics = new List<ExerciseMetrics>();
        private float _sessionStartTime;
        private bool _sessionActive;

        // Runtime-registered exercise objects (fallback when _exerciseObjects not wired)
        private readonly List<ExerciseObjectController> _registeredObjects =
            new List<ExerciseObjectController>();

        /// <summary>
        /// Called by ExerciseObjectController.Start() to self-register so progress bars
        /// update without requiring the Inspector array to be manually wired.
        /// </summary>
        public void RegisterExerciseObject(ExerciseObjectController obj)
        {
            if (obj != null && !_registeredObjects.Contains(obj))
                _registeredObjects.Add(obj);
        }

        /// <summary>
        /// Elapsed time since the session began.
        /// </summary>
        public float SessionElapsedTime => _sessionActive ? Time.time - _sessionStartTime : 0f;

        /// <summary>
        /// Fired when all exercises are completed with the aggregated session data.
        /// </summary>
        public event Action<SessionData> OnSessionComplete;

        /// <summary>
        /// Starts a new session with the given user ID and begins the first exercise.
        /// </summary>
        public void BeginSession(string userId)
        {
            // Ensure exercises are bound before starting
            if (_exercises == null || _exercises.Length == 0)
            {
                _exercises = FindObjectsByType<BaseExercise>(FindObjectsSortMode.InstanceID);
                Debug.Log($"[ExerciseCoordinator] Late-bound {_exercises.Length} exercises in BeginSession.");
            }

            if (_hudController == null)
            {
                _hudController = FindAnyObjectByType<HUDController>();
                Debug.Log($"[ExerciseCoordinator] Late-bound HUDController: {(_hudController != null ? "found" : "NULL")}");
            }

            _sessionData = new SessionData
            {
                sessionId = Guid.NewGuid().ToString(),
                userId = userId,
                startTimestamp = DateTime.UtcNow.ToString("o")
            };

            _metrics.Clear();
            _currentIndex = 0;
            _sessionStartTime = Time.time;
            _sessionActive = true;

            Debug.Log($"[ExerciseCoordinator] BeginSession: userId={userId}, exercises={(_exercises != null ? _exercises.Length : 0)}, hudController={(_hudController != null ? "OK" : "NULL")}");

            StartCurrentExercise();
        }

        /// <summary>
        /// Pauses the currently active exercise.
        /// </summary>
        public void PauseSession()
        {
            if (_currentIndex < _exercises.Length && _exercises[_currentIndex].IsActive)
            {
                _exercises[_currentIndex].StopExercise();
            }
        }

        /// <summary>
        /// Resumes the currently paused exercise.
        /// </summary>
        public void ResumeSession()
        {
            if (_currentIndex < _exercises.Length && !_exercises[_currentIndex].IsActive)
            {
                _exercises[_currentIndex].StartExercise();
            }
        }

        /// <summary>
        /// Returns the current session data snapshot.
        /// </summary>
        public SessionData GetCurrentSessionData()
        {
            if (_sessionData != null)
            {
                _sessionData.exercises = new List<ExerciseMetrics>(_metrics);
            }

            return _sessionData;
        }

        private void Start()
        {
            RebindReferences();

            // Auto-find exercises in scene if not wired in Inspector
            if (_exercises == null || _exercises.Length == 0)
            {
                _exercises = FindObjectsByType<BaseExercise>(FindObjectsSortMode.InstanceID);
                if (_exercises.Length > 0)
                    Debug.Log($"[ExerciseCoordinator] Auto-found {_exercises.Length} exercises in scene.");
                else
                    Debug.LogWarning("[ExerciseCoordinator] No exercises found. Wire _exercises in Inspector or add Exercise components to scene.");
            }

            // Auto-find HUD if not wired
            if (_hudController == null)
            {
                _hudController = FindAnyObjectByType<HUDController>();
                if (_hudController != null)
                    Debug.Log("[ExerciseCoordinator] Auto-found HUDController.");
            }

            // Auto-find audio cues if not wired
            if (_audioCues == null)
            {
                _audioCues = FindAnyObjectByType<ExerciseAudioCues>();
            }
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
            RebindReferences();
        }
        private void RebindReferences()
        {
            // Re-find exercises — covers both first load and scene reload
            if (_exercises == null || _exercises.Length == 0)
            {
                _exercises = FindObjectsByType<BaseExercise>(FindObjectsSortMode.InstanceID);
                if (_exercises.Length > 0)
                    Debug.Log($"[ExerciseCoordinator] Bound {_exercises.Length} exercises.");
                else
                    Debug.LogWarning("[ExerciseCoordinator] No BaseExercise components found in scene.");
            }

            if (_hudController == null)
            {
                _hudController = FindAnyObjectByType<HUDController>();
                if (_hudController != null)
                    Debug.Log("[ExerciseCoordinator] Bound HUDController.");
            }

            if (_audioCues == null)
                _audioCues = FindAnyObjectByType<ExerciseAudioCues>();
        }

        // ADD: allows BaseExercise.Start() to self-register if auto-find ran too early
        public void RegisterExercise(BaseExercise exercise)
        {
            if (exercise == null) return;

            // Grow the array by one slot
            var list = new List<BaseExercise>(_exercises ?? Array.Empty<BaseExercise>());
            if (!list.Contains(exercise))
            {
                list.Add(exercise);
                _exercises = list.ToArray();
                Debug.Log($"[ExerciseCoordinator] Late-registered exercise: {exercise.GetType().Name}");
            }
        }



        private void Update()
        {
            if (!_sessionActive || _exercises == null || _currentIndex >= _exercises.Length)
                return;

            BaseExercise exercise = _exercises[_currentIndex];
            if (!exercise.IsActive)
                return;

            // Gather exercise state
            float elapsed = SessionElapsedTime;
            string exerciseName = exercise.GetType().Name.Replace("Exercise", "");
            int reps = exercise.CurrentReps;
            int targetReps = exercise.TargetReps;
            float accuracy = exercise.Evaluate() * 100f;

            // Get hold progress from exercises that support it
            float holdProgress = 0f;
            string instruction = $"Complete {targetReps} repetitions.";
            string activeHandLabel = "";
            bool showSpreadOverlay = false;

            if (exercise is GripHoldExercise gripHold)
            {
                holdProgress = gripHold.HoldProgress;
                instruction = "Grip the object and hold steady.";
            }
            else if (exercise is PrecisionPinchingExercise pinch)
            {
                holdProgress = pinch.HoldProgress;
                string handHint = string.IsNullOrEmpty(pinch.ActiveHandLabel)
                    ? "(either hand)" : $"({pinch.ActiveHandLabel})";
                instruction = $"Pinch your {pinch.CurrentFingerName} finger {handHint} and hold.";
                activeHandLabel = pinch.ActiveHandLabel;
            }
            else if (exercise is FingerSpreadingExercise spread)
            {
                holdProgress = spread.HoldProgress;
                instruction = spread.GetSpreadAngleDisplay();
                showSpreadOverlay = true;
            }
            else if (exercise is ThumbOppositionExercise thumbOpp)
            {
                holdProgress = thumbOpp.HoldProgress;
                string handHint = string.IsNullOrEmpty(thumbOpp.ActiveHandLabel)
                    ? "(either hand)" : $"({thumbOpp.ActiveHandLabel})";
                instruction = $"Touch thumb to {thumbOpp.CurrentFingerName} {handHint}.";
                activeHandLabel = thumbOpp.ActiveHandLabel;
            }
            else if (exercise is PinchHoldSphereExercise pinchSphere)
            {
                holdProgress = pinchSphere.HoldProgress;
                string handHint = string.IsNullOrEmpty(pinchSphere.ActiveHandLabel)
                    ? "(either hand)" : $"({pinchSphere.ActiveHandLabel})";
                instruction = $"Pinch the sphere between thumb and index finger {handHint} and hold.";
                activeHandLabel = pinchSphere.ActiveHandLabel;
            }

            // Push state to HUD
            if (_hudController != null)
            {
                _hudController.UpdateHUD(
                    elapsed,
                    exerciseName,
                    instruction,
                    _currentIndex + 1,
                    reps,
                    targetReps,
                    accuracy,
                    holdProgress);

                // Update grip panels with curl-based grip strength
                if (HandTrackingManager.Instance != null)
                {
                    float leftGrip = 0f;
                    float rightGrip = 0f;
                    OVRHand leftHand = HandTrackingManager.Instance.LeftHand;
                    OVRHand rightHand = HandTrackingManager.Instance.RightHand;

                    if (leftHand != null && HandTrackingManager.Instance.IsLeftTracked)
                    {
                        OVRSkeleton leftSkel = HandTrackingManager.Instance.LeftSkeleton;
                        leftGrip = HandTrackingManager.Instance.GetFingerCurlGripStrength(leftHand, leftSkel);
                    }

                    if (rightHand != null && HandTrackingManager.Instance.IsRightTracked)
                    {
                        OVRSkeleton rightSkel = HandTrackingManager.Instance.RightSkeleton;
                        rightGrip = HandTrackingManager.Instance.GetFingerCurlGripStrength(rightHand, rightSkel);
                    }

                    _hudController.UpdateGripPanels(leftHand, leftGrip, rightHand, rightGrip);
                }

                // Update bilateral hand label
                _hudController.UpdateActiveHandLabel(activeHandLabel);

                // Show/hide spread angle overlay based on exercise type
                _hudController.SetSpreadOverlayVisible(showSpreadOverlay);
            }

            // Update exercise object progress — use serialized array if populated,
            // fall back to self-registered objects otherwise.
            // For registered objects: use _currentIndex if in range, otherwise use
            // the first registered object (single exercise object shared across exercises).
            ExerciseObjectController activeObject = null;

            bool hasSerializedObjects = _exerciseObjects != null
                && _currentIndex < _exerciseObjects.Length
                && _exerciseObjects[_currentIndex] != null;

            if (hasSerializedObjects)
            {
                activeObject = _exerciseObjects[_currentIndex];
            }
            else if (_registeredObjects.Count > 0)
            {
                // Use matching index if available, otherwise use the first object.
                // This supports both multi-object setups (1 object per exercise)
                // and single-object setups (1 shared exercise object).
                int objIdx = _currentIndex < _registeredObjects.Count ? _currentIndex : 0;
                activeObject = _registeredObjects[objIdx];
            }

            if (activeObject != null && targetReps > 0)
            {
                activeObject.SetProgress(reps / (float)targetReps);
            }
        }

        private void StartCurrentExercise()
        {
            if (_exercises == null || _currentIndex >= _exercises.Length)
            {
                Debug.LogWarning("[ExerciseCoordinator] No exercises configured or all completed.");
                return;
            }

            BaseExercise exercise = _exercises[_currentIndex];
            exercise.OnExerciseCompleted += OnExerciseFinished;
            exercise.StartExercise();

            // Subscribe audio cues to current exercise
            if (_audioCues != null)
            {
                _audioCues.SubscribeToExercise(exercise);
            }

            // Initialize HUD tracking state
            if (_hudController != null)
            {
                _hudController.ShowTrackingLost(false);
            }

            // Subscribe to tracking events for HUD
            if (HandTrackingManager.Instance != null)
            {
                HandTrackingManager.Instance.OnTrackingLost += HandleTrackingLostForHUD;
                HandTrackingManager.Instance.OnTrackingRestored += HandleTrackingRestoredForHUD;
            }

            // TTS exercise introduction with specific instructions
            if (TTSVoiceGuide.Instance != null)
            {
                string exerciseName = exercise.GetType().Name.Replace("Exercise", "");
                string voiceInstruction = GetExerciseVoiceInstruction(exercise);
                TTSVoiceGuide.Instance.SpeakExerciseIntro(exerciseName, voiceInstruction);
            }

            Debug.Log($"[ExerciseCoordinator] Started exercise {_currentIndex + 1}/{_exercises.Length}: {exercise.GetType().Name}");
        }

        private void OnExerciseFinished(ExerciseMetrics metrics)
        {
            BaseExercise exercise = _exercises[_currentIndex];
            exercise.OnExerciseCompleted -= OnExerciseFinished;

            // Unsubscribe tracking events for this exercise
            if (HandTrackingManager.Instance != null)
            {
                HandTrackingManager.Instance.OnTrackingLost -= HandleTrackingLostForHUD;
                HandTrackingManager.Instance.OnTrackingRestored -= HandleTrackingRestoredForHUD;
            }

            if (HandTrackingManager.Instance != null)
            {
                OVRSkeleton leftSkel = HandTrackingManager.Instance.LeftSkeleton;
                OVRSkeleton rightSkel = HandTrackingManager.Instance.RightSkeleton;
                float leftGrip = HandTrackingManager.Instance.GetFingerCurlGripStrength(
                    HandTrackingManager.Instance.LeftHand, leftSkel);
                float rightGrip = HandTrackingManager.Instance.GetFingerCurlGripStrength(
                    HandTrackingManager.Instance.RightHand, rightSkel);
                metrics.gripStrength = (leftGrip + rightGrip) / 2f;
            }

            _metrics.Add(metrics);
            _currentIndex++;

            // Show feedback on HUD
            if (_hudController != null)
            {
                _hudController.ShowFeedback($"Exercise complete! Accuracy: {metrics.accuracy:F0}%");
            }

            // TTS exercise completion
            if (TTSVoiceGuide.Instance != null)
            {
                TTSVoiceGuide.Instance.SpeakExerciseComplete(metrics.exerciseName, metrics.accuracy);
            }

            Debug.Log($"[ExerciseCoordinator] Exercise completed: {metrics.exerciseName} — Accuracy: {metrics.accuracy:F1}%");

            if (_currentIndex < _exercises.Length)
            {
                StartCurrentExercise();
            }
            else
            {
                FinalizeSession();
            }
        }

        private void HandleTrackingLostForHUD()
        {
            if (_hudController != null)
            {
                _hudController.ShowTrackingLost(true);
            }
        }

        private void HandleTrackingRestoredForHUD()
        {
            if (_hudController != null)
            {
                _hudController.ShowTrackingLost(false);
            }
        }

        /// <summary>
        /// Returns a detailed voice instruction for the given exercise type.
        /// Provides clear, rehab-focused guidance for audio-first VR experience.
        /// </summary>
        private string GetExerciseVoiceInstruction(BaseExercise exercise)
        {
            if (exercise is GripHoldExercise)
            {
                return $"Grab any object on the table and squeeze firmly for three seconds. " +
                    $"You can use either hand. Complete {exercise.TargetReps} holds.";
            }
            else if (exercise is PrecisionPinchingExercise)
            {
                return $"Touch your thumb to each finger one at a time and hold the pinch. " +
                    $"Start with your index finger. Use either hand. Complete {exercise.TargetReps} pinches.";
            }
            else if (exercise is FingerSpreadingExercise)
            {
                return $"Spread all your fingers as wide as you can, like a star. " +
                    $"Hold the spread position for the target duration. Complete {exercise.TargetReps} spreads.";
            }
            else if (exercise is FingerTappingExercise)
            {
                return $"Tap your index finger and thumb together quickly and repeatedly. " +
                    $"Each tap counts as one rep. Complete {exercise.TargetReps} taps.";
            }
            else if (exercise is ThumbOppositionExercise)
            {
                return $"Touch your thumb to each finger in sequence: index, middle, ring, then pinky. " +
                    $"Move smoothly from one finger to the next. Complete {exercise.TargetReps} full sequences.";
            }
            else if (exercise is PinchHoldSphereExercise)
            {
                return $"Bring your thumb and index finger together around the small sphere and hold for two seconds. " +
                    $"You can use either hand. Complete {exercise.TargetReps} holds.";
            }

            return $"Follow the on-screen instructions. Complete {exercise.TargetReps} repetitions.";
        }

        private void FinalizeSession()
        {
            _sessionActive = false;
            _sessionData.endTimestamp = DateTime.UtcNow.ToString("o");
            _sessionData.exercises = new List<ExerciseMetrics>(_metrics);

            float totalAccuracy = 0f;
            float totalGrip = 0f;
            float totalDuration = 0f;

            for (int i = 0; i < _metrics.Count; i++)
            {
                totalAccuracy += _metrics[i].accuracy;
                totalGrip += _metrics[i].gripStrength;
                totalDuration += _metrics[i].duration;
            }

            int count = Mathf.Max(1, _metrics.Count);
            _sessionData.overallAccuracy = totalAccuracy / count;
            _sessionData.averageGripStrength = totalGrip / count;
            _sessionData.totalDuration = totalDuration;

            Debug.Log($"[ExerciseCoordinator] Session complete. Overall accuracy: {_sessionData.overallAccuracy:F1}%, Duration: {_sessionData.totalDuration:F1}s");

            // TTS session completion
            if (TTSVoiceGuide.Instance != null)
            {
                TTSVoiceGuide.Instance.SpeakSessionComplete(_sessionData.overallAccuracy);
            }

            OnSessionComplete?.Invoke(_sessionData);
        }
    }
}
