using System.Collections.Generic;
using GunjinShogi.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GunjinShogi.UnityView
{
    /// <summary>
    /// 対局中に開ける「相性表」と「ルール表示」。どちらも今の対局のルール（ローカルルール込み）を表示する。
    /// </summary>
    public sealed class InfoScreens
    {
        const int Types = 15; // 大将〜地雷（軍旗は背後の駒で決まるので表に入れない）

        readonly GameObject tableRoot, rulesRoot;
        readonly RectTransform tableContent, gridHost;
        readonly RectTransform gridRect;
        readonly RectTransform[,] cellRects = new RectTransform[Types + 1, Types + 1];
        readonly TextMeshProUGUI tableNote, rulesBody;
        readonly List<Image> cellFrames = new List<Image>();
        readonly List<TextMeshProUGUI> cellTexts = new List<TextMeshProUGUI>();
        readonly List<TextMeshProUGUI> headerTexts = new List<TextMeshProUGUI>();
        readonly ScrollRect rulesScroll;
        bool landscape = true;

        public InfoScreens(Transform canvas)
        {
            // ── 相性表 ──
            var (tRoot, tContent) = Ui.Modal("CombatTable", canvas, 0.6f, 0.94f);
            tableRoot = tRoot;
            tableContent = tContent;
            Ui.Column(tContent, 10);
            Ui.Size(Ui.Text("Title", tContent, "相性表", 42, Theme.Text, bold: true), 56);
            var legend = Ui.Text("Legend", tContent,
                "行の駒が列の駒とぶつかったとき　<color=#E0705A><b>○</b></color> 勝ち　<color=#9C978A><b>×</b></color> 負け　<color=#C9A46A><b>△</b></color> 相討ち　"
                + "<color=#E2B23C>□</color> 標準から変わったマス", 24, Theme.TextMuted);
            Ui.Size(legend, 36);
            Ui.AutoSize(legend, 24);

            gridHost = Ui.Rect("GridHost", tContent);
            Ui.Size(gridHost).flexibleHeight = 1;
            gridRect = Ui.Rect("Grid", gridHost);
            gridRect.anchorMin = gridRect.anchorMax = new Vector2(0.5f, 1);
            gridRect.pivot = new Vector2(0.5f, 1);
            BuildGrid(gridRect);

            tableNote = Ui.Text("Note", tContent, "", 22, Theme.TextMuted);
            Ui.Size(tableNote, 64);
            Ui.AutoSize(tableNote, 22);
            var closeT = Ui.Button("CloseTable", tContent, "閉じる", () => tableRoot.SetActive(false), Ui.ButtonStyle.Primary, 30);
            Ui.Size(closeT, 76);

            // ── ルール表示 ──
            var (rRoot, rContent) = Ui.Modal("RulesView", canvas, 0.6f, 0.94f);
            rulesRoot = rRoot;
            Ui.Column(rContent, 12);
            Ui.Size(Ui.Text("Title", rContent, "この対局のルール", 42, Theme.Text, bold: true), 56);
            var host = Ui.Rect("ScrollHost", rContent);
            Ui.Size(host).flexibleHeight = 1;
            var scrollContent = Ui.ScrollArea("Scroll", host, out rulesScroll);
            Ui.Stretch((RectTransform)scrollContent.parent.parent);
            Ui.Column(scrollContent, 0);
            rulesBody = Ui.Text("Body", scrollContent, "", 27, Theme.Text);
            rulesBody.lineSpacing = 14;
            rulesBody.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var closeR = Ui.Button("CloseRules", rContent, "閉じる", () => rulesRoot.SetActive(false), Ui.ButtonStyle.Primary, 30);
            Ui.Size(closeR, 76);
        }

void BuildGrid(Transform parent)
        {
            for (int r = 0; r <= Types; r++)
            for (int c = 0; c <= Types; c++)
            {
                bool header = r == 0 || c == 0;
                var bg = header ? Theme.Panel : RowColor(r - 1);
                // 外側（枠用）と内側の2枚重ね。変更されたマスは外側だけ金色にして細い枠に見せる
                var frame = Ui.Image($"Cell{r}_{c}", parent, bg);
                cellRects[r, c] = frame.rectTransform;
                var face = Ui.Image("Face", frame.transform, bg);
                Ui.Stretch(face.rectTransform, 2, 2, 2, 2);
                var t = Ui.Text("T", face.transform, "", 24, Theme.Text, TextAlignmentOptions.Center, bold: true);
                Ui.Stretch(t.rectTransform, 0, 0, 0, 0);
                t.textWrappingMode = TextWrappingModes.Normal;
                if (header && (r > 0 || c > 0)) headerTexts.Add(t);
                if (!header)
                {
                    cellFrames.Add(frame);
                    cellTexts.Add(t);
                }
            }
        }

        static Color RowColor(int row) => Theme.Hex(row % 2 == 0 ? "2C322C" : "262B26");

        public void ApplyOrientation(bool isLandscape)
        {
            landscape = isLandscape;
            foreach (var root in new[] { tableRoot, rulesRoot })
            {
                var card = (RectTransform)root.transform.GetChild(0);
                if (landscape) Ui.Anchors(card, 0.2f, 0.03f, 0.8f, 0.97f);
                else Ui.Anchors(card, 0.02f, 0.12f, 0.98f, 0.88f);
            }
            if (tableRoot.activeSelf) FitGrid();
        }

        public void OpenTable(RuleSet rules, StandardRuleOptions options)
        {
            var standard = StandardRules.Create23().Combat;
            // 列見出しは縦書き（1文字ずつ改行）、行見出しは横書き
            int h = 0;
            for (int c = 1; c <= Types; c++) headerTexts[h++].text = Vertical(rules.Piece(c - 1).Name);
            for (int r = 1; r <= Types; r++) headerTexts[h++].text = rules.Piece(r - 1).Name;

            int i = 0;
            for (int a = 0; a < Types; a++)
            for (int b = 0; b < Types; b++, i++)
            {
                var t = cellTexts[i];
                bool bothImmobile = !rules.Piece(a).IsMobile && !rules.Piece(b).IsMobile;
                if (bothImmobile)
                {
                    t.text = "<color=#6E6A58>—</color>";
                }
                else
                {
                    var o = rules.Combat.Get(a, b);
                    t.text = o == Outcome.Win ? "<color=#E0705A>○</color>"
                        : o == Outcome.Lose ? "<color=#9C978A>×</color>"
                        : "<color=#C9A46A>△</color>";
                }
                bool changed = !bothImmobile && rules.Combat.Get(a, b) != standard.Get(a, b);
                cellFrames[i].color = changed ? Theme.Brass : RowColor(a);
            }

            var flag = rules.Pieces.Find(p => p.Ability == PieceAbility.MimicBehind);
            tableNote.text = (flag != null ? $"{flag.Name}はすぐ後ろの味方の駒と同じ強さ（後ろが空き・相手の駒・最後列なら必ず負け）。" : "")
                + (options.MineSurvivesWin ? "地雷は勝っても盤に残ります。" : "地雷の△は、地雷が勝って爆発した場合を含みます。")
                + "動けない駒同士は戦いません（—）。";

            tableRoot.SetActive(true);
            tableRoot.transform.SetAsLastSibling();
            FitGrid();
        }

        static string Vertical(string s)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        /// <summary>カードの大きさから1マスの大きさを決める（行見出しだけ幅を広げる余裕はないので正方形）。</summary>
/// <summary>
        /// カードの大きさから1マスの大きさを決めて並べる。
        /// 行見出しは横書きで幅広、列見出しは縦書きで背を高くする。
        /// </summary>
        void FitGrid()
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(tableContent);
            var size = gridHost.rect.size;
            const float headW = 2.3f, headH = 2.0f, gap = 2f;
            float cell = Mathf.Floor(Mathf.Min(
                (size.x - gap * Types) / (Types + headW),
                (size.y - gap * Types) / (Types + headH)));
            cell = Mathf.Max(cell, 12);
            float hw = cell * headW, hh = cell * headH;
            float totalW = hw + (cell + gap) * Types, totalH = hh + (cell + gap) * Types;
            gridRect.sizeDelta = new Vector2(totalW, totalH);

            for (int r = 0; r <= Types; r++)
            for (int c = 0; c <= Types; c++)
            {
                var rt = cellRects[r, c];
                rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
                rt.pivot = new Vector2(0, 1);
                float x = c == 0 ? 0 : hw + gap + (c - 1) * (cell + gap);
                float y = r == 0 ? 0 : hh + gap + (r - 1) * (cell + gap);
                rt.anchoredPosition = new Vector2(x, -y);
                rt.sizeDelta = new Vector2(c == 0 ? hw : cell, r == 0 ? hh : cell);
            }
            foreach (var t in cellTexts) t.fontSize = cell * 0.6f;
            for (int i = 0; i < headerTexts.Count; i++)
            {
                var t = headerTexts[i];
                bool columnHeader = i < Types;
                t.enableAutoSizing = true;
                t.fontSizeMin = 1;
                t.fontSizeMax = columnHeader ? cell * 0.5f : cell * 0.5f;
                t.lineSpacing = columnHeader ? -25 : 0;
                t.alignment = columnHeader ? TextAlignmentOptions.Bottom : TextAlignmentOptions.MidlineRight;
                t.margin = columnHeader ? Vector4.zero : new Vector4(0, 0, cell * 0.2f, 0);
            }
        }

        public void OpenRules(RuleSet rules, StandardRuleOptions options)
        {
            rulesBody.text = RulesText.Build(rules, options);
            rulesRoot.SetActive(true);
            rulesRoot.transform.SetAsLastSibling();
            rulesScroll.verticalNormalizedPosition = 1;
        }

        public void CloseAll()
        {
            tableRoot.SetActive(false);
            rulesRoot.SetActive(false);
        }
    }
}
