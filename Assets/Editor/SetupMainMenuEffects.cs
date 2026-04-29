using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor script to wire up MainMenuEffects and MainMenuParticles components
/// to the MainMenu scene objects, add required CanvasGroup components, and save.
/// </summary>
public static class SetupMainMenuEffects
{
    public static void Execute()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.name.Contains("MainMenu"))
        {
            Debug.LogError("[SetupMainMenuEffects] Active scene is not MainMenu.");
            return;
        }

        int changes = 0;

        // ============================================================
        // 1. Add CanvasGroup to AboutCanvas and SessionCanvas if missing
        // ============================================================
        var aboutCanvas = GameObject.Find("AboutBoard/AboutCanvas");
        var sessionCanvas = GameObject.Find("SessionBoard/SessionCanvas");

        CanvasGroup aboutCG = null;
        CanvasGroup sessionCG = null;

        if (aboutCanvas != null)
        {
            aboutCG = aboutCanvas.GetComponent<CanvasGroup>();
            if (aboutCG == null)
            {
                aboutCG = aboutCanvas.AddComponent<CanvasGroup>();
                EditorUtility.SetDirty(aboutCanvas);
                Debug.Log("[Setup] Added CanvasGroup to AboutCanvas");
                changes++;
            }
        }

        if (sessionCanvas != null)
        {
            sessionCG = sessionCanvas.GetComponent<CanvasGroup>();
            if (sessionCG == null)
            {
                sessionCG = sessionCanvas.AddComponent<CanvasGroup>();
                EditorUtility.SetDirty(sessionCanvas);
                Debug.Log("[Setup] Added CanvasGroup to SessionCanvas");
                changes++;
            }
        }

        // ============================================================
        // 2. Add MainMenuEffects to Managers if missing, wire references
        // ============================================================
        var managersGO = GameObject.Find("Managers");
        if (managersGO == null)
        {
            Debug.LogError("[Setup] Managers GameObject not found.");
            return;
        }

        var effects = managersGO.GetComponent<AGVRSystem.UI.MainMenuEffects>();
        if (effects == null)
        {
            effects = managersGO.AddComponent<AGVRSystem.UI.MainMenuEffects>();
            Debug.Log("[Setup] Added MainMenuEffects to Managers");
            changes++;
        }

        // Wire references via SerializedObject
        var effectsSO = new SerializedObject(effects);

        // Boards
        var aboutBoard = GameObject.Find("AboutBoard");
        var sessionBoard = GameObject.Find("SessionBoard");

        SetObjectRef(effectsSO, "_aboutBoard", aboutBoard?.transform);
        SetObjectRef(effectsSO, "_sessionBoard", sessionBoard?.transform);

        // Canvas groups
        SetObjectRef(effectsSO, "_aboutCanvasGroup", aboutCG);
        SetObjectRef(effectsSO, "_sessionCanvasGroup", sessionCG);

        // Start button BG (RoundedImage which is a Graphic)
        var startButtonBG = GameObject.Find("SessionBoard/SessionCanvas/StartButton/ButtonBG");
        if (startButtonBG != null)
        {
            var graphic = startButtonBG.GetComponent<Graphic>();
            SetObjectRef(effectsSO, "_startButtonBG", graphic);
        }

        // Accent line
        var accentLine = GameObject.Find("AboutBoard/AboutCanvas/AccentLine");
        if (accentLine != null)
        {
            var graphic = accentLine.GetComponent<Graphic>();
            SetObjectRef(effectsSO, "_accentLine", graphic);
        }

        // Directional light
        var dirLight = GameObject.Find("Directional Light");
        if (dirLight != null)
        {
            var light = dirLight.GetComponent<Light>();
            SetObjectRef(effectsSO, "_directionalLight", light);
        }

        effectsSO.ApplyModifiedProperties();
        EditorUtility.SetDirty(effects);

        // ============================================================
        // 3. Create AmbientEffects GameObject with MainMenuParticles
        // ============================================================
        var existingParticles = Object.FindFirstObjectByType<AGVRSystem.UI.MainMenuParticles>();
        if (existingParticles == null)
        {
            // Create near the camera rig position
            var cameraRig = GameObject.Find("OVRCameraRig");
            Vector3 spawnPos = cameraRig != null ? cameraRig.transform.position : Vector3.zero;

            var particlesGO = new GameObject("AmbientEffects");
            particlesGO.transform.position = spawnPos;

            var particles = particlesGO.AddComponent<AGVRSystem.UI.MainMenuParticles>();

            var particlesSO = new SerializedObject(particles);

            // Set center point to camera rig transform
            if (cameraRig != null)
            {
                SetObjectRef(particlesSO, "_centerPoint", cameraRig.transform);
            }

            particlesSO.ApplyModifiedProperties();
            EditorUtility.SetDirty(particlesGO);
            Debug.Log("[Setup] Created AmbientEffects with MainMenuParticles");
            changes++;
        }
        else
        {
            Debug.Log("[Setup] MainMenuParticles already exists — skipped");
        }

        // ============================================================
        // 4. Add a subtle point light near the boards for warm glow
        // ============================================================
        var existingBoardLight = GameObject.Find("BoardGlowLight");
        if (existingBoardLight == null)
        {
            var boardLightGO = new GameObject("BoardGlowLight");
            // Position between the two boards, slightly in front
            boardLightGO.transform.position = new Vector3(-1.9f, 1.5f, -3.0f);

            var pointLight = boardLightGO.AddComponent<Light>();
            pointLight.type = LightType.Point;
            pointLight.color = new Color(0.4f, 0.9f, 0.5f, 1f); // Soft green matching theme
            pointLight.intensity = 0.4f;
            pointLight.range = 3f;
            pointLight.shadows = LightShadows.None;

            EditorUtility.SetDirty(boardLightGO);
            Debug.Log("[Setup] Created BoardGlowLight point light");
            changes++;
        }

        // ============================================================
        // 5. Add a second warm accent light near the garden entrance
        // ============================================================
        var existingGardenLight = GameObject.Find("GardenWarmLight");
        if (existingGardenLight == null)
        {
            var gardenLightGO = new GameObject("GardenWarmLight");
            gardenLightGO.transform.position = new Vector3(-2.3f, 2.5f, -2.0f);

            var gardenLight = gardenLightGO.AddComponent<Light>();
            gardenLight.type = LightType.Point;
            gardenLight.color = new Color(1f, 0.85f, 0.55f, 1f); // Warm amber
            gardenLight.intensity = 0.3f;
            gardenLight.range = 5f;
            gardenLight.shadows = LightShadows.None;

            EditorUtility.SetDirty(gardenLightGO);
            Debug.Log("[Setup] Created GardenWarmLight point light");
            changes++;
        }

        // ============================================================
        // Save
        // ============================================================
        if (changes > 0)
        {
            EditorSceneManager.MarkSceneDirty(activeScene);
            EditorSceneManager.SaveScene(activeScene);
            Debug.Log($"[SetupMainMenuEffects] Applied {changes} changes and saved scene.");
        }
        else
        {
            Debug.Log("[SetupMainMenuEffects] No changes needed.");
        }
    }

    private static void SetObjectRef(SerializedObject so, string propName, Object value)
    {
        var prop = so.FindProperty(propName);
        if (prop != null && value != null)
        {
            prop.objectReferenceValue = value;
        }
        else if (prop == null)
        {
            Debug.LogWarning($"[Setup] Property '{propName}' not found on {so.targetObject.GetType().Name}");
        }
    }
}
