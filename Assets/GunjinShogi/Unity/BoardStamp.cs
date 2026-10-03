using TMPro;
using UnityEngine;

namespace GunjinShogi.UnityView
{
    /// <summary>審判の判定を示すハンコ。押された瞬間に縮んで着地し、少し残ってから消える。</summary>
    public sealed class BoardStamp : MonoBehaviour
    {
        RectTransform rt;
        CanvasGroup group;
        float age;
        const float Life = 1.4f;
        /// <summary>ハンコの大きさ（マスに対する割合）。駒と動きが見えるよう小さめにする。</summary>
        const float Size = 0.44f;

        internal static BoardStamp Create(Transform parent, Vector2 center, float cell, BoardView.StampKind kind)
        {
            Color color; string text;
            switch (kind)
            {
                case BoardView.StampKind.Win: color = Theme.StampWin; text = "勝"; break;
                case BoardView.StampKind.Lose: color = Theme.StampLose; text = "負"; break;
                default: color = Theme.StampBoth; text = "相討"; break;
            }
            var frame = Ui.Image("Stamp", parent, color);
            var s = frame.gameObject.AddComponent<BoardStamp>();
            s.rt = frame.rectTransform;
            s.group = frame.gameObject.AddComponent<CanvasGroup>();
            s.group.blocksRaycasts = false;
            s.group.interactable = false;
            // 駒を隠さないよう、マスの右上に小さく押す
            float size = cell * Size;
            Ui.Place(s.rt, center + new Vector2(cell * 0.5f - size * 0.42f, cell * 0.5f - size * 0.42f), Vector2.one * size);
            s.rt.localRotation = Quaternion.Euler(0, 0, -9);
            var inner = Ui.Image("Inner", frame.transform, Theme.WithAlpha(Theme.PieceLabel, 0.9f));
            Ui.Stretch(inner.rectTransform, size * 0.06f, size * 0.06f, size * 0.06f, size * 0.06f);
            var fill = Ui.Image("Fill", inner.transform, color);
            Ui.Stretch(fill.rectTransform, size * 0.03f, size * 0.03f, size * 0.03f, size * 0.03f);
            var t = Ui.Text("Text", fill.transform, text, size * 0.6f, Theme.PieceLabel, TextAlignmentOptions.Center, bold: true);
            Ui.Stretch(t.rectTransform, 2, 2, 2, 2);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(t, size * (text.Length > 1 ? 0.4f : 0.62f));
            s.Tick();
            return s;
        }

        internal bool Tick()
        {
            age += Time.unscaledDeltaTime;
            float pop = Mathf.Clamp01(age / 0.14f);
            rt.localScale = Vector3.one * Mathf.Lerp(1.35f, 1f, 1 - (1 - pop) * (1 - pop));
            group.alpha = age < Life - 0.35f ? Mathf.Clamp01(age / 0.08f) : Mathf.Clamp01((Life - age) / 0.35f);
            return age < Life;
        }
    }
}
