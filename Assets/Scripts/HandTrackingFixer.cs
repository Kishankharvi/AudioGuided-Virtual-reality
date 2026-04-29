using UnityEngine;
using System.Collections;
using System.Reflection;

namespace AGVRSystem
{
    /// <summary>
    /// Runtime auto-fix for OVR hand tracking issues:
    /// 1. Sets tracking origin to Floor Level
    /// 2. Disables OVRSkeleton._updateRootPose to prevent double-positioning
    /// 3. Restores OVRMeshRenderer if it was removed (renders the actual hand mesh)
    /// 4. Disables OVRSkeletonRenderer to hide the skeleton wireframe
    /// 5. Assigns a URP-compatible material to the hand SkinnedMeshRenderer
    ///
    /// Attach this to the OVRCameraRig or any always-active GameObject.
    /// </summary>
    public class HandTrackingFixer : MonoBehaviour
    {
        private const float RETRY_INTERVAL = 0.5f;
        private const int MAX_RETRIES = 20;

        [Header("Hand Material")]
        [Tooltip("Material for hand mesh rendering. If null, creates a URP Unlit material at runtime.")]
        [SerializeField] private Material _handMaterial;

        [Header("Hand Visual Names")]
        [SerializeField] private string _leftHandVisualName = "LeftHandVisual";
        [SerializeField] private string _rightHandVisualName = "RightHandVisual";

        [Header("Visibility")]
        [Tooltip("Show the actual hand mesh (OVRMeshRenderer) in addition to skeleton.")]
        [SerializeField] private bool _showHandMesh = true;
        [Tooltip("Show the skeleton wireframe (OVRSkeletonRenderer). Green lines between joints. Leave false for clean hand visuals.")]
        [SerializeField] private bool _showSkeleton = false;

        private bool _fixed;

        private void Start()
        {
            StartCoroutine(ApplyFixesWithRetry());
        }

        private IEnumerator ApplyFixesWithRetry()
        {
            for (int attempt = 0; attempt < MAX_RETRIES && !_fixed; attempt++)
            {
                yield return new WaitForSeconds(RETRY_INTERVAL);
                _fixed = TryApplyAllFixes();
            }

            if (!_fixed)
            {
                Debug.LogWarning("[HandTrackingFixer] Could not apply all fixes after max retries. " +
                    "Some hand components may not have initialized yet.");
            }
        }

        private bool TryApplyAllFixes()
        {
            bool allFixed = true;

            allFixed &= FixTrackingOrigin();
            allFixed &= FixSkeletonRootPose();
            allFixed &= FixHandRendering();

            if (allFixed)
                Debug.Log("[HandTrackingFixer] All hand tracking fixes applied successfully.");

            return allFixed;
        }

        // =====================================================================
        // FIX 1: Tracking Origin
        // =====================================================================

        private bool FixTrackingOrigin()
        {
            var ovrManager = OVRManager.instance;
            if (ovrManager == null) return false;

            if (ovrManager.trackingOriginType != OVRManager.TrackingOrigin.FloorLevel)
            {
                ovrManager.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
                Debug.Log("[HandTrackingFixer] Set tracking origin to FloorLevel.");
            }

            return true;
        }

        // =====================================================================
        // FIX 2: Skeleton Root Pose
        // =====================================================================

