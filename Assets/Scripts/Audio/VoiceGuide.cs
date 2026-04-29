using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace AGVRSystem.Audio
{
    /// <summary>
    /// Provides dynamic voice guidance using Meta's Text To Speech Agent.
    /// Uses reflection to access the TTS agent, avoiding direct assembly dependency.
    /// Manages a priority queue of voice lines, prevents overlap, and provides
    /// contextual guidance for calibration, exercises, and navigation.
    ///
    /// When TTS is unavailable (no internet, agent missing, or reflection failure),
    /// automatically falls back to pre-recorded offline AudioClips from OfflineVoiceClips.
    /// </summary>
    public class TTSVoiceGuide : MonoBehaviour
    {
        public static TTSVoiceGuide Instance { get; private set; }

        /// <summary>Priority levels for voice lines. Higher priority interrupts lower.</summary>
        public enum VoicePriority
        {
            Low = 0,
            Normal = 1,
            High = 2,
            Critical = 3
        }

        [Header("TTS Agent")]
        [Tooltip("Drag the component with TextToSpeechAgent here.")]
        [SerializeField] private MonoBehaviour _ttsAgentComponent;

        [Header("Offline Fallback")]
        [Tooltip("ScriptableObject with all pre-recorded AudioClips. Used when TTS is unavailable.")]
        [SerializeField] private OfflineVoiceClips _offlineClips;

        [Header("Settings")]
        [SerializeField] private float _delayBetweenLines = 0.5f;
        [SerializeField] private bool _enableVoiceGuide = true;
        [SerializeField] private float _estimatedSpeechDuration = 3f;

        private readonly Queue<(string text, VoicePriority priority, AudioClip fallback)> _lineQueue =
            new Queue<(string, VoicePriority, AudioClip)>();

        private bool _isSpeaking;
        private Coroutine _speakCoroutine;
        private AudioSource _offlineSource;

        // Reflection-cached members
        private PropertyInfo _currentTextProp;
        private FieldInfo _textField;
        private MethodInfo _speakMethod;
        private FieldInfo _finishedField;
        private bool _reflReady;
        private bool _evtSubscribed;

        /// <summary>Fired when a voice line starts speaking.</summary>
        public event Action<string> OnSpeakStarted;

        /// <summary>Fired when a voice line finishes.</summary>
        public event Action OnSpeakDone;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Dedicated AudioSource for offline clip playback — 2D, non-spatial voice guidance
            _offlineSource = gameObject.AddComponent<AudioSource>();
            _offlineSource.playOnAwake = false;
            _offlineSource.spatialBlend = 0f;

            InitReflection();
        }

        private void OnEnable()
        {
            SubscribeFinished();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            UnsubscribeFinished();
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        /// <summary>
        /// Re-discovers the TTS agent after scene transitions.
        /// Since TTSVoiceGuide uses DontDestroyOnLoad, the old _ttsAgentComponent
        /// reference becomes stale when a new scene loads with a new TTS building block.
        /// </summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            RebindTTSAgent();
        }

        /// <summary>
        /// Finds and binds the TTS agent component in the current scene.
        /// </summary>
        private void RebindTTSAgent()
        {
            // Check if the current agent is still alive
            if (_ttsAgentComponent != null && _ttsAgentComponent.isActiveAndEnabled)
                return;

            UnsubscribeFinished();
            _ttsAgentComponent = null;
            _reflReady = false;

            // Search for the TTS agent in the scene by type name
            // (avoids direct assembly dependency on Meta.WitAi)
            MonoBehaviour[] allBehaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var mb in allBehaviours)
            {
                if (mb == null) continue;
                string typeName = mb.GetType().Name;
                if (typeName.Contains("TextToSpeech") || typeName.Contains("TTSAgent") ||
                    typeName.Contains("SpeechSynthesis"))
                {
                    _ttsAgentComponent = mb;
                    Debug.Log($"[TTSVoiceGuide] Re-bound TTS agent: {typeName} on {mb.gameObject.name}");
                    break;
                }
            }

            InitReflection();
            SubscribeFinished();
        }

        // ===== TTS AVAILABILITY =====

        /// <summary>
        /// Returns true when TTS is ready — agent assigned, active, and reflection succeeded.
        /// When false, offline clips play instead.
        /// </summary>
        private bool IsTTSAvailable()
        {
            return _reflReady
                && _ttsAgentComponent != null
                && _ttsAgentComponent.isActiveAndEnabled;
        }

        // ===== PUBLIC API =====

        /// <summary>
        /// Assigns the offline clips at runtime (used by AudioSystemBootstrapper to inject
        /// the asset loaded from Resources without requiring a scene reference).
        /// </summary>
        public void SetOfflineClips(OfflineVoiceClips clips)
        {
            _offlineClips = clips;
        }

        /// <summary>
        /// Queues a voice line. Uses TTS when available, plays the fallbackClip when not.
        /// </summary>
        public void Speak(string text, VoicePriority priority = VoicePriority.Normal,
                          AudioClip fallbackClip = null)
        {
            if (!_enableVoiceGuide || string.IsNullOrWhiteSpace(text))
                return;

            if (priority == VoicePriority.Critical && _isSpeaking)
                StopAll();

            if (priority >= VoicePriority.High)
                ClearLowerPriority(priority);

            _lineQueue.Enqueue((text, priority, fallbackClip));

            if (!_isSpeaking)
                _speakCoroutine = StartCoroutine(ProcessQueue());
        }

        /// <summary>
        /// Stops current speech and clears the queue.
        /// </summary>
        public void StopAll()
        {
            if (_speakCoroutine != null)
            {
                StopCoroutine(_speakCoroutine);
                _speakCoroutine = null;
            }

            _lineQueue.Clear();
            _isSpeaking = false;

            if (_offlineSource != null && _offlineSource.isPlaying)
                _offlineSource.Stop();
        }

        /// <summary>
        /// Toggles voice guidance on or off.
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            _enableVoiceGuide = enabled;
            if (!enabled)
                StopAll();
        }

        // ===== CONTEXTUAL VOICE LINE HELPERS =====

        /// <summary>Speaks a welcome message for the main menu.</summary>
        public void SpeakWelcome()
        {
            Speak("Welcome to the hand rehabilitation system. Please enter your user ID to begin.",
                VoicePriority.Normal, _offlineClips != null ? _offlineClips.welcome : null);
        }

        /// <summary>Speaks calibration instructions.</summary>
        public void SpeakCalibrationStart()
        {
            Speak("Please hold both hands in front of you with fingers spread apart. Keep them steady for calibration.",
                VoicePriority.High, _offlineClips != null ? _offlineClips.calibrationStart : null);
        }

        /// <summary>Speaks calibration progress update.</summary>
        public void SpeakCalibrationProgress()
        {
            Speak("Good. Keep your hands steady. Almost there.",
                VoicePriority.Normal, _offlineClips != null ? _offlineClips.calibrationProgress : null);
        }

        /// <summary>Speaks calibration success.</summary>
        public void SpeakCalibrationComplete()
        {
            Speak("Calibration complete. Starting your rehabilitation session now.",
                VoicePriority.High, _offlineClips != null ? _offlineClips.calibrationComplete : null);
        }

        /// <summary>Speaks exercise introduction. Selects the matching intro clip by exercise name.</summary>
        public void SpeakExerciseIntro(string exerciseName, string instruction)
        {
            AudioClip clip = GetExerciseIntroClip(exerciseName);
            Speak($"Next exercise: {exerciseName}. {instruction}", VoicePriority.High, clip);
        }

        /// <summary>Speaks encouragement during exercises based on accuracy.</summary>
        public void SpeakEncouragement(float accuracy)
        {
            if (accuracy >= 0.85f)
            {
                Speak("Excellent work! Keep it up.", VoicePriority.Low,
                    _offlineClips != null ? _offlineClips.encourageHigh : null);
            }
            else if (accuracy >= 0.6f)
            {
                Speak("Good progress. Try to match the target position more closely.", VoicePriority.Low,
                    _offlineClips != null ? _offlineClips.encourageMid : null);
            }
            else
            {
                Speak("Take your time. Focus on controlled, steady movements.", VoicePriority.Normal,
                    _offlineClips != null ? _offlineClips.encourageLow : null);
            }
        }

        /// <summary>Speaks tracking lost warning.</summary>
        public void SpeakTrackingLost()
        {
            Speak("Hand tracking lost. Please bring your hands back into view.", VoicePriority.Critical,
                _offlineClips != null ? _offlineClips.trackingLost : null);
        }

        /// <summary>Speaks tracking restored confirmation.</summary>
        public void SpeakTrackingRestored()
        {
            Speak("Tracking restored. Continue your exercise.", VoicePriority.High,
                _offlineClips != null ? _offlineClips.trackingRestored : null);
        }

        /// <summary>Speaks exercise completion with accuracy-based rating clip.</summary>
        public void SpeakExerciseComplete(string exerciseName, float accuracy)
        {
            string rating = accuracy >= 85f ? "Outstanding" : accuracy >= 60f ? "Well done" : "Good effort";
            AudioClip clip = GetCompletionClip(accuracy);
            Speak($"{rating}! {exerciseName} complete with {accuracy:F0} percent accuracy.",
                VoicePriority.High, clip);
        }

        /// <summary>Speaks full session completion.</summary>
        public void SpeakSessionComplete(float overallAccuracy)
        {
            Speak($"Session complete. Your overall accuracy was {overallAccuracy:F0} percent. Great job today!",
                VoicePriority.High, _offlineClips != null ? _offlineClips.sessionComplete : null);
        }

        /// <summary>Speaks a halfway milestone.</summary>
        public void SpeakMilestone(int currentRep, int targetReps)
        {
            Speak($"Halfway there! {currentRep} of {targetReps} reps completed.", VoicePriority.Low,
                _offlineClips != null ? _offlineClips.milestone : null);
        }

        // ===== OFFLINE CLIP SELECTION =====

        private AudioClip GetExerciseIntroClip(string exerciseName)
        {
            if (_offlineClips == null) return null;

            string lower = exerciseName.ToLowerInvariant();
            if (lower.Contains("grip"))   return _offlineClips.introGripHold;
            if (lower.Contains("pinch"))  return _offlineClips.introPrecisionPinch;
            if (lower.Contains("spread")) return _offlineClips.introFingerSpreading;
            if (lower.Contains("tap"))    return _offlineClips.introFingerTapping;
            if (lower.Contains("thumb"))  return _offlineClips.introThumbOpposition;
            return null;
        }

        private AudioClip GetCompletionClip(float accuracy)
        {
            if (_offlineClips == null) return null;

            if (accuracy >= 85f) return _offlineClips.completionOutstanding;
            if (accuracy >= 60f) return _offlineClips.completionWellDone;
            return _offlineClips.completionGoodEffort;
        }

        // ===== REFLECTION-BASED TTS ACCESS =====

        private void InitReflection()
        {
            if (_ttsAgentComponent == null)
            {
                Debug.LogWarning("[TTSVoiceGuide] No TTS agent assigned — offline clips will be used as fallback.");
                return;
            }

            Type agentType = _ttsAgentComponent.GetType();
            BindingFlags pubInst = BindingFlags.Public | BindingFlags.Instance;
            BindingFlags allInst = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            _currentTextProp = agentType.GetProperty("CurrentText", pubInst);
            _textField = agentType.GetField("text", allInst);

            _speakMethod = agentType.GetMethod("SpeakText", pubInst, null, Type.EmptyTypes, null)
                        ?? agentType.GetMethod("Speak", pubInst, null, Type.EmptyTypes, null);

            _finishedField = agentType.GetField("onSpeakFinished", allInst);

            _reflReady = _speakMethod != null && (_currentTextProp != null || _textField != null);

            Debug.Log($"[TTSVoiceGuide] Reflection ready={_reflReady} for {agentType.Name}. " +
                      $"Offline fallback: {(_offlineClips != null ? "assigned" : "not assigned")}");
        }

        private void SetAgentText(string text)
        {
            if (_ttsAgentComponent == null) return;

            if (_currentTextProp != null && _currentTextProp.CanWrite)
                _currentTextProp.SetValue(_ttsAgentComponent, text);
            else if (_textField != null)
                _textField.SetValue(_ttsAgentComponent, text);
        }

        private void InvokeSpeak()
        {
            if (_speakMethod != null && _ttsAgentComponent != null)
                _speakMethod.Invoke(_ttsAgentComponent, null);
        }

        private void SubscribeFinished()
        {
            if (_evtSubscribed || _finishedField == null || _ttsAgentComponent == null)
                return;

            object eventObj = _finishedField.GetValue(_ttsAgentComponent);
            if (eventObj is UnityEvent unityEvent)
            {
                unityEvent.AddListener(HandleFinished);
                _evtSubscribed = true;
            }
        }

        private void UnsubscribeFinished()
        {
            if (!_evtSubscribed || _finishedField == null || _ttsAgentComponent == null)
                return;

            object eventObj = _finishedField.GetValue(_ttsAgentComponent);
            if (eventObj is UnityEvent unityEvent)
            {
                unityEvent.RemoveListener(HandleFinished);
                _evtSubscribed = false;
            }
        }

        // ===== QUEUE PROCESSING =====

        private IEnumerator ProcessQueue()
        {
            _isSpeaking = true;

            while (_lineQueue.Count > 0)
            {
                var (text, priority, fallbackClip) = _lineQueue.Dequeue();

                if (IsTTSAvailable())
                {
                    Debug.Log($"[TTSVoiceGuide] TTS ({priority}): \"{text}\"");

                    SetAgentText(text);
                    InvokeSpeak();
                    OnSpeakStarted?.Invoke(text);

                    // Wait for TTS to finish via event, or timeout based on text length
                    float elapsed = 0f;
                    float timeout = _estimatedSpeechDuration + (text.Length * 0.06f);

                    while (_isSpeaking && elapsed < timeout)
                    {
                        elapsed += Time.deltaTime;
                        yield return null;
                    }
                }
                else
                {
                    // TTS unavailable — play the pre-recorded offline clip
                    if (fallbackClip != null && _offlineSource != null)
                    {
                        Debug.Log($"[TTSVoiceGuide] Offline fallback ({priority}): \"{fallbackClip.name}\"");

                        _offlineSource.clip = fallbackClip;
                        _offlineSource.Play();
                        OnSpeakStarted?.Invoke(text);

                        yield return new WaitWhile(() => _offlineSource != null && _offlineSource.isPlaying);
                    }
                    else
                    {
                        Debug.LogWarning($"[TTSVoiceGuide] TTS unavailable, no offline clip for: \"{text}\"");
                    }
                }

                OnSpeakDone?.Invoke();

                if (_lineQueue.Count > 0)
                {
                    _isSpeaking = true;
                    yield return new WaitForSeconds(_delayBetweenLines);
                }
            }

            _isSpeaking = false;
            _speakCoroutine = null;
        }

        private void HandleFinished()
        {
            OnSpeakDone?.Invoke();

            if (_lineQueue.Count == 0)
                _isSpeaking = false;
        }

        private void ClearLowerPriority(VoicePriority minPriority)
        {
            if (_lineQueue.Count == 0) return;

            var temp = new Queue<(string text, VoicePriority priority, AudioClip fallback)>();
            while (_lineQueue.Count > 0)
            {
                var item = _lineQueue.Dequeue();
                if (item.priority >= minPriority)
                    temp.Enqueue(item);
            }

            while (temp.Count > 0)
                _lineQueue.Enqueue(temp.Dequeue());
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
