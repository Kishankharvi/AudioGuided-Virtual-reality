using UnityEngine;

namespace AGVRSystem.Audio
{
    /// <summary>
    /// Configures AudioSources for spatial audio in VR.
    /// Voice source positioned 0.5m ahead of player for clear speech.
    /// Ambient source provides calming background atmosphere using procedural audio.
    /// Manages audio listener on camera and spatial blend settings.
    /// </summary>
    public class SpatialAudioController : MonoBehaviour
    {
        [Header("Audio Sources")]
        [SerializeField] private AudioSource _voiceSource;
        [SerializeField] private AudioSource _ambientSource;

        [Header("References")]
        [SerializeField] private Transform _cameraTransform;

        [Header("Preloaded Clips")]
        [SerializeField] private AudioClip[] _preloadedClips;

        [Header("Ambient Settings")]
        [SerializeField] private float _ambientVolume = 0.08f;
        [SerializeField] private bool _generateAmbientOnStart = true;

        [Header("Voice Settings")]
        [SerializeField] private float _voiceDistance = 0.5f;
        [SerializeField] private float _voiceVolume = 1.0f;

        private AudioClip _generatedAmbientClip;
        private bool _ambientPlaying;

        private const float AmbientPadDuration = 10f;

        private void Start()
        {
            ConfigureVoiceSource();
            ConfigureAmbientSource();
            PreloadClips();

            if (_generateAmbientOnStart)
            {
                StartAmbient();
            }
        }

        private void LateUpdate()
        {
            if (_voiceSource == null || _cameraTransform == null)
                return;

            _voiceSource.transform.position =
                _cameraTransform.position + _cameraTransform.forward * _voiceDistance;
            _voiceSource.transform.rotation = _cameraTransform.rotation;
        }

        /// <summary>
        /// Starts the ambient background audio. Generates a procedural pad if no clip is assigned.
        /// </summary>
        public void StartAmbient()
        {
            if (_ambientSource == null)
                return;

            if (_ambientSource.clip == null)
            {
                _generatedAmbientClip = ProceduralToneGenerator.CreateAmbientPad(
                    "Ambient_Pad", AmbientPadDuration, 1.0f);
                _ambientSource.clip = _generatedAmbientClip;
                Debug.Log("[SpatialAudioController] Procedural ambient pad generated");
            }

            _ambientSource.loop = true;
            _ambientSource.volume = _ambientVolume;
            _ambientSource.Play();
            _ambientPlaying = true;

            Debug.Log("[SpatialAudioController] Ambient audio started");
        }

        /// <summary>
        /// Stops ambient audio playback.
        /// </summary>
        public void StopAmbient()
        {
            if (_ambientSource != null)
            {
                _ambientSource.Stop();
                _ambientPlaying = false;
            }
        }

        /// <summary>
        /// Sets ambient volume (0-1).
        /// </summary>
        public void SetAmbientVolume(float volume)
        {
            _ambientVolume = Mathf.Clamp01(volume);

            if (_ambientSource != null)
            {
                _ambientSource.volume = _ambientVolume;
            }
        }

        /// <summary>
        /// Sets voice source volume (0-1).
        /// </summary>
        public void SetVoiceVolume(float volume)
        {
            _voiceVolume = Mathf.Clamp01(volume);

            if (_voiceSource != null)
            {
                _voiceSource.volume = _voiceVolume;
            }
        }

        /// <summary>
        /// Returns whether ambient audio is currently playing.
        /// </summary>
        public bool IsAmbientPlaying => _ambientPlaying;

        private void ConfigureVoiceSource()
        {
            if (_voiceSource == null)
                return;

            _voiceSource.spatialBlend = 1f;
            _voiceSource.volume = _voiceVolume;
            _voiceSource.rolloffMode = AudioRolloffMode.Linear;
            _voiceSource.minDistance = 0.3f;
            _voiceSource.maxDistance = 3f;
            _voiceSource.priority = 64;
        }

        private void ConfigureAmbientSource()
        {
            if (_ambientSource == null)
                return;

            _ambientSource.spatialBlend = 0f; // 2D - ambient fills the space
            _ambientSource.volume = _ambientVolume;
            _ambientSource.loop = true;
            _ambientSource.priority = 200; // Low priority
        }

        private void PreloadClips()
        {
            if (_preloadedClips == null)
                return;

            for (int i = 0; i < _preloadedClips.Length; i++)
            {
                if (_preloadedClips[i] != null)
                {
                    _preloadedClips[i].LoadAudioData();
                }
            }
        }
    }
}
