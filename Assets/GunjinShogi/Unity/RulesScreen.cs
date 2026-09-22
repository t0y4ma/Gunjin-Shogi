using System;
using System.Collections.Generic;
using GunjinShogi.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GunjinShogi.UnityView
{
    /// <summary>
    /// 設定画面。基本ページは表示・操作の設定、サブ画面の「詳細ルール」は対局のルール。
    /// すべての項目を「選択肢のボタン列」で揃え、1行ごとに説明を添える。
    /// 選択肢は左端（index 0）が既定値。
    /// </summary>
    public sealed class RulesScreen
    {
        sealed class Row
        {
            public Func<int> Get;
            public Button[] Buttons;
        }

        readonly GameObject root;
        readonly RectTransform column;
        readonly List<LayoutElement> choiceSizes = new List<LayoutElement>();
        readonly List<TextMeshProUGUI> choiceLabels = new List<TextMeshProUGUI>();
        readonly List<Row> rows = new List<Row>();
        readonly GameObject mainPage, detailPage, codeRow;
        readonly TextMeshProUGUI titleText, codeText, messageText;
        readonly Button navButton;
        readonly TMP_InputField codeInput;
        StandardRuleOptions working;
        DisplaySettings display;
        bool showingDetail;
        Action onClose;

        public RulesScreen(Transform canvas)
        {
            var bg = Ui.Image("RulesScreen", canvas, Theme.Background, raycast: true);
            Ui.Stretch(bg.rectTransform);
            root = bg.gameObject;

            column = Ui.Rect("Column", bg.transform);
            Ui.Column(column, 14);

            titleText = Ui.Text("Title", column, "設定", 54, Theme.Text, bold: true);
            Ui.Size(titleText, 72);

            navButton = Ui.Button("Nav", column, "", () => ShowPage(!showingDetail), fontSize: 30);
            Ui.Size(navButton, 70);
            navButton.GetComponentInChildren<TextMeshProUGUI>().alignment = TextAlignmentOptions.MidlineLeft;

            // 基本：表示と操作
            mainPage = NewPage(column, "MainPage", out var mainContent);
            GroupHeader(mainContent, "表示と操作");
            foreach (var item in DisplayCatalog.Items)
            {
                var it = item;
                rows.Add(BuildRow(mainContent, it.Label, it.Note, it.Choices, () => it.Get(display), v => it.Set(display, v)));
            }

            // 詳細ルール：対局のルールすべて
            detailPage = NewPage(column, "DetailPage", out var detailContent);
            string group = null;
            foreach (var item in RuleCatalog.Items)
            {
                var it = item;
                if (it.Group != group)
                {
                    group = it.Group;
                    GroupHeader(detailContent, group);
                }
                rows.Add(BuildRow(detailContent, $"<color=#6E6A58>{it.Key}</color>　{it.Label}", it.Note, it.Choices,
                    () => it.Get(working), v => it.Set(working, v)));
            }

            // ルールコード（詳細ルールのページだけに出す）
            var codeRect = Ui.Rect("CodeRow", column);
            codeRow = codeRect.gameObject;
            Ui.RowGroup(codeRect, 14);
            Ui.Size(codeRect, 64);
            codeText = Ui.Text("Code", codeRect, "", 28, Theme.Text);
            Ui.Size(codeText, -1, 420, 0);
            codeText.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(codeText, 28);
            codeInput = Ui.InputField("CodeInput", codeRect, "ルールコードを貼り付け（例 A1K1）", 28);
            Ui.Size(codeInput, -1, 360, 1);
            var load = Ui.Button("LoadCode", codeRect, "コードを読込", LoadCode, fontSize: 28);
            Ui.Size(load, -1, 220);

            messageText = Ui.Text("Message", column, "", 26, Theme.TextMuted);
            Ui.Size(messageText, 40);

            var bottom = Ui.Rect("Bottom", column);
            Ui.RowGroup(bottom, 16);
            Ui.Size(bottom, 90);
            var cancel = Ui.Button("Cancel", bottom, "変更せずに戻る", () => Close(false), fontSize: 32);
            Ui.Size(cancel, -1, 300, 1);
            var save = Ui.Button("Save", bottom, "この設定で決定", () => Close(true), Ui.ButtonStyle.Primary, 32);
            Ui.Size(save, -1, 300, 1);

            root.SetActive(false);
        }

        static GameObject NewPage(Transform parent, string name, out RectTransform content)
        {
            var host = Ui.Rect(name, parent);
            Ui.Size(host).flexibleHeight = 1;
            content = Ui.ScrollArea("Scroll", host, out _);
            Ui.Stretch((RectTransform)content.parent.parent);
            Ui.Column(content, 6);
            return host.gameObject;
        }

        static void GroupHeader(Transform parent, string text)
        {
            var h = Ui.Text("Group", parent, text, 26, Theme.Brass, bold: true);
            h.characterSpacing = 10;
            Ui.Size(h, 54);
            h.alignment = TextAlignmentOptions.BottomLeft;
        }

        Row BuildRow(Transform parent, string labelText, string noteText, string[] choices, Func<int> get, Action<int> set)
        {
            var bg = Ui.Image("Row", parent, Theme.Hex("232823"));
            Ui.Size(bg, 104);
            var h = Ui.RowGroup(bg, 18);
            h.padding = new RectOffset(22, 16, 10, 10);

            var textCol = Ui.Rect("Text", bg.transform);
            Ui.Column(textCol, 2);
            Ui.Size(textCol, -1, 420, 1);
            var label = Ui.Text("Label", textCol, labelText, 30, Theme.Text, bold: true);
            Ui.Size(label, 40);
            Ui.AutoSize(label, 30);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            var note = Ui.Text("Note", textCol, noteText, 21, Theme.TextMuted);
            Ui.Size(note, 44);
            Ui.AutoSize(note, 21);

            var row = new Row { Get = get, Buttons = new Button[choices.Length] };
            for (int i = 0; i < choices.Length; i++)
            {
                int v = i;
                var b = Ui.Button("Choice" + i, bg.transform, choices[i], () => { set(v); Refresh(); }, fontSize: 26);
                choiceSizes.Add(Ui.Size(b, -1, 190));
                var choiceLabel = b.GetComponentInChildren<TextMeshProUGUI>();
                choiceLabel.fontSizeMin = 18;
                choiceLabels.Add(choiceLabel);
                row.Buttons[i] = b;
            }
            return row;
        }

        /// <summary>横長は中央に固定幅、縦長は画面いっぱいに広げ、選択肢ボタンを細くする。</summary>
        public void ApplyOrientation(bool landscape)
        {
            if (landscape)
            {
                Ui.Anchors(column, 0.5f, 0, 0.5f, 1);
                column.offsetMin = new Vector2(-620, 28);
                column.offsetMax = new Vector2(620, -28);
            }
            else
            {
                Ui.Anchors(column, 0, 0, 1, 1);
                column.offsetMin = new Vector2(24, 40);
                column.offsetMax = new Vector2(-24, -60);
            }
            foreach (var le in choiceSizes) le.preferredWidth = landscape ? 190 : 138;
            // 縦長ではボタンが細いので折り返し、2行で読めるようにする。横長では1行に収める
            foreach (var l in choiceLabels) l.textWrappingMode = landscape ? TextWrappingModes.NoWrap : TextWrappingModes.Normal;
        }

        public void Open(Action closed)
        {
            onClose = closed;
            working = AppSettings.Rules;
            display = AppSettings.Display;
            messageText.text = "";
            codeInput.text = "";
            root.SetActive(true);
            root.transform.SetAsLastSibling();
            ShowPage(false);
        }

        void ShowPage(bool detail)
        {
            showingDetail = detail;
            mainPage.SetActive(!detail);
            detailPage.SetActive(detail);
            codeRow.SetActive(detail);
            messageText.text = "";
            titleText.text = detail ? "詳細ルール" : "設定";
            Refresh();
        }

        void LoadCode()
        {
            if (RuleCodec.TryDecode(codeInput.text, out var o))
            {
                working = o;
                messageText.text = "コードを読み込みました。";
            }
            else
            {
                messageText.text = "<color=#E0705A>コードの形式が正しくありません。</color>";
            }
            Refresh();
        }

        void Refresh()
        {
            foreach (var row in rows)
            {
                int v = row.Get();
                for (int i = 0; i < row.Buttons.Length; i++) Ui.SetSelected(row.Buttons[i], i == v);
            }

            int changed = RuleCodec.DiffCount(working);
            Ui.SetLabel(navButton, showingDetail
                ? "‹　設定に戻る"
                : $"詳細ルール（勝敗・移動・軍旗・勝利条件・総司令部）　<color=#E2B23C>{(changed == 0 ? "標準" : changed + "項目変更")}</color>　›");
            codeText.text = $"コード <b>{RuleCodec.Encode(working)}</b>　<color=#A39D88>{Summary(working)}</color>";
        }

        public static string Summary(StandardRuleOptions o)
        {
            int n = RuleCodec.DiffCount(o);
            return n == 0 ? "標準" : $"標準から{n}項目変更";
        }

        void Close(bool save)
        {
            if (save)
            {
                AppSettings.Rules = working;
                AppSettings.Display = display;
            }
            root.SetActive(false);
            onClose?.Invoke();
        }
    }
}
