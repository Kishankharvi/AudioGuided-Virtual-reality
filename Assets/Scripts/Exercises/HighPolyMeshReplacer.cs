using UnityEngine;

namespace AGVRSystem.Exercises
{
    /// <summary>
    /// Replaces the default Unity sphere mesh on exercise objects with a
    /// procedurally generated high-poly sphere for a smoother, more realistic look.
    /// Attach to any GameObject with a MeshFilter that uses the default sphere.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    public class HighPolyMeshReplacer : MonoBehaviour
    {
        [Header("Sphere Quality")]
        [Tooltip("Number of longitude segments. Higher = smoother sphere.")]
        [SerializeField] private int _longitudeSegments = 32;

        [Tooltip("Number of latitude segments. Higher = smoother sphere.")]
        [SerializeField] private int _latitudeSegments = 24;

        private const float SphereRadius = 0.5f;

        private void Awake()
        {
            var meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null) return;

            // Only replace if it's the default Unity sphere (768 verts, 2880 tris)
            Mesh currentMesh = meshFilter.sharedMesh;
            bool isDefaultSphere = currentMesh != null
                && currentMesh.name == "Sphere"
                && currentMesh.vertexCount <= 800;

            if (isDefaultSphere)
            {
                meshFilter.mesh = CreateHighPolySphere(_longitudeSegments, _latitudeSegments);
                Debug.Log($"[HighPolyMeshReplacer] Replaced mesh on {name} " +
                    $"({_longitudeSegments}x{_latitudeSegments} segments).");
            }
        }

        /// <summary>
        /// Generates a UV sphere mesh with the specified number of segments.
        /// </summary>
        private static Mesh CreateHighPolySphere(int lonSegments, int latSegments)
        {
            int vertCount = (lonSegments + 1) * (latSegments + 1);
            var vertices = new Vector3[vertCount];
            var normals = new Vector3[vertCount];
            var uvs = new Vector2[vertCount];

            int idx = 0;
            for (int lat = 0; lat <= latSegments; lat++)
            {
                float theta = Mathf.PI * lat / latSegments;
                float sinTheta = Mathf.Sin(theta);
                float cosTheta = Mathf.Cos(theta);

                for (int lon = 0; lon <= lonSegments; lon++)
                {
                    float phi = 2f * Mathf.PI * lon / lonSegments;
                    float sinPhi = Mathf.Sin(phi);
                    float cosPhi = Mathf.Cos(phi);

                    float x = cosPhi * sinTheta;
                    float y = cosTheta;
                    float z = sinPhi * sinTheta;

                    vertices[idx] = new Vector3(x, y, z) * SphereRadius;
                    normals[idx] = new Vector3(x, y, z).normalized;
                    uvs[idx] = new Vector2((float)lon / lonSegments, 1f - (float)lat / latSegments);
                    idx++;
                }
            }

            int triCount = lonSegments * latSegments * 6;
            var triangles = new int[triCount];
            int triIdx = 0;

            for (int lat = 0; lat < latSegments; lat++)
            {
                for (int lon = 0; lon < lonSegments; lon++)
                {
                    int current = lat * (lonSegments + 1) + lon;
                    int next = current + lonSegments + 1;

                    triangles[triIdx++] = current;
                    triangles[triIdx++] = next;
                    triangles[triIdx++] = current + 1;

                    triangles[triIdx++] = current + 1;
                    triangles[triIdx++] = next;
                    triangles[triIdx++] = next + 1;
                }
            }

            var mesh = new Mesh
            {
                name = "HighPolySphere",
                vertices = vertices,
                normals = normals,
                uv = uvs,
                triangles = triangles
            };

            mesh.RecalculateBounds();
            mesh.RecalculateTangents();

            return mesh;
        }
    }
}
