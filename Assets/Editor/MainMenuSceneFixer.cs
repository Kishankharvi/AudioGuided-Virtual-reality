using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// One-shot editor script that fixes the MainMenu scene:
/// 1. Hand tracking: tracking origin → Floor Level, add OVRMeshRenderer + SkinnedMeshRenderer
/// 2. OVRCameraRig position restored to garden spawn point
/// 3. Scene UI boards positioned at comfortable interaction height near the camera rig
/// 4. UI interactivity: fix Button targetGraphic references, widen poke thresholds
/// 5. Audio: wire UIAudioFeedback._audioSource to VoiceSource or create dedicated source
/// </summary>
public static class MainMenuSceneFixer
{
    // Camera rig spawn point inside the garden (matches Calibration scene placement).
    private static readonly Vector3 RigPosition = new Vector3(-2.3f, 0f, -3.8f);

    // Board positions: same X as the rig, at comfortable standing-user eye height,
    // 0.7m in front of the rig along Z (matching Calibration scene layout).
    private static readonly Vector3 SessionBoardPosition = new Vector3(-2.3f, 1.3f, -3.1f);
    private static readonly Vector3 AboutBoardPosition = new Vector3(-1.5f, 1.3f, -3.1f);

    [MenuItem("AGVRSystem/Fix MainMenu Scene")]
    public static void Execute()
    {
        // Ensure we're in the right scene
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.name.Contains("MainMenu"))
        {
            Debug.LogError("[MainMenuSceneFixer] Active scene is not MainMenu. Open MainMenu scene first.");
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
            if (trackingProp != null)
            {
                // OVRManager.TrackingOrigin.FloorLevel = 1
                if (trackingProp.enumValueIndex != 1)
                {
                    trackingProp.enumValueIndex = 1;
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(ovrManager);
                    Debug.Log("[Fix 1] Set OVRManager tracking origin to Floor Level");
                    fixCount++;
                }
                else
                {
                    Debug.Log("[Fix 1] Tracking origin already Floor Level — skipped");
                }
            }
            else
            {
                Debug.LogWarning("[Fix 1] Could not find _trackingOriginType property on OVRManager");
            }
        }
        else
        {
            Debug.LogWarning("[Fix 1] OVRManager not found in scene");
        }

        // ============================================================
        // FIX 2: Restore OVRCameraRig to garden spawn point (-2.3, 0, -3.8)
        // The rig position places the user inside the garden environment.
        // Hand tracking is relative to the rig, so this does not affect
        // hand-to-head alignment — only where the user spawns in the scene.
        // ============================================================
        var cameraRig = GameObject.Find("OVRCameraRig");
        if (cameraRig != null)
        {
            var rigTransform = cameraRig.transform;
            if (rigTransform.localPosition != RigPosition || rigTransform.localScale != Vector3.one)
            {
                Undo.RecordObject(rigTransform, "Restore OVRCameraRig Position");
                rigTransform.localPosition = RigPosition;
                rigTransform.localScale = Vector3.one;
                EditorUtility.SetDirty(cameraRig);
                Debug.Log($"[Fix 2] Restored OVRCameraRig to {RigPosition}");
                fixCount++;
            }
            else
            {
                Debug.Log("[Fix 2] OVRCameraRig already at correct position — skipped");
            }
        }
        else
        {
            Debug.LogWarning("[Fix 2] OVRCameraRig not found in scene");
        }

        // ============================================================
        // FIX 3: Position boards near the camera rig at comfortable height
        // Y=1.3: chest/eye height for a standing user at Floor Level.
        // Z=-3.1: 0.7m in front of the rig (Z=-3.8), within arm's reach.
        // ============================================================
        RepositionBoard("SessionBoard", SessionBoardPosition, ref fixCount);
        RepositionBoard("AboutBoard", AboutBoardPosition, ref fixCount);

        // ============================================================
        // FIX 4: Fix OVRSkeleton._updateRootPose double-positioning bug
        // When LeftHandVisual is a child of LeftHandAnchor, both OVRCameraRig
        // (UpdateAnchors) and OVRSkeleton (_updateRootPose) set positions using
        // the same tracked-hand coordinates. Since the visual is a CHILD of the
        // anchor, these offsets compound: world = rig + hand + hand = 2x offset.
        // Fix: disable _updateRootPose so only the HandAnchor controls position.
        // Bone rotations and translations still work via _applyBoneTranslations.
        // ============================================================
        FixSkeletonRootPose("OVRCameraRig/TrackingSpace/LeftHandAnchor/LeftHandVisual", ref fixCount);
        FixSkeletonRootPose("OVRCameraRig/TrackingSpace/RightHandAnchor/RightHandVisual", ref fixCount);

