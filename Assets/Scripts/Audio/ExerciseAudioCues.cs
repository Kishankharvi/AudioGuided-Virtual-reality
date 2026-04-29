using System.Collections;
using UnityEngine;
using AGVRSystem.Exercises;

namespace AGVRSystem.Audio
{
    /// <summary>
    /// Provides audio feedback for exercise events: rep completion dings,
    /// exercise start/end fanfares, accuracy-based tonal variations,
    /// countdown beeps, and per-finger voice cues for precision exercises.
    /// Hooks into BaseExercise and ExerciseCoordinator events.
    /// </summary>
    public class ExerciseAudioCues : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private AudioSource _cueSource;
        [SerializeField] private ExerciseCoordinator _coordinator;

        [Header("Volume")]
        [SerializeField] private float _repDingVolume = 0.4f;
        [SerializeField] private float _exerciseStartVolume = 0.35f;
        [SerializeField] private float _sessionCompleteVolume = 0.5f;
        [SerializeField] private float _fingerCueVolume = 0.3f;

        [Header("Spatial")]
        [SerializeField] private float _spatialBlend = 0.3f;
        [SerializeField] private Transform _cameraTransform;

        // Cached procedural clips
        private AudioClip _repDingLow;
        private AudioClip _repDingMid;
        private AudioClip _repDingHigh;
        private AudioClip _exerciseStartClip;
        private AudioClip _exerciseCompleteClip;
        private AudioClip _sessionCompleteClip;
        private AudioClip _milestoneClip;

        // Per-finger procedural tones (ascending pitch: Index lowest -> Pinky highest)
        private AudioClip _fingerIndexClip;
        private AudioClip _fingerMiddleClip;
        private AudioClip _fingerRingClip;
        private AudioClip _fingerPinkyClip;
        private AudioClip _tapStartClip;

        private const float LowAccuracyThreshold = 0.5f;
        private const float HighAccuracyThreshold = 0.85f;
        private const float CuePositionDistance = 0.4f;

        private BaseExercise _currentExercise;

        // Track finger changes to avoid repeating the same cue
        private int _lastAnnouncedFingerIdx = -1;

        private void Awake()
        {
            EnsureAudioSource();
            GenerateClips();

            if (_coordinator == null)
            {
                _coordinator = FindAnyObjectByType<ExerciseCoordinator>();
                if (_coordinator != null)
                    Debug.Log("[ExerciseAudioCues] Auto-found ExerciseCoordinator.");
            }

            if (_cameraTransform == null)
            {
                var mainCam = Camera.main;
                if (mainCam != null)
                    _cameraTransform = mainCam.transform;
            }
        }

        private void OnEnable()
        {
            if (_coordinator != null)
            {
                _coordinator.OnSessionComplete += HandleSessionComplete;
            }
        }

        private void OnDisable()
        {
            if (_coordinator != null)
            {
                _coordinator.OnSessionComplete -= HandleSessionComplete;
            }

            UnsubscribeFromExercise();
        }

        private void LateUpdate()
        {
            // Position audio slightly in front of the player
            if (_cameraTransform != null && _cueSource != null)
            {
                _cueSource.transform.position =
                    _cameraTransform.position + _cameraTransform.forward * CuePositionDistance;
            }

            // Monitor finger changes in precision exercises
            MonitorFingerChanges();
        }

        /// <summary>
        /// Subscribe to an exercise's events for audio feedback.
        /// Call this when a new exercise starts.
        /// </summary>
        public void SubscribeToExercise(BaseExercise exercise)
        {
            UnsubscribeFromExercise();

            _currentExercise = exercise;
            _lastAnnouncedFingerIdx = -1;

            if (_currentExercise != null)
            {
                _currentExercise.OnRepCompleted += HandleRepCompleted;
                _currentExercise.OnExerciseCompleted += HandleExerciseCompleted;
                PlayExerciseStart();

                // Announce first finger for precision exercises
                if (_currentExercise is PrecisionPinchingExercise pinch)
                {
                    // Brief delay so the exercise-start chime plays first
                    StartCoroutine(DelayedAnnounceFingerCoroutine(pinch.CurrentFingerName, pinch.CurrentFingerIndex, 0.6f));
                }
                else if (_currentExercise is FingerTappingExercise)
                {
                    PlayCue(_tapStartClip, _fingerCueVolume);
                    SpeakFingerGuidance("Tap your index finger and thumb together quickly and repeatedly.");
                }
                else if (_currentExercise is PinchHoldSphereExercise)
                {
                    PlayCue(_fingerIndexClip, _fingerCueVolume);
                    SpeakFingerGuidance("Pinch the small sphere between your thumb and index finger and hold for two seconds.");
                }
            }
        }

