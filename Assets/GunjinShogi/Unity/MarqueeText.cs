using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GunjinShogi.UnityView
{
    /// <summary>
    /// 枠に収まらない1行のテキストを自動でスクロールさせる（文字は縮めない）。
    /// 左寄せで少し止まる → 右へ流れる → 最後の文字まで見えたら少し止まる → 左寄せに戻る、を繰り返す。
    /// 収まるときは動かさず、元の文字揃えのまま表示する。
    /// このコンポーネントは RectMask2D を持つ「窓」に付き、テキストはその子になる（Ui.Marquee で作る）。
    /// </summary>
    [RequireComponent(typeof(RectMask2D))]
    public sealed class MarqueeText : MonoBehaviour
    {
        public const float Speed = 70f;       // 流れる速さ（キャンバス単位／秒）
        public const float WaitStart = 1.4f;  // 左寄せで止まる時間
        public const float WaitEnd = 1.2f;    // 最後まで見えてから止まる時間

        TextMeshProUGUI text;
        RectTransform view, rt;
        string lastText;
        float lastViewWidth = -1, textWidth, offset, wait;
        bool atEnd;

        public void Init(TextMeshProUGUI t)
        {
            text = t;
            view = (RectTransform)transform;
            rt = t.rectTransform;
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            text.enableAutoSizing = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            lastText = null;
        }

        void OnEnable() => lastText = null; // 表示し直したら左寄せから

        void LateUpdate()
        {
            if (text == null) return;
            float viewWidth = view.rect.width;
            if (text.text != lastText || !Mathf.Approximately(viewWidth, lastViewWidth))
            {
                lastText = text.text;
                lastViewWidth = viewWidth;
                textWidth = text.GetPreferredValues(text.text, float.PositiveInfinity, float.PositiveInfinity).x;
                offset = 0;
                wait = WaitStart;
                atEnd = false;
            }

            float overflow = textWidth - viewWidth;
            if (overflow <= 0.5f)
            {
                Place(0, 0); // 収まる：元の揃えのまま
                return;
            }

            float dt = Time.unscaledDeltaTime;
            if (wait > 0) wait -= dt;
            else if (atEnd)
            {
                offset = 0;           // 左寄せに戻る
                atEnd = false;
                wait = WaitStart;
            }
            else
            {
                offset = Mathf.Min(overflow, offset + Speed * dt);
                if (offset >= overflow) { atEnd = true; wait = WaitEnd; }
            }
            Place(overflow, offset);
        }

        void Place(float extraWidth, float shift)
        {
            // テキストの幅を中身の幅まで広げ、左へずらして見せる
            rt.offsetMin = new Vector2(-shift, 0);
            rt.offsetMax = new Vector2(extraWidth - shift, 0);
        }
    }
}
