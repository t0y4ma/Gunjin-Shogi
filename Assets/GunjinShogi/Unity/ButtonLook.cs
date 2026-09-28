using UnityEngine;
using UnityEngine.UI;

namespace GunjinShogi.UnityView
{
    /// <summary>
    /// 押せないボタンを、面だけでなく枠と文字も含めて一様に暗くする。
    /// Button の色の遷移は面（targetGraphic）にしか効かないため、枠と文字はここで同じ割合だけ沈める。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ButtonLook : MonoBehaviour
    {
        /// <summary>押せないときに掛ける色（面の disabledColor × colorMultiplier と同じ明るさ）。</summary>
        public static readonly Color DimTint = new Color(0.588f, 0.588f, 0.588f, 1f);

        Button button;
        Graphic frame, label;
        int state = -1;

        public void Init(Button b, Graphic frameGraphic, Graphic labelGraphic)
        {
            button = b;
            frame = frameGraphic;
            label = labelGraphic;
            Apply(true);
        }

        void OnEnable() => Apply(true);

        void LateUpdate() => Apply(false);

        void Apply(bool instant)
        {
            if (button == null) return;
            int now = button.IsInteractable() ? 1 : 0;
            if (now == state && !instant) return;
            state = now;
            var tint = now == 1 ? Color.white : DimTint;
            float d = instant ? 0f : 0.08f;
            if (frame != null) frame.CrossFadeColor(tint, d, true, true);
            if (label != null) label.CrossFadeColor(tint, d, true, true);
        }
    }
}