        /// <summary>
        /// Plays the rep completion sound with pitch variation based on accuracy.
        /// </summary>
        public void PlayRepDing(float accuracy)
        {
            AudioClip clip;

            if (accuracy >= HighAccuracyThreshold)
                clip = _repDingHigh;
            else if (accuracy >= LowAccuracyThreshold)
                clip = _repDingMid;
            else
                clip = _repDingLow;

            PlayCue(clip, _repDingVolume);
        }

        /// <summary>
        /// Plays the exercise start sound.
        /// </summary>
        public void PlayExerciseStart()
        {
            PlayCue(_exerciseStartClip, _exerciseStartVolume);
        }

        /// <summary>
        /// Plays the exercise completion sound.
        /// </summary>
        public void PlayExerciseComplete()
        {
            PlayCue(_exerciseCompleteClip, _exerciseStartVolume * 1.2f);
        }

        /// <summary>
        /// Plays the full session completion fanfare.
        /// </summary>
        public void PlaySessionComplete()
        {
            PlayCue(_sessionCompleteClip, _sessionCompleteVolume);
        }

        /// <summary>
        /// Plays a milestone sound (e.g., halfway through reps).
        /// </summary>
        public void PlayMilestone()
        {
            PlayCue(_milestoneClip, _repDingVolume * 1.1f);
        }

        /// <summary>
        /// Plays the per-finger identification tone (ascending pitch per finger).
        /// </summary>
        public void PlayFingerTone(int fingerIndex)
        {
            AudioClip clip = fingerIndex switch
            {
                0 => _fingerIndexClip,
                1 => _fingerMiddleClip,
                2 => _fingerRingClip,
                3 => _fingerPinkyClip,
                _ => _fingerIndexClip
            };
            PlayCue(clip, _fingerCueVolume);
        }

        // ===== FINGER CHANGE MONITORING =====

        /// <summary>
        /// Monitors the current exercise for finger target changes and announces them.
        /// Called every frame in LateUpdate to catch finger transitions.
        /// </summary>
        private void MonitorFingerChanges()
        {
            if (_currentExercise == null) return;

            if (_currentExercise is PrecisionPinchingExercise pinch)
            {
                int currentIdx = pinch.CurrentFingerIndex;
                if (currentIdx != _lastAnnouncedFingerIdx)
                {
                    AnnounceFingerChange(pinch.CurrentFingerName, currentIdx);
                }
            }
            else if (_currentExercise is ThumbOppositionExercise thumbOpp)
            {
                // ThumbOpposition also cycles fingers
                int seqIdx = Mathf.FloorToInt(thumbOpp.SequenceProgress * 4f);
                if (seqIdx != _lastAnnouncedFingerIdx)
                {
                    AnnounceFingerChange(thumbOpp.CurrentFingerName, seqIdx);
                }
            }
        }

        /// <summary>
        /// Announces a finger change with both a tonal cue and voice guidance.
        /// Uses High priority so the cue interrupts any queued narration and is
        /// heard immediately — critical for the user to know which finger to use.
        /// </summary>
        private void AnnounceFingerChange(string fingerName, int fingerIndex)
        {
            _lastAnnouncedFingerIdx = fingerIndex;

            // Tonal identifying cue (ascending pitch: index lowest → pinky highest)
            PlayFingerTone(fingerIndex);

            // Build a clear, descriptive instruction
            string instruction;
            if (_currentExercise is ThumbOppositionExercise)
            {
                instruction = $"Touch your thumb to your {fingerName} finger.";
            }
            else
            {
                // For precision pinching: name the finger AND repeat the hand hint
                string handHint = "";
                if (_currentExercise is PrecisionPinchingExercise pinch)
                {
                    string side = pinch.ActiveHandLabel;
                    handHint = string.IsNullOrEmpty(side)
                        ? " — use either hand"
                        : $" with your {side} hand";
                }
                instruction = $"Now pinch your {fingerName} finger{handHint}.";
            }

            SpeakFingerGuidance(instruction);

            Debug.Log($"[ExerciseAudioCues] Finger change announced: {fingerName} (idx={fingerIndex})");
        }

        /// <summary>
        /// Speaks finger-specific guidance at High priority so it plays immediately
        /// and is not pushed behind lower-priority narration in the queue.
        /// </summary>
        private void SpeakFingerGuidance(string text)
        {
            if (TTSVoiceGuide.Instance != null)
            {
                TTSVoiceGuide.Instance.Speak(text, TTSVoiceGuide.VoicePriority.High);
            }
        }

        // ===== EVENT HANDLERS =====

