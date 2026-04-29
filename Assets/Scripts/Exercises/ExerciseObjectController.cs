using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using AGVRSystem.Interaction;
using AGVRSystem.Audio;
using TMPro;
using UnityEngine.UI;

namespace AGVRSystem.Exercises
{
    /// <summary>
    /// Controls realistic exercise object behaviour: curl-based grip deformation,
    /// visual feedback (color tinting on squeeze), floating labels with progress bar,
    /// weight resistance, and auto-reset when objects fall out of bounds.
    /// Supports both XRI (XRGrabInteractable) and OVR hand tracking (HandGrabber).
    /// </summary>
    public class ExerciseObjectController : MonoBehaviour
    {
        [Header("Object Identity")]
        [SerializeField] private string _displayName = "Object";
        [SerializeField] private ExerciseObjectType _objectType = ExerciseObjectType.Ball;

        [Header("Physical Properties")]
        [Tooltip("Simulated weight in kg. Affects resistance to hand movement when grabbed.")]
        [SerializeField] private float _weight = 0.1f;

        [Tooltip("Material softness (0 = rigid like coin, 1 = very soft like stress ball).")]
        [SerializeField, Range(0f, 1f)] private float _softness = 0.5f;

        [Header("Deformation")]
        [SerializeField] private bool _enableDeformation = true;
        [SerializeField] private float _squishAmount = 0.15f;
        [SerializeField] private float _deformSpeed = 8f;

        [Header("Visual Feedback")]
        [Tooltip("Color to tint towards when fully gripped.")]
        [SerializeField] private Color _gripTintColor = new Color(1f, 0.6f, 0.4f, 1f);
        [SerializeField] private bool _enableGripTint = true;

        [Header("Label")]
        [SerializeField] private bool _showLabel = true;
        [SerializeField] private float _labelHeight = 0.12f;
        [SerializeField] private Color _labelColor = new Color(1f, 1f, 1f, 0.7f);
        [SerializeField] private Color _progressBgColor = new Color(0.2f, 0.2f, 0.3f, 0.6f);
        [SerializeField] private Color _progressFillColor = new Color(0.2f, 0.8f, 0.5f, 0.85f);

        [Header("Reset")]
        [SerializeField] private float _fallThreshold = -2f;
        [SerializeField] private float _maxDistance = 3f;

        [Header("Audio Feedback")]
        [Tooltip("Play spatial grab/release sounds from this object's position.")]
        [SerializeField] private bool _enableAudioFeedback = true;
        [SerializeField] private float _grabSoundVolume = 0.35f;
        [SerializeField] private float _releaseSoundVolume = 0.25f;
        [SerializeField] private float _squeezeSoundVolume = 0.15f;

        private XRGrabInteractable _grabInteractable;
        private Rigidbody _rb;
        private Vector3 _originalScale;
        private Vector3 _originalPosition;
        private Quaternion _originalRotation;
        private Vector3 _targetScale;
        private bool _isGrabbed;
        private float _gripStrength;

        // Track which hand is grabbing for accurate per-hand grip
        private HandGrabber _activeGrabber;
        private Renderer _renderer;
        private Color _originalColor;
        private bool _hasOriginalColor;

        // Audio
        private AudioSource _objectAudioSource;
        private AudioClip _grabClip;
        private AudioClip _releaseClip;
        private AudioClip _squeezeLoopClip;
        private bool _isPlayingSqueeze;
        private float _lastSqueezeStrength;

        // Label UI
        private Canvas _labelCanvas;
        private TMP_Text _nameLabel;
        private Image _progressBg;
        private Image _progressFill;
        private RectTransform _progressFillRect;
        private float _currentProgress;

        private Camera _mainCamera;

        private const float LabelFontSize = 14f;
        [SerializeField] private float _canvasScale = 0.003f;
        private const float WeightResistanceFactor = 0.15f;

        /// <summary>
        /// Type of exercise object, determines deformation behaviour.
        /// </summary>
        public enum ExerciseObjectType
        {
            Ball,       // Squishes uniformly (stress ball, tennis ball)
            Cylinder,   // Compresses along length (pen, marker)
            Flat        // Minimal deformation (coin, card)
        }

