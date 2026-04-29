using System;
using System.Collections.Generic;
using UnityEngine;

namespace AGVRSystem.Data
{
    /// <summary>
    /// Serializable container for full session data.
    /// </summary>
    [Serializable]
    public class SessionData
    {
        public string sessionId;
        public string userId;
        public string startTimestamp;
        public string endTimestamp;
        public float overallAccuracy;
        public float averageGripStrength;
        public float totalDuration;
        public List<ExerciseMetrics> exercises = new List<ExerciseMetrics>();

        /// <summary>
        /// Serializes this session data to a JSON string.
        /// </summary>
        public string ToJson()
        {
            return JsonUtility.ToJson(this, true);
        }

        /// <summary>
        /// Deserializes a JSON string into a SessionData instance.
        /// </summary>
        public static SessionData FromJson(string json)
        {
            return JsonUtility.FromJson<SessionData>(json);
        }
    }
}
