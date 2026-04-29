using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AGVRSystem
{
    /// <summary>
    /// Auto-starts the exercise session when the RehabSession scene loads.
    /// Reads the userId from PlayerPrefs (saved by MainMenuController).
    /// Also ensures HandGrabber components exist on hand visuals.
    ///
    /// Auto-adds itself to the SessionManager at runtime if not already present.
    /// Uses a MonoBehaviour bootstrap shim to defer the attachment by one frame
    /// so all scene Awake() calls complete before FindAnyObjectByType is used.
    /// </summary>
    public class RehabSessionStarter : MonoBehaviour
    {
        private const string UserIdKey = "LastUserId";
        private const string DefaultUserId = "Guest";
        private const string LeftHandVisualName = "LeftHandVisual";
        private const string RightHandVisualName = "RightHandVisual";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialize()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;

            // Handle direct-launch into RehabSession
            if (SceneManager.GetActiveScene().name == "RehabSession")
            {
                SpawnBootstrap();
            }
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "RehabSession")
            {
                SpawnBootstrap();
            }
        }

        /// <summary>
        /// Creates a temporary GameObject that waits one frame for all Awake() calls
        /// to finish, then attaches RehabSessionStarter to the SessionManager.
        /// </summary>
        private static void SpawnBootstrap()
        {
            var go = new GameObject("[RehabSessionBootstrap]");
            go.AddComponent<RehabSessionBootstrap>();
        }

        private static void TryAttachToSessionManager()
        {
            var sessionManager = FindAnyObjectByType<SessionManager>();
            if (sessionManager == null)
            {
                Debug.LogError("[RehabSessionStarter] SessionManager not found in RehabSession scene.");
                return;
            }

            if (sessionManager.GetComponent<RehabSessionStarter>() == null)
            {
                sessionManager.gameObject.AddComponent<RehabSessionStarter>();
                Debug.Log("[RehabSessionStarter] Auto-attached to SessionManager.");
            }
        }

        // ── Bootstrap shim ────────────────────────────────────────────────

        private class RehabSessionBootstrap : MonoBehaviour
        {
            private IEnumerator Start()
            {
                // Wait one frame so all scene Awake() calls have completed.
                yield return null;
                TryAttachToSessionManager();
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// Ensures HandGrabbers exist as early as possible — before any Start() runs —
        /// so FindObjectsByType calls in ExerciseObjectController.Start and
        /// GripHoldExercise.StartExercise always find them.
        /// </summary>
        private void Awake()
        {
            EnsureHandGrabbers();
        }

        private IEnumerator Start()
        {
            var sessionManager = GetComponent<SessionManager>();
            if (sessionManager == null)
            {
                Debug.LogWarning("[RehabSessionStarter] No SessionManager found on this GameObject.");
                yield break;
            }

            Debug.Log("[RehabSessionStarter] Waiting for systems to initialize...");

            // Wait for ExerciseCoordinator to find exercises
            yield return null;
            yield return null;

            // Wait for HandTrackingManager to be ready
            float timeout = 5f;
            float elapsed = 0f;
            while (HandTrackingManager.Instance == null && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (HandTrackingManager.Instance != null)
                Debug.Log("[RehabSessionStarter] HandTrackingManager ready.");
            else
                Debug.LogWarning("[RehabSessionStarter] HandTrackingManager not found within timeout. Proceeding anyway.");

            // Extra frames for ExerciseCoordinator.Start() and BaseExercise.Start() to finish
            yield return null;
            yield return null;
            yield return null;

            // Skip if session already running (e.g. SessionManager.OnSceneLoaded started it)
            if (sessionManager.CurrentState != SessionManager.SessionState.Idle)
            {
                Debug.Log($"[RehabSessionStarter] Session already in state {sessionManager.CurrentState}, skipping auto-start.");
                yield break;
            }

            string userId = PlayerPrefs.GetString(UserIdKey, DefaultUserId);
            if (string.IsNullOrEmpty(userId))
            {
                userId = DefaultUserId;
            }

            Debug.Log($"[RehabSessionStarter] Auto-starting session for user: {userId}");
            sessionManager.StartExercising(userId);
        }

        /// <summary>
        /// Ensures HandGrabber components exist on both hand visuals.
        /// </summary>
        private void EnsureHandGrabbers()
        {
            // Try exact name first, then fallback to finding any OVRHand in the scene
            // so this works regardless of the OVRCameraRig prefab variant used.
            bool addedAny = false;
            addedAny |= AddHandGrabberToNamed(LeftHandVisualName);
            addedAny |= AddHandGrabberToNamed(RightHandVisualName);

            if (!addedAny)
            {
                // Fallback: add to all OVRHand GameObjects in the scene
                var ovrHands = FindObjectsByType<OVRHand>(FindObjectsSortMode.None);
                foreach (var hand in ovrHands)
                {
                    if (hand.GetComponent<Interaction.HandGrabber>() == null)
                    {
                        hand.gameObject.AddComponent<Interaction.HandGrabber>();
                        Debug.Log($"[RehabSessionStarter] Added HandGrabber to OVRHand on {hand.gameObject.name}.");
                    }
                }
            }
        }

        private bool AddHandGrabberToNamed(string handVisualName)
        {
            GameObject handVisual = GameObject.Find(handVisualName);
            if (handVisual == null) return false;

            if (handVisual.GetComponent<Interaction.HandGrabber>() == null)
            {
                handVisual.AddComponent<Interaction.HandGrabber>();
                Debug.Log($"[RehabSessionStarter] Added HandGrabber to {handVisualName}.");
            }
            return true;
        }
    }
}
