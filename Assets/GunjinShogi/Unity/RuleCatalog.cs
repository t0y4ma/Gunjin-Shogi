using System;
using System.Collections.Generic;
using GunjinShogi.Core;

namespace GunjinShogi.UnityView
{
    /// <summary>
    /// 詳細ルール1項目の表示用の定義。値の読み書きは Core の RuleField（英字キー）に任せる。
    /// 選択肢は index 0 が既定値で、ルールコードの番号とそのまま対応する。
    /// </summary>
    public sealed class RuleItem
    {
        public char Key;
        public string Group, Label, Note;
        public string[] Choices;

        RuleField field;
        RuleField Field => field ?? (field = RuleFields.ByKey(Key));

        public int Get(StandardRuleOptions o) => Field.Get(o);
        public void Set(StandardRuleOptions o, int v) => Field.Set(o, v);
        public string ValueText(StandardRuleOptions o) => Choices[Get(o)];
        public bool IsDefault(StandardRuleOptions o) => Get(o) == 0;
    }

    public static class RuleCatalog
    {
        static List<RuleItem> items;

        public static IReadOnlyList<RuleItem> Items => items ?? (items = Build());

        static readonly string[] OffOn = { "オフ", "オン" };

        static RuleItem I(char key, string group, string label, string note, params string[] choices) =>
            new RuleItem { Key = key, Group = group, Label = label, Note = note, Choices = choices.Length > 0 ? choices : OffOn };

        static List<RuleItem> Build()
        {
            var list = new List<RuleItem>
            {
                I('A', "勝敗", "タンク対工兵", "TVゲーム版ではタンクが勝つのが定着。勝敗表どおりなら工兵が勝ちます。",
                    "タンクが勝つ", "工兵が勝つ"),
                I('B', "勝敗", "地雷が勝ったとき", "盤に残る場合、地雷はその後も何度でも相手を倒します。",
                    "爆発して消える", "盤に残る"),
                I('C', "勝敗", "飛行機は工兵に負ける", "工兵が飛行機も撃ち落とせるようになります。"),
                I('D', "勝敗", "騎兵の強さを地雷と同じにする", "騎兵が「動ける地雷」になり、飛行機と工兵以外とは相討ちになります。"),
                I('E', "移動", "飛行機の横移動", "縦は何マスでも飛び越えて進めます。",
                    "1マス", "できない", "何マスでも"),
                I('F', "移動", "タンク・騎兵の動き", "標準は前へ2マス、横と後ろへ1マス。",
                    "標準", "全方向1マス", "全方向2マス"),
                I('G', "軍旗", "軍旗の移動", "軍旗はすぐ後ろの味方の駒と同じ強さになります。",
                    "動けない", "1マス動ける"),
                I('H', "軍旗", "軍旗を最後列に置く", "最後列の軍旗はどの駒にも負けます。",
                    "置けない", "置ける"),
                I('I', "勝利条件", "総司令部を占領できる駒", "占領した瞬間に勝ちが決まります。",
                    "将官・佐官", "飛行機以外すべて"),
                I('J', "勝利条件", "大将が倒されたら負け", "総司令部を占領されなくても、大将を失った時点で負けます。"),
                I('K', "勝利条件", "少尉で軍旗を倒したら勝ち", "旧陸軍で少尉が連隊旗手だったことに由来するルールです。"),
                I('L', "勝利条件", "千日手（同じ局面の繰り返し）", "指定の回数、同じ局面になったら引き分けにします。",
                    "3回で引き分け", "5回で引き分け", "判定しない"),
                I('M', "総司令部", "地雷・飛行機を置けない", "総司令部の守りを地雷や飛行機に任せられなくなります。"),
                I('N', "総司令部", "将官を必ず置く", "大将・中将・少将のどれかを総司令部に置かなければなりません。"),
                I('O', "総司令部", "最初に置いた駒は動かせない", "総司令部の駒は最後までそこで守ることになります。"),
                I('P', "総司令部", "空いた後は自軍も入れない", "内側から守れなくなり、周りのマスで食い止める必要があります。"),
            };
            // Core と項目の数・選択肢の数がずれていないか確かめる
            foreach (var item in list)
            {
                var f = RuleFields.ByKey(item.Key);
                if (f == null || f.ChoiceCount != item.Choices.Length)
                    throw new InvalidOperationException($"ルール {item.Key} の定義が Core と一致しません");
            }
            return list;
        }
    }

    /// <summary>
    /// ゲームの表示と操作に関わる設定（ルールではないのでルールコードには含めない）。
    /// </summary>
    public sealed class DisplayItem
    {
        public string Label, Note;
        public string[] Choices;
        public Func<DisplaySettings, int> Get;
        public Action<DisplaySettings, int> Set;
    }

    public static class DisplayCatalog
    {
        public static readonly IReadOnlyList<DisplayItem> Items = new List<DisplayItem>
        {
            new DisplayItem
            {
                Label = "直前の手の強調", Note = "最後に動いた駒の移動元と移動先のマスに色を付けます。",
                Choices = new[] { "オン", "オフ" },
                Get = s => s.HighlightLastMove ? 0 : 1, Set = (s, v) => s.HighlightLastMove = v == 0,
            },
            new DisplayItem
            {
                Label = "棋譜の表示", Note = "「すべて」にすると、最初の手までスクロールして見返せます。",
                Choices = new[] { "すべて", "最新の数手だけ" },
                Get = s => s.ScrollableLog ? 0 : 1, Set = (s, v) => s.ScrollableLog = v == 0,
            },
            new DisplayItem
            {
                Label = "メモ機能", Note = "敵の駒に「将・佐・地…」などの印を付けられます。",
                Choices = new[] { "オン", "オフ" },
                Get = s => s.MemoEnabled ? 0 : 1, Set = (s, v) => s.MemoEnabled = v == 0,
            },
            new DisplayItem
            {
                Label = "推理アシスト", Note = "メモする敵駒を選んだとき、これまでの戦闘と動きから考えられる駒種を表示します。",
                Choices = new[] { "オフ", "オン" },
                Get = s => s.Assist ? 1 : 0, Set = (s, v) => s.Assist = v == 1,
            },
        };
    }
}
