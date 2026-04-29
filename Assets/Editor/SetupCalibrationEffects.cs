using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Wires up CalibrationEffects, adds CanvasGroups, particles, and accent lights
/// to the Calibration scene.
/// </summary>
public static class SetupCalibrationEffects
{
    public static void Execute()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.name.Contains("Calibration"))
        {
            Debug.LogError("[SetupCalibEffects] Active scene is not Calibration.");
            return;
        }

        int changes = 0;

        // 1. Add CanvasGroups
        var calibCanvas = GameObject.Find("CalibrationUI/CalibCanvas");
        var instrCanvas = GameObject.Find("CalibInstructionBoard/InstrCanvas");

        CanvasGroup calibCG = EnsureComponent<CanvasGroup>(calibCanvas, ref changes);
        CanvasGroup instrCG = EnsureComponent<CanvasGroup>(instrCanvas, ref changes);

        // 2. Add CalibrationEffects to Managers
        var managersGO = GameObject.Find("Managers");
        if (managersGO == null) { Debug.LogError("[Setup] Managers not found."); return; }

        var effects = managersGO.GetComponent<AGVRSystem.UI.CalibrationEffects>();
        if (effects == null)
        {
            effects = managersGO.AddComponent<AGVRSystem.UI.CalibrationEffects>();
            Debug.Log("[Setup] Added CalibrationEffects to Managers");
            changes++;
        }

        var so = new SerializedObject(effects);

        // Boards
        SetRef(so, "_calibrationBoard", GameObject.Find("CalibrationUI")?.transform);
        SetRef(so, "_instructionBoard", GameObject.Find("CalibInstructionBoard")?.transform);

        // Canvas groups
        SetRef(so, "_calibCanvasGroup", calibCG);
        SetRef(so, "_instrCanvasGroup", instrCG);

        // Corner brackets
        SetRef(so, "_cornerTL", GameObject.Find("CalibrationUI/CalibCanvas/CornerTL")?.GetComponent<TMP_Text>());
        SetRef(so, "_cornerTR", GameObject.Find("CalibrationUI/CalibCanvas/CornerTR")?.GetComponent<TMP_Text>());
        SetRef(so, "_cornerBL", GameObject.Find("CalibrationUI/CalibCanvas/CornerBL")?.GetComponent<TMP_Text>());
        SetRef(so, "_cornerBR", GameObject.Find("CalibrationUI/CalibCanvas/CornerBR")?.GetComponent<TMP_Text>());

        // Render areas
        SetRef(so, "_leftRenderArea", GameObject.Find("CalibrationUI/CalibCanvas/HandSections/LeftHandSection/LeftRenderArea")?.GetComponent<RectTransform>());
        SetRef(so, "_rightRenderArea", GameObject.Find("CalibrationUI/CalibCanvas/HandSections/RightHandSection/RightRenderArea")?.GetComponent<RectTransform>());

        // Accent elements
        var accentLine = GameObject.Find("CalibInstructionBoard/InstrCanvas/AccentLine");
        if (accentLine != null) SetRef(so, "_instrAccentLine", accentLine.GetComponent<Graphic>());

        var divider = GameObject.Find("CalibrationUI/CalibCanvas/HandSections/CenterDivider");
        if (divider != null) SetRef(so, "_centerDivider", divider.GetComponent<Graphic>());

        // Light
        var dirLight = GameObject.Find("Directional Light");
        if (dirLight != null) SetRef(so, "_directionalLight", dirLight.GetComponent<Light>());

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(effects);

        // 3. Add ambient particles (reuse MainMenuParticles)
        var existingParticles = Object.FindFirstObjectByType<AGVRSystem.UI.MainMenuParticles>();
        if (existingParticles == null)
        {
            var cameraRig = GameObject.Find("OVRCameraRig");
            Vector3 spawnPos = cameraRig != null ? cameraRig.transform.position : Vector3.zero;

            var particlesGO = new GameObject("AmbientEffects");
            particlesGO.transform.position = spawnPos;

            var particles = particlesGO.AddComponent<AGVRSystem.UI.MainMenuParticles>();
            var pso = new SerializedObject(particles);

            if (cameraRig != null) SetRef(pso, "_centerPoint", cameraRig.transform);

            // Fewer particles for calibration — keep it subtle
            var fireflyCount = pso.FindProperty("_fireflyCount");
            if (fireflyCount != null) fireflyCount.intValue = 15;

            var pollenCount = pso.FindProperty("_pollenCount");
            if (pollenCount != null) pollenCount.intValue = 25;

            var fireflyRadius = pso.FindProperty("_fireflyRadius");
            if (fireflyRadius != null) fireflyRadius.floatValue = 4f;

            pso.ApplyModifiedProperties();
            EditorUtility.SetDirty(particlesGO);
            Debug.Log("[Setup] Created AmbientEffects with reduced particles");
            changes++;
        }

        // 4. Add calibration glow light near the boards
        if (GameObject.Find("CalibGlowLight") == null)
        {
            var glowGO = new GameObject("CalibGlowLight");
            glowGO.transform.position = new Vector3(-2.0f, 1.4f, -2.8f);

            var light = glowGO.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.3f, 0.85f, 0.45f, 1f);
            light.intensity = 0.35f;
            light.range = 3f;
            light.shadows = LightShadows.None;

            EditorUtility.SetDirty(glowGO);
            Debug.Log("[Setup] Created CalibGlowLight");
            changes++;
        }

        // 5. Add warm ambient light
        if (GameObject.Find("CalibWarmLight") == null)
        {
            var warmGO = new GameObject("CalibWarmLight");
            warmGO.transform.position = new Vector3(-1.4f, 2.0f, -2.5f);

            var light = warmGO.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.88f, 0.6f, 1f);
            light.intensity = 0.25f;
            light.range = 4f;
            light.shadows = LightShadows.None;

            EditorUtility.SetDirty(warmGO);
            Debug.Log("[Setup] Created CalibWarmLight");
            changes++;
        }

        // Save
        if (changes > 0)
        {
            EditorSceneManager.MarkSceneDirty(activeScene);
            EditorSceneManager.SaveScene(activeScene);
            Debug.Log($"[SetupCalibEffects] Applied {changes} changes and saved.");
        }
        else
        {
            Debug.Log("[SetupCalibEffects] No changes needed.");
        }
    }

    private static T EnsureComponent<T>(GameObject go, ref int changes) where T : Component
    {
        if (go == null) return null;
        var comp = go.GetComponent<T>();
        if (comp == null)
        {
            comp = go.AddComponent<T>();
            EditorUtility.SetDirty(go);
            Debug.Log($"[Setup] Added {typeof(T).Name} to {go.name}");
            changes++;
        }
        return comp;
    }

    private static void SetRef(SerializedObject so, string prop, Object value)
    {
        var p = so.FindProperty(prop);
        if (p != null && value != null) p.objectReferenceValue = value;
    }
}
