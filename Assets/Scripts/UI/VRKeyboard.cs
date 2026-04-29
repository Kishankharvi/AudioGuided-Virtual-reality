using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using TMPro;

namespace AGVRSystem.UI
{
    /// <summary>
    /// In-world virtual QWERTY keyboard for VR text input.
    /// Spawns a world-space canvas with pokeable letter/number/action keys.
    /// Integrates with TMP_InputField — shows when an input field is selected,
    /// hides only on Submit (OK key) or explicit Hide() call. Never hides on
    /// input field deselect so clicking a key does not dismiss the keyboard.
    /// </summary>
    public class VRKeyboard : MonoBehaviour
    {
        public static VRKeyboard Instance { get; private set; }

        [Header("Keyboard Appearance")]
        [SerializeField] private float _keyWidth = 38f;
        [SerializeField] private float _keyHeight = 38f;
        [SerializeField] private float _keySpacing = 4f;
        [SerializeField] private float _canvasScale = 0.001f;

        [Header("Positioning")]
        [Tooltip("X = horizontal offset, Y = vertical from canvas center (negative = down), Z = distance from the board toward the player")]
        [SerializeField] private Vector3 _offset = new Vector3(0f, -0.35f, 0.80f);

        [Header("Horizontal Tilt")]
        [Tooltip("Degrees to tilt the keyboard back from vertical. 90 = fully flat/horizontal like a tabletop.")]
        [SerializeField] private float _tiltAngle = 30f;

        [Header("Colors")]
        [SerializeField] private Color _keyNormalColor = new Color(0.18f, 0.22f, 0.18f, 0.95f);
        [SerializeField] private Color _keyHighlightColor = new Color(0.25f, 0.55f, 0.30f, 1f);
        [SerializeField] private Color _keyPressedColor = new Color(0.14f, 0.42f, 0.22f, 1f);
        [SerializeField] private Color _actionKeyColor = new Color(0.12f, 0.15f, 0.12f, 0.95f);
        [SerializeField] private Color _backgroundPanelColor = new Color(0.08f, 0.10f, 0.08f, 0.92f);
        [SerializeField] private Color _textColor = Color.white;

        private TMP_InputField _activeInputField;
        private GameObject _keyboardRoot;
        private Canvas _keyboardCanvas;
        private bool _isShiftActive;
        private bool _isVisible;

        // Track which input field opened the keyboard so deselect events from
        // other sources (e.g. clicking a key) do not trigger Hide().
        private TMP_InputField _subscribedInputField;

        private static readonly string[] KeyboardRows =
        {
            "1234567890",
            "QWERTYUIOP",
            "ASDFGHJKL",
            "ZXCVBNM"
        };

        private const float RowIndent0 = 0f;
        private const float RowIndent1 = 0f;
        private const float RowIndent2 = 19f;
        private const float RowIndent3 = 38f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            BuildKeyboard();
            Hide();
        }

