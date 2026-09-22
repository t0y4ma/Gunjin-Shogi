using GunjinShogi.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GunjinShogi.UnityView
{
    /// <summary>盤上の駒1枚の見た目。位置はマスの中心へなめらかに移動する。</summary>
    public sealed class PieceView : MonoBehaviour
    {
        BoardView board;
        RectTransform rt;
        PieceGraphic shadow, body;
        TextMeshProUGUI label;
        Image movedMark, selectRing;
        int node = -1;
        bool visible;
        bool selected;
        Vector2 target;

        internal static PieceView Create(Transform parent, BoardView board)
        {
            var rt = Ui.Rect("Piece", parent);
            var v = rt.gameObject.AddComponent<PieceView>();
            v.board = board;
            v.rt = rt;
            v.selectRing = Ui.Image("Select", rt, Theme.Brass);
            v.shadow = AddGraphic("Shadow", rt);
            v.shadow.color = new Color(0, 0, 0, 0.35f);
            v.shadow.BorderColor = new Color(0, 0, 0, 1f);
            v.shadow.raycastTarget = false;
            v.body = AddGraphic("Body", rt);
            v.body.raycastTarget = false;
            v.label = Ui.Text("Label", v.body.transform, "", 40, Theme.PieceLabel, TextAlignmentOptions.Center, bold: true);
            v.label.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(v.label, 200);
            v.movedMark = Ui.Image("Moved", v.body.transform, Theme.WithAlpha(Theme.PieceLabel, 0.85f));
            v.gameObject.SetActive(false);
            return v;
        }

        static PieceGraphic AddGraphic(string name, Transform parent)
        {
            var go = Ui.Rect(name, parent).gameObject;
            go.AddComponent<CanvasRenderer>();
            return go.AddComponent<PieceGraphic>();
        }

        internal void Show(PieceDisplay d, bool enemy, bool isSelected, RuleSet rules)
        {
            bool wasVisible = visible;
            visible = true;
            gameObject.SetActive(true);
            bool moved = node != d.Node;
            node = d.Node;
            selected = isSelected;
            body.color = Theme.Team[d.Owner];
            body.BorderColor = Theme.TeamBorder[d.Owner];
            body.PointDown = enemy;
            shadow.PointDown = enemy;
            bool hidden = d.TypeId == Visibility.HiddenType;
            if (hidden)
            {
                // メモは「推測」と分かるように薄い色で出す
                label.text = string.IsNullOrEmpty(d.Memo) ? "" : d.Memo;
                label.color = Theme.WithAlpha(Theme.PieceLabel, 0.62f);
            }
            else
            {
                label.text = rules.Piece(d.TypeId).Name;
                label.color = Theme.PieceLabel;
            }
            // 相手の駒が「動いたことがある」印（地雷・軍旗ではないという手がかり）
            movedMark.enabled = hidden && d.HasMoved;
            selectRing.enabled = isSelected;
            if (moved) target = board.NodeCenter(node);
            if (!wasVisible) Snap();
            Layout();
        }

        internal void Hide()
        {
            visible = false;
            node = -1;
            gameObject.SetActive(false);
        }

        void Layout()
        {
            float c = board.cell;
            var size = new Vector2(c * 0.8f, c * 0.88f);
            rt.sizeDelta = size;
            Ui.Place((RectTransform)shadow.transform, new Vector2(size.x / 2 + c * 0.03f, size.y / 2 - c * 0.04f), size);
            Ui.Place((RectTransform)body.transform, size / 2, size);
            bool down = body.PointDown;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(size.x * 0.13f, size.y * (down ? 0.26f : 0.1f));
            label.rectTransform.offsetMax = new Vector2(-size.x * 0.13f, -size.y * (down ? 0.1f : 0.26f));
            label.fontSizeMax = c * 0.3f;
            float m = c * 0.1f;
            Ui.Place(movedMark.rectTransform, new Vector2(size.x / 2, down ? size.y * 0.8f : size.y * 0.2f), new Vector2(m, m));
            movedMark.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            Ui.Place(selectRing.rectTransform, size / 2, size + Vector2.one * c * 0.12f);
            rt.localScale = Vector3.one * (selected ? 1.08f : 1f);
        }

        internal void Snap()
        {
            if (!visible || node < 0) return;
            target = board.NodeCenter(node);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = target;
            Layout();
        }

        internal void Tick()
        {
            if (!visible) return;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.Lerp(rt.anchoredPosition, target, 1 - Mathf.Exp(-Time.unscaledDeltaTime * 18f));
        }
    }
}
