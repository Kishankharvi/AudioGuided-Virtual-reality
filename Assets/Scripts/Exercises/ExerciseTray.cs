using System.Collections;
using UnityEngine;

namespace AGVRSystem.Exercises
{
    /// <summary>
    /// Builds a shallow wooden-style tray and arranges all child
    /// ExerciseObjectController objects evenly across its floor.
    ///
    /// Usage — add this component to the parent GameObject that groups the exercise
    /// objects in the RehabSession scene. The tray geometry is created at runtime
    /// so no additional assets or prefabs are required.
    /// </summary>
    public class ExerciseTray : MonoBehaviour
    {
        [Header("Tray Size")]
        [SerializeField] private float _width        = 0.55f;
        [SerializeField] private float _depth        = 0.28f;
        [SerializeField] private float _wallHeight   = 0.045f;
        [SerializeField] private float _wallThick    = 0.010f;

        [Header("Appearance")]
        [SerializeField] private Color _baseColor = new Color(0.28f, 0.22f, 0.15f, 1f);
        [SerializeField] private Color _rimColor  = new Color(0.38f, 0.30f, 0.20f, 1f);

        [Header("Object Layout")]
        [SerializeField] private bool  _autoArrange  = true;
        [SerializeField] private float _objectHeight = 0.030f; // local Y offset above tray floor
        [SerializeField] private float _sidePadding  = 0.025f;

        private GameObject _trayRoot;

        private void Start()
        {
            BuildTray();

            if (_autoArrange)
                StartCoroutine(ArrangeObjects());
        }

        // ─── Tray geometry ────────────────────────────────────────────

        private void BuildTray()
        {
            _trayRoot = new GameObject("TrayGeometry");
            _trayRoot.transform.SetParent(transform, false);
            _trayRoot.transform.localPosition = Vector3.zero;

            float halfH = _wallHeight * 0.5f;

            // Floor
            Slab("Tray_Floor",
                new Vector3(0f, -halfH + _wallThick * 0.5f, 0f),
                new Vector3(_width, _wallThick, _depth),
                _baseColor);

            // Front wall (negative Z face towards the player)
            Slab("Tray_Front",
                new Vector3(0f, 0f, -_depth * 0.5f + _wallThick * 0.5f),
                new Vector3(_width, _wallHeight, _wallThick),
                _rimColor);

            // Back wall
            Slab("Tray_Back",
                new Vector3(0f, 0f, _depth * 0.5f - _wallThick * 0.5f),
                new Vector3(_width, _wallHeight, _wallThick),
                _rimColor);

            // Left wall
            Slab("Tray_Left",
                new Vector3(-_width * 0.5f + _wallThick * 0.5f, 0f, 0f),
                new Vector3(_wallThick, _wallHeight, _depth),
                _rimColor);

            // Right wall
            Slab("Tray_Right",
                new Vector3(_width * 0.5f - _wallThick * 0.5f, 0f, 0f),
                new Vector3(_wallThick, _wallHeight, _depth),
                _rimColor);
        }

        private void Slab(string slabName, Vector3 localPos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = slabName;
            go.transform.SetParent(_trayRoot.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale    = scale;

            // Visual only — destroy the box collider so exercise objects are unaffected
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var rend = go.GetComponent<Renderer>();
            if (rend != null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Standard");

                var mat = new Material(sh) { color = color };
                rend.material = mat;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.receiveShadows    = false;
            }
        }

        // ─── Object placement ─────────────────────────────────────────

        private IEnumerator ArrangeObjects()
        {
            // Let exercise objects finish their own Awake/Start before we move them
            yield return new WaitForSeconds(0.1f);

            var objects = GetComponentsInChildren<ExerciseObjectController>(includeInactive: true);

            if (objects == null || objects.Length == 0)
            {
                Debug.LogWarning("[ExerciseTray] No ExerciseObjectController children found.");
                yield break;
            }

            int   count      = objects.Length;
            float usable     = _width - _wallThick * 2f - _sidePadding * 2f;
            float spacing    = count > 1 ? usable / (count - 1) : 0f;
            float startX     = count > 1 ? -(usable * 0.5f) : 0f;
            float floorY     = -_wallHeight * 0.5f + _wallThick + _objectHeight;

            for (int i = 0; i < count; i++)
            {
                float x = count == 1 ? 0f : startX + i * spacing;
                objects[i].transform.localPosition = new Vector3(x, floorY, 0f);
            }

            Debug.Log($"[ExerciseTray] Arranged {count} exercise object(s) in tray '{name}'.");
        }

        private void OnDestroy()
        {
            if (_trayRoot != null)
                Destroy(_trayRoot);
        }
    }
}
