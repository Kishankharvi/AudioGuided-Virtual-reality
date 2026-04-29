using UnityEditor;
using UnityEngine;
using AGVRSystem.Audio;

namespace AGVRSystem.Editor
{
    /// <summary>
    /// Creates and wires the OfflineVoiceClips ScriptableObject in Assets/Resources/.
    /// Run once via the menu: AGVRSystem > Create Offline Voice Clips Asset.
    /// </summary>
    public static class OfflineVoiceClipsCreator
    {
        private const string ResourceDir = "Assets/Resources";
        private const string AssetPath = "Assets/Resources/OfflineVoiceClips.asset";
        private const string AudioDir = "Assets/audios";

        [MenuItem("AGVRSystem/Create Offline Voice Clips Asset")]
        public static void CreateAsset()
        {
            // Ensure Resources folder exists
            if (!AssetDatabase.IsValidFolder(ResourceDir))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            // Load or create the asset
            var existing = AssetDatabase.LoadAssetAtPath<OfflineVoiceClips>(AssetPath);
            if (existing == null)
            {
                existing = ScriptableObject.CreateInstance<OfflineVoiceClips>();
                AssetDatabase.CreateAsset(existing, AssetPath);
                Debug.Log("[OfflineVoiceClipsCreator] Created OfflineVoiceClips.asset");
            }

            // Wire all audio clips from Assets/audios/
            existing.welcome              = LoadClip("welcome");
            existing.calibrationStart     = LoadClip("calib_start");
            existing.calibrationProgress  = LoadClip("calib_progress");
            existing.calibrationComplete  = LoadClip("calib_complete");
            existing.introGripHold        = LoadClip("ex_intro_grip");
            existing.introPrecisionPinch  = LoadClip("ex_intro_pinch");
            existing.introFingerSpreading = LoadClip("ex_intro_spread");
            existing.introFingerTapping   = LoadClip("ex_intro_tap");
            existing.introThumbOpposition = LoadClip("ex_intro_thumb");
            existing.completionOutstanding = LoadClip("ex_complete_outstanding");
            existing.completionWellDone   = LoadClip("ex_complete_welldone");
            existing.completionGoodEffort = LoadClip("ex_complete_goodeffort");
            existing.sessionComplete      = LoadClip("session_complete");
            existing.milestone            = LoadClip("milestone_halfway");
            existing.trackingLost         = LoadClip("tracking_los");
            existing.trackingRestored     = LoadClip("tracking_restored");
            existing.encourageHigh        = LoadClip("encourage_high");
            existing.encourageMid         = LoadClip("encourage_mid");
            existing.encourageLow         = LoadClip("encourage_low");

            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[OfflineVoiceClipsCreator] All 19 audio clips wired. Asset saved at: " + AssetPath);
            Selection.activeObject = existing;
        }

        private static AudioClip LoadClip(string fileName)
        {
            string path = $"{AudioDir}/{fileName}.wav";
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
            {
                Debug.LogWarning($"[OfflineVoiceClipsCreator] Audio clip not found: {path}");
            }
            return clip;
        }

        /// <summary>
        /// Auto-run on editor startup to ensure the asset exists.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void EnsureAssetExists()
        {
            var existing = AssetDatabase.LoadAssetAtPath<OfflineVoiceClips>(AssetPath);
            if (existing == null)
            {
                // Delay to avoid asset database issues during import
                EditorApplication.delayCall += CreateAsset;
            }
        }
    }
}