        private void HandleRepCompleted(int repCount)
        {
            if (_currentExercise == null)
                return;

            float accuracy = _currentExercise.Evaluate();
            PlayRepDing(accuracy);

            // Milestone at halfway
            if (_currentExercise.TargetReps > 0 &&
                repCount == _currentExercise.TargetReps / 2)
            {
                StartCoroutine(DelayedPlayCoroutine(_milestoneClip, _repDingVolume, 0.2f));

                if (TTSVoiceGuide.Instance != null)
                {
                    TTSVoiceGuide.Instance.SpeakMilestone(repCount, _currentExercise.TargetReps);
                }
            }

            // Encouragement every 3 reps
            if (repCount > 0 && repCount % 3 == 0)
            {
                if (TTSVoiceGuide.Instance != null)
                {
                    TTSVoiceGuide.Instance.SpeakEncouragement(accuracy);
                }
            }
        }

        private void HandleExerciseCompleted(Data.ExerciseMetrics metrics)
        {
            PlayExerciseComplete();

            if (TTSVoiceGuide.Instance != null)
            {
                TTSVoiceGuide.Instance.SpeakExerciseComplete(metrics.exerciseName, metrics.accuracy);
            }

            Debug.Log($"[ExerciseAudioCues] Exercise '{metrics.exerciseName}' completed — playing completion cue");
        }

        private void HandleSessionComplete(Data.SessionData sessionData)
        {
            StartCoroutine(DelayedPlayCoroutine(_sessionCompleteClip, _sessionCompleteVolume, 0.5f));

            if (TTSVoiceGuide.Instance != null)
            {
                TTSVoiceGuide.Instance.SpeakSessionComplete(sessionData.overallAccuracy);
            }

            Debug.Log("[ExerciseAudioCues] Session completed — playing fanfare");
        }

        private void UnsubscribeFromExercise()
        {
            if (_currentExercise != null)
            {
                _currentExercise.OnRepCompleted -= HandleRepCompleted;
                _currentExercise.OnExerciseCompleted -= HandleExerciseCompleted;
                _currentExercise = null;
            }
            _lastAnnouncedFingerIdx = -1;
        }

        private void PlayCue(AudioClip clip, float volume)
        {
            if (_cueSource == null || clip == null)
                return;

            _cueSource.PlayOneShot(clip, volume);
        }

        private IEnumerator DelayedPlayCoroutine(AudioClip clip, float volume, float delay)
        {
            yield return new WaitForSeconds(delay);
            PlayCue(clip, volume);
        }

        private IEnumerator DelayedAnnounceFingerCoroutine(string fingerName, int fingerIndex, float delay)
        {
            yield return new WaitForSeconds(delay);
            AnnounceFingerChange(fingerName, fingerIndex);
        }

        private void EnsureAudioSource()
        {
            if (_cueSource == null)
            {
                _cueSource = gameObject.AddComponent<AudioSource>();
            }

            _cueSource.playOnAwake = false;
            _cueSource.spatialBlend = _spatialBlend;
            _cueSource.priority = 96;
        }

        private void GenerateClips()
        {
            // Rep dings at different pitches for accuracy feedback
            _repDingLow = ProceduralToneGenerator.CreateDing("Rep_Low", 660f, 0.12f, 0.35f);
            _repDingMid = ProceduralToneGenerator.CreateDing("Rep_Mid", 880f, 0.15f, 0.4f);
            _repDingHigh = ProceduralToneGenerator.CreateDing("Rep_High", 1100f, 0.15f, 0.45f);

            // Exercise start (ascending two-note)
            _exerciseStartClip = ProceduralToneGenerator.CreateDing("ExStart", 523f, 0.25f, 0.35f);

            // Exercise complete (success chime)
            _exerciseCompleteClip = ProceduralToneGenerator.CreateSuccessChime("ExComplete", 0.5f, 0.4f);

            // Session complete (longer, richer fanfare)
            _sessionCompleteClip = ProceduralToneGenerator.CreateSuccessChime("SessionComplete", 0.8f, 0.45f);

            // Milestone (bright ding)
            _milestoneClip = ProceduralToneGenerator.CreateDing("Milestone", 1320f, 0.2f, 0.4f);

            // Per-finger identification tones (ascending pitch: Index -> Pinky)
            // Each finger gets a distinct note: C5, E5, G5, B5
            _fingerIndexClip = ProceduralToneGenerator.CreateDing("Finger_Index", 523f, 0.18f, 0.3f);
            _fingerMiddleClip = ProceduralToneGenerator.CreateDing("Finger_Middle", 659f, 0.18f, 0.3f);
            _fingerRingClip = ProceduralToneGenerator.CreateDing("Finger_Ring", 784f, 0.18f, 0.3f);
            _fingerPinkyClip = ProceduralToneGenerator.CreateDing("Finger_Pinky", 988f, 0.18f, 0.3f);

            // Finger tapping start cue (quick double-tap sound)
            _tapStartClip = ProceduralToneGenerator.CreateDing("Tap_Start", 700f, 0.1f, 0.25f);

            Debug.Log("[ExerciseAudioCues] All audio clips generated (reps, exercises, fingers, taps)");
        }
    }
}
