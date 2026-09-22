using UnityEngine;
using UnityEngine.UI;

namespace GunjinShogi.UnityView
{
    /// <summary>将棋の駒形（五角形）を縁取り付きで描く。スプライト不要。</summary>
    public sealed class PieceGraphic : MaskableGraphic
    {
        static readonly Vector2[] Shape =
        {
            new Vector2(0.04f, 0f), new Vector2(0.96f, 0f), new Vector2(0.85f, 0.76f),
            new Vector2(0.5f, 1f), new Vector2(0.15f, 0.76f),
        };

        [SerializeField] Color borderColor = Color.black;
        [SerializeField] float borderWidth = 0.07f;
        [SerializeField] bool pointDown;

        public Color BorderColor { get => borderColor; set { borderColor = value; SetVerticesDirty(); } }
        public bool PointDown { get => pointDown; set { if (pointDown == value) return; pointDown = value; SetVerticesDirty(); } }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            AddPolygon(vh, r, 0f, borderColor * new Color(1, 1, 1, color.a));
            AddPolygon(vh, r, borderWidth, color);
        }

        void AddPolygon(VertexHelper vh, Rect r, float inset, Color c)
        {
            var center = new Vector2(0.5f, 0.42f);
            int start = vh.currentVertCount;
            vh.AddVert(ToRect(r, Flip(center)), c, Vector2.zero);
            for (int i = 0; i < Shape.Length; i++)
            {
                var p = Shape[i];
                var d = p - center;
                // 横方向と縦方向で縁の太さがなるべく揃うように縮める
                var shrunk = center + new Vector2(d.x * (1 - inset * 2.1f), d.y * (1 - inset * 1.9f));
                vh.AddVert(ToRect(r, Flip(shrunk)), c, Vector2.zero);
            }
            for (int i = 0; i < Shape.Length; i++)
                vh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % Shape.Length);
        }

        Vector2 Flip(Vector2 p) => pointDown ? new Vector2(p.x, 1 - p.y) : p;

        static Vector3 ToRect(Rect r, Vector2 p) => new Vector3(r.x + p.x * r.width, r.y + p.y * r.height);
    }
}
