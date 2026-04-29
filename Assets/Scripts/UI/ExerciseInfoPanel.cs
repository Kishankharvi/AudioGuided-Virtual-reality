using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AGVRSystem.Exercises;

namespace AGVRSystem.UI
{
    /// <summary>
    /// Displays detailed information about the current exercise on a world-space info board.
    /// Listens to ExerciseCoordinator transitions and updates description, image, tips, and metrics.
    /// </summary>
    public class ExerciseInfoPanel : MonoBehaviour
    {
        [Header("Exercise Data (ordered to match ExerciseCoordinator)")]
        [SerializeField] private List<ExerciseInfoData> _exerciseInfoList = new List<ExerciseInfoData>();

        [Header("UI References")]
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _descriptionText;
        [SerializeField] private TMP_Text _tipsText;
        [SerializeField] private TMP_Text _tipsLabel;
        [SerializeField] private Image _illustrationImage;
        [SerializeField] private TMP_Text _repsText;
        [SerializeField] private TMP_Text _difficultyText;
        [SerializeField] private TMP_Text _stepIndicatorText;
        [SerializeField] private Image _accentBar;

        [Header("Coordinator Reference")]
        [SerializeField] private ExerciseCoordinator _coordinator;

        private const float PollInterval = 0.25f;

        private int _lastExerciseIndex = -1;
        private float _pollTimer;
        private BaseExercise[] _exercises;

        private void Start()
        {
            CacheExercises();
            UpdatePanel(0);
        }

        private void Update()
        {
            _pollTimer += Time.deltaTime;
            if (_pollTimer < PollInterval)
                return;

            _pollTimer = 0f;
            int currentIndex = GetCurrentExerciseIndex();

            if (currentIndex != _lastExerciseIndex)
            {
                UpdatePanel(currentIndex);
            }
        }

        /// <summary>
        /// Updates all UI elements to reflect the exercise at the given index.
        /// </summary>
        public void UpdatePanel(int exerciseIndex)
        {
            _lastExerciseIndex = exerciseIndex;

            if (_exerciseInfoList == null || exerciseIndex < 0 || exerciseIndex >= _exerciseInfoList.Count)
            {
                SetFallbackContent(exerciseIndex);
                return;
            }

            ExerciseInfoData info = _exerciseInfoList[exerciseIndex];
            if (info == null)
            {
                SetFallbackContent(exerciseIndex);
                return;
            }

            if (_titleText != null)
            {
                _titleText.text = info.DisplayName;
            }

            if (_descriptionText != null)
            {
                _descriptionText.text = info.Description;
            }

            if (_tipsText != null)
            {
                _tipsText.text = info.TechniqueTips;
                _tipsText.gameObject.SetActive(!string.IsNullOrEmpty(info.TechniqueTips));
            }

            if (_tipsLabel != null)
            {
                _tipsLabel.gameObject.SetActive(!string.IsNullOrEmpty(info.TechniqueTips));
            }

            if (_illustrationImage != null)
            {
                if (info.Illustration != null)
                {
                    _illustrationImage.sprite = info.Illustration;
                    _illustrationImage.gameObject.SetActive(true);
                    _illustrationImage.preserveAspect = true;
                }
                else
                {
                    _illustrationImage.gameObject.SetActive(false);
                }
            }

            if (_repsText != null)
            {
                _repsText.text = info.TargetReps > 0
                    ? $"Target: {info.TargetReps} reps"
                    : "";
            }

            if (_difficultyText != null)
            {
                _difficultyText.text = !string.IsNullOrEmpty(info.Difficulty)
                    ? $"Difficulty: {info.Difficulty}"
                    : "";
            }

            if (_stepIndicatorText != null)
            {
                _stepIndicatorText.text = $"Exercise {exerciseIndex + 1} of {_exerciseInfoList.Count}";
            }

            if (_accentBar != null)
            {
                _accentBar.color = info.AccentColor;
            }
        }

        private void SetFallbackContent(int exerciseIndex)
        {
            if (_titleText != null)
            {
                _titleText.text = $"Exercise {exerciseIndex + 1}";
            }

            if (_descriptionText != null)
            {
                _descriptionText.text = "Preparing exercise...";
            }

            if (_tipsText != null)
            {
                _tipsText.gameObject.SetActive(false);
            }

            if (_tipsLabel != null)
            {
                _tipsLabel.gameObject.SetActive(false);
            }

            if (_illustrationImage != null)
            {
                _illustrationImage.gameObject.SetActive(false);
            }

            if (_stepIndicatorText != null)
            {
                int total = _exerciseInfoList != null ? _exerciseInfoList.Count : 0;
                _stepIndicatorText.text = total > 0
                    ? $"Exercise {exerciseIndex + 1} of {total}"
                    : "";
            }
        }

        private void CacheExercises()
        {
            if (_coordinator == null)
            {
                _coordinator = FindFirstObjectByType<ExerciseCoordinator>();
            }

            if (_coordinator != null)
            {
                // Use reflection-free approach: find BaseExercise children in Exercises container
                var exercisesParent = GameObject.Find("Exercises");
                if (exercisesParent != null)
                {
                    _exercises = exercisesParent.GetComponentsInChildren<BaseExercise>(true);
                }
            }
        }

        private int GetCurrentExerciseIndex()
        {
            if (_exercises == null || _exercises.Length == 0)
                return 0;

            for (int i = 0; i < _exercises.Length; i++)
            {
                if (_exercises[i] != null && _exercises[i].IsActive)
                {
                    return i;
                }
            }

            // If none active, find last completed by checking from end
            for (int i = _exercises.Length - 1; i >= 0; i--)
            {
                if (_exercises[i] != null && _exercises[i].CurrentReps > 0)
                {
                    return i;
                }
            }

            return _lastExerciseIndex >= 0 ? _lastExerciseIndex : 0;
        }
    }
}
