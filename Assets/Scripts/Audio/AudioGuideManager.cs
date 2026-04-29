using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AGVRSystem.Audio
{
    /// <summary>
    /// Manages voice guidance through 5 phases. Coroutine-based queue prevents overlap.
    /// Integrates with TTSVoiceGuide for dynamic speech and falls back to AudioClips.
    /// Auto-triggers correction when tracking is lost or accuracy drops.
    /// </summary>
    public class AudioGuideManager : MonoBehaviour
    {
        public enum AudioPhase
        {
            Intro,
            Instruction,
            Encouragement,
            Correction,
            Completion
        }

        [Header("Audio Source")]
        [SerializeField] private AudioSource _voiceSource;

        [Header("Optional Fallback Clips")]
        [SerializeField] private AudioClip _correctionClip;
        [SerializeField] private AudioClip _introClip;
        [SerializeField] private AudioClip _completionClip;

        [Header("TTS Fallback Messages")]
        [SerializeField] private string _correctionMessage = "Please bring your hands back into view for tracking.";
        [SerializeField] private string _trackingRestoredMessage = "Tracking restored. Let's continue.";

        [Header("Timing")]
        [SerializeField] private float _encouragementInterval = 30f;

        private const float TrackingLostThreshold = 2f;

        private readonly Queue<(AudioPhase phase, AudioClip clip, string ttsText)> _clipQueue =
            new Queue<(AudioPhase, AudioClip, string)>();

        private Coroutine _playbackCoroutine;
        private float _trackingLostDuration;
        private bool _correctionQueued;
        private float _lastEncouragementTime;
        private bool _trackingEventsSubscribed;

        private void Awake()
        {
            GenerateFallbackClips();

            // Auto-find voice source if not wired
            if (_voiceSource == null)
            {
                _voiceSource = GetComponentInChildren<AudioSource>();
                if (_voiceSource == null)
                {
                    _voiceSource = gameObject.AddComponent<AudioSource>();
                    _voiceSource.playOnAwake = false;
                    _voiceSource.spatialBlend = 0f;
                    Debug.Log("[AudioGuideManager] Auto-created AudioSource.");
                }
            }
        }

        private void OnEnable()
        {
            TrySubscribeTrackingEvents();
        }

        private void OnDisable()
        {
            UnsubscribeTrackingEvents();
        }

        private void Update()
        {
            // Retry subscription if HandTrackingManager wasn't available at OnEnable time
            if (!_trackingEventsSubscribed)
            {
                TrySubscribeTrackingEvents();
            }

            if (HandTrackingManager.Instance == null)
                return;

            // Tracking lost detection
            if (!HandTrackingManager.Instance.IsLeftTracked &&
                !HandTrackingManager.Instance.IsRightTracked)
            {
                _trackingLostDuration += Time.deltaTime;

                if (_trackingLostDuration >= TrackingLostThreshold && !_correctionQueued)
                {
                    PlayCorrection();
                }
            }
            else
            {
                _trackingLostDuration = 0f;
                _correctionQueued = false;
            }

            // Periodic encouragement via TTS
            if (Time.time - _lastEncouragementTime > _encouragementInterval)
            {
                _lastEncouragementTime = Time.time;
                if (TTSVoiceGuide.Instance != null &&
                    HandTrackingManager.Instance.IsLeftTracked &&
                    HandTrackingManager.Instance.IsRightTracked)
                {
                    TTSVoiceGuide.Instance.SpeakEncouragement(0.7f);
                }
            }
        }

        /// <summary>
        /// Enqueues a voice clip with the given phase. Starts playback if idle.
        /// If clip is null, uses TTS with the provided text instead.
        /// </summary>
        public void PlayGuide(AudioPhase phase, AudioClip clip, string ttsText = null)
        {
            if (clip == null && string.IsNullOrEmpty(ttsText))
                return;

            _clipQueue.Enqueue((phase, clip, ttsText));

            if (_playbackCoroutine == null)
            {
                _playbackCoroutine = StartCoroutine(PlayQueueCoroutine());
            }
        }

        /// <summary>
        /// Convenience overload for TTS-only guidance.
        /// </summary>
        public void PlayGuide(AudioPhase phase, string ttsText)
        {
            PlayGuide(phase, null, ttsText);
        }

        /// <summary>
        /// Auto-triggered when tracking is lost for too long.
        /// Always plays the fallback clip, and additionally uses TTS if available.
        /// </summary>
        public void PlayCorrection()
        {
            _correctionQueued = true;

            if (_correctionClip != null)
            {
                PlayGuide(AudioPhase.Correction, _correctionClip);
            }

            if (TTSVoiceGuide.Instance != null)
            {
                TTSVoiceGuide.Instance.SpeakTrackingLost();
            }
            else if (_correctionClip == null && UIAudioFeedback.Instance != null)
            {
                UIAudioFeedback.Instance.PlayError();
            }
        }

        /// <summary>
        /// Stops all playback and clears the queue.
        /// </summary>
        public void StopAll()
        {
            if (_playbackCoroutine != null)
            {
                StopCoroutine(_playbackCoroutine);
                _playbackCoroutine = null;
            }

            _clipQueue.Clear();

            if (_voiceSource != null)
            {
                _voiceSource.Stop();
            }

            if (TTSVoiceGuide.Instance != null)
            {
                TTSVoiceGuide.Instance.StopAll();
            }
        }

        private IEnumerator PlayQueueCoroutine()
        {
            while (_clipQueue.Count > 0)
            {
                var (phase, clip, ttsText) = _clipQueue.Dequeue();

                if (clip != null && _voiceSource != null)
                {
                    _voiceSource.clip = clip;
                    _voiceSource.Play();
                    Debug.Log($"[AudioGuideManager] Playing clip for phase: {phase}");
                    yield return new WaitForSeconds(clip.length);
                }
                else if (!string.IsNullOrEmpty(ttsText) && TTSVoiceGuide.Instance != null)
                {
                    TTSVoiceGuide.Instance.Speak(ttsText, TTSVoiceGuide.VoicePriority.Normal);
                    Debug.Log($"[AudioGuideManager] TTS for phase {phase}: \"{ttsText}\"");
                    yield return new WaitForSeconds(2f);
                }
            }

            _playbackCoroutine = null;
        }

        /// <summary>
        /// Generates procedural fallback clips for phases that have no assigned AudioClip.
        /// </summary>
        private void GenerateFallbackClips()
        {
            if (_correctionClip == null)
            {
                _correctionClip = ProceduralToneGenerator.CreateErrorBuzz("Correction_Fallback", 0.4f, 0.3f);
                Debug.Log("[AudioGuideManager] Generated procedural correction fallback clip");
            }

            if (_introClip == null)
            {
                _introClip = ProceduralToneGenerator.CreateDing("Intro_Fallback", 523f, 0.3f, 0.35f);
                Debug.Log("[AudioGuideManager] Generated procedural intro fallback clip");
            }

            if (_completionClip == null)
            {
                _completionClip = ProceduralToneGenerator.CreateSuccessChime("Completion_Fallback", 0.6f, 0.4f);
                Debug.Log("[AudioGuideManager] Generated procedural completion fallback clip");
            }
        }

        private void HandleTrackingLost()
        {
            _trackingLostDuration = 0f;

            if (UIAudioFeedback.Instance != null)
            {
                UIAudioFeedback.Instance.PlayError();
            }
        }

        private void HandleTrackingRestored()
        {
            _trackingLostDuration = 0f;
            _correctionQueued = false;

            if (TTSVoiceGuide.Instance != null)
            {
                TTSVoiceGuide.Instance.Speak(_trackingRestoredMessage, TTSVoiceGuide.VoicePriority.Normal);
            }

            if (UIAudioFeedback.Instance != null)
            {
                UIAudioFeedback.Instance.PlaySuccess();
            }
        }

        /// <summary>
        /// Subscribes to HandTrackingManager tracking events.
        /// Safe to call multiple times -- only subscribes once.
        /// </summary>
        private void TrySubscribeTrackingEvents()
        {
            if (_trackingEventsSubscribed) return;
            if (HandTrackingManager.Instance == null) return;

            HandTrackingManager.Instance.OnTrackingLost += HandleTrackingLost;
            HandTrackingManager.Instance.OnTrackingRestored += HandleTrackingRestored;
            _trackingEventsSubscribed = true;
            Debug.Log("[AudioGuideManager] Subscribed to HandTrackingManager events.");
        }

        /// <summary>
        /// Unsubscribes from HandTrackingManager tracking events.
        /// </summary>
        private void UnsubscribeTrackingEvents()
        {
            if (!_trackingEventsSubscribed) return;
            if (HandTrackingManager.Instance == null) return;

            HandTrackingManager.Instance.OnTrackingLost -= HandleTrackingLost;
            HandTrackingManager.Instance.OnTrackingRestored -= HandleTrackingRestored;
            _trackingEventsSubscribed = false;
        }
    }
}
