using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>
/// Fixes all issues in the Calibration scene:
/// 1. OVRManager tracking origin → Floor Level (hand position mismatch)
/// 2. Missing OVRMeshRenderer / SkinnedMeshRenderer on hand visuals (hands invisible)
/// 3. Missing EventSystem (no UI interaction possible)
/// 4. Missing AudioSystem with UIAudioFeedback (no audio feedback / countdown beeps)
/// 5. World-space canvases missing worldCamera (UI raycasting broken)
/// 6. HandTrackingManager on child object with DontDestroyOnLoad (destroys parent Managers)
/// </summary>
public static class FixCalibrationScene
{
    public static void Execute()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.name.Contains("Calibration"))
        {
            Debug.LogError("[FixCalibrationScene] Active scene is not Calibration.");
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
                trackingProp.enumValueIndex = 1; // FloorLevel
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

        // ============================================================
        // FIX 2: Add SkinnedMeshRenderer to hand visuals if missing
        // (OVRMeshRenderer already exists but needs SkinnedMeshRenderer)
        // ============================================================
        FixHandVisualRenderer("OVRCameraRig/TrackingSpace/LeftHandAnchor/LeftHandVisual", ref fixCount);
        FixHandVisualRenderer("OVRCameraRig/TrackingSpace/RightHandAnchor/RightHandVisual", ref fixCount);

        // ============================================================
        // FIX 2b: Fix OVRSkeleton._updateRootPose double-positioning
        // ============================================================
        FixSkeletonRootPose("OVRCameraRig/TrackingSpace/LeftHandAnchor/LeftHandVisual", ref fixCount);
        FixSkeletonRootPose("OVRCameraRig/TrackingSpace/RightHandAnchor/RightHandVisual", ref fixCount);

        // ============================================================
        // FIX 3: Add EventSystem (missing entirely — needed for any UI interaction)
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
        else
        {
            Debug.Log("[Fix 3] EventSystem already exists — skipped");
        }

        // ============================================================
        // FIX 4: Add AudioSystem with UIAudioFeedback + SpatialAudioController
        // (CalibrationUI.cs references UIAudioFeedback.Instance for countdown beeps,
        //  and TTSVoiceGuide.Instance for voice guidance — both need AudioSources)
        // ============================================================
        var existingUIAudio = Object.FindFirstObjectByType<AGVRSystem.Audio.UIAudioFeedback>();
        if (existingUIAudio == null)
        {
            var audioGO = new GameObject("AudioSystem");

            // UIAudioFeedback with its own AudioSource
            var uiAudio = audioGO.AddComponent<AGVRSystem.Audio.UIAudioFeedback>();
            var uiAudioSource = audioGO.AddComponent<AudioSource>();
            uiAudioSource.playOnAwake = false;
            uiAudioSource.spatialBlend = 0f;
            uiAudioSource.priority = 64;

            // Wire the audio source
            var uiAudioSO = new SerializedObject(uiAudio);
            var audioSourceProp = uiAudioSO.FindProperty("_audioSource");
            if (audioSourceProp != null)
            {
                audioSourceProp.objectReferenceValue = uiAudioSource;
                uiAudioSO.ApplyModifiedProperties();
            }

            // Create VoiceSource child
            var voiceGO = new GameObject("VoiceSource");
            voiceGO.transform.SetParent(audioGO.transform, false);
            var voiceSource = voiceGO.AddComponent<AudioSource>();
            voiceSource.playOnAwake = false;
            voiceSource.spatialBlend = 1f;
            voiceSource.rolloffMode = AudioRolloffMode.Linear;
            voiceSource.minDistance = 0.3f;
            voiceSource.maxDistance = 3f;
            voiceSource.priority = 64;

            // Create AmbientSource child
            var ambientGO = new GameObject("AmbientSource");
            ambientGO.transform.SetParent(audioGO.transform, false);
            var ambientSource = ambientGO.AddComponent<AudioSource>();
            ambientSource.playOnAwake = false;
            ambientSource.spatialBlend = 0f;
            ambientSource.loop = true;
            ambientSource.priority = 200;
            ambientSource.volume = 0.08f;

            // Add SpatialAudioController
            var spatialAudio = audioGO.AddComponent<AGVRSystem.Audio.SpatialAudioController>();
            var spatialSO = new SerializedObject(spatialAudio);

            var voiceSourceProp = spatialSO.FindProperty("_voiceSource");
            if (voiceSourceProp != null) voiceSourceProp.objectReferenceValue = voiceSource;

            var ambientSourceProp = spatialSO.FindProperty("_ambientSource");
            if (ambientSourceProp != null) ambientSourceProp.objectReferenceValue = ambientSource;

            var cameraProp = spatialSO.FindProperty("_cameraTransform");
            var centerEye = GameObject.Find("OVRCameraRig/TrackingSpace/CenterEyeAnchor");
            if (cameraProp != null && centerEye != null)
                cameraProp.objectReferenceValue = centerEye.transform;

            spatialSO.ApplyModifiedProperties();

            // Add TTSVoiceGuide
            var ttsGuide = audioGO.AddComponent<AGVRSystem.Audio.TTSVoiceGuide>();
            var ttsSO = new SerializedObject(ttsGuide);

            var ttsAgentProp = ttsSO.FindProperty("_ttsAgentComponent");
            if (ttsAgentProp != null)
            {
                var ttsGO = GameObject.Find("[BuildingBlock] Text To Speech");
                if (ttsGO != null)
                {
                    var monos = ttsGO.GetComponents<MonoBehaviour>();
                    foreach (var mono in monos)
                    {
                        if (mono != null && mono.GetType().Name.Contains("TextToSpeech"))
                        {
                            ttsAgentProp.objectReferenceValue = mono;
                            break;
                        }
                    }
                }
            }
            ttsSO.ApplyModifiedProperties();

            // Add AudioGuideManager
            var audioGuide = audioGO.AddComponent<AGVRSystem.Audio.AudioGuideManager>();
            var audioGuideSO = new SerializedObject(audioGuide);
            var guideVoiceProp = audioGuideSO.FindProperty("_voiceSource");
            if (guideVoiceProp != null) guideVoiceProp.objectReferenceValue = voiceSource;
            audioGuideSO.ApplyModifiedProperties();

            EditorUtility.SetDirty(audioGO);
            Debug.Log("[Fix 4] Created AudioSystem with UIAudioFeedback, SpatialAudioController, TTSVoiceGuide, AudioGuideManager");
            fixCount++;
        }
        else
        {
            Debug.Log("[Fix 4] UIAudioFeedback already exists — skipped");
        }

        // ============================================================
        // FIX 5: Assign worldCamera to world-space canvases
        // (CalibCanvas and InstrCanvas both have camera: "None")
        // ============================================================
        Camera mainCam = Camera.main;
        FixCanvasCamera("CalibrationUI/CalibCanvas", mainCam, ref fixCount);
        FixCanvasCamera("CalibInstructionBoard/InstrCanvas", mainCam, ref fixCount);

        // ============================================================
        // FIX 6: Move HandTrackingManager from child to Managers root
        // HandTrackingManager uses DontDestroyOnLoad(gameObject) in Awake.
        // It's on "Managers/HandTrackingManager" (a child GO).
        // DontDestroyOnLoad only works on root GameObjects.
        // When called on a child, Unity throws a warning and it does NOT persist.
        // The HandTrackingManager singleton will be destroyed on scene transition.
        // Fix: Move the component to the Managers root object (which is a root GO).
        // ============================================================
        var managersGO = GameObject.Find("Managers");
        var htmChildGO = GameObject.Find("Managers/HandTrackingManager");

        if (managersGO != null && htmChildGO != null)
        {
            var htmChild = htmChildGO.GetComponent<AGVRSystem.HandTrackingManager>();
            if (htmChild != null)
            {
                // Check if Managers root already has HandTrackingManager
                var htmRoot = managersGO.GetComponent<AGVRSystem.HandTrackingManager>();
                if (htmRoot == null)
                {
                    // Add to root and copy references
                    htmRoot = managersGO.AddComponent<AGVRSystem.HandTrackingManager>();

                    var srcSO = new SerializedObject(htmChild);
                    var dstSO = new SerializedObject(htmRoot);

                    CopyProperty(srcSO, dstSO, "_leftHand");
                    CopyProperty(srcSO, dstSO, "_rightHand");
                    CopyProperty(srcSO, dstSO, "_leftSkeleton");
                    CopyProperty(srcSO, dstSO, "_rightSkeleton");

                    dstSO.ApplyModifiedProperties();
                    EditorUtility.SetDirty(managersGO);

                    // Remove the child component
                    Object.DestroyImmediate(htmChild);

                    // If the child GO is now empty (only Transform), remove it
                    if (htmChildGO.GetComponents<Component>().Length <= 1) // Only Transform
                    {
                        Object.DestroyImmediate(htmChildGO);
                        Debug.Log("[Fix 6] Moved HandTrackingManager to Managers root and removed empty child GO");
                    }
                    else
                    {
                        Debug.Log("[Fix 6] Moved HandTrackingManager to Managers root (child GO kept — has other components)");
                    }
                    fixCount++;
                }
                else
                {
                    Debug.Log("[Fix 6] HandTrackingManager already on Managers root — skipped");
                }
            }
        }

        // ============================================================
        // FIX 7: Add GraphicRaycaster to canvases if missing
        // (Needed for any UI raycasting to work)
        // ============================================================
        FixGraphicRaycaster("CalibrationUI/CalibCanvas", ref fixCount);
        FixGraphicRaycaster("CalibInstructionBoard/InstrCanvas", ref fixCount);

        // ============================================================
        // Save
        // ============================================================
        if (fixCount > 0)
        {
            EditorSceneManager.MarkSceneDirty(activeScene);
            EditorSceneManager.SaveScene(activeScene);
            Debug.Log($"[FixCalibrationScene] Applied {fixCount} fixes and saved scene.");
        }
        else
        {
            Debug.Log("[FixCalibrationScene] No fixes needed.");
        }
    }

    private static void FixHandVisualRenderer(string path, ref int fixCount)
    {
        var go = GameObject.Find(path);
        if (go == null) return;

        var smr = go.GetComponent<SkinnedMeshRenderer>();
        if (smr == null)
        {
            smr = go.AddComponent<SkinnedMeshRenderer>();
            EditorUtility.SetDirty(go);
            Debug.Log($"[Fix 2] Added SkinnedMeshRenderer to {go.name}");
            fixCount++;
        }
        else
        {
            Debug.Log($"[Fix 2] {go.name} already has SkinnedMeshRenderer — skipped");
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

    private static void FixCanvasCamera(string canvasPath, Camera cam, ref int fixCount)
    {
        var canvasGO = GameObject.Find(canvasPath);
        if (canvasGO == null) return;

        var canvas = canvasGO.GetComponent<Canvas>();
        if (canvas == null || canvas.renderMode != RenderMode.WorldSpace) return;

        if (canvas.worldCamera == null && cam != null)
        {
            canvas.worldCamera = cam;
            EditorUtility.SetDirty(canvas);
            Debug.Log($"[Fix 5] Assigned worldCamera to {canvasGO.name}");
            fixCount++;
        }
        else
        {
            Debug.Log($"[Fix 5] {canvasGO.name} worldCamera already assigned — skipped");
        }
    }

    private static void FixGraphicRaycaster(string canvasPath, ref int fixCount)
    {
        var canvasGO = GameObject.Find(canvasPath);
        if (canvasGO == null) return;

        var raycaster = canvasGO.GetComponent<GraphicRaycaster>();
        if (raycaster == null)
        {
            canvasGO.AddComponent<GraphicRaycaster>();
            EditorUtility.SetDirty(canvasGO);
            Debug.Log($"[Fix 7] Added GraphicRaycaster to {canvasGO.name}");
            fixCount++;
        }
    }

    private static void CopyProperty(SerializedObject src, SerializedObject dst, string propName)
    {
        var srcProp = src.FindProperty(propName);
        var dstProp = dst.FindProperty(propName);
        if (srcProp != null && dstProp != null)
        {
            dstProp.objectReferenceValue = srcProp.objectReferenceValue;
        }
    }
}
