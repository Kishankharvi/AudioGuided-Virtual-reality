using UnityEditor;
using UnityEngine;

namespace AGVRSystem.Editor
{
    /// <summary>
    /// Backward-compatible wrapper. Delegates to OfflineVoiceClipsCreator.
    /// </summary>
    [InitializeOnLoad]
    public static class OfflineClipsMover
    {
        static OfflineClipsMover()
        {
            // Delegate to the creator which both creates and wires the asset
        }

        [MenuItem("AGVRSystem/Copy OfflineVoiceClips to Resources")]
        public static void EnsureClipsInResources()
        {
            OfflineVoiceClipsCreator.CreateAsset();
        }
    }
}
