using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GunjinShogi.UnityView
{
    /// <summary>盤のマス1つ（総司令部は2マス分で1つ）。クリックを BoardView に伝え、移動先の印を持つ。</summary>
    public sealed class NodeCell : MonoBehaviour, IPointerClickHandler
    {
        BoardView board;
        int node;
        Image line, face, last, dot;
        Image[] frame;
        TextMeshProUGUI label;

        internal static NodeCell Create(BoardView board, int node, bool hq)
        {
            var line = Ui.Image("Node" + node, board.cellLayer, Theme.Grid, raycast: true);
            var c = line.gameObject.AddComponent<NodeCell>();
            c.board = board;
            c.node = node;
            c.line = line;
            c.face = Ui.Image("Face", line.transform, hq ? Theme.PaperHq : Theme.Paper);
            c.last = Ui.Image("Last", line.transform, Theme.WithAlpha(Theme.Brass, 0.28f));
            Ui.Stretch(c.last.rectTransform);
            if (hq)
            {
                c.label = Ui.Text("HqLabel", c.face.transform, "総司令部", 20, Theme.WithAlpha(Theme.Ink, 0.45f),
                    TextAlignmentOptions.Center, bold: true);
                Ui.Stretch(c.label.rectTransform);
                c.label.characterSpacing = 30;
                c.label.textWrappingMode = TextWrappingModes.NoWrap;
            }

            c.dot = Ui.Image("MoveDot", board.markLayer, Theme.Brass);
            c.dot.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            c.frame = new Image[4];
            for (int i = 0; i < 4; i++) c.frame[i] = Ui.Image("Frame" + i, board.markLayer, Theme.Brass);
            c.SetState(MarkKind.None, false, false);
            return c;
        }

        internal void Layout(Rect r, float cell)
        {
            Ui.Place(line.rectTransform, r.center, r.size);
            float gl = Mathf.Max(1, cell * 0.022f);
            Ui.Stretch(face.rectTransform, gl, gl, gl, gl);
            if (label != null)
            {
                label.fontSize = cell * 0.16f;
                // 駒と重ならないよう、盤の外側の縁に小さく置く
                bool nearTop = r.center.y > ((RectTransform)board.transform).rect.height * 0.5f;
                label.rectTransform.offsetMin = new Vector2(0, nearTop ? cell * 0.8f : 0);
                label.rectTransform.offsetMax = new Vector2(0, nearTop ? 0 : -cell * 0.8f);
            }
            Ui.Place(dot.rectTransform, r.center, Vector2.one * cell * 0.2f);
            float t = Mathf.Max(3, cell * 0.075f);
            float inset = cell * 0.04f;
            var inner = new Rect(r.x + inset, r.y + inset, r.width - inset * 2, r.height - inset * 2);
            Ui.Place(frame[0].rectTransform, new Vector2(inner.center.x, inner.yMax - t / 2), new Vector2(inner.width, t));
            Ui.Place(frame[1].rectTransform, new Vector2(inner.center.x, inner.yMin + t / 2), new Vector2(inner.width, t));
            Ui.Place(frame[2].rectTransform, new Vector2(inner.xMin + t / 2, inner.center.y), new Vector2(t, inner.height));
            Ui.Place(frame[3].rectTransform, new Vector2(inner.xMax - t / 2, inner.center.y), new Vector2(t, inner.height));
        }

        internal void SetState(MarkKind kind, bool isLast, bool dim)
        {
            dot.enabled = kind == MarkKind.Move;
            foreach (var f in frame) f.enabled = kind == MarkKind.Attack;
            last.enabled = isLast;
            face.color = Color.Lerp(label != null ? Theme.PaperHq : Theme.Paper, Theme.Grid, dim ? 0.35f : 0f);
        }

        public void OnPointerClick(PointerEventData e) => board.OnCellClicked(node);
    }
}
