using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using TMPro;

namespace AGVRSystem.UI
{
    /// <summary>
    /// Detects fingertip poke interactions with world-space canvases using
    /// perpendicular canvas-plane projection for camera-independent hit detection.
    /// All five fingers are tracked; whichever fingertip is closest to a canvas wins.
    /// Attach to a GameObject with an OVRHand and OVRSkeleton.
    /// </summary>
    public class HandPokeInteractor : MonoBehaviour
    {
        [Header("Hand References")]
        [SerializeField] private OVRHand _hand;
        [SerializeField] private OVRSkeleton _skeleton;

        [Header("Poke Settings")]
        [SerializeField] private float _pokeRadius = 0.05f;
        [SerializeField] private float _pokeDepthThreshold = 0.02f;
        [SerializeField] private LayerMask _canvasLayerMask = ~0;

        [Header("Visual Feedback")]
        [SerializeField] private GameObject _pokeDotPrefab;
        [SerializeField] private Color _hoverColor = new Color(0.2f, 0.78f, 0.38f, 0.8f);
        [SerializeField] private Color _pressColor = new Color(1f, 1f, 1f, 0.95f);

        // All five fingertips — any finger can poke a canvas; the closest one wins.
        private static readonly OVRSkeleton.BoneId[] FingerTipBoneIds =
        {
            OVRSkeleton.BoneId.Hand_ThumbTip,
            OVRSkeleton.BoneId.Hand_IndexTip,
            OVRSkeleton.BoneId.Hand_MiddleTip,
            OVRSkeleton.BoneId.Hand_RingTip,
            OVRSkeleton.BoneId.Hand_PinkyTip,
        };

        private Transform[] _fingerTips;
        private bool _fingerTipsCached;

        private GameObject _pokeDot;
        private Renderer _pokeDotRenderer;

        private GameObject _hoveredObject;
        private GameObject _pressedObject;
        private bool _wasPoking;
        private float _lastPokeTime;
        private int _activeFingerIndex = -1;

        // Cached to avoid FindObjectsByType every frame
        private Canvas[] _worldCanvases = System.Array.Empty<Canvas>();
        private Camera _eventCamera;

        private static readonly List<RaycastResult> s_raycastResults = new List<RaycastResult>();

        private const float PokeDebounceTime = 0.15f;
        private const float PokeDotScale = 0.008f;

        private void Awake()
        {
            _fingerTips = new Transform[FingerTipBoneIds.Length];
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (_pokeDot != null)
                Destroy(_pokeDot);
        }

        private void Start()
        {
            CreatePokeDot();
            RefreshCanvasCache();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            RefreshCanvasCache();
        }

