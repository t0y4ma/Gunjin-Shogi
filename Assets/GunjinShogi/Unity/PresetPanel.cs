using System;
using System.Collections.Generic;
using GunjinShogi.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GunjinShogi.UnityView
{
    /// <summary>
    /// 配置の保存・読込ダイアログ。5つの枠（PlayerPrefs）と、配置コードの表示・貼り付け。
    /// </summary>
    public sealed class PresetPanel
    {
        readonly GameObject root;
        readonly IPresetStorage storage;
        readonly TextMeshProUGUI[] slotLabels = new TextMeshProUGUI[PlayerPrefsPresetStorage.SlotCount];
        readonly Button[] loadButtons = new Button[PlayerPrefsPresetStorage.SlotCount];
        readonly TMP_InputField codeInput;
        readonly TextMeshProUGUI message;

        RuleSet rules;
        BoardTopology topo;
        int player;
        Func<List<Placement>> getCurrent;
        Action<List<Placement>> apply;
        IReadOnlyList<PresetSlot> slots;

        public bool IsOpen => root.activeSelf;

        public PresetPanel(Transform canvas, IPresetStorage storage)
        {
            this.storage = storage;
            var (r, content) = Ui.Modal("PresetPanel", canvas, 0.72f, 0.86f);
            root = r;
            Ui.Column(content, 12);

            Ui.Size(Ui.Text("Title", content, "配置の保存・読込", 44, Theme.Text, bold: true), 60);
            var lead = Ui.Text("Lead", content, "この端末（ブラウザ）に5つまで保存できます。ブラウザのデータを消すと消えるので、大事な配置はコードで控えてください。", 22, Theme.TextMuted);
            Ui.Size(lead, 62);

            for (int i = 0; i < slotLabels.Length; i++)
            {
                int slot = i;
                var row = Ui.Image("Slot" + i, content, Theme.Hex("232823"));
                Ui.Size(row, 76);
                var h = Ui.RowGroup(row, 14);
                h.padding = new RectOffset(20, 12, 8, 8);
                slotLabels[i] = Ui.Text("Label", row.transform, "", 28, Theme.Text);
                Ui.Size(slotLabels[i], -1, 300, 1);
                loadButtons[i] = Ui.Button("Load", row.transform, "読込", () => LoadSlot(slot), fontSize: 26);
                Ui.Size(loadButtons[i], -1, 150);
                var save = Ui.Button("Save", row.transform, "今の配置を保存", () => SaveSlot(slot), fontSize: 26);
                Ui.Size(save, -1, 240);
            }

            Ui.Size(Ui.Text("CodeTitle", content, "配置コード（先手・後手どちらでも同じ形で読み込めます）", 24, Theme.TextMuted, bold: true), 44);
            var codeRow = Ui.Rect("CodeRow", content);
            Ui.RowGroup(codeRow, 14);
            Ui.Size(codeRow, 68);
            codeInput = Ui.InputField("Code", codeRow, "23文字の配置コード", 30);
            Ui.Size(codeInput, -1, 400, 1);
            var loadCode = Ui.Button("LoadCode", codeRow, "コードを読込", LoadCode, fontSize: 26);
            Ui.Size(loadCode, -1, 220);

            message = Ui.Text("Message", content, "", 24, Theme.TextMuted);
            Ui.Size(message, 40);

            var spacer = Ui.Size(Ui.Rect("Spacer", content));
            spacer.flexibleHeight = 1;
            var close = Ui.Button("Close", content, "閉じる", Close, Ui.ButtonStyle.Primary, 32);
            Ui.Size(close, 84);
        }

        public void Open(RuleSet rules, BoardTopology topo, int player,
            Func<List<Placement>> current, Action<List<Placement>> apply)
        {
            this.rules = rules;
            this.topo = topo;
            this.player = player;
            getCurrent = current;
            this.apply = apply;
            message.text = "";
            root.SetActive(true);
            root.transform.SetAsLastSibling();
            Refresh();
        }

        void Refresh()
        {
            slots = storage.Load(SetupCodec.CompositionKey(rules));
            for (int i = 0; i < slots.Count; i++)
            {
                slotLabels[i].text = slots[i].IsEmpty
                    ? $"枠{i + 1}　<color=#6E6A58>空き</color>"
                    : $"枠{i + 1}　<color=#A39D88>{slots[i].SavedAt}</color>";
                loadButtons[i].interactable = !slots[i].IsEmpty;
            }
            codeInput.text = SetupCodec.Encode(topo, player, getCurrent());
        }

        void SaveSlot(int slot)
        {
            storage.Save(SetupCodec.CompositionKey(rules), slot, SetupCodec.Encode(topo, player, getCurrent()));
            message.text = $"枠{slot + 1}に保存しました。";
            Refresh();
        }

        void LoadSlot(int slot)
        {
            if (TryApply(slots[slot].Code)) message.text = $"枠{slot + 1}を読み込みました。";
        }

        void LoadCode()
        {
            if (TryApply(codeInput.text)) message.text = "コードを読み込みました。";
        }

        bool TryApply(string code)
        {
            var placements = SetupCodec.Decode(rules, topo, player, code);
            if (placements == null)
            {
                message.text = "<color=#E0705A>コードの形式が正しくありません。</color>";
                return false;
            }
            var errors = SetupValidator.Validate(rules, topo, player, placements);
            if (errors.Count > 0)
            {
                // 例：「軍旗を最後列に置ける」ルールで保存した配置を、置けないルールで読んだ場合
                message.text = $"<color=#E0705A>今のルールでは使えません：{errors[0]}</color>";
                return false;
            }
            apply(placements);
            codeInput.text = SetupCodec.Encode(topo, player, placements);
            return true;
        }

        public void Close() => root.SetActive(false);

        public void ApplyOrientation(bool landscape)
        {
            var card = (RectTransform)root.transform.GetChild(0);
            if (landscape) Ui.Anchors(card, 0.16f, 0.06f, 0.84f, 0.94f);
            else Ui.Anchors(card, 0.03f, 0.15f, 0.97f, 0.85f);
        }
    }
}