        private bool FixSkeletonRootPose()
        {
            var skeletons = FindObjectsByType<OVRSkeleton>(FindObjectsSortMode.None);
            if (skeletons == null || skeletons.Length == 0) return false;

            foreach (var skeleton in skeletons)
            {
                Transform parent = skeleton.transform.parent;
                if (parent == null || !parent.name.Contains("HandAnchor"))
                    continue;

                var field = typeof(OVRSkeleton).GetField(
                    "_updateRootPose",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (field != null && (bool)field.GetValue(skeleton))
                {
                    field.SetValue(skeleton, false);
                    Debug.Log($"[HandTrackingFixer] Disabled _updateRootPose on {skeleton.gameObject.name}.");
                }
            }

            return true;
        }

        // =====================================================================
        // FIX 3: Restore OVRMeshRenderer and fix materials
        // =====================================================================

        /// <summary>
        /// Ensures each hand visual has the correct rendering components:
        /// - OVRMesh (loads hand mesh data from OVR runtime)
        /// - OVRMeshRenderer (drives SkinnedMeshRenderer with mesh + skeleton data)
        /// - SkinnedMeshRenderer with a valid URP material
        /// - OVRSkeletonRenderer enabled/disabled based on _showSkeleton
        /// </summary>
        private bool FixHandRendering()
        {
            if (_handMaterial == null)
                _handMaterial = CreateURPHandMaterial();

            bool foundAny = false;
            var allTransforms = FindObjectsByType<Transform>(FindObjectsSortMode.None);

            foreach (var t in allTransforms)
            {
                bool isLeft = t.name == _leftHandVisualName;
                bool isRight = t.name == _rightHandVisualName;
                if (!isLeft && !isRight) continue;

                foundAny = true;
                FixSingleHand(t.gameObject, isLeft);
            }

            return foundAny;
        }

        private void FixSingleHand(GameObject handGO, bool isLeft)
        {
            // --- OVRMesh: provides the mesh topology from OVR runtime ---
            var ovrMesh = handGO.GetComponent<OVRMesh>();
            if (ovrMesh == null && _showHandMesh)
            {
                ovrMesh = handGO.AddComponent<OVRMesh>();

                // Set _meshType via reflection: HandLeft=4, HandRight=5
                var meshTypeField = typeof(OVRMesh).GetField(
                    "_meshType",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (meshTypeField != null)
                {
                    // OVRMesh.MeshType enum: HandLeft=4, HandRight=5
                    object meshTypeValue = isLeft ? 4 : 5;
                    // The field is an enum type, so cast to the actual enum
                    var enumType = meshTypeField.FieldType;
                    meshTypeField.SetValue(ovrMesh, System.Enum.ToObject(enumType, (int)meshTypeValue));
                }

                Debug.Log($"[HandTrackingFixer] Added OVRMesh to {handGO.name} " +
                    $"(MeshType={(isLeft ? "HandLeft" : "HandRight")}).");
            }

            // --- OVRMeshRenderer: combines OVRMesh + OVRSkeleton to render hand mesh ---
            var ovrMeshRenderer = handGO.GetComponent<OVRMeshRenderer>();
            if (ovrMeshRenderer == null && _showHandMesh)
            {
                ovrMeshRenderer = handGO.AddComponent<OVRMeshRenderer>();

                // Wire _ovrMesh reference via reflection
                if (ovrMesh != null)
                {
                    var ovrMeshField = typeof(OVRMeshRenderer).GetField(
                        "_ovrMesh",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    ovrMeshField?.SetValue(ovrMeshRenderer, ovrMesh);
                }

                // Wire _ovrSkeleton reference via reflection
                var skeleton = handGO.GetComponent<OVRSkeleton>();
                if (skeleton != null)
                {
                    var skelField = typeof(OVRMeshRenderer).GetField(
                        "_ovrSkeleton",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    skelField?.SetValue(ovrMeshRenderer, skeleton);
                }

                Debug.Log($"[HandTrackingFixer] Added OVRMeshRenderer to {handGO.name}.");
            }

            // --- SkinnedMeshRenderer: ensure it exists and has a valid URP material ---
            var smr = handGO.GetComponent<SkinnedMeshRenderer>();
            if (smr == null)
            {
                smr = handGO.AddComponent<SkinnedMeshRenderer>();
                Debug.Log($"[HandTrackingFixer] Added SkinnedMeshRenderer to {handGO.name}.");
            }

            if (_handMaterial != null && (smr.sharedMaterial == null || IsPinkMaterial(smr.sharedMaterial)))
            {
                smr.sharedMaterial = _handMaterial;
                Debug.Log($"[HandTrackingFixer] Assigned URP material to {handGO.name} SkinnedMeshRenderer.");
            }

            // --- OVRSkeletonRenderer: toggle visibility ---
            var skelRenderer = handGO.GetComponent<OVRSkeletonRenderer>();
            if (skelRenderer != null)
            {
                if (skelRenderer.enabled != _showSkeleton)
                {
                    skelRenderer.enabled = _showSkeleton;
                    Debug.Log($"[HandTrackingFixer] OVRSkeletonRenderer on {handGO.name} " +
                        $"set to {(_showSkeleton ? "enabled" : "disabled")}.");
                }

                // Fix skeleton material regardless
                SetSkeletonRendererMaterial(skelRenderer);
            }
        }

        /// <summary>
        /// Checks if a material is "pink" (shader not found or incompatible).
        /// </summary>
        private bool IsPinkMaterial(Material mat)
        {
            if (mat == null) return true;

            // Pink material is typically caused by a null/missing shader
            if (mat.shader == null) return true;

            // "Hidden/InternalErrorShader" is Unity's fallback for missing shaders
            string shaderName = mat.shader.name;
            return shaderName.Contains("InternalError") || shaderName.Contains("Pink");
        }

        private void SetSkeletonRendererMaterial(OVRSkeletonRenderer renderer)
        {
            SetMaterialField(renderer, "_skeletonMaterial");
            SetMaterialField(renderer, "_capsuleMaterial");
        }

        private void SetMaterialField(Component component, string fieldName)
        {
            var field = component.GetType().GetField(
                fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) return;

            var currentMat = field.GetValue(component) as Material;
            if (currentMat == null && _handMaterial != null)
            {
                field.SetValue(component, _handMaterial);
                Debug.Log($"[HandTrackingFixer] Set {fieldName} on {component.gameObject.name}.");
            }
        }

        // =====================================================================
        // Material Creation
        // =====================================================================

        /// <summary>
        /// Creates a URP Lit material with a natural skin tone for hand rendering.
        /// Uses Lit shader for better visual quality (lighting, shadows) on Quest.
        /// Falls back to Unlit if Lit is unavailable.
        /// </summary>
        private Material CreateURPHandMaterial()
        {
            // Try URP Lit first for better visual quality
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            if (shader == null)
            {
                Debug.LogError("[HandTrackingFixer] No compatible shader found for hand material.");
                return null;
            }

            var mat = new Material(shader);
            mat.name = "HandMesh_Runtime";

            // Natural skin tone, semi-transparent
            Color skinColor = new Color(0.87f, 0.74f, 0.63f, 0.9f);
            mat.SetColor("_BaseColor", skinColor);
            mat.SetColor("_Color", skinColor);

            // Transparency setup
            mat.SetFloat("_Surface", 1f);   // Transparent
            mat.SetFloat("_Blend", 0f);     // Alpha blend
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_AlphaClip", 0f);
            mat.renderQueue = 3000;

            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

            // Smoothness for a natural skin look (only applies to Lit shader)
            mat.SetFloat("_Smoothness", 0.3f);
            mat.SetFloat("_Metallic", 0f);

            Debug.Log("[HandTrackingFixer] Created runtime hand material with skin tone.");
            return mat;
        }
    }
}
