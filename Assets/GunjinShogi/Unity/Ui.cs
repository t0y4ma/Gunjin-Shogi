using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GunjinShogi.UnityView
{
    /// <summary>uGUI の部品をコードで組み立てるための小さなヘルパー。</summary>
    public static class Ui
    {
        public static TMP_FontAsset Bold;
        public static TMP_FontAsset Regular;

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Stretch(RectTransform rt, float left = 0, float bottom = 0, float right = 0, float top = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        public static RectTransform Anchors(RectTransform rt, float minX, float minY, float maxX, float maxY)
        {
            rt.anchorMin = new Vector2(minX, minY);
            rt.anchorMax = new Vector2(maxX, maxY);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>左下アンカーで絶対配置（盤のマスなど、コンテナの大きさから計算して置くもの）。</summary>
        public static void Place(RectTransform rt, Vector2 center, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = center;
            rt.sizeDelta = size;
        }

        public static Image Image(string name, Transform parent, Color color, bool raycast = false)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        public static TextMeshProUGUI Text(string name, Transform parent, string text, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Left, bool bold = false)
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = bold ? Bold : Regular;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            return t;
        }

        public static void AutoSize(TextMeshProUGUI t, float max)
        {
            t.enableAutoSizing = true;
            t.fontSizeMin = 1;
            t.fontSizeMax = max;
        }

        public enum ButtonStyle { Primary, Secondary, Danger }

        public static Button Button(string name, Transform parent, string label, Action onClick,
            ButtonStyle style = ButtonStyle.Secondary, float fontSize = 34)
        {
            Color bg, fg, line;
            switch (style)
            {
                case ButtonStyle.Primary: bg = Theme.Brass; fg = Theme.Ink; line = Theme.Brass; break;
                case ButtonStyle.Danger: bg = Theme.Panel; fg = Theme.Hex("D98C7C"); line = Theme.Hex("8A4A3E"); break;
                default: bg = Theme.Panel; fg = Theme.Text; line = Theme.Hex("6E6A58"); break;
            }

            var frame = Image(name, parent, line, raycast: true);
            var inner = Image("Face", frame.transform, bg);
            Stretch(inner.rectTransform, 3, 3, 3, 3);
            var text = Text("Label", inner.transform, label, fontSize, fg, TextAlignmentOptions.Center, bold: true);
            Stretch(text.rectTransform, 12, 4, 12, 4);
            AutoSize(text, fontSize);
            text.textWrappingMode = TextWrappingModes.NoWrap;

            var button = frame.gameObject.AddComponent<Button>();
            button.targetGraphic = inner;
            var colors = button.colors;
            // multiplier 1.4 × 0.715 ≈ 1 で通常時は元の色。ホバーで少し明るく、押すと暗くなる
            var normal = new Color(0.715f, 0.715f, 0.715f);
            colors.normalColor = normal;
            colors.selectedColor = normal;
            colors.highlightedColor = new Color(0.82f, 0.82f, 0.82f);
            colors.pressedColor = new Color(0.55f, 0.55f, 0.55f);
            colors.disabledColor = new Color(0.42f, 0.42f, 0.42f, 1f); // 暗く沈める（透けると下地で逆に明るく見える）
            colors.colorMultiplier = 1.4f;
            button.colors = colors;
            if (onClick != null) button.onClick.AddListener(() => onClick());
            return button;
        }

        public static void SetLabel(Button b, string label) =>
            b.GetComponentInChildren<TextMeshProUGUI>().text = label;

        /// <summary>選ばれているかどうかでボタンの色を切り替える（セグメント選択・トグル用）。</summary>
        public static void SetSelected(Button b, bool selected)
        {
            var face = (Image)b.targetGraphic;
            var frame = b.GetComponent<Image>();
            var label = b.GetComponentInChildren<TextMeshProUGUI>();
            face.color = selected ? Theme.Brass : Theme.Panel;
            frame.color = selected ? Theme.Brass : Theme.Hex("4E4B3F");
            label.color = selected ? Theme.Ink : Theme.TextMuted;
        }

        public static LayoutElement Size(Component c, float preferredHeight = -1, float preferredWidth = -1, float flexibleWidth = -1)
        {
            var le = c.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            if (preferredHeight >= 0) { le.preferredHeight = preferredHeight; le.minHeight = preferredHeight; le.flexibleHeight = 0; }
            if (preferredWidth >= 0) { le.preferredWidth = preferredWidth; le.minWidth = 0; }
            if (flexibleWidth >= 0) le.flexibleWidth = flexibleWidth;
            return le;
        }

        public static VerticalLayoutGroup Column(Component c, float spacing, int padding = 0)
        {
            var v = c.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(padding, padding, padding, padding);
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return v;
        }

        public static HorizontalLayoutGroup RowGroup(Component c, float spacing)
        {
            var h = c.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childAlignment = TextAnchor.MiddleLeft;
            return h;
        }

        /// <summary>
        /// 全画面を暗く覆い、中央にカードを置く。カードの幅は画面に対する割合。戻り値は（覆い, カードの中身）。
        /// </summary>
        public static (GameObject root, RectTransform content) Modal(string name, Transform parent, float widthRatio, float heightRatio)
        {
            var dim = Image(name, parent, new Color(0, 0, 0, 0.62f), raycast: true);
            Stretch(dim.rectTransform);
            var frame = Image("Card", dim.transform, Theme.PanelLine);
            float mx = (1 - widthRatio) / 2, my = (1 - heightRatio) / 2;
            Anchors(frame.rectTransform, mx, my, 1 - mx, 1 - my);
            var card = Image("Face", frame.transform, Theme.Panel);
            Stretch(card.rectTransform, 3, 3, 3, 3);
            var content = Stretch(Rect("Content", card.transform), 36, 28, 36, 28);
            dim.gameObject.SetActive(false);
            return (dim.gameObject, content);
        }

        /// <summary>縦スクロールする領域。戻り値の content に行を足していく。</summary>
        public static RectTransform ScrollArea(string name, Transform parent, out ScrollRect scroll)
        {
            var root = Rect(name, parent);
            scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40;
            var viewport = Stretch(Rect("Viewport", root));
            viewport.gameObject.AddComponent<RectMask2D>();
            var hit = viewport.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            var content = Rect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;
            return content;
        }

        public static TMP_InputField InputField(string name, Transform parent, string placeholder, float fontSize = 30)
        {
            var go = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
            go.name = name;
            go.transform.SetParent(parent, false);
            var input = go.GetComponent<TMP_InputField>();
            var bg = go.GetComponent<Image>();
            bg.color = Theme.Hex("1A1E1B");
            foreach (var t in go.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                t.font = Regular;
                t.fontSize = fontSize;
                t.alignment = TextAlignmentOptions.MidlineLeft;
            }
            input.textComponent.color = Theme.Text;
            var ph = (TextMeshProUGUI)input.placeholder;
            ph.text = placeholder;
            ph.color = Theme.WithAlpha(Theme.TextMuted, 0.6f);
            ph.fontStyle = FontStyles.Normal;
            input.caretColor = Theme.Brass;
            input.selectionColor = Theme.WithAlpha(Theme.Brass, 0.35f);
            input.textViewport.offsetMin = new Vector2(16, 6);
            input.textViewport.offsetMax = new Vector2(-16, -6);
            return input;
        }
    }
}
