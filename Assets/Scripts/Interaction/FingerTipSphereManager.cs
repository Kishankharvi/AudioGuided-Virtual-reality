using System.Collections;
using UnityEngine;

namespace AGVRSystem.Interaction
{
    /// <summary>
    /// Spawns a small interactive indicator sphere at every fingertip of both hands.
    /// Spheres follow the bones in real-time, change colour when close to a world-space
    /// canvas, and turn bright white when actually poking (pressing) the canvas surface.
    /// This gives users clear visual confirmation that all five fingers can interact
    /// with session canvases in the MainMenu and other scenes.
    ///
    /// Attach to the Managers GameObject in any scene that uses hand-tracked UI.
    /// </summary>
    public class FingerTipSphereManager : MonoBehaviour
    {
        [Header("Sphere Appearance")]
        [SerializeField] private float _sphereRadius = 0.007f;
        [SerializeField] private Color _defaultColor  = new Color(0.35f, 0.70f, 1.00f, 0.30f);
        [SerializeField] private Color _hoverColor    = new Color(0.20f, 1.00f, 0.55f, 0.80f);
        [SerializeField] private Color _pokeColor     = new Color(1.00f, 1.00f, 1.00f, 0.95f);

        [Header("Detection")]
        [SerializeField] private float _hoverRadius  = 0.10f;
        [SerializeField] private float _pokeDistance = 0.025f;

        // Fingertips we attach spheres to
        private static readonly OVRSkeleton.BoneId[] FingertipBones =
        {
            OVRSkeleton.BoneId.Hand_ThumbTip,
            OVRSkeleton.BoneId.Hand_IndexTip,
            OVRSkeleton.BoneId.Hand_MiddleTip,
            OVRSkeleton.BoneId.Hand_RingTip,
            OVRSkeleton.BoneId.Hand_PinkyTip,
        };

        private struct FingerSphere
        {
            public GameObject go;
            public Renderer   rend;
            public Transform  bone;
        }

        private OVRSkeleton[]   _skeletons;
        private FingerSphere[][] _spheres;      // [skeletonIdx][fingerIdx]
        private Canvas[]         _canvasCache;
        private float            _cacheTimer;
        private const float      CacheInterval = 2.5f;

        private void Start()
        {
            StartCoroutine(InitWhenReady());
        }

        private IEnumerator InitWhenReady()
        {
            const float maxWait = 10f;
            float waited = 0f;

            // Wait until at least one OVRSkeleton is initialised
            while (waited < maxWait)
            {
                _skeletons = FindObjectsByType<OVRSkeleton>(FindObjectsSortMode.None);
                foreach (var sk in _skeletons)
                {
                    if (sk != null && sk.IsInitialized) goto skeletonsReady;
                }
                waited += 0.5f;
                yield return new WaitForSeconds(0.5f);
            }

            Debug.LogWarning("[FingerTipSphereManager] No OVRSkeletons initialised within timeout.");
            yield break;

            skeletonsReady:
            _spheres = new FingerSphere[_skeletons.Length][];

            for (int s = 0; s < _skeletons.Length; s++)
            {
                var skeleton = _skeletons[s];
                if (skeleton == null) continue;

                // Wait for this specific skeleton
                float skelWait = 0f;
                while (!skeleton.IsInitialized && skelWait < maxWait)
                {
                    skelWait += 0.5f;
                    yield return new WaitForSeconds(0.5f);
                }
                if (!skeleton.IsInitialized) continue;

                _spheres[s] = new FingerSphere[FingertipBones.Length];

                for (int f = 0; f < FingertipBones.Length; f++)
                {
                    Transform bone = FindBone(skeleton, FingertipBones[f]);
                    if (bone == null) continue;
                    _spheres[s][f] = BuildSphere(bone, s, f);
                }

                Debug.Log($"[FingerTipSphereManager] Created {FingertipBones.Length} fingertip spheres for '{skeleton.name}'.");
            }

            RefreshCanvasCache();
        }

