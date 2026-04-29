using System;

namespace AGVRSystem.Data
{
    /// <summary>
    /// Serializable data class for per-exercise results.
    /// </summary>
    [Serializable]
    public class ExerciseMetrics
    {
        public string exerciseName;
        public float accuracy;
        public float gripStrength;
        public int repsCompleted;
        public int targetReps;
        public float duration;
        public string startTimestamp;
        public string endTimestamp;
    }
}
