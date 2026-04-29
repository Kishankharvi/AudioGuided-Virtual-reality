using UnityEngine;
using UnityEngine.SceneManagement;

namespace AGVRSystem.Interaction
{
    /// <summary>
    /// Ensures HandTrackingManager and HandGrabber components exist in every scene.
    /// Runs automatically before any scene starts -- no manual scene setup required.
    ///
    /// This solves:
    /// - HandTrackingManager.Instance being null (visualizers/audio can't get hand data)
    /// - HandGrabbers missing in MainMenu/Calibration scenes (objects not grabbable)
    /// </summary>
    public static class HandTrackingBootstrapper
    {
        private const string ManagerObjectName = "[HandTrackingManager]";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            EnsureHandTrackingManager();
            EnsureHandGrabbers();

            // Re-run on every scene load to handle scene transitions
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // HandTrackingManager persists across scenes (DontDestroyOnLoad)
            // but HandGrabbers need to be re-added to new scene's OVR hands
            EnsureHandGrabbers();
        }

        /// <summary>
        /// Creates HandTrackingManager if it doesn't exist.
        /// This singleton persists across scenes via DontDestroyOnLoad.
        /// </summary>
        private static void EnsureHandTrackingManager()
        {
            if (HandTrackingManager.Instance != null) return;

            // Check if one exists but Instance hasn't been set yet (Awake not called)
            var existing = Object.FindAnyObjectByType<HandTrackingManager>();
            if (existing != null) return;

            var go = new GameObject(ManagerObjectName);
            go.AddComponent<HandTrackingManager>();
            Debug.Log("[HandTrackingBootstrapper] Created HandTrackingManager.");
        }

        /// <summary>
        /// Adds HandGrabber to each OVRHand visual in the scene.
        /// Safe to call multiple times -- skips objects that already have HandGrabber.
        /// </summary>
        private static void EnsureHandGrabbers()
        {
            // Try named hand visuals first (standard OVRCameraRig hierarchy)
            bool addedAny = false;
            addedAny |= TryAddGrabber("LeftHandVisual");
            addedAny |= TryAddGrabber("RightHandVisual");

            // Fallback: add to all OVRHand GameObjects
            if (!addedAny)
            {
                var hands = Object.FindObjectsByType<OVRHand>(FindObjectsSortMode.None);
                foreach (var hand in hands)
                {
                    if (hand == null) continue;
                    if (hand.GetComponent<HandGrabber>() == null)
                    {
                        hand.gameObject.AddComponent<HandGrabber>();
                        Debug.Log($"[HandTrackingBootstrapper] Added HandGrabber to OVRHand on {hand.gameObject.name}.");
                    }
                }
            }
        }

        private static bool TryAddGrabber(string handVisualName)
        {
            var go = GameObject.Find(handVisualName);
            if (go == null) return false;

            if (go.GetComponent<HandGrabber>() == null)
            {
                go.AddComponent<HandGrabber>();
                Debug.Log($"[HandTrackingBootstrapper] Added HandGrabber to {handVisualName}.");
            }

            return true;
        }
    }
}
