using UnityEngine;

namespace AGVRSystem.Audio
{
    /// <summary>
    /// ScriptableObject holding all offline fallback AudioClips used when TTS is unavailable.
    /// Assign each clip in the Inspector via the OfflineVoiceClips asset.
    /// </summary>
    [CreateAssetMenu(fileName = "OfflineVoiceClips", menuName = "AGVRSystem/Offline Voice Clips")]
    public class OfflineVoiceClips : ScriptableObject
    {
        [Header("Onboarding")]
        public AudioClip welcome;

        [Header("Calibration")]
        public AudioClip calibrationStart;
        public AudioClip calibrationProgress;
        public AudioClip calibrationComplete;

        [Header("Exercise Intros")]
        public AudioClip introGripHold;
        public AudioClip introPrecisionPinch;
        public AudioClip introFingerSpreading;
        public AudioClip introFingerTapping;
        public AudioClip introThumbOpposition;

        [Header("Exercise Completion")]
        public AudioClip completionOutstanding;
        public AudioClip completionWellDone;
        public AudioClip completionGoodEffort;

        [Header("Session")]
        public AudioClip sessionComplete;
        public AudioClip milestone;

        [Header("Tracking")]
        public AudioClip trackingLost;
        public AudioClip trackingRestored;

        [Header("Encouragement")]
        public AudioClip encourageHigh;
        public AudioClip encourageMid;
        public AudioClip encourageLow;
    }
}
