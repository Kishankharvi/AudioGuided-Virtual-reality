using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Comprehensive fix + effects setup for the RehabSession scene.
/// Fixes: tracking origin, EventSystem, hand visuals, HandTrackingManager placement,
///        HUD camera ref, canvas raycasters, physics properties, effects, lights, particles.
/// </summary>
public static class FixRehabSessionScene
{
    public static void Execute()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.name.Contains("RehabSession"))
        {
            Debug.LogError("[FixRehabSession] Active scene is not RehabSession.");
            return;
        }

        int fixCount = 0;

        // ============================================================
        // FIX 1: Tracking Origin → Floor Level
        // ============================================================
        var ovrManager = Object.FindFirstObjectByType<OVRManager>();
        if (ovrManager != null)
        {
            var so = new SerializedObject(ovrManager);
            var trackingProp = so.FindProperty("_trackingOriginType");
            if (trackingProp != null && trackingProp.enumValueIndex != 1)
            {
                trackingProp.enumValueIndex = 1;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(ovrManager);
                Debug.Log("[Fix 1] Set OVRManager tracking origin to Floor Level");
                fixCount++;
            }
        }

        // ============================================================
        // FIX 2: Fix hand visuals (OVRSkeletonRenderer + material)
        // ============================================================
        FixHandVisual("OVRCameraRig/TrackingSpace/LeftHandAnchor/LeftHandVisual", ref fixCount);
        FixHandVisual("OVRCameraRig/TrackingSpace/RightHandAnchor/RightHandVisual", ref fixCount);

        // ============================================================
        // FIX 2b: Fix OVRSkeleton._updateRootPose double-positioning
        // ============================================================
        FixSkeletonRootPose("OVRCameraRig/TrackingSpace/LeftHandAnchor/LeftHandVisual", ref fixCount);
        FixSkeletonRootPose("OVRCameraRig/TrackingSpace/RightHandAnchor/RightHandVisual", ref fixCount);

        // ============================================================
        // FIX 3: Add EventSystem
        // ============================================================
        var existingES = Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
        if (existingES == null)
        {
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            EditorUtility.SetDirty(esGO);
            Debug.Log("[Fix 3] Created EventSystem");
            fixCount++;
        }

        // ============================================================
        // FIX 4: Move HandTrackingManager to Managers root
        // ============================================================
        var managersGO = GameObject.Find("Managers");
        var htmChildGO = GameObject.Find("Managers/HandTrackingManager");
        if (managersGO != null && htmChildGO != null)
        {
            var htmChild = htmChildGO.GetComponent<AGVRSystem.HandTrackingManager>();
            if (htmChild != null)
            {
                var htmRoot = managersGO.GetComponent<AGVRSystem.HandTrackingManager>();
                if (htmRoot == null)
                {
                    htmRoot = managersGO.AddComponent<AGVRSystem.HandTrackingManager>();
                    var srcSO = new SerializedObject(htmChild);
                    var dstSO = new SerializedObject(htmRoot);
                    CopyProp(srcSO, dstSO, "_leftHand");
                    CopyProp(srcSO, dstSO, "_rightHand");
                    CopyProp(srcSO, dstSO, "_leftSkeleton");
                    CopyProp(srcSO, dstSO, "_rightSkeleton");
                    dstSO.ApplyModifiedProperties();
                    EditorUtility.SetDirty(managersGO);
                    Object.DestroyImmediate(htmChild);
                    if (htmChildGO.GetComponents<Component>().Length <= 1)
                        Object.DestroyImmediate(htmChildGO);
                    Debug.Log("[Fix 4] Moved HandTrackingManager to Managers root");
                    fixCount++;
                }
            }
        }

        // ============================================================
        // FIX 5: Wire HUDController._cameraTransform
        // ============================================================
        var hudGO = GameObject.Find("HUD");
        if (hudGO != null)
        {
            var hudCtrl = hudGO.GetComponent<AGVRSystem.UI.HUDController>();
            if (hudCtrl != null)
            {
                var hudSO = new SerializedObject(hudCtrl);
                var camProp = hudSO.FindProperty("_cameraTransform");
                if (camProp != null && camProp.objectReferenceValue == null)
                {
                    var centerEye = GameObject.Find("OVRCameraRig/TrackingSpace/CenterEyeAnchor");
                    if (centerEye != null)
                    {
                        camProp.objectReferenceValue = centerEye.transform;
                        hudSO.ApplyModifiedProperties();
                        EditorUtility.SetDirty(hudCtrl);
                        Debug.Log("[Fix 5] Wired HUDController._cameraTransform to CenterEyeAnchor");
                        fixCount++;
                    }
                }
            }
        }

        // ============================================================
        // FIX 6: Add GraphicRaycaster to canvases missing it
        // ============================================================
        FixRaycaster("HUD/HUDCanvas", ref fixCount);
        FixRaycaster("ExerciseInfoBoard/InfoCanvas", ref fixCount);
        FixRaycaster("ReportBoard/ReportCanvas", ref fixCount);

        // ============================================================
        // FIX 7: Fix exercise object physics (realistic masses & materials)
        // ============================================================
        // StressBall: 60mm diameter, ~60g, very squishy rubber
        FixObjectPhysics("ExerciseTable/StressBall", 0.06f, 0.8f, 1.5f, ref fixCount);
        // TennisBall: 65mm, ~58g, bouncy felt
        FixObjectPhysics("ExerciseTable/TennisBall", 0.058f, 0.5f, 1.0f, ref fixCount);
        // MedicineBall: 100mm, ~500g, heavy rubber
        FixObjectPhysics("ExerciseTable/MedicineBall", 0.5f, 2.0f, 3.0f, ref fixCount);
        // Marble: 20mm, ~5g, glass (low drag, rolls easily)
        FixObjectPhysics("ExerciseTable/Marble", 0.005f, 0.3f, 0.5f, ref fixCount);
        // Pen: ~30g, moderate drag
        FixObjectPhysics("ExerciseTable/Pen", 0.03f, 1.2f, 2.5f, ref fixCount);
        // Coin: ~8g, flat (high drag to not slide)
        FixObjectPhysics("ExerciseTable/Coin", 0.008f, 3.0f, 4.0f, ref fixCount);

        // ============================================================
        // FIX 8: Add CanvasGroups for fade animations
        // ============================================================
        var hudCanvas = GameObject.Find("HUD/HUDCanvas");
        var infoCanvas = GameObject.Find("ExerciseInfoBoard/InfoCanvas");
        CanvasGroup hudCG = EnsureComp<CanvasGroup>(hudCanvas, ref fixCount);
        CanvasGroup infoCG = EnsureComp<CanvasGroup>(infoCanvas, ref fixCount);

        // ============================================================
        // SETUP: RehabSessionEffects
        // ============================================================
        if (managersGO != null)
        {
            var effects = managersGO.GetComponent<AGVRSystem.UI.RehabSessionEffects>();
            if (effects == null)
            {
                effects = managersGO.AddComponent<AGVRSystem.UI.RehabSessionEffects>();
                Debug.Log("[Setup] Added RehabSessionEffects to Managers");
                fixCount++;
            }

            var eso = new SerializedObject(effects);
            SetRef(eso, "_hudBoard", GameObject.Find("HUD")?.transform);
            SetRef(eso, "_infoBoard", GameObject.Find("ExerciseInfoBoard")?.transform);
            SetRef(eso, "_reportBoard", GameObject.Find("ReportBoard")?.transform);
            SetRef(eso, "_hudCanvasGroup", hudCG);
            SetRef(eso, "_infoCanvasGroup", infoCG);

            var accentBar = GameObject.Find("ExerciseInfoBoard/InfoCanvas/AccentBar");
            if (accentBar != null) SetRef(eso, "_infoAccentBar", accentBar.GetComponent<Graphic>());

            var reportAccent = GameObject.Find("ReportBoard/ReportCanvas/ReportAccent");
            if (reportAccent != null) SetRef(eso, "_reportAccent", reportAccent.GetComponent<Graphic>());

            var progressFill = GameObject.Find("HUD/HUDCanvas/CenterPanel/HoldProgressBarBG/HoldProgressFill");
            if (progressFill != null) SetRef(eso, "_holdProgressFill", progressFill.GetComponent<Graphic>());

            var feedbackText = GameObject.Find("HUD/HUDCanvas/CenterPanel/FeedbackText");
            if (feedbackText != null) SetRef(eso, "_feedbackText", feedbackText.GetComponent<TMP_Text>());

            var dirLight = GameObject.Find("Directional Light");
            if (dirLight != null) SetRef(eso, "_directionalLight", dirLight.GetComponent<Light>());

            eso.ApplyModifiedProperties();
            EditorUtility.SetDirty(effects);
        }

        // ============================================================
        // SETUP: Ambient particles
        // ============================================================
        if (Object.FindFirstObjectByType<AGVRSystem.UI.MainMenuParticles>() == null)
        {
            var cameraRig = GameObject.Find("OVRCameraRig");
            var particlesGO = new GameObject("AmbientEffects");
            particlesGO.transform.position = cameraRig != null ? cameraRig.transform.position : Vector3.zero;

            var particles = particlesGO.AddComponent<AGVRSystem.UI.MainMenuParticles>();
            var pso = new SerializedObject(particles);
            if (cameraRig != null) SetRef(pso, "_centerPoint", cameraRig.transform);

            var fc = pso.FindProperty("_fireflyCount");
            if (fc != null) fc.intValue = 12;
            var pc = pso.FindProperty("_pollenCount");
            if (pc != null) pc.intValue = 20;
            var fr = pso.FindProperty("_fireflyRadius");
            if (fr != null) fr.floatValue = 3.5f;

            pso.ApplyModifiedProperties();
            EditorUtility.SetDirty(particlesGO);
            Debug.Log("[Setup] Created AmbientEffects");
            fixCount++;
        }

        // ============================================================
        // SETUP: Table spotlight
        // ============================================================
        if (GameObject.Find("TableSpotlight") == null)
        {
            var spotGO = new GameObject("TableSpotlight");
            spotGO.transform.position = new Vector3(-2.917f, 1.8f, -5.632f);
            spotGO.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            var light = spotGO.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = new Color(1f, 0.95f, 0.85f, 1f);
            light.intensity = 0.6f;
            light.range = 2.5f;
            light.spotAngle = 60f;
            light.innerSpotAngle = 40f;
            light.shadows = LightShadows.Soft;

            EditorUtility.SetDirty(spotGO);
            Debug.Log("[Setup] Created TableSpotlight");
            fixCount++;
        }

        // ============================================================
        // SETUP: Board glow light
        // ============================================================
        if (GameObject.Find("SessionGlowLight") == null)
        {
            var glowGO = new GameObject("SessionGlowLight");
            glowGO.transform.position = new Vector3(-2.5f, 1.5f, -5.2f);

            var light = glowGO.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.3f, 0.85f, 0.45f, 1f);
            light.intensity = 0.3f;
            light.range = 3f;
            light.shadows = LightShadows.None;

            EditorUtility.SetDirty(glowGO);
            Debug.Log("[Setup] Created SessionGlowLight");
            fixCount++;
        }

        // ============================================================
        // SETUP: Warm fill light
        // ============================================================
        if (GameObject.Find("SessionWarmLight") == null)
        {
            var warmGO = new GameObject("SessionWarmLight");
            warmGO.transform.position = new Vector3(-3.2f, 2.0f, -5.0f);

            var light = warmGO.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.88f, 0.6f, 1f);
            light.intensity = 0.2f;
            light.range = 4f;
            light.shadows = LightShadows.None;

            EditorUtility.SetDirty(warmGO);
            Debug.Log("[Setup] Created SessionWarmLight");
            fixCount++;
        }

        // ============================================================
        // Save
        // ============================================================
        if (fixCount > 0)
        {
            EditorSceneManager.MarkSceneDirty(activeScene);
            EditorSceneManager.SaveScene(activeScene);
            Debug.Log($"[FixRehabSession] Applied {fixCount} fixes/changes and saved.");
        }
        else
        {
            Debug.Log("[FixRehabSession] No changes needed.");
        }
    }

    private static void FixHandVisual(string path, ref int fixCount)
    {
        var go = GameObject.Find(path);
        if (go == null) return;

        // Ensure SkinnedMeshRenderer exists (required for hand mesh rendering)
        if (go.GetComponent<SkinnedMeshRenderer>() == null)
        {
            go.AddComponent<SkinnedMeshRenderer>();
            EditorUtility.SetDirty(go);
            Debug.Log($"[Fix 2] Added SkinnedMeshRenderer to {go.name}");
            fixCount++;
        }

        // Ensure OVRMesh exists (provides mesh data from OVR runtime)
        var ovrMesh = go.GetComponent("OVRMesh") as Component;
        if (ovrMesh == null)
        {
            System.Type meshType = null;
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                meshType = asm.GetType("OVRMesh");
                if (meshType != null) break;
            }
            if (meshType != null)
            {
                ovrMesh = go.AddComponent(meshType);
                bool isLeft = path.Contains("Left");
                var so = new SerializedObject(ovrMesh);
                var meshTypeProp = so.FindProperty("_meshType");
                if (meshTypeProp != null)
                {
                    meshTypeProp.enumValueIndex = isLeft ? 4 : 5;
                    so.ApplyModifiedProperties();
                }
                EditorUtility.SetDirty(go);
                Debug.Log($"[Fix 2] Added OVRMesh to {go.name}");
                fixCount++;
            }
        }

        // Ensure OVRMeshRenderer exists — this renders the actual hand mesh (skin),
        // NOT the skeleton wireframe. The previous pink material issue is fixed by
        // assigning a valid URP material, not by removing this component.
        var meshRenderer = go.GetComponent("OVRMeshRenderer") as Component;
        if (meshRenderer == null)
        {
            System.Type meshRendererType = null;
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                meshRendererType = asm.GetType("OVRMeshRenderer");
                if (meshRendererType != null) break;
            }
            if (meshRendererType != null)
            {
                meshRenderer = go.AddComponent(meshRendererType);
                var so = new SerializedObject(meshRenderer);

                if (ovrMesh != null)
                {
                    var ovrMeshProp = so.FindProperty("_ovrMesh");
                    if (ovrMeshProp != null)
                        ovrMeshProp.objectReferenceValue = ovrMesh;
                }

                var skeleton = go.GetComponent("OVRSkeleton") as Component;
                if (skeleton != null)
                {
                    var skelProp = so.FindProperty("_ovrSkeleton");
                    if (skelProp != null)
                        skelProp.objectReferenceValue = skeleton;
                }

                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(go);
                Debug.Log($"[Fix 2] Added OVRMeshRenderer to {go.name}");
                fixCount++;
            }
        }

        // Assign URP material to SkinnedMeshRenderer to prevent pink hands
        Material handMat = null;
        string[] guids = AssetDatabase.FindAssets("HandMaterial t:Material");
        if (guids.Length > 0)
        {
            string matPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            handMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        }

        var smr = go.GetComponent<SkinnedMeshRenderer>();
        if (smr != null && handMat != null && smr.sharedMaterial == null)
        {
            smr.sharedMaterial = handMat;
            EditorUtility.SetDirty(smr);
            Debug.Log($"[Fix 2] Assigned hand material to {go.name}");
            fixCount++;
        }

        // Disable OVRSkeletonRenderer — show hand mesh only, not skeleton wireframe
        var skelRenderer = go.GetComponent("OVRSkeletonRenderer") as Behaviour;
        if (skelRenderer != null && skelRenderer.enabled)
        {
            skelRenderer.enabled = false;
            EditorUtility.SetDirty(skelRenderer);
            Debug.Log($"[Fix 2] Disabled OVRSkeletonRenderer on {go.name}");
            fixCount++;
        }
    }

    private static void FixRaycaster(string path, ref int fixCount)
    {
        var go = GameObject.Find(path);
        if (go == null) return;
        if (go.GetComponent<GraphicRaycaster>() == null)
        {
            go.AddComponent<GraphicRaycaster>();
            EditorUtility.SetDirty(go);
            Debug.Log($"[Fix 6] Added GraphicRaycaster to {go.name}");
            fixCount++;
        }
    }

    /// <summary>
    /// Disables _updateRootPose on OVRSkeleton to prevent double-positioning
    /// when the hand visual is a child of a HandAnchor.
    /// </summary>
    private static void FixSkeletonRootPose(string path, ref int fixCount)
    {
        var go = GameObject.Find(path);
        if (go == null) return;

        var ovrSkeleton = go.GetComponent("OVRSkeleton") as Component;
        if (ovrSkeleton == null) return;

        var so = new SerializedObject(ovrSkeleton);
        var rootPoseProp = so.FindProperty("_updateRootPose");
        if (rootPoseProp != null && rootPoseProp.boolValue)
        {
            rootPoseProp.boolValue = false;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(ovrSkeleton);
            Debug.Log($"[Fix 2b] Disabled _updateRootPose on {go.name} (prevents double-positioning)");
            fixCount++;
        }
    }

    private static void FixObjectPhysics(string path, float mass, float drag, float angDrag, ref int fixCount)
    {
        var go = GameObject.Find(path);
        if (go == null) return;
        var rb = go.GetComponent<Rigidbody>();
        if (rb == null) return;

        bool changed = false;
        if (!Mathf.Approximately(rb.mass, mass))
        {
            rb.mass = mass;
            changed = true;
        }
        if (!Mathf.Approximately(rb.linearDamping, drag))
        {
            rb.linearDamping = drag;
            changed = true;
        }
        if (!Mathf.Approximately(rb.angularDamping, angDrag))
        {
            rb.angularDamping = angDrag;
            changed = true;
        }

        if (changed)
        {
            EditorUtility.SetDirty(rb);
            Debug.Log($"[Fix 7] Updated physics for {go.name}: mass={mass}, drag={drag}, angDrag={angDrag}");
            fixCount++;
        }
    }

    private static T EnsureComp<T>(GameObject go, ref int fixCount) where T : Component
    {
        if (go == null) return null;
        var comp = go.GetComponent<T>();
        if (comp == null)
        {
            comp = go.AddComponent<T>();
            EditorUtility.SetDirty(go);
            Debug.Log($"[Fix 8] Added {typeof(T).Name} to {go.name}");
            fixCount++;
        }
        return comp;
    }

    private static void SetRef(SerializedObject so, string prop, Object value)
    {
        var p = so.FindProperty(prop);
        if (p != null && value != null) p.objectReferenceValue = value;
    }

    private static void CopyProp(SerializedObject src, SerializedObject dst, string propName)
    {
        var s = src.FindProperty(propName);
        var d = dst.FindProperty(propName);
        if (s != null && d != null) d.objectReferenceValue = s.objectReferenceValue;
    }
}