        /// <summary>Whether this object is currently being held.</summary>
        public bool IsGrabbed => _isGrabbed;

        /// <summary>Current normalized grip strength on this object (0-1).</summary>
        public float CurrentGripStrength => _gripStrength;

        private void Awake()
        {
            _grabInteractable = GetComponent<XRGrabInteractable>();
            _rb = GetComponent<Rigidbody>();
            _originalScale = transform.localScale;
            _originalPosition = transform.localPosition;
            _originalRotation = transform.localRotation;
            _targetScale = _originalScale;
            _mainCamera = Camera.main;

            // Cache renderer for grip tinting
            _renderer = GetComponentInChildren<Renderer>();
            if (_renderer != null && _renderer.material != null)
            {
                _originalColor = _renderer.material.color;
                _hasOriginalColor = true;
            }

            // Auto-configure softness/squish based on object type if using defaults
            ConfigurePhysicalDefaults();

            // Subscribe to XRI events if available
            if (_grabInteractable != null)
            {
                _grabInteractable.selectEntered.AddListener(OnGrab);
                _grabInteractable.selectExited.AddListener(OnRelease);
            }

            if (_showLabel)
            {
                CreateFloatingLabel();
            }

            if (_enableAudioFeedback)
            {
                InitializeAudio();
            }
        }

        private void Start()
        {
            SubscribeHandGrabbers();

            if (_handGrabbers == null || _handGrabbers.Length == 0)
            {
                StartCoroutine(RetrySubscribeHandGrabbers());
            }

            EnsurePhysicsComponents();

            var coordinator = FindAnyObjectByType<ExerciseCoordinator>();
            if (coordinator != null)
            {
                coordinator.RegisterExerciseObject(this);
            }
        }

        /// <summary>
        /// Configures realistic physical defaults based on object type.
        /// </summary>
        private void ConfigurePhysicalDefaults()
        {
            switch (_objectType)
            {
                case ExerciseObjectType.Ball:
                    if (_softness == 0.5f) _softness = 0.8f;  // Stress ball is soft
                    if (_squishAmount == 0.15f) _squishAmount = 0.25f;
                    break;
                case ExerciseObjectType.Cylinder:
                    if (_softness == 0.5f) _softness = 0.1f;  // Pen is rigid
                    if (_squishAmount == 0.15f) _squishAmount = 0.05f;
                    break;
                case ExerciseObjectType.Flat:
                    if (_softness == 0.5f) _softness = 0.0f;  // Coin is rigid
                    if (_squishAmount == 0.15f) _squishAmount = 0.02f;
                    break;
            }
        }

        /// <summary>
        /// Retries finding HandGrabbers for up to 5 seconds.
        /// </summary>
        private IEnumerator RetrySubscribeHandGrabbers()
        {
            const float retryInterval = 0.5f;
            const float maxWait = 5f;
            float elapsed = 0f;

            while (elapsed < maxWait)
            {
                yield return new WaitForSeconds(retryInterval);
                elapsed += retryInterval;

                SubscribeHandGrabbers();

                if (_handGrabbers != null && _handGrabbers.Length > 0)
                {
                    Debug.Log($"[ExerciseObjectController] Found {_handGrabbers.Length} HandGrabbers after {elapsed}s retry.");
                    yield break;
                }
            }

            Debug.LogWarning($"[ExerciseObjectController] No HandGrabbers found after {maxWait}s. " +
                "Grab events will not fire for this object.");
        }