        // ============================================================
        // FIX 5: Add OVRSkeletonRenderer to hand visuals so hands are visible
        // ============================================================
        FixHandVisual("OVRCameraRig/TrackingSpace/LeftHandAnchor/LeftHandVisual", ref fixCount);
        FixHandVisual("OVRCameraRig/TrackingSpace/RightHandAnchor/RightHandVisual", ref fixCount);

        // ============================================================
        // FIX 5: Fix Button targetGraphic references
        // ============================================================
        FixButtonTargetGraphic("SessionBoard/SessionCanvas/StartButton", "SessionBoard/SessionCanvas/StartButton/ButtonBG", ref fixCount);
        FixButtonTargetGraphic("SessionBoard/SessionCanvas/ThemeSection/ThemeToggleBtn", "SessionBoard/SessionCanvas/ThemeSection/ThemeToggleBtn/ThemeBtnBG", ref fixCount);

        // ============================================================
        // FIX 6: Widen HandPokeInteractor thresholds for more reliable poke detection
        // ============================================================
        var pokeInteractors = Object.FindObjectsByType<AGVRSystem.UI.HandPokeInteractor>(FindObjectsSortMode.None);
        foreach (var poke in pokeInteractors)
        {
            var pokeSO = new SerializedObject(poke);

            var radiusProp = pokeSO.FindProperty("_pokeRadius");
            var depthProp = pokeSO.FindProperty("_pokeDepthThreshold");

            bool changed = false;

            if (radiusProp != null && radiusProp.floatValue < 0.025f)
            {
                radiusProp.floatValue = 0.035f;
                changed = true;
            }

            if (depthProp != null && depthProp.floatValue < 0.015f)
            {
                depthProp.floatValue = 0.02f;
                changed = true;
            }

            if (changed)
            {
                pokeSO.ApplyModifiedProperties();
                EditorUtility.SetDirty(poke);
                Debug.Log($"[Fix 6] Widened poke thresholds on {poke.gameObject.name} (radius=0.035, depth=0.02)");
                fixCount++;
            }
        }

        // ============================================================
        // FIX 7: Wire UIAudioFeedback._audioSource
        // ============================================================
        var uiAudio = Object.FindFirstObjectByType<AGVRSystem.Audio.UIAudioFeedback>();
        if (uiAudio != null)
        {
            var uiAudioSO = new SerializedObject(uiAudio);
            var audioSourceProp = uiAudioSO.FindProperty("_audioSource");

            if (audioSourceProp != null && audioSourceProp.objectReferenceValue == null)
            {
                AudioSource existingSource = uiAudio.GetComponent<AudioSource>();
                if (existingSource == null)
                {
                    existingSource = uiAudio.gameObject.AddComponent<AudioSource>();
                    existingSource.playOnAwake = false;
                    existingSource.spatialBlend = 0f;
                    existingSource.priority = 64;
                    Debug.Log("[Fix 7] Created dedicated AudioSource on AudioSystem for UIAudioFeedback");
                }

                audioSourceProp.objectReferenceValue = existingSource;
                uiAudioSO.ApplyModifiedProperties();
                EditorUtility.SetDirty(uiAudio);
                Debug.Log("[Fix 7] Wired UIAudioFeedback._audioSource");
                fixCount++;
            }
            else
            {
                Debug.Log("[Fix 7] UIAudioFeedback._audioSource already assigned — skipped");
            }
        }
        else
        {
            Debug.LogWarning("[Fix 7] UIAudioFeedback not found in scene");
        }

