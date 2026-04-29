using UnityEngine;

namespace AGVRSystem.UI
{
    /// <summary>
    /// Holds display metadata for a single exercise: name, description, technique tips, and illustration.
    /// </summary>
    [CreateAssetMenu(fileName = "ExerciseInfo_New", menuName = "AGVRSystem/Exercise Info Data")]
    public class ExerciseInfoData : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string _displayName = "Exercise";

        [Header("Content")]
        [TextArea(3, 6)]
        [SerializeField] private string _description = "";

        [TextArea(2, 4)]
        [SerializeField] private string _techniqueTips = "";

        [Header("Visuals")]
        [SerializeField] private Sprite _illustration;
        [SerializeField] private Color _accentColor = Color.white;

        [Header("Metrics")]
        [SerializeField] private int _targetReps;
        [SerializeField] private string _difficulty = "Moderate";

        /// <summary>Exercise display name.</summary>
        public string DisplayName => _displayName;

        /// <summary>Multi-line description of the exercise.</summary>
        public string Description => _description;

        /// <summary>Technique tips or coaching notes.</summary>
        public string TechniqueTips => _techniqueTips;

        /// <summary>Illustration sprite shown on the info board.</summary>
        public Sprite Illustration => _illustration;

        /// <summary>Accent color for exercise-specific highlights.</summary>
        public Color AccentColor => _accentColor;

        /// <summary>Target repetitions for this exercise.</summary>
        public int TargetReps => _targetReps;

        /// <summary>Difficulty label (Easy / Moderate / Hard).</summary>
        public string Difficulty => _difficulty;
    }
}