        /// <summary>
        /// Ensures the exercise object has a Collider and Rigidbody for grab detection.
        /// </summary>
        private void EnsurePhysicsComponents()
        {
            if (_rb == null)
            {
                _rb = gameObject.AddComponent<Rigidbody>();
                _rb.useGravity = false;
                _rb.isKinematic = true;
                Debug.Log($"[ExerciseObjectController] Added Rigidbody to {name}.");
            }

            if (GetComponent<Collider>() == null)
            {
                switch (_objectType)
                {
                    case ExerciseObjectType.Ball:
                        var sphere = gameObject.AddComponent<SphereCollider>();
                        sphere.isTrigger = false;
                        break;
                    case ExerciseObjectType.Cylinder:
                        var capsule = gameObject.AddComponent<CapsuleCollider>();
                        capsule.isTrigger = false;
                        break;
                    case ExerciseObjectType.Flat:
                        var box = gameObject.AddComponent<BoxCollider>();
                        box.isTrigger = false;
                        break;
                }
                Debug.Log($"[ExerciseObjectController] Added Collider to {name}.");
            }
        }

        /// <summary>
        /// Finds all HandGrabber components and subscribes to their events.
        /// </summary>
        private void SubscribeHandGrabbers()
        {
            var current = FindObjectsByType<HandGrabber>(FindObjectsSortMode.None);

            if (_handGrabbers != null)
            {
                foreach (var grabber in _handGrabbers)
                {
                    if (grabber != null)
                    {
                        grabber.OnGrabStarted -= OnHandGrabStarted;
                        grabber.OnGrabEnded -= OnHandGrabEnded;
                    }
                }
            }

            _handGrabbers = current;
            foreach (var grabber in _handGrabbers)
            {
                grabber.OnGrabStarted += OnHandGrabStarted;
                grabber.OnGrabEnded += OnHandGrabEnded;
            }
        }

        private void OnHandGrabStarted(Rigidbody grabbedRb)
        {
            if (_rb != null && grabbedRb == _rb)
            {
                _isGrabbed = true;
                _gripStrength = 0f;

                // Find which grabber grabbed us for per-hand grip tracking
                _activeGrabber = FindActiveGrabber();

                if (_nameLabel != null)
                {
                    _nameLabel.color = new Color(_labelColor.r, _labelColor.g, _labelColor.b, 0.4f);
                }

                PlayGrabSound();
            }
        }

        private void OnHandGrabEnded(Rigidbody releasedRb)
        {
            if (_rb != null && releasedRb == _rb)
            {
                _isGrabbed = false;
                _gripStrength = 0f;
                _targetScale = _originalScale;
                _activeGrabber = null;

                RestoreOriginalColor();
                StopSqueezeSound();

                if (_nameLabel != null)
                {
                    _nameLabel.color = _labelColor;
                }

                PlayReleaseSound();
            }
        }

        /// <summary>
        /// Finds the HandGrabber that is currently holding this object's Rigidbody.
        /// </summary>
        private HandGrabber FindActiveGrabber()
        {
            if (_handGrabbers == null) return null;

            foreach (var grabber in _handGrabbers)
            {
                if (grabber != null && grabber.GrabbedObject == _rb)
                    return grabber;
            }
            return null;
        }

        private HandGrabber[] _handGrabbers;

        private void Update()
        {
            // Deformation interpolation
            if (_enableDeformation)
            {
                transform.localScale = Vector3.Lerp(transform.localScale, _targetScale,
                    Time.deltaTime * _deformSpeed);
            }

            // Update grip deformation and visual feedback if grabbed
            if (_isGrabbed)
            {
                float grip = GetGrabbingHandGripStrength();
                _gripStrength = Mathf.Lerp(_gripStrength, grip, Time.deltaTime * _deformSpeed);

                if (_enableDeformation)
                {
                    UpdateGripDeformation();
                }

                if (_enableGripTint)
                {
                    UpdateGripTint();
                }

                // Squeeze sound feedback for soft objects
                if (_enableAudioFeedback && _softness > 0.3f)
                {
                    UpdateSqueezeSound();
                }
            }

            // Billboard label to face camera
            if (_labelCanvas != null && _mainCamera != null)
            {
                UpdateLabel();
            }

            // Auto-reset if fallen or too far
            CheckBounds();
        }

        private void OnGrab(SelectEnterEventArgs args)
        {
            _isGrabbed = true;
            _gripStrength = 0f;
            _activeGrabber = FindActiveGrabber();

            if (_nameLabel != null)
            {
                _nameLabel.color = new Color(_labelColor.r, _labelColor.g, _labelColor.b, 0.4f);
            }

            PlayGrabSound();
        }

