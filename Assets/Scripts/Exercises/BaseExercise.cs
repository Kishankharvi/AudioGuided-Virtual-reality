using System;
using System.Collections.Generic;
using UnityEngine;
using AGVRSystem.Data;

namespace AGVRSystem.Exercises
{
    /// <summary>
    /// Abstract base class for all exercises. Handles rep counting, adaptive difficulty, and completion events.
    /// </summary>
    public abstract class BaseExercise : MonoBehaviour
    {
        private const int RollingWindowSize = 3;
        private const float HighAccuracyThreshold = 0.85f;
        private const float LowAccuracyThreshold = 0.50f;
        private const float DifficultyStep = 0.1f;
        private const float MinDifficulty = 0.5f;
        private const float MaxDifficulty = 2.0f;

        public int TargetReps { get; protected set; }
        public int CurrentReps { get; protected set; }
        public float DifficultyMultiplier { get; protected set; } = 1.0f;
        public bool IsActive { get; protected set; }

        /// <summary>
        /// Passes current rep count when a rep is completed.
        /// </summary>
        public event Action<int> OnRepCompleted;

        /// <summary>
        /// Fired when all target reps are completed with the final metrics.
        /// </summary>
        public event Action<ExerciseMetrics> OnExerciseCompleted;

        protected List<float> _recentAccuracies = new List<float>();
        protected float _startTime;
        protected int _totalAttempts;

        /// <summary>
        /// Self-registers with ExerciseCoordinator on Start so late-added exercises are found.
        /// </summary>
        protected virtual void Start()
        {
            var coordinator = FindAnyObjectByType<ExerciseCoordinator>();
            coordinator?.RegisterExercise(this);
        }

        /// <summary>
        /// Registers a completed rep with an accuracy value. Adjusts difficulty adaptively.
        /// </summary>
        public void RegisterRep(float accuracy)
        {
            if (!IsActive)
                return;

            CurrentReps++;
            _totalAttempts++;

            _recentAccuracies.Add(accuracy);
            if (_recentAccuracies.Count > RollingWindowSize)
            {
                _recentAccuracies.RemoveAt(0);
            }

            AdjustDifficulty();
            OnRepCompleted?.Invoke(CurrentReps);

            if (CurrentReps >= TargetReps)
            {
                CompleteExercise();
            }
        }

        private void AdjustDifficulty()
        {
            if (_recentAccuracies.Count < RollingWindowSize)
                return;

            if (_recentAccuracies.TrueForAll(a => a > HighAccuracyThreshold))
            {
                DifficultyMultiplier = Mathf.Min(MaxDifficulty, DifficultyMultiplier + DifficultyStep);
            }
            else if (_recentAccuracies.TrueForAll(a => a < LowAccuracyThreshold))
            {
                DifficultyMultiplier = Mathf.Max(MinDifficulty, DifficultyMultiplier - DifficultyStep);
            }
        }

        private void CompleteExercise()
        {
            IsActive = false;

            var metrics = new ExerciseMetrics
            {
                exerciseName = GetType().Name,
                accuracy = Evaluate() * 100f,
                repsCompleted = CurrentReps,
                targetReps = TargetReps,
                duration = Time.time - _startTime,
                startTimestamp = DateTime.UtcNow.AddSeconds(-(Time.time - _startTime)).ToString("o"),
                endTimestamp = DateTime.UtcNow.ToString("o")
            };

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

            OnExerciseCompleted?.Invoke(metrics);
        }

        /// <summary>
        /// Starts the exercise. Resets all counters and state.
        /// </summary>
        public abstract void StartExercise();

        /// <summary>
        /// Stops the exercise prematurely.
        /// </summary>
        public abstract void StopExercise();

        /// <summary>
        /// Returns current accuracy score (0-1).
        /// </summary>
        public abstract float Evaluate();

        /// <summary>
        /// Resets base state for a new exercise run.
        /// </summary>
        protected void ResetBase()
        {
            CurrentReps = 0;
            _totalAttempts = 0;
            DifficultyMultiplier = 1.0f;
            _recentAccuracies.Clear();
            _startTime = Time.time;
            IsActive = true;
        }
    }
}
