using UnityEngine;

namespace AGVRSystem.UI
{
    /// <summary>
    /// Creates ambient particle effects for the MainMenu garden scene:
    /// - Firefly particles (warm glowing dots floating around)
    /// - Pollen/dust particles (soft white drifting motes)
    /// All particles are created procedurally at runtime — no assets needed.
    /// Attach to a GameObject near the player camera.
    /// </summary>
    public class MainMenuParticles : MonoBehaviour
    {
        [Header("Spawn Center")]
        [SerializeField] private Transform _centerPoint;

        [Header("Fireflies")]
        [SerializeField] private bool _enableFireflies = true;
        [SerializeField] private int _fireflyCount = 30;
        [SerializeField] private float _fireflyRadius = 6f;
        [SerializeField] private float _fireflyMinHeight = 0.3f;
        [SerializeField] private float _fireflyMaxHeight = 3.5f;
        [SerializeField] private Color _fireflyColorA = new Color(0.95f, 0.85f, 0.3f, 1f);
        [SerializeField] private Color _fireflyColorB = new Color(0.4f, 0.95f, 0.5f, 1f);
        [SerializeField] private float _fireflySize = 0.025f;

        [Header("Pollen / Dust")]
        [SerializeField] private bool _enablePollen = true;
        [SerializeField] private int _pollenCount = 50;
        [SerializeField] private float _pollenRadius = 5f;
        [SerializeField] private float _pollenMaxHeight = 4f;
        [SerializeField] private Color _pollenColor = new Color(1f, 1f, 0.9f, 0.35f);
        [SerializeField] private float _pollenSize = 0.012f;

        private ParticleSystem _fireflySystem;
        private ParticleSystem _pollenSystem;

        private void Start()
        {
            if (_centerPoint == null)
                _centerPoint = transform;

            if (_enableFireflies)
                CreateFireflySystem();

            if (_enablePollen)
                CreatePollenSystem();
        }

        private void CreateFireflySystem()
        {
            GameObject go = new GameObject("Fireflies");
            go.transform.SetParent(transform, false);
            go.transform.position = _centerPoint.position + Vector3.up * ((_fireflyMinHeight + _fireflyMaxHeight) * 0.5f);

            _fireflySystem = go.AddComponent<ParticleSystem>();

            // Stop auto-play so we can configure first
            var main = _fireflySystem.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 10f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 12f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
            main.startSize = new ParticleSystem.MinMaxCurve(_fireflySize * 0.6f, _fireflySize);
            main.maxParticles = _fireflyCount;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;

            // Start color gradient between warm yellow and green
            var colorGrad = new ParticleSystem.MinMaxGradient(_fireflyColorA, _fireflyColorB);
            main.startColor = colorGrad;

            // Emission
            var emission = _fireflySystem.emission;
            emission.enabled = true;
            emission.rateOverTime = _fireflyCount / 4f;

            // Shape: sphere around center
            var shape = _fireflySystem.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = _fireflyRadius;

            // Velocity over lifetime: gentle random wandering
            var vel = _fireflySystem.velocityOverLifetime;
            vel.enabled = true;
            vel.x = new ParticleSystem.MinMaxCurve(-0.06f, 0.06f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.02f, 0.04f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.06f, 0.06f);

            // Noise for organic movement
            var noise = _fireflySystem.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(0.08f);
            noise.frequency = 0.5f;
            noise.scrollSpeed = 0.3f;
            noise.octaveCount = 2;

            // Color over lifetime: fade in, glow, fade out
            var col = _fireflySystem.colorOverLifetime;
            col.enabled = true;
            Gradient fadeGrad = new Gradient();
            fadeGrad.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 0.5f),
                    new GradientColorKey(Color.white, 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.15f),
                    new GradientAlphaKey(0.6f, 0.5f),
                    new GradientAlphaKey(1f, 0.75f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
            col.color = new ParticleSystem.MinMaxGradient(fadeGrad);

            // Size over lifetime: gentle pulse
            var sizeOverLife = _fireflySystem.sizeOverLifetime;
            sizeOverLife.enabled = true;
            AnimationCurve sizeCurve = new AnimationCurve();
            sizeCurve.AddKey(0f, 0.5f);
            sizeCurve.AddKey(0.25f, 1f);
            sizeCurve.AddKey(0.5f, 0.7f);
            sizeCurve.AddKey(0.75f, 1f);
            sizeCurve.AddKey(1f, 0.3f);
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            // Renderer: use default particle material with additive blending
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = CreateParticleMaterial(true);

            _fireflySystem.Play();
        }

        private void CreatePollenSystem()
        {
            GameObject go = new GameObject("PollenDust");
            go.transform.SetParent(transform, false);
            go.transform.position = _centerPoint.position + Vector3.up * (_pollenMaxHeight * 0.5f);

            _pollenSystem = go.AddComponent<ParticleSystem>();

            var main = _pollenSystem.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 15f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(8f, 18f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.01f, 0.04f);
            main.startSize = new ParticleSystem.MinMaxCurve(_pollenSize * 0.5f, _pollenSize);
            main.maxParticles = _pollenCount;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.002f; // Very slight upward drift
            main.startColor = _pollenColor;

            // Emission
            var emission = _pollenSystem.emission;
            emission.enabled = true;
            emission.rateOverTime = _pollenCount / 6f;

            // Shape: large box area
            var shape = _pollenSystem.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(_pollenRadius * 2f, _pollenMaxHeight, _pollenRadius * 2f);

            // Velocity: gentle drift
            var vel = _pollenSystem.velocityOverLifetime;
            vel.enabled = true;
            vel.x = new ParticleSystem.MinMaxCurve(-0.02f, 0.02f);
            vel.y = new ParticleSystem.MinMaxCurve(0.005f, 0.02f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.02f, 0.02f);

            // Noise for organic floating
            var noise = _pollenSystem.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(0.04f);
            noise.frequency = 0.3f;
            noise.scrollSpeed = 0.15f;
            noise.octaveCount = 1;

            // Color over lifetime: soft fade in/out
            var col = _pollenSystem.colorOverLifetime;
            col.enabled = true;
            Gradient pollenGrad = new Gradient();
            pollenGrad.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.5f, 0.2f),
                    new GradientAlphaKey(0.5f, 0.8f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
            col.color = new ParticleSystem.MinMaxGradient(pollenGrad);

            // Renderer
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = CreateParticleMaterial(false);

            _pollenSystem.Play();
        }

        /// <summary>
        /// Creates a simple URP-compatible particle material.
        /// Additive for fireflies (glowing), alpha-blended for pollen (soft).
        /// </summary>
        private Material CreateParticleMaterial(bool additive)
        {
            // Try URP particle shaders first
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
                shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");

            Material mat = new Material(shader);

            if (additive)
            {
                // Additive blending for glow effect
                mat.SetFloat("_Surface", 1f); // Transparent
                mat.SetFloat("_Blend", 1f);   // Additive
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                mat.SetColor("_BaseColor", Color.white);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3000;
            }
            else
            {
                // Alpha blended for soft particles
                mat.SetFloat("_Surface", 1f); // Transparent
                mat.SetFloat("_Blend", 0f);   // Alpha
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetColor("_BaseColor", Color.white);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = 3000;
            }

            return mat;
        }

        private void OnDestroy()
        {
            if (_fireflySystem != null)
                Destroy(_fireflySystem.gameObject);
            if (_pollenSystem != null)
                Destroy(_pollenSystem.gameObject);
        }
    }
}
