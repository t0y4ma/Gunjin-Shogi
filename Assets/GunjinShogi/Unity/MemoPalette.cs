using System;
using System.Collections.Generic;
using GunjinShogi.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GunjinShogi.UnityView
{
    /// <summary>
    /// 敵駒メモの選択パネル。駒種のボタンを 4 列で並べ、将官・佐官・尉官は押すと大・中・少を選ぶ段が開く。
    /// 推理アシストがオンのときは、あり得ない駒種のボタンを押せなく（暗く）する。
    /// メモの文字：階級まで決めたら駒の名前（大将など）、区別しないなら「将」「佐」「尉」、ほかは駒の名前、分からないは「？」。
    /// </summary>
    public sealed class MemoPalette
    {
        public const float ButtonHeight = 64;
        const int Columns = 4;

        /// <summary>メモを決めたとき（null で消す）。</summary>
        public Action<string> Apply;

        sealed class Entry
        {
            public string Label;          // ボタンの文字
            public string Memo;           // 付けるメモ（グループは null）
            public string[] Members;      // 含む駒の名前（？ は null）
            public string Short;          // グループを区別しないときのメモ
            public Button Button;
        }

        readonly GameObject root;
        readonly TextMeshProUGUI assist;
        readonly RectTransform subRow;
        readonly List<Entry> main = new List<Entry>();
        readonly List<Entry> sub = new List<Entry>();
        Entry expanded;
        RuleSet rules;
        Func<int, bool> isCandidate; // 駒種 → あり得るか（アシストがオフなら null）
        string current;
        int shownPiece = -1;

        public GameObject Root => root;

        public MemoPalette(Transform parent)
        {
            var frame = Ui.Image("MemoPalette", parent, Theme.Hex("20251F"));
            root = frame.gameObject;
            var le = root.AddComponent<LayoutElement>();
            le.flexibleHeight = 1;
            le.minHeight = 0;
            var col = Ui.Stretch(Ui.Rect("Column", frame.transform), 16, 14, 16, 14);
            Ui.Column(col, 10);

            assist = Ui.Text("Assist", col, "", 24, Theme.TextMuted);
            Ui.Size(assist, 64);
            Ui.AutoSize(assist, 24);

            var defs = new[]
            {
                Group("将官", "将", "大将", "中将", "少将"),
                Group("佐官", "佐", "大佐", "中佐", "少佐"),
                Group("尉官", "尉", "大尉", "中尉", "少尉"),
                Single("飛行機"), Single("タンク"), Single("騎兵"), Single("工兵"),
                Single("スパイ"), Single("地雷"), Single("軍旗"),
                new Entry { Label = "？", Memo = "？" },
                new Entry { Label = "消す", Memo = null, Members = null, Short = "\0clear" },
            };
            RectTransform row = null;
            for (int i = 0; i < defs.Length; i++)
            {
                if (i % Columns == 0) row = GridRow(col, "Row" + i / Columns);
                var e = defs[i];
                e.Button = Ui.Button("Memo" + e.Label, row, e.Label, () => OnMain(e), fontSize: 28);
                main.Add(e);
            }

            // 将官・佐官・尉官を押したときに開く段
            subRow = GridRow(col, "Sub");
            // 開いた段だと分かるように、下地を敷いて少し内側に寄せる
            var subBg = subRow.gameObject.AddComponent<Image>();
            subBg.color = Theme.WithAlpha(Theme.Brass, 0.18f);
            subBg.raycastTarget = false;
            subRow.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(8, 8, 6, 6);
            Ui.Size(subRow, ButtonHeight + 12);
            subRow.SetSiblingIndex(main[0].Button.transform.parent.GetSiblingIndex() + 1); // 将官・佐官・尉官の段のすぐ下に開く
            subRow.gameObject.SetActive(false);

            var spacer = Ui.Size(Ui.Rect("Spacer", col));
            spacer.flexibleHeight = 1;
            root.SetActive(false);
        }

        static Entry Group(string label, string shortMemo, params string[] members) =>
            new Entry { Label = label, Members = members, Short = shortMemo };

        static Entry Single(string name) => new Entry { Label = name, Memo = name, Members = new[] { name } };

        static RectTransform GridRow(Transform parent, string name)
        {
            var row = Ui.Rect(name, parent);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 10;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = true;
            Ui.Size(row, ButtonHeight);
            return row;
        }

        public bool IsOpen => root.activeSelf;

        public void Hide() => root.SetActive(false);

        /// <summary>
        /// 開く。currentMemo は今付いているメモ、candidate は推理アシストの判定（オフなら null）、assistLine は上に出す説明。
        /// </summary>
        public void Show(RuleSet ruleSet, int pieceId, string currentMemo, Func<int, bool> candidate, string assistLine)
        {
            bool reopened = !root.activeSelf || rules != ruleSet || pieceId != shownPiece;
            shownPiece = pieceId;
            rules = ruleSet;
            current = currentMemo;
            isCandidate = candidate;
            assist.text = assistLine;
            if (reopened) Collapse();
            // 今のメモが階級付きなら、そのグループを開いておく
            if (currentMemo != null && expanded == null)
                foreach (var e in main)
                    if (e.Short != null && e.Members != null && (Array.IndexOf(e.Members, currentMemo) >= 0 || e.Short == currentMemo)) { Expand(e); break; }
            root.SetActive(true);
            RefreshButtons();
        }

        void OnMain(Entry e)
        {
            if (e.Short == "\0clear") { Apply?.Invoke(null); return; }
            if (e.Members != null && e.Members.Length > 1)
            {
                if (expanded == e) Collapse(); else Expand(e);
                RefreshButtons();
                return;
            }
            Apply?.Invoke(e.Memo);
        }

        void Expand(Entry group)
        {
            Collapse();
            expanded = group;
            foreach (var name in group.Members)
            {
                if (TypeOf(name) < 0) continue; // このルールにない駒
                string n = name;
                var e = new Entry { Label = n, Memo = n, Members = new[] { n } };
                e.Button = Ui.Button("Sub" + n, subRow, n, () => Apply?.Invoke(n), fontSize: 28);
                sub.Add(e);
            }
            var any = new Entry { Label = "どれか", Memo = group.Short, Members = group.Members };
            any.Button = Ui.Button("SubAny", subRow, any.Label, () => Apply?.Invoke(any.Memo), fontSize: 28);
            sub.Add(any);
            subRow.gameObject.SetActive(true);
        }

        void Collapse()
        {
            expanded = null;
            foreach (var e in sub) UnityEngine.Object.Destroy(e.Button.gameObject);
            sub.Clear();
            if (subRow != null) subRow.gameObject.SetActive(false);
        }

        int TypeOf(string name)
        {
            if (rules == null) return -1;
            for (int t = 0; t < rules.Pieces.Count; t++) if (rules.Piece(t).Name == name) return t;
            return -1;
        }

        /// <summary>含む駒のどれかがこのルールにあり、推理であり得るか。</summary>
        bool Possible(Entry e)
        {
            if (e.Members == null) return true; // ？・消す
            foreach (var name in e.Members)
            {
                int t = TypeOf(name);
                if (t >= 0 && (isCandidate == null || isCandidate(t))) return true;
            }
            return false;
        }

        void RefreshButtons()
        {
            foreach (var e in main)
            {
                bool exists = e.Members == null;
                if (!exists) foreach (var n in e.Members) if (TypeOf(n) >= 0) { exists = true; break; }
                e.Button.gameObject.SetActive(exists);
                e.Button.interactable = Possible(e);
                bool on = e == expanded || (e.Memo != null && e.Memo == current) || (e.Short != null && e.Short == current && e.Members != null);
                if (e.Short == "\0clear") on = false;
                Mark(e.Button, on);
            }
            foreach (var e in sub)
            {
                e.Button.interactable = Possible(e);
                Mark(e.Button, e.Memo == current);
            }
        }

        /// <summary>今のメモ・開いているグループを明るく示す。</summary>
        static void Mark(Button b, bool on)
        {
            var face = (Image)b.targetGraphic;
            var frame = b.GetComponent<Image>();
            var label = b.GetComponentInChildren<TextMeshProUGUI>();
            face.color = on ? Theme.Brass : Theme.Panel;
            frame.color = on ? Theme.Brass : Theme.Hex("6E6A58");
            label.color = on ? Theme.Ink : Theme.Text;
        }
    }
}