        private FingerSphere BuildSphere(Transform bone, int skelIdx, int fingerIdx)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"FingerSphere_{skelIdx}_{fingerIdx}";
            go.transform.localScale = Vector3.one * (_sphereRadius * 2f);

            // No collider — interaction is handled by XRPokeInteractor/HandPokeInteractor
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var rend = go.GetComponent<Renderer>();
            if (rend != null)
            {
                // Use URP Unlit with transparency; fall back to Legacy if URP not present
                Shader sh = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Unlit/Color");

                var mat = new Material(sh) { color = _defaultColor };

                // Enable alpha transparency on URP Unlit
                if (sh != null && sh.name.Contains("Universal"))
                {
                    mat.SetFloat("_Surface", 1f);       // 0 = Opaque, 1 = Transparent
                    mat.SetFloat("_Blend", 0f);
                    mat.renderQueue = 3000;
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                }

                rend.material = mat;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.receiveShadows    = false;
            }

            return new FingerSphere { go = go, rend = rend, bone = bone };
        }

        private void Update()
        {
            if (_spheres == null) return;

            // Refresh canvas cache periodically
            _cacheTimer += Time.deltaTime;
            if (_cacheTimer >= CacheInterval)
            {
                _cacheTimer = 0f;
                RefreshCanvasCache();
            }

            for (int s = 0; s < _spheres.Length; s++)
            {
                if (_spheres[s] == null || _skeletons[s] == null) continue;

                bool handActive = _skeletons[s].IsInitialized;

                for (int f = 0; f < _spheres[s].Length; f++)
                {
                    ref FingerSphere sphere = ref _spheres[s][f];
                    if (sphere.go == null) continue;

                    if (!handActive || sphere.bone == null)
                    {
                        if (sphere.go.activeSelf) sphere.go.SetActive(false);
                        continue;
                    }

                    if (!sphere.go.activeSelf) sphere.go.SetActive(true);

                    // Track fingertip position
                    sphere.go.transform.position = sphere.bone.position;

                    // Choose colour based on proximity to the nearest canvas
                    if (sphere.rend == null) continue;

                    float dist     = NearestCanvasDistance(sphere.bone.position);
                    Color newColor;

                    if (dist <= _pokeDistance)
                    {
                        newColor = _pokeColor;
                    }
                    else if (dist <= _hoverRadius)
                    {
                        float t  = (dist - _pokeDistance) / (_hoverRadius - _pokeDistance);
                        newColor = Color.Lerp(_hoverColor, _defaultColor, t);
                    }
                    else
                    {
                        newColor = _defaultColor;
                    }

                    sphere.rend.material.color = newColor;
                }
            }
        }

        private float NearestCanvasDistance(Vector3 worldPos)
        {
            float min = float.MaxValue;

            if (_canvasCache == null) return min;

            foreach (Canvas c in _canvasCache)
            {
                if (c == null || c.renderMode != RenderMode.WorldSpace) continue;

                float signed = Vector3.Dot(worldPos - c.transform.position, c.transform.forward);
                if (signed >= 0f && signed < min)
                    min = signed;
            }

            return min;
        }

        private void RefreshCanvasCache()
        {
            _canvasCache = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        }

        private static Transform FindBone(OVRSkeleton skeleton, OVRSkeleton.BoneId id)
        {
            if (skeleton.Bones == null) return null;

            foreach (var bone in skeleton.Bones)
            {
                if (bone != null && bone.Transform != null && bone.Id == id)
                    return bone.Transform;
            }

            return null;
        }

        private void OnDestroy()
        {
            if (_spheres == null) return;

            foreach (var skelSpheres in _spheres)
            {
                if (skelSpheres == null) continue;
                foreach (var s in skelSpheres)
                    if (s.go != null) Destroy(s.go);
            }
        }
    }
}
