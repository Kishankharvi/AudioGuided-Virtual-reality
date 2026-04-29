using System.Collections;
using System.Reflection;
using UnityEngine;
using TMPro;

namespace AGVRSystem.UI
{
    /// <summary>
    /// Bridges the Meta OVRVirtualKeyboard with TMP_InputField.
    /// Finds or creates an OVRVirtualKeyboard, wires hand references from
    /// OVRCameraRig, and routes text commits to the active input field.
    /// The keyboard floats in front of the user when shown.
    /// </summary>
    public class OculusKeyboardBridge : MonoBehaviour
    {
        public static OculusKeyboardBridge Instance { get; private set; }

        [Header("Keyboard Reference (auto-found if null)")]
        [SerializeField] private OVRVirtualKeyboard _virtualKeyboard;

        [Header("Positioning")]
        [Tooltip("Offset from user camera: x=lateral, y=vertical, z=forward distance.")]
        [SerializeField] private Vector3 _keyboardOffset = new Vector3(0f, -0.4f, 0.55f);
        [SerializeField] private Vector3 _keyboardRotation = new Vector3(35f, 0f, 0f);

        private TMP_InputField _activeInputField;
        private bool _isReady;
        private OVRCameraRig _rig;

        private const float RigWaitTimeout = 8f;
        private const float RigPollInterval = 0.3f;
        private const int InitSettleFrames = 3;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private IEnumerator Start()
        {
            yield return StartCoroutine(InitKeyboard());
        }

        private void OnDestroy()
        {
            UnsubscribeEvents();
            if (Instance == this)
                Instance = null;
        }

        // ─── Public API ──────────────────────────────────────────────────────

        /// <summary>
        /// Shows the floating OVR keyboard for the given input field.
        /// </summary>
        public void Show(TMP_InputField inputField)
        {
            if (inputField == null) return;
            _activeInputField = inputField;

            if (_virtualKeyboard != null && _isReady)
            {
                PositionInFrontOfUser();
                _virtualKeyboard.gameObject.SetActive(true);
                StartCoroutine(DeferTextContext(inputField.text));
                Debug.Log($"[OculusKeyboardBridge] Showing keyboard for '{inputField.name}'.");
            }
            else
            {
                // Fallback: system soft keyboard
                inputField.shouldHideSoftKeyboard = false;
                inputField.ActivateInputField();
                Debug.LogWarning("[OculusKeyboardBridge] Keyboard not ready — system fallback.");
            }
        }

        /// <summary>
        /// Hides the floating OVR keyboard.
        /// </summary>
        public void Hide()
        {
            if (_virtualKeyboard != null)
                _virtualKeyboard.gameObject.SetActive(false);

            _activeInputField = null;
        }

        /// <summary>True when the keyboard is visible.</summary>
        public bool IsVisible =>
            _virtualKeyboard != null && _virtualKeyboard.gameObject.activeSelf;

        // ─── Initialization ──────────────────────────────────────────────────

        private IEnumerator InitKeyboard()
        {
            // 1. Wait for OVRCameraRig (may take a few frames on Quest)
            float waited = 0f;
            while (_rig == null && waited < RigWaitTimeout)
            {
                _rig = FindAnyObjectByType<OVRCameraRig>();
                if (_rig != null) break;
                waited += RigPollInterval;
                yield return new WaitForSeconds(RigPollInterval);
            }

            if (_rig == null)
                Debug.LogWarning("[OculusKeyboardBridge] OVRCameraRig not found. " +
                    "Keyboard hand interaction may not work.");

            // 2. Find or create the OVRVirtualKeyboard component
            if (_virtualKeyboard == null)
                _virtualKeyboard = FindAnyObjectByType<OVRVirtualKeyboard>();

            if (_virtualKeyboard == null)
            {
                var kbGo = new GameObject("OVRVirtualKeyboard_Runtime");
                _virtualKeyboard = kbGo.AddComponent<OVRVirtualKeyboard>();

                // Enable hand tracking input via reflection — the serialized
                // field controlling input sources is private in Meta XR SDK v83.
                TryEnableHandInput(_virtualKeyboard);

                Debug.Log("[OculusKeyboardBridge] Created OVRVirtualKeyboard at runtime.");
            }

            // 3. Wire hand + controller references from the rig
            WireHandReferences();

            // 4. Let the component run its internal init (Awake → Start → first Updates)
            _virtualKeyboard.gameObject.SetActive(true);
            for (int i = 0; i < InitSettleFrames; i++)
                yield return null;

            // Re-wire in case rig anchors populated late
            WireHandReferences();

            // 5. Subscribe events and hide until requested
            SubscribeEvents();
            _virtualKeyboard.gameObject.SetActive(false);
            _isReady = true;

            string lh = _virtualKeyboard.handLeft != null
                ? _virtualKeyboard.handLeft.name : "null";
            string rh = _virtualKeyboard.handRight != null
                ? _virtualKeyboard.handRight.name : "null";
            Debug.Log($"[OculusKeyboardBridge] Keyboard ready. handLeft={lh}, handRight={rh}");
        }