        /// <summary>
        /// Rebuilds the cached list of world-space canvases and ensures each has
        /// a worldCamera and a TrackedDeviceGraphicRaycaster.
        /// </summary>
        private void RefreshCanvasCache()
        {
            _worldCanvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            _eventCamera = Camera.main;

            foreach (Canvas canvas in _worldCanvases)
            {
                if (canvas.renderMode != RenderMode.WorldSpace)
                    continue;

                if (canvas.worldCamera == null)
                    canvas.worldCamera = Camera.main;

                if (canvas.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                    canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();

                if (_eventCamera == null)
                    _eventCamera = canvas.worldCamera;
            }

            SanitizeGraphicsWithoutCanvasRenderer();
        }

        private void SanitizeGraphicsWithoutCanvasRenderer()
        {
            MaskableGraphic[] graphics = FindObjectsByType<MaskableGraphic>(FindObjectsSortMode.None);
            foreach (MaskableGraphic graphic in graphics)
            {
                if (graphic.raycastTarget && graphic.GetComponent<CanvasRenderer>() == null)
                {
                    graphic.raycastTarget = false;
                    Debug.LogWarning($"[HandPokeInteractor] Disabled raycastTarget on '{graphic.name}' — missing CanvasRenderer.");
                }
            }
        }

        private void Update()
        {
            if (_hand == null || _skeleton == null || !_hand.IsTracked)
            {
                ClearInteractionState();
                SetPokeDotVisible(false);
                return;
            }

            CacheFingerTips();

            Vector3 bestTipPos = Vector3.zero;
            int bestFingerIdx = -1;
            float bestDist = float.MaxValue;

            for (int i = 0; i < _fingerTips.Length; i++)
            {
                if (_fingerTips[i] == null)
                    continue;

                float dist = GetPerpendicularDistanceToNearestCanvas(_fingerTips[i].position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestTipPos = _fingerTips[i].position;
                    bestFingerIdx = i;
                }
            }

            if (bestFingerIdx < 0 || bestDist > _pokeRadius)
            {
                ClearInteractionState();
                SetPokeDotVisible(false);
                return;
            }

            _activeFingerIndex = bestFingerIdx;
            UpdatePokeDot(bestTipPos);

            // Project the winning fingertip perpendicularly onto the canvas plane
            // so the EventSystem ray hits the element directly in front of the finger,
            // not the element in the camera-shadow direction of the finger.
            PointerEventData pointerData = BuildPointerDataFromCanvasProjection(bestTipPos);
            if (pointerData == null)
                return;

            GameObject hitObject = RaycastUI(pointerData, bestTipPos);

            HandleHover(hitObject, pointerData);
            HandlePoke(hitObject, pointerData, bestDist);
        }

        // ──────────────────────────────────────────────────────────────
        // Canvas projection
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the perpendicular distance from worldPos to the nearest
        /// world-space canvas plane (front face only).
        /// </summary>
        private float GetPerpendicularDistanceToNearestCanvas(Vector3 worldPos)
        {
            float minDist = float.MaxValue;

            foreach (Canvas canvas in _worldCanvases)
            {
                if (canvas == null || canvas.renderMode != RenderMode.WorldSpace)
                    continue;

                float signedDist = Vector3.Dot(worldPos - canvas.transform.position, canvas.transform.forward);

                // Ignore fingers behind the canvas
                if (signedDist >= 0f && signedDist < minDist)
                    minDist = signedDist;
            }

            return minDist;
        }

        /// <summary>
        /// Projects tipPos perpendicularly onto the nearest canvas plane along the canvas normal,
        /// then converts the projected point to screen space.
        /// This ensures every finger maps to the UI element directly in front of it.
        /// </summary>
        private PointerEventData BuildPointerDataFromCanvasProjection(Vector3 tipPos)
        {
            Canvas nearestCanvas = null;
            float minDist = float.MaxValue;

            foreach (Canvas canvas in _worldCanvases)
            {
                if (canvas == null || canvas.renderMode != RenderMode.WorldSpace)
                    continue;

                float signedDist = Vector3.Dot(tipPos - canvas.transform.position, canvas.transform.forward);
                if (signedDist >= 0f && signedDist < minDist)
                {
                    minDist = signedDist;
                    nearestCanvas = canvas;
                }
            }

            if (nearestCanvas == null)
                return null;

            // Remove the component along the canvas normal to project onto the canvas surface
            Vector3 normal = nearestCanvas.transform.forward;
            float distAlongNormal = Vector3.Dot(tipPos - nearestCanvas.transform.position, normal);
            Vector3 projectedPoint = tipPos - normal * distAlongNormal;

            Camera cam = (nearestCanvas.worldCamera != null) ? nearestCanvas.worldCamera : _eventCamera;
            if (cam == null)
                return null;

            // The ray from cam through WorldToScreenPoint(projectedPoint) passes through
            // projectedPoint on the canvas surface — correct element is always hit
            Vector2 screenPos = cam.WorldToScreenPoint(projectedPoint);

            return new PointerEventData(EventSystem.current)
            {
                position = screenPos,
                pressPosition = screenPos
            };
        }

        // ──────────────────────────────────────────────────────────────
        // Finger tip caching
        // ──────────────────────────────────────────────────────────────

        private void CacheFingerTips()
        {
            if (_fingerTipsCached) return;
            if (!_skeleton.IsInitialized || _skeleton.Bones == null) return;

            foreach (var bone in _skeleton.Bones)
            {
                if (bone == null || bone.Transform == null) continue;

                for (int i = 0; i < FingerTipBoneIds.Length; i++)
                {
                    if (bone.Id == FingerTipBoneIds[i])
                        _fingerTips[i] = bone.Transform;
                }
            }

            for (int i = 0; i < _fingerTips.Length; i++)
            {
                if (_fingerTips[i] != null)
                {
                    _fingerTipsCached = true;
                    Debug.Log($"[HandPokeInteractor] Finger tips cached on '{_hand.gameObject.name}'.");
                    return;
                }
            }
        }

        // ──────────────────────────────────────────────────────────────
        // Raycasting
        // ──────────────────────────────────────────────────────────────

        private GameObject RaycastUI(PointerEventData pointerData, Vector3 tipPos)
        {
            s_raycastResults.Clear();

            if (EventSystem.current == null)
                return null;

            EventSystem.current.RaycastAll(pointerData, s_raycastResults);

            GameObject closest = null;
            float closestDist = float.MaxValue;

            foreach (var result in s_raycastResults)
            {
                if (result.gameObject == null) continue;

                float dist = result.worldPosition != Vector3.zero
                    ? Vector3.Distance(tipPos, result.worldPosition)
                    : result.distance;

                if (dist < _pokeRadius && dist < closestDist)
                {
                    closestDist = dist;
                    closest = result.gameObject;
                    pointerData.pointerCurrentRaycast = result;
                }
            }

            return closest;
        }

        // ──────────────────────────────────────────────────────────────
        // Hover and poke
        // ──────────────────────────────────────────────────────────────

        private void HandleHover(GameObject hitObject, PointerEventData pointerData)
        {
            GameObject hoverTarget = hitObject != null
                ? ExecuteEvents.GetEventHandler<IPointerEnterHandler>(hitObject)
                : null;

            if (hoverTarget == _hoveredObject) return;

            if (_hoveredObject != null)
                ExecuteEvents.ExecuteHierarchy(_hoveredObject, pointerData, ExecuteEvents.pointerExitHandler);

            if (hoverTarget != null)
            {
                ExecuteEvents.ExecuteHierarchy(hoverTarget, pointerData, ExecuteEvents.pointerEnterHandler);
                if (Audio.UIAudioFeedback.Instance != null)
                    Audio.UIAudioFeedback.Instance.PlayHover();
            }

            _hoveredObject = hoverTarget;
            UpdatePokeDotColor(hoverTarget != null);
        }

        /// <summary>
        /// Fires PointerDown/Up/Click events. Uses perpendicular canvas distance
        /// for poke depth — consistent and finger-agnostic.
        /// </summary>
        private void HandlePoke(GameObject hitObject, PointerEventData pointerData, float distToCanvas)
        {
            if (hitObject == null)
            {
                if (_pressedObject != null) ReleasePress(pointerData);
                _wasPoking = false;
                return;
            }

            bool isPoking = distToCanvas < _pokeDepthThreshold;
            GameObject clickTarget = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hitObject) ?? hitObject;

            if (isPoking && !_wasPoking && Time.time - _lastPokeTime > PokeDebounceTime)
            {
                _pressedObject = clickTarget;
                pointerData.pointerPress = clickTarget;
                pointerData.rawPointerPress = hitObject;
                ExecuteEvents.ExecuteHierarchy(clickTarget, pointerData, ExecuteEvents.pointerDownHandler);
                TrySelectInputField(hitObject);
                _lastPokeTime = Time.time;
            }
            else if (!isPoking && _wasPoking && _pressedObject != null)
            {
                pointerData.pointerPress = _pressedObject;
                ExecuteEvents.ExecuteHierarchy(_pressedObject, pointerData, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.ExecuteHierarchy(_pressedObject, pointerData, ExecuteEvents.pointerClickHandler);
                _pressedObject = null;
            }

            _wasPoking = isPoking;
        }

        /// <summary>
        /// Activates a TMP_InputField if the poked object is or belongs to one.
        /// </summary>
        private void TrySelectInputField(GameObject target)
        {
            if (target == null) return;
            var inputField = target.GetComponentInParent<TMP_InputField>();
            if (inputField != null)
            {
                inputField.Select();
                inputField.ActivateInputField();
            }
        }

        private void ReleasePress(PointerEventData pointerData)
        {
            if (_pressedObject == null) return;
            ExecuteEvents.ExecuteHierarchy(_pressedObject, pointerData, ExecuteEvents.pointerUpHandler);
            _pressedObject = null;
        }

        private void ClearInteractionState()
        {
            if (_hoveredObject != null || _pressedObject != null)
            {
                var pointerData = new PointerEventData(EventSystem.current);

                if (_hoveredObject != null)
                {
                    ExecuteEvents.ExecuteHierarchy(_hoveredObject, pointerData, ExecuteEvents.pointerExitHandler);
                    _hoveredObject = null;
                }

                ReleasePress(pointerData);
            }

            _wasPoking = false;
            _activeFingerIndex = -1;

            // Reset tip cache so it is rebuilt cleanly if hand tracking restarts
            _fingerTipsCached = false;
            for (int i = 0; i < _fingerTips.Length; i++)
                _fingerTips[i] = null;
        }

        // ──────────────────────────────────────────────────────────────
        // Poke dot visual
        // ──────────────────────────────────────────────────────────────

        private void CreatePokeDot()
        {
            _pokeDot = _pokeDotPrefab != null
                ? Instantiate(_pokeDotPrefab)
                : GameObject.CreatePrimitive(PrimitiveType.Sphere);

            _pokeDot.name = "PokeDot";
            _pokeDot.transform.localScale = Vector3.one * PokeDotScale;

            var col = _pokeDot.GetComponent<Collider>();
            if (col != null) Destroy(col);

            _pokeDotRenderer = _pokeDot.GetComponent<Renderer>();
            if (_pokeDotRenderer != null && _pokeDotPrefab == null)
            {
                _pokeDotRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                _pokeDotRenderer.material.color = _hoverColor;
            }

            _pokeDot.SetActive(false);
        }

        private void UpdatePokeDot(Vector3 position)
        {
            if (_pokeDot == null) return;
            _pokeDot.transform.position = position;
            SetPokeDotVisible(true);
        }

        private void UpdatePokeDotColor(bool isHovering)
        {
            if (_pokeDotRenderer == null) return;
            _pokeDotRenderer.material.color = isHovering ? _pressColor : _hoverColor;
            _pokeDot.transform.localScale = Vector3.one * (isHovering ? PokeDotScale * 1.5f : PokeDotScale);
        }

        private void SetPokeDotVisible(bool visible)
        {
            if (_pokeDot != null && _pokeDot.activeSelf != visible)
                _pokeDot.SetActive(visible);
        }
    }
}