        // ============================================================
        // FIX 8: Ensure TTS agent reference is correct on TTSVoiceGuide
        // ============================================================
        var ttsGuide = Object.FindFirstObjectByType<AGVRSystem.Audio.TTSVoiceGuide>();
        if (ttsGuide != null)
        {
            var ttsSO = new SerializedObject(ttsGuide);
            var agentProp = ttsSO.FindProperty("_ttsAgentComponent");
            if (agentProp != null && agentProp.objectReferenceValue == null)
            {
                var ttsGO = GameObject.Find("[BuildingBlock] Text To Speech");
                if (ttsGO != null)
                {
                    var monos = ttsGO.GetComponents<MonoBehaviour>();
                    foreach (var mono in monos)
                    {
                        if (mono != null && mono.GetType().Name.Contains("TextToSpeech"))
                        {
                            agentProp.objectReferenceValue = mono;
                            ttsSO.ApplyModifiedProperties();
                            EditorUtility.SetDirty(ttsGuide);
                            Debug.Log($"[Fix 8] Wired TTSVoiceGuide._ttsAgentComponent to {mono.GetType().Name}");
                            fixCount++;
                            break;
                        }
                    }
                }
            }
            else
            {
                Debug.Log("[Fix 8] TTSVoiceGuide._ttsAgentComponent already assigned — skipped");
            }
        }

        // ============================================================
        // FIX 9: Ensure SessionCanvas worldCamera is assigned
        // ============================================================
        FixCanvasWorldCamera("SessionBoard/SessionCanvas", ref fixCount);
        FixCanvasWorldCamera("AboutBoard/AboutCanvas", ref fixCount);

        // ============================================================
        // FIX 10: Enable all fingers on HandPokeInteractor instances
        // The scene YAML has _enableMiddlePoke:0, _enableRingPoke:0,
        // _enablePinkyPoke:0 baked in. This writes all four to true
        // and saves the scene so serialization can never block a finger again.
        // ============================================================
        FixPokeInteractorFingers("OVRCameraRig/TrackingSpace/LeftHandAnchor/LeftHandVisual", ref fixCount);
        FixPokeInteractorFingers("OVRCameraRig/TrackingSpace/RightHandAnchor/RightHandVisual", ref fixCount);

        // ============================================================
        // FIX 11: Disable OVRSkeletonRenderer (green skeleton lines) on hand visuals
        // ============================================================
        DisableSkeletonRenderer("OVRCameraRig/TrackingSpace/LeftHandAnchor/LeftHandVisual", ref fixCount);
        DisableSkeletonRenderer("OVRCameraRig/TrackingSpace/RightHandAnchor/RightHandVisual", ref fixCount);