        private void OnRelease(SelectExitEventArgs args)
        {
            _isGrabbed = false;
            _gripStrength = 0f;
            _activeGrabber = null;
            _targetScale = _originalScale;

            RestoreOriginalColor();
            StopSqueezeSound();

            if (_nameLabel != null)
            {
                _nameLabel.color = _labelColor;
            }

            PlayReleaseSound();
        }

        /// <summary>
        /// Gets curl-based grip strength from the specific hand that is grabbing this object.
        /// Falls back to pinch-based if curl data unavailable.
        /// Returns 0-1 range.
        /// </summary>
        private float GetGrabbingHandGripStrength()
        {
            var manager = HandTrackingManager.Instance;
            if (manager == null)
                return 0.5f;

            // If we know which grabber is holding us, use that hand specifically
            if (_activeGrabber != null)
            {
                // Determine which hand the active grabber corresponds to
                bool isLeft = _activeGrabber.name.Contains("Left", System.StringComparison.OrdinalIgnoreCase);
                OVRHand hand = isLeft ? manager.LeftHand : manager.RightHand;
                OVRSkeleton skeleton = isLeft ? manager.LeftSkeleton : manager.RightSkeleton;

                if (hand != null && hand.IsTracked)
                {
                    return manager.GetFingerCurlGripStrength(hand, skeleton) / 100f;
                }
            }

            // Fallback: use max of both hands (less accurate but functional)
            float leftGrip = 0f;
            float rightGrip = 0f;

            if (manager.LeftHand != null && manager.IsLeftTracked)
            {
                leftGrip = manager.GetFingerCurlGripStrength(
                    manager.LeftHand, manager.LeftSkeleton) / 100f;
            }

            if (manager.RightHand != null && manager.IsRightTracked)
            {
                rightGrip = manager.GetFingerCurlGripStrength(
                    manager.RightHand, manager.RightSkeleton) / 100f;
            }

            return Mathf.Max(leftGrip, rightGrip);
        }

        private void UpdateGripDeformation()
        {
            float squish = _gripStrength * _squishAmount * _softness;

            switch (_objectType)
            {
                case ExerciseObjectType.Ball:
                    // Ball squishes: flatten Y, expand XZ (volume preservation)
                    float yCompress = 1f - squish;
                    float xzExpand = 1f + squish * 0.5f;
                    _targetScale = new Vector3(
                        _originalScale.x * xzExpand,
                        _originalScale.y * yCompress,
                        _originalScale.z * xzExpand
                    );
                    break;

                case ExerciseObjectType.Cylinder:
                    // Pen/cylinder: very slight compression, minor bulge
                    _targetScale = new Vector3(
                        _originalScale.x * (1f + squish * 0.15f),
                        _originalScale.y * (1f - squish * 0.05f),
                        _originalScale.z * (1f + squish * 0.15f)
                    );
                    break;

                case ExerciseObjectType.Flat:
                    // Coin: virtually no deformation
                    _targetScale = new Vector3(
                        _originalScale.x * (1f + squish * 0.02f),
                        _originalScale.y * (1f - squish * 0.05f),
                        _originalScale.z * (1f + squish * 0.02f)
                    );
                    break;
            }
        }

        /// <summary>
        /// Tints the object color based on grip intensity for visual squeeze feedback.
        /// Stress ball gets redder when squeezed, etc.
        /// </summary>
        private void UpdateGripTint()
        {
            if (_renderer == null || _renderer.material == null || !_hasOriginalColor)
                return;

            float tintAmount = _gripStrength * _softness;
            _renderer.material.color = Color.Lerp(_originalColor, _gripTintColor, tintAmount);
        }

        private void RestoreOriginalColor()
        {
            if (_renderer != null && _renderer.material != null && _hasOriginalColor)
            {
                _renderer.material.color = _originalColor;
            }
        }

