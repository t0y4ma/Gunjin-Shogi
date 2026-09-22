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
        const float Life = 1.5f;

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
            Ui.Place(s.rt, center, Vector2.one * cell * 0.86f);
            s.rt.localRotation = Quaternion.Euler(0, 0, -9);
            var inner = Ui.Image("Inner", frame.transform, Theme.WithAlpha(Theme.PieceLabel, 0.9f));
            Ui.Stretch(inner.rectTransform, cell * 0.05f, cell * 0.05f, cell * 0.05f, cell * 0.05f);
            var fill = Ui.Image("Fill", inner.transform, color);
            Ui.Stretch(fill.rectTransform, cell * 0.025f, cell * 0.025f, cell * 0.025f, cell * 0.025f);
            var t = Ui.Text("Text", fill.transform, text, cell * 0.5f, Theme.PieceLabel, TextAlignmentOptions.Center, bold: true);
            Ui.Stretch(t.rectTransform, 2, 2, 2, 2);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(t, cell * (text.Length > 1 ? 0.34f : 0.52f));
            s.Tick();
            return s;
        }

        internal bool Tick()
        {
            age += Time.unscaledDeltaTime;
            float pop = Mathf.Clamp01(age / 0.14f);
            rt.localScale = Vector3.one * Mathf.Lerp(1.7f, 1f, 1 - (1 - pop) * (1 - pop));
            group.alpha = age < Life - 0.35f ? Mathf.Clamp01(age / 0.08f) : Mathf.Clamp01((Life - age) / 0.35f);
            return age < Life;
        }
    }
}
