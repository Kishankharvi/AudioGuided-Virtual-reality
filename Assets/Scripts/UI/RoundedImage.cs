using UnityEngine;
using UnityEngine.UI;

namespace AGVRSystem.UI
{
    /// <summary>
    /// Custom MaskableGraphic that renders a rounded rectangle via mesh generation.
    /// No external sprite needed — fully procedural with configurable corner radius and border.
    /// </summary>
    [AddComponentMenu("UI/Rounded Image")]
    [RequireComponent(typeof(CanvasRenderer))]
    public class RoundedImage : MaskableGraphic
    {
        [SerializeField] private float _cornerRadius = 12f;
        [SerializeField] private int _cornerSegments = 8;
        [SerializeField] private bool _hasBorder;
        [SerializeField] private float _borderWidth = 1f;
        [SerializeField] private Color _borderColor = new Color(0.3f, 0.35f, 0.45f, 1f);

        private const int MinSegments = 2;
        private const int MaxSegments = 32;

        public float CornerRadius
        {
            get => _cornerRadius;
            set { _cornerRadius = value; SetVerticesDirty(); }
        }

        public bool HasBorder
        {
            get => _hasBorder;
            set { _hasBorder = value; SetVerticesDirty(); }
        }

        public float BorderWidth
        {
            get => _borderWidth;
            set { _borderWidth = value; SetVerticesDirty(); }
        }

        public Color BorderColor
        {
            get => _borderColor;
            set { _borderColor = value; SetVerticesDirty(); }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Rect rect = GetPixelAdjustedRect();
            float radius = Mathf.Min(_cornerRadius, rect.width * 0.5f, rect.height * 0.5f);
            int segments = Mathf.Clamp(_cornerSegments, MinSegments, MaxSegments);

            AddRoundedRect(vh, rect, radius, segments, color);

            if (_hasBorder && _borderWidth > 0f)
            {
                AddRoundedRectBorder(vh, rect, radius, segments, _borderColor, _borderWidth);
            }
        }

        private void AddRoundedRect(VertexHelper vh, Rect rect, float radius, int segments, Color col)
        {
            Vector2 center = rect.center;
            int centerIdx = AddVert(vh, center, col);

            int totalVerts = segments * 4;
            int firstOuterIdx = centerIdx + 1;

            // Generate corner vertices (4 corners, each with 'segments' verts)
            Vector2[] corners = new Vector2[]
            {
                new Vector2(rect.xMax - radius, rect.yMax - radius), // top-right
                new Vector2(rect.xMin + radius, rect.yMax - radius), // top-left
                new Vector2(rect.xMin + radius, rect.yMin + radius), // bottom-left
                new Vector2(rect.xMax - radius, rect.yMin + radius), // bottom-right
            };

            for (int c = 0; c < 4; c++)
            {
                float startAngle = c * 90f;
                for (int s = 0; s < segments; s++)
                {
                    float angle = Mathf.Deg2Rad * (startAngle + (90f * s / (segments - 1)));
                    Vector2 pos = corners[c] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    AddVert(vh, pos, col);
                }
            }

            // Generate triangles (fan from center)
            for (int i = 0; i < totalVerts; i++)
            {
                int current = firstOuterIdx + i;
                int next = firstOuterIdx + (i + 1) % totalVerts;
                vh.AddTriangle(centerIdx, current, next);
            }
        }

        private void AddRoundedRectBorder(VertexHelper vh, Rect rect, float radius, int segments, Color col, float width)
        {
            int totalVerts = segments * 4;
            int baseIdx = vh.currentVertCount;

            // Outer ring
            Vector2[] outerCorners = new Vector2[]
            {
                new Vector2(rect.xMax - radius, rect.yMax - radius),
                new Vector2(rect.xMin + radius, rect.yMax - radius),
                new Vector2(rect.xMin + radius, rect.yMin + radius),
                new Vector2(rect.xMax - radius, rect.yMin + radius),
            };

            float innerRadius = Mathf.Max(0f, radius - width);
            Rect innerRect = new Rect(rect.x + width, rect.y + width, rect.width - width * 2f, rect.height - width * 2f);
            float innerR = Mathf.Min(innerRadius, innerRect.width * 0.5f, innerRect.height * 0.5f);

            Vector2[] innerCorners = new Vector2[]
            {
                new Vector2(innerRect.xMax - innerR, innerRect.yMax - innerR),
                new Vector2(innerRect.xMin + innerR, innerRect.yMax - innerR),
                new Vector2(innerRect.xMin + innerR, innerRect.yMin + innerR),
                new Vector2(innerRect.xMax - innerR, innerRect.yMin + innerR),
            };

            // Add outer and inner ring verts
            for (int c = 0; c < 4; c++)
            {
                float startAngle = c * 90f;
                for (int s = 0; s < segments; s++)
                {
                    float angle = Mathf.Deg2Rad * (startAngle + (90f * s / (segments - 1)));
                    Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

                    Vector2 outerPos = outerCorners[c] + dir * radius;
                    Vector2 innerPos = innerCorners[c] + dir * innerR;

                    AddVert(vh, outerPos, col);
                    AddVert(vh, innerPos, col);
                }
            }

            // Generate quads for the border strip
            for (int i = 0; i < totalVerts; i++)
            {
                int outerCurrent = baseIdx + i * 2;
                int innerCurrent = baseIdx + i * 2 + 1;
                int nextI = (i + 1) % totalVerts;
                int outerNext = baseIdx + nextI * 2;
                int innerNext = baseIdx + nextI * 2 + 1;

                vh.AddTriangle(outerCurrent, innerCurrent, outerNext);
                vh.AddTriangle(innerCurrent, innerNext, outerNext);
            }
        }

        private int AddVert(VertexHelper vh, Vector2 pos, Color col)
        {
            int idx = vh.currentVertCount;
            vh.AddVert(new Vector3(pos.x, pos.y, 0f), col, Vector4.zero);
            return idx;
        }
    }
}