        /// <summary>
        /// Sets the exercise progress (0-1) shown on the mini progress bar.
        /// </summary>
        public void SetProgress(float progress)
        {
            _currentProgress = Mathf.Clamp01(progress);

            if (_progressFill != null)
                _progressFill.fillAmount = _currentProgress;
        }

        /// <summary>
        /// Updates the display name shown on the floating label.
        /// </summary>
        public void SetDisplayName(string name)
        {
            _displayName = name;
            if (_nameLabel != null)
            {
                _nameLabel.text = _displayName;
            }
        }

        private void CreateFloatingLabel()
        {
            GameObject labelObj = new GameObject($"{_displayName}_Label");
            labelObj.transform.SetParent(transform, false);
            labelObj.transform.localPosition = Vector3.up * (_labelHeight / transform.localScale.y);

            _labelCanvas = labelObj.AddComponent<Canvas>();
            _labelCanvas.renderMode = RenderMode.WorldSpace;

            RectTransform canvasRect = _labelCanvas.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(80, 24);
            canvasRect.localScale = Vector3.one * _canvasScale;

            GameObject textObj = new GameObject("NameText");
            textObj.transform.SetParent(labelObj.transform, false);
            _nameLabel = textObj.AddComponent<TextMeshProUGUI>();
            _nameLabel.text = _displayName;
            _nameLabel.fontSize = LabelFontSize;
            _nameLabel.color = _labelColor;
            _nameLabel.alignment = TextAlignmentOptions.Center;
            _nameLabel.fontStyle = FontStyles.Bold;
            _nameLabel.raycastTarget = false;
            _nameLabel.enableAutoSizing = false;

            RectTransform textRect = _nameLabel.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0, 0.4f);
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            GameObject progressBgObj = new GameObject("ProgressBG");
            progressBgObj.transform.SetParent(labelObj.transform, false);
            _progressBg = progressBgObj.AddComponent<Image>();
            _progressBg.color = _progressBgColor;
            _progressBg.raycastTarget = false;

            RectTransform bgRect = _progressBg.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0.15f, 0.05f);
            bgRect.anchorMax = new Vector2(0.85f, 0.3f);
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            GameObject progressFillObj = new GameObject("ProgressFill");
            progressFillObj.transform.SetParent(progressBgObj.transform, false);
            _progressFill = progressFillObj.AddComponent<Image>();
            _progressFill.type = Image.Type.Filled;
            _progressFill.fillMethod = Image.FillMethod.Horizontal;
            _progressFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            _progressFill.fillAmount = 0f;
            _progressFill.color = _progressFillColor;
            _progressFill.raycastTarget = false;

