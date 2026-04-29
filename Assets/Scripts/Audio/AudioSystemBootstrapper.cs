using UnityEngine;

namespace AGVRSystem.Audio
{
    /// <summary>
    /// Ensures UIAudioFeedback and TTSVoiceGuide singletons exist in every scene.
    /// Runs automatically before any scene starts — no manual scene setup required.
    ///
    /// INTERNET IS NOT REQUIRED FOR AUDIO.
    /// TTSVoiceGuide uses offline AudioClips from OfflineVoiceClips when no TTS agent is
    /// assigned. Place OfflineVoiceClips.asset inside Assets/Resources/ for auto-loading.
    /// </summary>
    public static class AudioSystemBootstrapper
    {
        private const string AudioRootPrefix = "[AudioSystem]";
        private const string OfflineClipsResourcePath = "OfflineVoiceClips";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            EnsureUIAudioFeedback();
            EnsureTTSVoiceGuide();
            EnsureAudioListener();
        }

        private static void EnsureUIAudioFeedback()
        {
            if (UIAudioFeedback.Instance != null) return;
            CreatePersistentRoot("UIAudioFeedback").AddComponent<UIAudioFeedback>();
            Debug.Log("[AudioSystemBootstrapper] Created UIAudioFeedback.");
        }

        private static void EnsureTTSVoiceGuide()
        {
            if (TTSVoiceGuide.Instance != null) return;

            var go = CreatePersistentRoot("TTSVoiceGuide");
            var guide = go.AddComponent<TTSVoiceGuide>();

            var clips = Resources.Load<OfflineVoiceClips>(OfflineClipsResourcePath);
            if (clips != null)
            {
                guide.SetOfflineClips(clips);
                Debug.Log("[AudioSystemBootstrapper] OfflineVoiceClips loaded — audio fully offline.");
            }
            else
            {
                Debug.LogWarning("[AudioSystemBootstrapper] OfflineVoiceClips not in Resources/. " +
                    "Move Assets/Settings/OfflineVoiceClips.asset → Assets/Resources/OfflineVoiceClips.asset");
            }
        }

        private static void EnsureAudioListener()
        {
            if (Object.FindAnyObjectByType<AudioListener>() != null) return;
            CreatePersistentRoot("AudioListener").AddComponent<AudioListener>();
            Debug.Log("[AudioSystemBootstrapper] Added fallback AudioListener.");
        }

        private static GameObject CreatePersistentRoot(string label)
        {
            string name = $"{AudioRootPrefix}_{label}";
            var existing = GameObject.Find(name);
            if (existing != null) return existing;
            var go = new GameObject(name);
            Object.DontDestroyOnLoad(go);
            return go;
        }
    }
}