        private void WireHandReferences()
        {
            if (_rig == null || _virtualKeyboard == null) return;

            if (_virtualKeyboard.leftControllerRootTransform == null)
                _virtualKeyboard.leftControllerRootTransform = _rig.leftControllerAnchor;

            if (_virtualKeyboard.rightControllerRootTransform == null)
                _virtualKeyboard.rightControllerRootTransform = _rig.rightControllerAnchor;

            if (_virtualKeyboard.handLeft == null && _rig.leftHandAnchor != null)
                _virtualKeyboard.handLeft =
                    _rig.leftHandAnchor.GetComponentInChildren<OVRHand>(true);

            if (_virtualKeyboard.handRight == null && _rig.rightHandAnchor != null)
                _virtualKeyboard.handRight =
                    _rig.rightHandAnchor.GetComponentInChildren<OVRHand>(true);

            // Fallback: search entire scene for OVRHand by skeleton type
            if (_virtualKeyboard.handLeft == null || _virtualKeyboard.handRight == null)
            {
                var allHands = FindObjectsByType<OVRHand>(FindObjectsSortMode.None);
                foreach (var h in allHands)
                {
                    if (h == null) continue;
                    var sk = h.GetComponent<OVRSkeleton>();
                    if (sk == null) continue;

                    bool isLeft = sk.GetSkeletonType() == OVRSkeleton.SkeletonType.HandLeft
                               || sk.GetSkeletonType() == OVRSkeleton.SkeletonType.XRHandLeft;

                    if (isLeft && _virtualKeyboard.handLeft == null)
                        _virtualKeyboard.handLeft = h;
                    else if (!isLeft && _virtualKeyboard.handRight == null)
                        _virtualKeyboard.handRight = h;
                }
            }
        }

        // ─── Positioning ─────────────────────────────────────────────────────

        private void PositionInFrontOfUser()
        {
            Camera cam = Camera.main;
            if (cam == null || _virtualKeyboard == null) return;

            Transform camT = cam.transform;
            Vector3 forward = Vector3.ProjectOnPlane(camT.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f) forward = camT.forward.normalized;

            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

            Vector3 pos = camT.position
                        + forward * _keyboardOffset.z
                        + Vector3.up * _keyboardOffset.y
                        + right * _keyboardOffset.x;

            _virtualKeyboard.transform.position = pos;
            _virtualKeyboard.transform.rotation =
                Quaternion.LookRotation(forward, Vector3.up)
                * Quaternion.Euler(_keyboardRotation);
        }

        private IEnumerator DeferTextContext(string text)
        {
            yield return null;
            if (_virtualKeyboard != null && _virtualKeyboard.gameObject.activeSelf)
                _virtualKeyboard.ChangeTextContext(text ?? string.Empty);
        }

        // ─── Event routing ───────────────────────────────────────────────────

        private void SubscribeEvents()
        {
            if (_virtualKeyboard == null) return;
            _virtualKeyboard.CommitTextEvent.AddListener(OnCommitText);
            _virtualKeyboard.BackspaceEvent.AddListener(OnBackspace);
            _virtualKeyboard.EnterEvent.AddListener(OnEnter);
            _virtualKeyboard.KeyboardHiddenEvent.AddListener(OnKeyboardHidden);
        }

        private void UnsubscribeEvents()
        {
            if (_virtualKeyboard == null) return;
            _virtualKeyboard.CommitTextEvent.RemoveListener(OnCommitText);
            _virtualKeyboard.BackspaceEvent.RemoveListener(OnBackspace);
            _virtualKeyboard.EnterEvent.RemoveListener(OnEnter);
            _virtualKeyboard.KeyboardHiddenEvent.RemoveListener(OnKeyboardHidden);
        }

        private void OnCommitText(string text)
        {
            if (_activeInputField == null) return;
            _activeInputField.text += text;
            _activeInputField.MoveTextEnd(false);
        }

        private void OnBackspace()
        {
            if (_activeInputField == null || _activeInputField.text.Length == 0) return;
            _activeInputField.text =
                _activeInputField.text.Substring(0, _activeInputField.text.Length - 1);
            _activeInputField.MoveTextEnd(false);
        }

        private void OnEnter()
        {
            if (_activeInputField != null)
                _activeInputField.DeactivateInputField();
            Hide();
        }

        private void OnKeyboardHidden()
        {
            _activeInputField = null;
        }

        // ─── Reflection helper ───────────────────────────────────────────────

        /// <summary>
        /// Tries to set the keyboard input sources to include hand tracking.
        /// The field name varies across Meta XR SDK versions, so we try
        /// multiple candidates via reflection.
        /// </summary>
        private static void TryEnableHandInput(OVRVirtualKeyboard vk)
        {
            if (vk == null) return;

            var type = vk.GetType();
            string[] fieldNames = { "inputSources", "_inputSources", "InputSources" };

            foreach (var name in fieldNames)
            {
                var field = type.GetField(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null) continue;

                // The InputSource enum is flags: HandLeft=4, HandRight=8
                // Combined = 12 (or ControllerLeft|ControllerRight|HandLeft|HandRight = 15)
                try
                {
                    var enumType = field.FieldType;
                    object allHands = System.Enum.ToObject(enumType, 0xF); // all four sources
                    field.SetValue(vk, allHands);
                    Debug.Log($"[OculusKeyboardBridge] Set {name} to include hand input via reflection.");
                    return;
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[OculusKeyboardBridge] Failed to set {name}: {ex.Message}");
                }
            }

            Debug.LogWarning("[OculusKeyboardBridge] Could not find inputSources field. " +
                "The keyboard may not respond to hand tracking. " +
                "Consider adding OVRVirtualKeyboard via Meta Building Blocks instead.");
        }
    }
}