            _progressFillRect = _progressFill.GetComponent<RectTransform>();
            _progressFillRect.anchorMin = Vector2.zero;
            _progressFillRect.anchorMax = Vector2.one;
            _progressFillRect.offsetMin = Vector2.zero;
            _progressFillRect.offsetMax = Vector2.zero;
        }

        private void UpdateLabel()
        {
            if (_labelCanvas == null || _mainCamera == null)
                return;

            Vector3 dirToCamera = _mainCamera.transform.position - _labelCanvas.transform.position;
            if (dirToCamera.sqrMagnitude > 0.001f)
            {
                _labelCanvas.transform.rotation = Quaternion.LookRotation(-dirToCamera.normalized, Vector3.up);
            }
        }

        private void CheckBounds()
        {
            if (_isGrabbed)
                return;

            Vector3 worldPos = transform.position;
            Vector3 originWorld = transform.parent != null
                ? transform.parent.TransformPoint(_originalPosition)
                : _originalPosition;

            bool outOfBounds = worldPos.y < _fallThreshold
                || Vector3.Distance(worldPos, originWorld) > _maxDistance;

            if (outOfBounds)
            {
                ResetToOriginalPosition();
            }
        }

        /// <summary>
        /// Resets the object to its original position on the table.
        /// </summary>
        public void ResetToOriginalPosition()
        {
            if (_rb != null)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }

            transform.localPosition = _originalPosition;
            transform.localRotation = _originalRotation;
            transform.localScale = _originalScale;
            _targetScale = _originalScale;
            RestoreOriginalColor();
        }

        private void OnDestroy()
        {
            if (_grabInteractable != null)
            {
                _grabInteractable.selectEntered.RemoveListener(OnGrab);
                _grabInteractable.selectExited.RemoveListener(OnRelease);
            }

            if (_handGrabbers != null)
            {
                foreach (var grabber in _handGrabbers)
                {
                    if (grabber != null)
                    {
                        grabber.OnGrabStarted -= OnHandGrabStarted;
                        grabber.OnGrabEnded -= OnHandGrabEnded;
                    }
                }
            }

            StopSqueezeSound();
            RestoreOriginalColor();
        }

        // ===== AUDIO FEEDBACK =====

        /// <summary>
        /// Initializes a spatial AudioSource and generates procedural grab/release/squeeze clips.
        /// </summary>
        private void InitializeAudio()
        {
            _objectAudioSource = gameObject.AddComponent<AudioSource>();
            _objectAudioSource.playOnAwake = false;
            _objectAudioSource.spatialBlend = 1f; // Full 3D spatial
            _objectAudioSource.rolloffMode = AudioRolloffMode.Linear;
            _objectAudioSource.minDistance = 0.05f;
            _objectAudioSource.maxDistance = 2f;
            _objectAudioSource.priority = 128;

            // Generate procedural clips based on object properties
            float grabFreq = _objectType == ExerciseObjectType.Ball ? 400f : 600f;
            float releaseFreq = _objectType == ExerciseObjectType.Ball ? 300f : 500f;

            _grabClip = ProceduralToneGenerator.CreateDing(
                $"Grab_{_displayName}", grabFreq, 0.08f, 0.3f);
            _releaseClip = ProceduralToneGenerator.CreateDing(
                $"Release_{_displayName}", releaseFreq, 0.1f, 0.2f);

            // Squeeze loop: soft tone that varies with grip strength
            if (_softness > 0.3f)
            {
                _squeezeLoopClip = ProceduralToneGenerator.CreateTone(
                    $"Squeeze_{_displayName}", 180f, 0.5f, 0.15f,
                    ProceduralToneGenerator.WaveShape.SoftSine);
            }
        }

        private void PlayGrabSound()
        {
            if (!_enableAudioFeedback || _objectAudioSource == null || _grabClip == null)
                return;

            _objectAudioSource.PlayOneShot(_grabClip, _grabSoundVolume);
        }

        private void PlayReleaseSound()
        {
            if (!_enableAudioFeedback || _objectAudioSource == null || _releaseClip == null)
                return;

            _objectAudioSource.PlayOneShot(_releaseClip, _releaseSoundVolume);
        }

        /// <summary>
        /// Plays a subtle squeeze sound when grip strength increases on soft objects.
        /// Volume scales with grip intensity for natural tactile audio feedback.
        /// </summary>
        private void UpdateSqueezeSound()
        {
            if (_objectAudioSource == null || _squeezeLoopClip == null)
                return;

            const float squeezeThreshold = 0.4f;
            float gripDelta = _gripStrength - _lastSqueezeStrength;
            _lastSqueezeStrength = _gripStrength;

            if (_gripStrength >= squeezeThreshold && gripDelta > 0.02f && !_isPlayingSqueeze)
            {
                _objectAudioSource.clip = _squeezeLoopClip;
                _objectAudioSource.loop = true;
                _objectAudioSource.volume = _squeezeSoundVolume * _gripStrength * _softness;
                _objectAudioSource.Play();
                _isPlayingSqueeze = true;
            }
            else if (_isPlayingSqueeze)
            {
                _objectAudioSource.volume = _squeezeSoundVolume * _gripStrength * _softness;

                if (_gripStrength < squeezeThreshold * 0.5f)
                {
                    StopSqueezeSound();
                }
            }
        }

        private void StopSqueezeSound()
        {
            if (_isPlayingSqueeze && _objectAudioSource != null)
            {
                _objectAudioSource.Stop();
                _objectAudioSource.loop = false;
                _isPlayingSqueeze = false;
            }
            _lastSqueezeStrength = 0f;
        }
    }
}