        private void OnDestroy()
        {
            UnsubscribeInputField();
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// Shows the keyboard targeting the given input field.
        /// The keyboard will only hide when Submit() is called, not when the input
        /// field loses focus (which happens every time a key button is pressed).
        /// </summary>
        public void Show(TMP_InputField inputField)
        {
            if (inputField == null)
                return;

            // Unsubscribe from old field first
            UnsubscribeInputField();

            _activeInputField = inputField;
            _subscribedInputField = inputField;

            PositionNearInputField(inputField);
            _keyboardRoot.SetActive(true);
            _isVisible = true;

            // Assign the world camera in case it changed (e.g. after scene load)
            AssignWorldCamera();

            Debug.Log("[VRKeyboard] Shown for: " + inputField.name);
        }

        /// <summary>
        /// Hides the keyboard. Only call this from Submit() or explicit external dismissal.
        /// </summary>
        public void Hide()
        {
            if (_keyboardRoot != null)
                _keyboardRoot.SetActive(false);

            UnsubscribeInputField();
            _activeInputField = null;
            _isVisible = false;
            _isShiftActive = false;
        }

        /// <summary>Returns true if the keyboard is currently visible.</summary>
        public bool IsVisible => _isVisible;

        private void UnsubscribeInputField()
        {
            _subscribedInputField = null;
        }

        private void PositionNearInputField(TMP_InputField inputField)
        {
            Canvas parentCanvas = inputField.GetComponentInParent<Canvas>();
            if (parentCanvas == null)
                return;

            Transform ct = parentCanvas.transform;

            // World-space canvas content faces local -Z (the "front" side).
            // ct.forward (+Z) therefore points BEHIND the board surface.
            // To place the keyboard in front of the board (toward the player),
            // we move in -ct.forward.
            //
            // _offset.z controls the distance FROM the board TOWARD the player.
            // _offset.y < 0 moves the keyboard below the canvas center.
            Vector3 worldPos = ct.position
                + ct.right   * _offset.x
                + ct.up      * _offset.y
                - ct.forward * _offset.z;

            _keyboardRoot.transform.position = worldPos;

            // Tilt the keyboard toward horizontal by rotating around the canvas's right axis
            Quaternion tilt = Quaternion.AngleAxis(_tiltAngle, ct.right);
            _keyboardRoot.transform.rotation = tilt * ct.rotation;
        }

        private void BuildKeyboard()
        {
            _keyboardRoot = new GameObject("VRKeyboard");
            _keyboardRoot.transform.SetParent(transform, false);

            // World-space canvas
            _keyboardCanvas = _keyboardRoot.AddComponent<Canvas>();
            _keyboardCanvas.renderMode = RenderMode.WorldSpace;

            var scaler = _keyboardRoot.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 1f;

            _keyboardRoot.AddComponent<TrackedDeviceGraphicRaycaster>();
            _keyboardRoot.AddComponent<CanvasGroup>();

            _keyboardRoot.transform.localScale = Vector3.one * _canvasScale;

            float maxRowWidth = KeyboardRows[1].Length * (_keyWidth + _keySpacing);
            float totalHeight = (KeyboardRows.Length + 1) * (_keyHeight + _keySpacing) + _keySpacing;

            RectTransform canvasRect = _keyboardCanvas.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(
                maxRowWidth + _keySpacing * 2f,
                totalHeight + _keySpacing * 2f);

            // Background
            GameObject bgPanel = CreateUIElement("Background", _keyboardRoot.transform);
            var bgImage = bgPanel.AddComponent<Image>();
            bgImage.color = _backgroundPanelColor;
            bgImage.raycastTarget = false;
            RectTransform bgRect = bgPanel.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            // Key rows
            float[] indents = { RowIndent0, RowIndent1, RowIndent2, RowIndent3 };
            float startY = canvasRect.sizeDelta.y * 0.5f - _keySpacing - _keyHeight * 0.5f;

            for (int row = 0; row < KeyboardRows.Length; row++)
            {
                float yPos = startY - row * (_keyHeight + _keySpacing);
                float xStart = -canvasRect.sizeDelta.x * 0.5f
                    + _keySpacing + indents[row] + _keyWidth * 0.5f;

                for (int col = 0; col < KeyboardRows[row].Length; col++)
                {
                    char c = KeyboardRows[row][col];
                    float xPos = xStart + col * (_keyWidth + _keySpacing);
                    CreateKey(c.ToString(), new Vector2(xPos, yPos), _keyWidth, _keyNormalColor);
                }
            }

            // Bottom action row
            float bottomY = startY - KeyboardRows.Length * (_keyHeight + _keySpacing);
            float spaceWidth = (_keyWidth + _keySpacing) * 5f - _keySpacing;
            float leftX = -canvasRect.sizeDelta.x * 0.5f + _keySpacing;

            CreateActionKey("Shift", new Vector2(leftX + _keyWidth, bottomY),
                _keyWidth * 1.8f, ToggleShift);

            float spaceX = leftX + _keyWidth * 1.8f + _keySpacing + spaceWidth * 0.5f + _keyWidth * 0.5f;
            CreateActionKey("Space", new Vector2(spaceX, bottomY),
                spaceWidth, () => TypeCharacter(' '));

            float bsX = spaceX + spaceWidth * 0.5f + _keySpacing + _keyWidth;
            CreateActionKey("<-", new Vector2(bsX, bottomY),
                _keyWidth * 1.8f, Backspace);

            float enterX = bsX + _keyWidth * 1.8f * 0.5f + _keySpacing + _keyWidth;
            CreateActionKey("OK", new Vector2(enterX, bottomY),
                _keyWidth * 1.8f, Submit);

            AssignWorldCamera();
        }

        private void CreateKey(string label, Vector2 position, float width, Color color)
        {
            GameObject keyGo = CreateUIElement("Key_" + label, _keyboardCanvas.transform);
            RectTransform rect = keyGo.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(width, _keyHeight);

            var image = keyGo.AddComponent<Image>();
            image.color = color;

            var button = keyGo.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = _keyHighlightColor;
            colors.pressedColor = _keyPressedColor;
            colors.fadeDuration = 0.05f;
            button.colors = colors;
            button.targetGraphic = image;

            GameObject textGo = CreateUIElement("Label", keyGo.transform);
            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = 18f;
            text.color = _textColor;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            RectTransform textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            string charToType = label;
            button.onClick.AddListener(() =>
            {
                // Re-activate the input field before typing so TMP accepts the input
                if (_activeInputField != null)
                    _activeInputField.ActivateInputField();

                string ch = _isShiftActive ? charToType.ToUpper() : charToType.ToLower();
                TypeCharacter(ch[0]);
            });
        }

        private void CreateActionKey(string label, Vector2 position, float width,
            UnityEngine.Events.UnityAction action)
        {
            GameObject keyGo = CreateUIElement("Key_" + label, _keyboardCanvas.transform);
            RectTransform rect = keyGo.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(width, _keyHeight);

            var image = keyGo.AddComponent<Image>();
            image.color = _actionKeyColor;

            var button = keyGo.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = _actionKeyColor;
            colors.highlightedColor = _keyHighlightColor;
            colors.pressedColor = _keyPressedColor;
            colors.fadeDuration = 0.05f;
            button.colors = colors;
            button.targetGraphic = image;
            button.onClick.AddListener(action);

            GameObject textGo = CreateUIElement("Label", keyGo.transform);
            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = 16f;
            text.color = _textColor;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            RectTransform textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
        }

        private void TypeCharacter(char c)
        {
            if (_activeInputField == null)
                return;

            if (_activeInputField.characterLimit > 0
                && _activeInputField.text.Length >= _activeInputField.characterLimit)
                return;

            _activeInputField.text += c;
            _activeInputField.caretPosition = _activeInputField.text.Length;

            if (_isShiftActive && char.IsLetter(c))
                _isShiftActive = false;

            PlayKeySound();
        }

        private void Backspace()
        {
            if (_activeInputField == null || _activeInputField.text.Length == 0)
                return;

            _activeInputField.text =
                _activeInputField.text.Substring(0, _activeInputField.text.Length - 1);
            _activeInputField.caretPosition = _activeInputField.text.Length;
            PlayKeySound();
        }

        private void Submit()
        {
            if (_activeInputField != null)
                _activeInputField.DeactivateInputField();
            Hide();
        }

        private void ToggleShift()
        {
            _isShiftActive = !_isShiftActive;
            PlayKeySound();
        }

        private void PlayKeySound()
        {
            Audio.UIAudioFeedback.Instance?.PlayClick();
        }

        private void AssignWorldCamera()
        {
            Camera cam = Camera.main;
            if (cam != null && _keyboardCanvas != null)
                _keyboardCanvas.worldCamera = cam;
        }

        private GameObject CreateUIElement(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }
    }
}
