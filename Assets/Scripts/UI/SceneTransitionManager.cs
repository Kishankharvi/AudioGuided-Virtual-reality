using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AGVRSystem.UI
{
    /// <summary>
    /// Manages scene transitions with a fullscreen fade overlay.
    /// Provides fade-out, scene load, and fade-in animation.
    /// Persists across scene loads via DontDestroyOnLoad.
    /// </summary>
    public class SceneTransitionManager : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float _fadeDuration = 0.6f;
        [SerializeField] private Color _fadeColor = Color.black;
        [SerializeField] private AnimationCurve _fadeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        private static SceneTransitionManager _instance;
        private Canvas _fadeCanvas;
        private CanvasGroup _fadeGroup;
        private Image _fadeImage;
        private bool _isTransitioning;

        /// <summary>Singleton accessor.</summary>
        public static SceneTransitionManager Instance => _instance;

        /// <summary>True while a transition is in progress.</summary>
        public bool IsTransitioning => _isTransitioning;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                // Destroy ONLY this component — NOT the gameObject.
                // SceneTransitionManager lives on the shared /Managers root which also
                // parents SessionManager, ExerciseCoordinator, HandJointVisualizer, etc.
                // Destroying the gameObject would wipe out the entire scene hierarchy.
                Destroy(this);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            CreateFadeOverlay();
            _fadeGroup.alpha = 0f;
            _fadeCanvas.enabled = false;
        }

        /// <summary>
        /// Starts a fade-out, loads the target scene, then fades back in.
        /// </summary>
        public void TransitionToScene(string sceneName)
        {
            if (_isTransitioning)
                return;

            StartCoroutine(TransitionCoroutine(sceneName));
        }

        /// <summary>
        /// Plays only the fade-in effect (useful for scene start).
        /// </summary>
        public void FadeIn()
        {
            if (_isTransitioning)
                return;

            StartCoroutine(FadeCoroutine(1f, 0f));
        }

        private IEnumerator TransitionCoroutine(string sceneName)
        {
            _isTransitioning = true;

            // Fade out (alpha 0 -> 1)
            yield return StartCoroutine(FadeCoroutine(0f, 1f));

            // Load scene
            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);
            if (asyncLoad != null)
            {
                while (!asyncLoad.isDone)
                {
                    yield return null;
                }
            }

            // Brief hold
            yield return new WaitForSeconds(0.15f);

            // Fade in (alpha 1 -> 0)
            yield return StartCoroutine(FadeCoroutine(1f, 0f));

            _isTransitioning = false;
        }

        private IEnumerator FadeCoroutine(float fromAlpha, float toAlpha)
        {
            _fadeCanvas.enabled = true;
            float elapsed = 0f;

            while (elapsed < _fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = _fadeCurve.Evaluate(elapsed / _fadeDuration);
                _fadeGroup.alpha = Mathf.Lerp(fromAlpha, toAlpha, t);
                yield return null;
            }

            _fadeGroup.alpha = toAlpha;

            if (Mathf.Approximately(toAlpha, 0f))
            {
                _fadeCanvas.enabled = false;
            }
        }

        private void CreateFadeOverlay()
        {
            // Create overlay canvas that renders on top of everything
            GameObject canvasObj = new GameObject("FadeOverlayCanvas");
            canvasObj.transform.SetParent(transform, false);

            _fadeCanvas = canvasObj.AddComponent<Canvas>();
            _fadeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _fadeCanvas.sortingOrder = 9999;

            _fadeGroup = canvasObj.AddComponent<CanvasGroup>();
            _fadeGroup.blocksRaycasts = false;
            _fadeGroup.interactable = false;

            // Fullscreen fade image
            GameObject imageObj = new GameObject("FadeImage");
            imageObj.transform.SetParent(canvasObj.transform, false);

            _fadeImage = imageObj.AddComponent<Image>();
            _fadeImage.color = _fadeColor;
            _fadeImage.raycastTarget = false;

            RectTransform rt = imageObj.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}
