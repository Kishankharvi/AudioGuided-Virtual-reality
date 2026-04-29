using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AGVRSystem.UI
{
    /// <summary>
    /// A floating 3D virtual QWERTY keyboard built with Unity UI.
    /// Pokeable with HandPokeInteractor — each key is a Button on a
    /// world-space Canvas. Text is routed to the active TMP_InputField.
    ///
    /// Usage:
    ///   VirtualKeyboard.Instance.Show(inputField);
    ///   VirtualKeyboard.Instance.Hide();
    /// </summary>
    public class VirtualKeyboard : MonoBehaviour
    {
        public static VirtualKeyboard Instance { get; private set; }

        [Header("Positioning")]
        [Tooltip("Offset from camera: x=lateral, y=vertical, z=forward.")]
        [SerializeField] private Vector3 _offset = new Vector3(0f, -0.35f, 0.55f);
        [SerializeField] private float _tiltAngle = 30f;
        [SerializeField] private float _keyboardScale = 0.0006f;

        [Header("Appearance")]
        [SerializeField] private Color _keyNormalColor = new Color(0.18f, 0.18f, 0.22f, 1f);
        [SerializeField] private Color _keyPressedColor = new Color(0.35f, 0.55f, 0.95f, 1f);
        [SerializeField] private Color _keyTextColor = Color.white;
        [SerializeField] private Color _specialKeyColor = new Color(0.25f, 0.25f, 0.30f, 1f);
        [SerializeField] private Color _panelColor = new Color(0.10f, 0.10f, 0.13f, 0.95f);

        private TMP_InputField _activeInputField;
        private GameObject _keyboardRoot;
        private Canvas _canvas;
        private bool _isShift;
        private bool _isBuilt;

        // Layout constants
        private const float KeyWidth = 64f;
        private const float KeyHeight = 64f;
        private const float KeySpacing = 6f;
        private const float RowHeight = KeyHeight + KeySpacing;
        private const int FontSize = 24;
        private const int SmallFontSize = 16;

        private static readonly string[] Row0 = { "1","2","3","4","5","6","7","8","9","0" };
        private static readonly string[] Row1 = { "q","w","e","r","t","y","u","i","o","p" };
        private static readonly string[] Row2 = { "a","s","d","f","g","h","j","k","l" };
        private static readonly string[] Row3 = { "z","x","c","v","b","n","m" };

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>Shows the keyboard for the given input field.</summary>
        public void Show(TMP_InputField inputField)
        {
            if (inputField == null) return;
            _activeInputField = inputField;

            if (!_isBuilt) BuildKeyboard();

            PositionInFrontOfUser();
            _keyboardRoot.SetActive(true);
            Debug.Log($"[VirtualKeyboard] Showing for '{inputField.name}'.");
        }

        /// <summary>Hides the keyboard.</summary>
        public void Hide()
        {
            if (_keyboardRoot != null)
                _keyboardRoot.SetActive(false);
            _activeInputField = null;
        }

        /// <summary>True when the keyboard is visible.</summary>
        public bool IsVisible => _keyboardRoot != null && _keyboardRoot.activeSelf;

        // ─── Build ───────────────────────────────────────────────────────────

        private void BuildKeyboard()
        {
            _isBuilt = true;

            // Root
            _keyboardRoot = new GameObject("VirtualKeyboard_UI");
            _keyboardRoot.transform.SetParent(transform, false);
            _keyboardRoot.SetActive(false);

            // Canvas (world-space)
            _canvas = _keyboardRoot.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.worldCamera = Camera.main;

            var scaler = _keyboardRoot.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;

            _keyboardRoot.AddComponent<GraphicRaycaster>();

            // Try to add TrackedDeviceGraphicRaycaster for XRI poke support
            var tdgrType = System.Type.GetType(
                "UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, " +
                "Unity.XR.Interaction.Toolkit");
            if (tdgrType != null)
                _keyboardRoot.AddComponent(tdgrType);

            RectTransform canvasRect = _keyboardRoot.GetComponent<RectTransform>();
            float totalWidth = Row0.Length * (KeyWidth + KeySpacing) + KeySpacing;
            float totalHeight = 5 * RowHeight + KeySpacing + 10f; // 5 rows + bottom bar
            canvasRect.sizeDelta = new Vector2(totalWidth, totalHeight);
            canvasRect.localScale = Vector3.one * _keyboardScale;

            // Background panel
            var panel = CreateChild("Panel", _keyboardRoot);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            var panelImg = panel.AddComponent<Image>();
            panelImg.color = _panelColor;
            panelImg.raycastTarget = false;

            // Rows (top to bottom)
            float y = totalHeight * 0.5f - KeySpacing;

            y = CreateRow(Row0, y, 0f, false);
            y = CreateRow(Row1, y, 0f, false);
            y = CreateRow(Row2, y, KeyWidth * 0.5f, false);
            y = CreateShiftRow(Row3, y);
            CreateBottomRow(y);

            Debug.Log("[VirtualKeyboard] Keyboard built.");
        }

        private float CreateRow(string[] keys, float yTop, float xOffset, bool isSpecial)
        {
            float startX = -((Row0.Length * (KeyWidth + KeySpacing)) * 0.5f) + KeySpacing * 0.5f + xOffset;
            float y = yTop - KeyHeight * 0.5f;

            for (int i = 0; i < keys.Length; i++)
            {
                float x = startX + i * (KeyWidth + KeySpacing) + KeyWidth * 0.5f;
                string key = keys[i];
                CreateKeyButton(key, key.ToUpper(), x, y, KeyWidth, KeyHeight, isSpecial);
            }

            return yTop - RowHeight;
        }

        private float CreateShiftRow(string[] keys, float yTop)
        {
            float rowWidth = Row0.Length * (KeyWidth + KeySpacing);
            float startX = -(rowWidth * 0.5f) + KeySpacing * 0.5f;
            float y = yTop - KeyHeight * 0.5f;

            // Shift key (left)
            float shiftWidth = KeyWidth * 1.5f;
            CreateActionButton("\u21E7", startX + shiftWidth * 0.5f, y, shiftWidth, KeyHeight, OnShiftPressed);

            // Letter keys
            float lettersStart = startX + shiftWidth + KeySpacing;
            for (int i = 0; i < keys.Length; i++)
            {
                float x = lettersStart + i * (KeyWidth + KeySpacing) + KeyWidth * 0.5f;
                string key = keys[i];
                CreateKeyButton(key, key.ToUpper(), x, y, KeyWidth, KeyHeight, false);
            }

            // Backspace (right)
            float bsX = lettersStart + keys.Length * (KeyWidth + KeySpacing) + shiftWidth * 0.5f;
            CreateActionButton("\u232B", bsX, y, shiftWidth, KeyHeight, OnBackspace);

            return yTop - RowHeight;
        }

        private void CreateBottomRow(float yTop)
        {
            float rowWidth = Row0.Length * (KeyWidth + KeySpacing);
            float startX = -(rowWidth * 0.5f) + KeySpacing * 0.5f;
            float y = yTop - KeyHeight * 0.5f;

            // Space bar (wide)
            float spaceWidth = KeyWidth * 5f + KeySpacing * 4f;
            float spaceX = 0f;
            CreateActionButton("Space", spaceX, y, spaceWidth, KeyHeight, () => TypeCharacter(" "));

            // Done button (right)
            float doneWidth = KeyWidth * 2f;
            float doneX = spaceX + spaceWidth * 0.5f + KeySpacing + doneWidth * 0.5f;
            CreateActionButton("Done", doneX, y, doneWidth, KeyHeight, OnDonePressed);

            // Clear button (left)
            float clearWidth = KeyWidth * 2f;
            float clearX = spaceX - spaceWidth * 0.5f - KeySpacing - clearWidth * 0.5f;
            CreateActionButton("Clr", clearX, y, clearWidth, KeyHeight, OnClearPressed);
        }

        // ─── Key creation helpers ────────────────────────────────────────────

        private void CreateKeyButton(string lower, string upper, float x, float y,
            float w, float h, bool isSpecial)
        {
            var go = CreateChild($"Key_{upper}", _keyboardRoot);
            var rect = go.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(w, h);

            var img = go.AddComponent<Image>();
            img.color = isSpecial ? _specialKeyColor : _keyNormalColor;

            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = isSpecial ? _specialKeyColor : _keyNormalColor;
            colors.highlightedColor = Color.Lerp(_keyNormalColor, _keyPressedColor, 0.5f);
            colors.pressedColor = _keyPressedColor;
            colors.selectedColor = colors.normalColor;
            btn.colors = colors;

            string lowerCopy = lower;
            string upperCopy = upper;
            btn.onClick.AddListener(() => TypeCharacter(_isShift ? upperCopy : lowerCopy));

            // Label
            var labelGo = CreateChild("Label", go);
            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text = upper;
            label.fontSize = FontSize;
            label.color = _keyTextColor;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
        }

        private void CreateActionButton(string labelText, float x, float y,
            float w, float h, UnityEngine.Events.UnityAction action)
        {
            var go = CreateChild($"Key_{labelText}", _keyboardRoot);
            var rect = go.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(w, h);

            var img = go.AddComponent<Image>();
            img.color = _specialKeyColor;

            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = _specialKeyColor;
            colors.highlightedColor = Color.Lerp(_specialKeyColor, _keyPressedColor, 0.5f);
            colors.pressedColor = _keyPressedColor;
            colors.selectedColor = colors.normalColor;
            btn.colors = colors;
            btn.onClick.AddListener(action);

            var labelGo = CreateChild("Label", go);
            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text = labelText;
            label.fontSize = SmallFontSize;
            label.color = _keyTextColor;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
        }

        private static GameObject CreateChild(string name, GameObject parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        // ─── Input handlers ──────────────────────────────────────────────────

        private void TypeCharacter(string ch)
        {
            if (_activeInputField == null) return;
            _activeInputField.text += ch;
            _activeInputField.MoveTextEnd(false);

            // Auto-disable shift after typing a letter
            if (_isShift && ch.Length == 1 && char.IsLetter(ch[0]))
                _isShift = false;
        }

        private void OnBackspace()
        {
            if (_activeInputField == null || _activeInputField.text.Length == 0) return;
            _activeInputField.text =
                _activeInputField.text.Substring(0, _activeInputField.text.Length - 1);
            _activeInputField.MoveTextEnd(false);
        }

        private void OnShiftPressed()
        {
            _isShift = !_isShift;
        }

        private void OnClearPressed()
        {
            if (_activeInputField == null) return;
            _activeInputField.text = string.Empty;
        }

        private void OnDonePressed()
        {
            if (_activeInputField != null)
                _activeInputField.DeactivateInputField();
            Hide();
        }

        // ─── Positioning ─────────────────────────────────────────────────────

        private void PositionInFrontOfUser()
        {
            Camera cam = Camera.main;
            if (cam == null || _keyboardRoot == null) return;

            Transform camT = cam.transform;
            Vector3 forward = Vector3.ProjectOnPlane(camT.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f) forward = camT.forward.normalized;

            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

            Vector3 pos = camT.position
                        + forward * _offset.z
                        + Vector3.up * _offset.y
                        + right * _offset.x;

            _keyboardRoot.transform.position = pos;
            _keyboardRoot.transform.rotation =
                Quaternion.LookRotation(forward, Vector3.up)
                * Quaternion.Euler(_tiltAngle, 0f, 0f);
        }
    }
}
