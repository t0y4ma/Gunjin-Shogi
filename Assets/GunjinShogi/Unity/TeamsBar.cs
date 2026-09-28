using TMPro;
using UnityEngine;

namespace GunjinShogi.UnityView
{
    /// <summary>
    /// 対局画面の「■ 朱 名前　■ 藍 名前」の行。色の印と「朱／藍」は動かさず、名前だけを
    /// 収まらないときに自動でスクロールさせる。
    /// </summary>
    public sealed class TeamsBar
    {
        const float FontSize = 28;
        readonly TextMeshProUGUI[] names = new TextMeshProUGUI[2];
        readonly TextMeshProUGUI extra;

        public TeamsBar(Transform parent, float height)
        {
            var row = Ui.Rect("Teams", parent);
            var h = Ui.RowGroup(row, 8);
            h.childForceExpandHeight = true;
            Ui.Size(row, height);
            for (int p = 0; p < 2; p++)
            {
                var color = Color.Lerp(Theme.Team[p], Color.white, p == 0 ? 0.35f : 0.45f);
                var mark = Ui.Text("Mark" + p, row, $"■ {Theme.TeamName[p]}", FontSize, color);
                mark.textWrappingMode = TextWrappingModes.NoWrap;
                mark.alignment = TextAlignmentOptions.MidlineLeft;
                var le = Ui.Size(mark, -1, mark.GetPreferredValues().x + 2);
                le.flexibleWidth = 0;
                le.minWidth = le.preferredWidth; // 印は縮めない
                names[p] = Ui.Text("Name" + p, row, "", FontSize, Theme.Text);
                names[p].alignment = TextAlignmentOptions.MidlineLeft;
                Ui.Size(names[p], -1, 0, 1);
                Ui.Marquee(names[p], FontSize);
                if (p == 0) Ui.Size(Ui.Rect("Gap", row), -1, 14);
            }
            extra = Ui.Text("Extra", row, "", FontSize, Theme.TextMuted);
            extra.textWrappingMode = TextWrappingModes.NoWrap;
            extra.alignment = TextAlignmentOptions.MidlineRight;
            Ui.Size(extra, -1, 0, 0);
        }

        /// <summary>名前を設定する。note は右端に固定で出す短い文字（「観戦中」など、無ければ空）。</summary>
        public void Set(string first, string second, string note = "")
        {
            if (names[0].text != first) names[0].text = first;
            if (names[1].text != second) names[1].text = second;
            if (extra.text != note)
            {
                extra.text = note;
                Ui.Size(extra, -1, string.IsNullOrEmpty(note) ? 0 : extra.GetPreferredValues().x + 4, 0);
            }
        }
    }
}