        // ============================================================
        // Save
        // ============================================================
        if (fixCount > 0)
        {
            EditorSceneManager.MarkSceneDirty(activeScene);
            EditorSceneManager.SaveScene(activeScene);
            Debug.Log($"[MainMenuSceneFixer] Applied {fixCount} fixes and saved scene.");
        }
        else
        {
            Debug.Log("[MainMenuSceneFixer] No fixes needed — everything looks correct.");
        }
    }

    /// <summary>
    /// Writes all four finger-enable booleans to true on HandPokeInteractor.
    /// Fixes the baked YAML values that override script defaults at runtime.
    /// </summary>
    private static void FixPokeInteractorFingers(string path, ref int fixCount)
    {
        var go = GameObject.Find(path);
        if (go == null)
        {
            Debug.LogWarning($"[Fix 10] GameObject not found: {path}");
            return;
        }

        var poke = go.GetComponent<AGVRSystem.UI.HandPokeInteractor>();
        if (poke == null)
        {
            Debug.LogWarning($"[Fix 10] HandPokeInteractor not found on: {path}");
            return;
        }

        var so = new SerializedObject(poke);
        string[] fingerProps = { "_enableIndexPoke", "_enableMiddlePoke", "_enableRingPoke", "_enablePinkyPoke" };
        bool changed = false;

        foreach (string propName in fingerProps)
        {
            var prop = so.FindProperty(propName);
            if (prop != null && !prop.boolValue)
            {
                prop.boolValue = true;
                changed = true;
                Debug.Log($"[Fix 10] Enabled {propName} on {go.name}");
            }
        }

        if (changed)
        {
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(poke);
            fixCount++;
        }
        else
        {
            Debug.Log($"[Fix 10] All finger enables already true on {go.name} — skipped");
        }
    }

    /// <summary>
    /// Disables OVRSkeletonRenderer on a hand visual to remove green skeleton lines.
    /// </summary>
    private static void DisableSkeletonRenderer(string path, ref int fixCount)
    {
        var go = GameObject.Find(path);
        if (go == null) return;

        var skelRenderer = go.GetComponent("OVRSkeletonRenderer") as Behaviour;
        if (skelRenderer == null) return;

        if (skelRenderer.enabled)
        {
            skelRenderer.enabled = false;
            EditorUtility.SetDirty(skelRenderer);
            Debug.Log($"[Fix 11] Disabled OVRSkeletonRenderer on {go.name}");
            fixCount++;
        }
        else
        {
            Debug.Log($"[Fix 11] OVRSkeletonRenderer already disabled on {go.name} — skipped");
        }
    }

    private static void RepositionBoard(string goName, Vector3 targetPosition, ref int fixCount)
    {
        var go = GameObject.Find(goName);
        if (go == null)
        {
            Debug.LogWarning($"[Fix 3] Could not find '{goName}' in scene — skipped");
            return;
        }

        if (go.transform.position == targetPosition)
        {
            Debug.Log($"[Fix 3] '{goName}' already at {targetPosition} — skipped");
            return;
        }

        Undo.RecordObject(go.transform, $"Reposition {goName}");
        go.transform.position = targetPosition;
        EditorUtility.SetDirty(go);
        Debug.Log($"[Fix 3] Repositioned '{goName}' to {targetPosition}");
        fixCount++;
    }

    /// <summary>
    /// Fixes the OVRSkeleton._updateRootPose double-positioning issue.
    /// When a hand visual is a child of a HandAnchor, both OVRCameraRig and
    /// OVRSkeleton try to position it at the tracked hand location, causing
    /// a 2x offset. Disabling _updateRootPose lets only the HandAnchor
    /// control position while OVRSkeleton handles bone rotations/translations.
    /// </summary>
    private static void FixSkeletonRootPose(string path, ref int fixCount)
    {
        var go = GameObject.Find(path);
        if (go == null)
        {
            Debug.LogWarning($"[Fix 4] Could not find {path}");
            return;
        }

        var ovrSkeleton = go.GetComponent("OVRSkeleton") as Component;
        if (ovrSkeleton == null)
        {
            Debug.LogWarning($"[Fix 4] No OVRSkeleton on {go.name}");
            return;
        }

        var so = new SerializedObject(ovrSkeleton);
        var rootPoseProp = so.FindProperty("_updateRootPose");
        if (rootPoseProp != null && rootPoseProp.boolValue)
        {
            rootPoseProp.boolValue = false;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(ovrSkeleton);
            Debug.Log($"[Fix 4] Disabled _updateRootPose on {go.name} OVRSkeleton (prevents double-positioning)");
            fixCount++;
        }
        else
        {
            Debug.Log($"[Fix 4] {go.name} _updateRootPose already disabled — skipped");
        }
    }

    private static void FixHandVisual(string path, ref int fixCount)
    {
        var go = GameObject.Find(path);
        if (go == null)
        {
            Debug.LogWarning($"[Fix 4] Could not find {path}");
            return;
        }

        // ---- Strategy: Use OVRMeshRenderer for hand mesh (skin appearance) ----
        // OVRMeshRenderer renders the actual hand mesh from OVR runtime data.
        // OVRSkeletonRenderer only renders wireframe (bones/capsules) — disable it
        // so only the hand mesh is visible, not the skeleton.

        Material handMat = FindHandMaterial();

        // Ensure OVRMesh exists (provides mesh data to OVRMeshRenderer)
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

                // Set _meshType: HandLeft=4, HandRight=5
                var so = new SerializedObject(ovrMesh);
                var meshTypeProp = so.FindProperty("_meshType");
                if (meshTypeProp != null)
                {
                    meshTypeProp.enumValueIndex = isLeft ? 4 : 5;
                    so.ApplyModifiedProperties();
                }

                EditorUtility.SetDirty(go);
                Debug.Log($"[Fix 4] Added OVRMesh to {go.name}");
                fixCount++;
            }
        }

        // Ensure OVRMeshRenderer exists (renders the hand mesh using OVRMesh + OVRSkeleton)
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

                // Wire _ovrMesh reference
                if (ovrMesh != null)
                {
                    var ovrMeshProp = so.FindProperty("_ovrMesh");
                    if (ovrMeshProp != null)
                        ovrMeshProp.objectReferenceValue = ovrMesh;
                }

                // Wire _ovrSkeleton reference
                var skeleton = go.GetComponent("OVRSkeleton") as Component;
                if (skeleton != null)
                {
                    var skelProp = so.FindProperty("_ovrSkeleton");
                    if (skelProp != null)
                        skelProp.objectReferenceValue = skeleton;
                }

                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(go);
                Debug.Log($"[Fix 4] Added OVRMeshRenderer to {go.name}");
                fixCount++;
            }
        }

        // Ensure SkinnedMeshRenderer exists with a valid material
        var smr = go.GetComponent<SkinnedMeshRenderer>();
        if (smr == null)
        {
            smr = go.AddComponent<SkinnedMeshRenderer>();
            EditorUtility.SetDirty(go);
            Debug.Log($"[Fix 4] Added SkinnedMeshRenderer to {go.name}");
            fixCount++;
        }

        if (handMat != null && smr.sharedMaterial == null)
        {
            smr.sharedMaterial = handMat;
            EditorUtility.SetDirty(smr);
            Debug.Log($"[Fix 4] Assigned hand material to {go.name} SkinnedMeshRenderer");
            fixCount++;
        }

        // Disable OVRSkeletonRenderer — hide skeleton wireframe, show hand mesh only
        var skelRenderer = go.GetComponent("OVRSkeletonRenderer") as Behaviour;
        if (skelRenderer != null && skelRenderer.enabled)
        {
            skelRenderer.enabled = false;
            EditorUtility.SetDirty(skelRenderer);
            Debug.Log($"[Fix 4] Disabled OVRSkeletonRenderer on {go.name} (hand mesh visible instead)");
            fixCount++;
        }
    }

    /// <summary>
    /// Searches for the best hand material in the project.
    /// Priority: HandMaterial.mat > HandGhost.mat > HandsDefaultMaterial.mat
    /// </summary>
    private static Material FindHandMaterial()
    {
        string[] searchTerms = { "HandMaterial", "HandGhost", "HandsDefaultMaterial" };
        foreach (string term in searchTerms)
        {
            string[] guids = AssetDatabase.FindAssets($"{term} t:Material");
            foreach (string guid in guids)
            {
                string matPath = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat != null)
                    return mat;
            }
        }
        return null;
    }

    private static void FixButtonTargetGraphic(string buttonPath, string graphicPath, ref int fixCount)
    {
        var buttonGO = GameObject.Find(buttonPath);
        var graphicGO = GameObject.Find(graphicPath);

        if (buttonGO == null)
        {
            Debug.LogWarning($"[Fix 3] Could not find button at {buttonPath}");
            return;
        }

        var button = buttonGO.GetComponent<Button>();
        if (button == null)
        {
            Debug.LogWarning($"[Fix 3] No Button component on {buttonPath}");
            return;
        }

        if (button.targetGraphic != null)
        {
            Debug.Log($"[Fix 3] {buttonGO.name} targetGraphic already assigned — skipped");
            return;
        }

        Graphic graphic = null;

        // First try the specified graphic child
        if (graphicGO != null)
        {
            graphic = graphicGO.GetComponent<Graphic>();
        }

        // Fallback: look for any Image on the button itself or children
        if (graphic == null)
        {
            graphic = buttonGO.GetComponentInChildren<Graphic>();
        }

        if (graphic != null)
        {
            // Ensure the graphic has raycastTarget enabled
            graphic.raycastTarget = true;

            var buttonSO = new SerializedObject(button);
            var targetProp = buttonSO.FindProperty("m_TargetGraphic");
            if (targetProp != null)
            {
                targetProp.objectReferenceValue = graphic;
                buttonSO.ApplyModifiedProperties();
                EditorUtility.SetDirty(button);
                Debug.Log($"[Fix 3] Set {buttonGO.name}.targetGraphic to {graphic.gameObject.name}");
                fixCount++;
            }
        }
        else
        {
            Debug.LogWarning($"[Fix 3] No Graphic found for button {buttonPath}");
        }
    }

    private static void FixCanvasWorldCamera(string canvasPath, ref int fixCount)
    {
        var canvasGO = GameObject.Find(canvasPath);
        if (canvasGO == null) return;

        var canvas = canvasGO.GetComponent<Canvas>();
        if (canvas == null || canvas.renderMode != RenderMode.WorldSpace) return;

        if (canvas.worldCamera != null) 
        {
            Debug.Log($"[Fix 7] {canvasGO.name} worldCamera already assigned — skipped");
            return;
        }

        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            canvas.worldCamera = mainCam;
            EditorUtility.SetDirty(canvas);
            Debug.Log($"[Fix 7] Assigned worldCamera to {canvasGO.name}");
            fixCount++;
        }
    }
}
