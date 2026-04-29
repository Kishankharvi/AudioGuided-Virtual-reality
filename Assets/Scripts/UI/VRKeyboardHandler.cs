using UnityEngine;
using TMPro;

namespace AGVRSystem.UI
{
    /// <summary>
    /// Handles virtual keyboard input for TMP_InputField in VR.
    /// When the input field is selected (e.g. via finger poke), opens the
    /// floating VirtualKeyboard. All text input comes from the virtual keyboard,
    /// not from the system keyboard.
    /// </summary>
    [RequireComponent(typeof(TMP_InputField))]
    public class VRKeyboardHandler : MonoBehaviour
    {
        [Header("Keyboard Settings")]
        [SerializeField] private int _maxCharacters = 50;

        private TMP_InputField _inputField;

        private void Awake()
        {
            _inputField = GetComponent<TMP_InputField>();

            if (_inputField != null)
            {
                // Always hide the system soft keyboard — only virtual keyboard is used
                _inputField.shouldHideSoftKeyboard = true;
                _inputField.characterLimit = _maxCharacters;
            }
        }

        private void OnEnable()
        {
            if (_inputField != null)
            {
                _inputField.onSelect.AddListener(OnInputFieldSelected);
                _inputField.onDeselect.AddListener(OnInputFieldDeselected);
            }
        }

        private void OnDisable()
        {
            if (_inputField != null)
            {
                _inputField.onSelect.RemoveListener(OnInputFieldSelected);
                _inputField.onDeselect.RemoveListener(OnInputFieldDeselected);
            }

            CloseKeyboard();
        }

        private void OnInputFieldSelected(string text)
        {
            OpenKeyboard();
        }

        private void OnInputFieldDeselected(string text)
        {
            // Do NOT close the keyboard if the VirtualKeyboard is currently showing.
            // Clicking a key on the keyboard causes the input field to deselect,
            // but we want the keyboard to remain open.
            if (VirtualKeyboard.Instance != null && VirtualKeyboard.Instance.IsVisible)
                return;

            Invoke(nameof(DelayedClose), 0.3f);
        }

        private void DelayedClose()
        {
            if (_inputField != null && !_inputField.isFocused)
            {
                CloseKeyboard();
            }
        }

        /// <summary>
        /// Opens the virtual keyboard for this input field.
        /// Tries VirtualKeyboard first, then OculusKeyboardBridge as fallback.
        /// </summary>
        private void OpenKeyboard()
        {
            // Primary: custom floating virtual keyboard (always works)
            if (VirtualKeyboard.Instance != null)
            {
                VirtualKeyboard.Instance.Show(_inputField);
                Debug.Log($"[VRKeyboardHandler] Virtual keyboard shown for: {gameObject.name}");
                return;
            }

            // Fallback: OVR keyboard bridge (if available)
            if (OculusKeyboardBridge.Instance != null)
            {
                OculusKeyboardBridge.Instance.Show(_inputField);
                Debug.Log($"[VRKeyboardHandler] OVR keyboard shown for: {gameObject.name}");
                return;
            }

            Debug.LogWarning("[VRKeyboardHandler] No virtual keyboard available.");
        }

        /// <summary>
        /// Closes whichever virtual keyboard is open.
        /// </summary>
        private void CloseKeyboard()
        {
            if (VirtualKeyboard.Instance != null && VirtualKeyboard.Instance.IsVisible)
                VirtualKeyboard.Instance.Hide();

            if (OculusKeyboardBridge.Instance != null && OculusKeyboardBridge.Instance.IsVisible)
                OculusKeyboardBridge.Instance.Hide();
        }
    }
}
