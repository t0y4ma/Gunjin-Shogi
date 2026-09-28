using System;
using GunjinShogi.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GunjinShogi.UnityView
{
    /// <summary>
    /// オフライン対戦の開始前の画面。先手・後手（CPU戦のみ）、CPU の強さ、ルール、表示設定を決めてから始める。
    /// </summary>
    public sealed class MatchSetupScreen
    {
        public static readonly string[] LevelName = { "やさしい", "ふつう", "つよい" };
        static readonly string[] LevelNotes =
        {
            "推理はざっくりで、指し手にムラがあります。取られそうな駒もあまり気にしません。はじめての人向け。",
            "戦闘の結果と動きから相手の駒を推理し、取られそうな駒は逃がします。ルールを覚えた人向け。",
            "推理の試行を増やしてほとんど迷わず指します。防御も固く、勝てないと見ると投了します。",
        };
        const string SideKey = "gs.humanSide"; // 0=先手 1=後手 2=ランダム

        /// <summary>開始：（人間の手番 0/1、CPU の強さ）。ふたりで対戦では手番は 0。</summary>
        public Action<int, int> Start;
        public Action Back;
        public Action EditRules;
        public Action OpenDisplay;

        readonly GameObject root;
        readonly RectTransform column;
        readonly TextMeshProUGUI titleText, levelNote, rulesText;
        readonly GameObject cpuPart;
        readonly Button[] sideButtons = new Button[3];
        readonly Button[] levelButtons = new Button[3];
        bool vsCpu;

        public MatchSetupScreen(Transform canvas)
        {
            var bg = Ui.Image("MatchSetup", canvas, Theme.Background, raycast: true);
            Ui.Stretch(bg.rectTransform);
            root = bg.gameObject;
            column = Ui.Rect("Column", bg.transform);
            Ui.Column(column, 16);

            titleText = Ui.Text("Title", column, "", 56, Theme.Text, TextAlignmentOptions.Center, bold: true);
            Ui.Size(titleText, 84);

            // CPU 戦だけの項目
            var cpu = Ui.Rect("CpuPart", column);
            cpuPart = cpu.gameObject;
            Ui.Column(cpu, 12);
            Ui.Size(cpu, 330);
            Section(cpu, "あなたの手番");
            var sideRow = Row(cpu, 64);
            string[] sides = { "先手（朱）", "後手（藍）", "ランダム" };
            for (int i = 0; i < 3; i++)
            {
                int v = i;
                sideButtons[i] = Ui.Button("Side" + i, sideRow, sides[i], () => { PlayerPrefs.SetInt(SideKey, v); PlayerPrefs.Save(); Refresh(); }, fontSize: 28);
            }
            Section(cpu, "CPUの強さ");
            var levelRow = Row(cpu, 64);
            for (int i = 0; i < 3; i++)
            {
                int v = i;
                levelButtons[i] = Ui.Button("Level" + i, levelRow, LevelName[i], () => { AppSettings.CpuLevel = v; Refresh(); }, fontSize: 28);
            }
            levelNote = Ui.Text("LevelNote", cpu, "", 24, Theme.TextMuted);
            Ui.Size(levelNote, 64);
            Ui.AutoSize(levelNote, 24);

            // ルール・表示
            Section(column, "ルールと表示");
            var ruleRow = Row(column, 70);
            rulesText = Ui.Text("Rules", ruleRow, "", 26, Theme.Text);
            Ui.AutoSize(rulesText, 26);
            ruleRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            Ui.Size(rulesText, -1, 300, 1);
            var edit = Ui.Button("EditRules", ruleRow, "ルールを変更", () => EditRules?.Invoke(), fontSize: 26);
            Ui.Size(edit, -1, 220);
            var disp = Ui.Button("Display", ruleRow, "表示設定", () => OpenDisplay?.Invoke(), fontSize: 26);
            Ui.Size(disp, -1, 180);

            var spacer = Ui.Size(Ui.Rect("Spacer", column));
            spacer.flexibleHeight = 1;

            var bottom = Row(column, 92);
            Ui.Button("Back", bottom, "戻る", () => Back?.Invoke(), fontSize: 34);
            Ui.Button("Start", bottom, "対局開始", OnStart, Ui.ButtonStyle.Primary, 34);

            root.SetActive(false);
        }

        static void Section(Transform parent, string text)
        {
            var t = Ui.Text("Section", parent, text, 26, Theme.Brass, bold: true);
            Ui.Size(t, 38);
        }

        static RectTransform Row(Transform parent, float height)
        {
            var r = Ui.Rect("Row", parent);
            var h = r.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 12;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = true;
            h.childAlignment = TextAnchor.MiddleLeft;
            Ui.Size(r, height);
            return r;
        }

        public void ApplyOrientation(bool landscape)
        {
            if (landscape)
            {
                Ui.Anchors(column, 0.5f, 0.5f, 0.5f, 0.5f);
                column.sizeDelta = new Vector2(1100, 880);
            }
            else
            {
                Ui.Anchors(column, 0, 0, 1, 1);
                column.offsetMin = new Vector2(28, 80);
                column.offsetMax = new Vector2(-28, -100);
            }
        }

        public bool IsOpen => root.activeSelf;

        public void Open(bool cpu)
        {
            vsCpu = cpu;
            titleText.text = cpu ? "CPUと対戦" : "ふたりで対戦（1台を交互に）";
            cpuPart.SetActive(cpu);
            root.SetActive(true);
            root.transform.SetAsLastSibling();
            Refresh();
        }

        public void Close() => root.SetActive(false);

        public void Refresh()
        {
            int side = Mathf.Clamp(PlayerPrefs.GetInt(SideKey, 0), 0, 2);
            for (int i = 0; i < 3; i++) Ui.SetSelected(sideButtons[i], i == side);
            int lv = AppSettings.CpuLevel;
            for (int i = 0; i < 3; i++) Ui.SetSelected(levelButtons[i], i == lv);
            levelNote.text = $"<color=#E2B23C>{LevelName[lv]}</color>　{LevelNotes[lv]}";
            var o = AppSettings.Rules;
            rulesText.text = $"ルール：{RulesScreen.Summary(o)}（コード {RuleCodec.Encode(o)}）";
        }

        void OnStart()
        {
            int side = 0;
            if (vsCpu)
            {
                side = Mathf.Clamp(PlayerPrefs.GetInt(SideKey, 0), 0, 2);
                if (side == 2) side = UnityEngine.Random.Range(0, 2);
            }
            Start?.Invoke(side, AppSettings.CpuLevel);
        }
    }
}
