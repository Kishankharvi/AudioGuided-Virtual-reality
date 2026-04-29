using UnityEngine;

namespace AGVRSystem.Audio
{
    /// <summary>
    /// Provides audio feedback for UI interactions (button clicks, hovers, errors, success).
    /// Uses procedurally generated tones - no AudioClip assets required.
    /// Singleton that persists across scenes.
    /// </summary>
    public class UIAudioFeedback : MonoBehaviour
    {
        public static UIAudioFeedback Instance { get; private set; }

        [Header("Volume Controls")]
        [SerializeField] private float _masterVolume = 0.6f;
        [SerializeField] private float _clickVolume = 0.5f;
        [SerializeField] private float _hoverVolume = 0.25f;
        [SerializeField] private float _errorVolume = 0.4f;
        [SerializeField] private float _successVolume = 0.45f;

        [Header("Audio Source")]
        [SerializeField] private AudioSource _audioSource;

        private AudioClip _clickClip;
        private AudioClip _hoverClip;
        private AudioClip _errorClip;
        private AudioClip _successClip;
        private AudioClip _countdownClip;
        private AudioClip _countdownFinalClip;
        private AudioClip _transitionClip;

        private const float ClickFrequency = 1200f;
        private const float ClickDuration = 0.06f;
        private const float HoverFrequency = 800f;
        private const float HoverDuration = 0.04f;
        private const float CountdownFrequency = 880f;
        private const float CountdownFinalFrequency = 1320f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            EnsureAudioSource();
            GenerateClips();
        }

        /// <summary>
        /// Plays a crisp click sound for button presses.
        /// </summary>
        public void PlayClick()
        {
            PlayClip(_clickClip, _clickVolume);
        }

        /// <summary>
        /// Plays a subtle hover sound for UI element focus.
        /// </summary>
        public void PlayHover()
        {
            PlayClip(_hoverClip, _hoverVolume);
        }

        /// <summary>
        /// Plays an error buzz for validation failures.
        /// </summary>
        public void PlayError()
        {
            PlayClip(_errorClip, _errorVolume);
        }

        /// <summary>
        /// Plays a success chime for completed actions.
        /// </summary>
        public void PlaySuccess()
        {
            PlayClip(_successClip, _successVolume);
        }

        /// <summary>
        /// Plays a countdown beep (non-final seconds).
        /// </summary>
        public void PlayCountdownBeep()
        {
            PlayClip(_countdownClip, _clickVolume);
        }

        /// <summary>
        /// Plays the final countdown beep (higher pitch).
        /// </summary>
        public void PlayCountdownFinal()
        {
            PlayClip(_countdownFinalClip, _clickVolume * 1.2f);
        }

        /// <summary>
        /// Plays a smooth transition whoosh.
        /// </summary>
        public void PlayTransition()
        {
            PlayClip(_transitionClip, _clickVolume * 0.8f);
        }

        private void PlayClip(AudioClip clip, float volume)
        {
            if (_audioSource == null || clip == null)
                return;

            _audioSource.PlayOneShot(clip, volume * _masterVolume);
        }

        private void EnsureAudioSource()
        {
            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
            }

            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 0f; // 2D - UI sounds should be non-spatial
            _audioSource.priority = 64; // High priority for UI
        }

        private void GenerateClips()
        {
            _clickClip = ProceduralToneGenerator.CreateTone(
                "UI_Click", ClickFrequency, ClickDuration, 0.5f,
                ProceduralToneGenerator.WaveShape.SoftSine);

            _hoverClip = ProceduralToneGenerator.CreateTone(
                "UI_Hover", HoverFrequency, HoverDuration, 0.3f,
                ProceduralToneGenerator.WaveShape.SoftSine);

            _errorClip = ProceduralToneGenerator.CreateErrorBuzz("UI_Error");

            _successClip = ProceduralToneGenerator.CreateSuccessChime("UI_Success");

            _countdownClip = ProceduralToneGenerator.CreateCountdownBeep(
                "UI_Countdown", CountdownFrequency);

            _countdownFinalClip = ProceduralToneGenerator.CreateCountdownBeep(
                "UI_CountdownFinal", CountdownFinalFrequency, 0.12f, 0.45f);

            _transitionClip = ProceduralToneGenerator.CreateDing(
                "UI_Transition", 440f, 0.3f, 0.3f);

            Debug.Log("[UIAudioFeedback] Procedural audio clips generated");
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
