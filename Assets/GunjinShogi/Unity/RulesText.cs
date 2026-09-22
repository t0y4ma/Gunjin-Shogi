using System.Collections.Generic;
using System.Text;
using GunjinShogi.Core;

namespace GunjinShogi.UnityView
{
    /// <summary>
    /// 現在のルールを文章にする。駒の動きや配置の制限は RuleSet（データ）から組み立てるので、
    /// ローカルルールを変えれば説明も自動で変わる。
    /// </summary>
    public static class RulesText
    {
        const string Accent = "#E2B23C";
        const string Muted = "#A39D88";

        public static string Build(RuleSet rules, StandardRuleOptions o)
        {
            var sb = new StringBuilder();
            var topo = new BoardTopology(rules.Board);

            Section(sb, "勝利条件");
            sb.AppendLine($"・相手の総司令部を <b>{OccupierNames(rules)}</b> で占領する（入った瞬間に勝ち）");
            sb.AppendLine("・相手の動かせる駒をすべて倒す");
            if (rules.CommanderLossLoses) sb.AppendLine("・相手の大将を倒す（自分の大将が倒されたら負け）");
            if (rules.Pieces.Exists(p => p.CapturesFlagToWin))
                sb.AppendLine($"・{Names(rules, p => p.CapturesFlagToWin)}で相手の軍旗を倒す");
            sb.AppendLine(rules.RepetitionDrawCount > 0
                ? $"・同じ局面が{rules.RepetitionDrawCount}回現れたら引き分け（千日手）"
                : "・千日手による引き分けはありません");

            Section(sb, "駒の動き");
            foreach (var group in GroupByMovement(rules))
                sb.AppendLine($"・<b>{group.Key}</b>：{group.Value}");
            sb.AppendLine($"・川は突入口（橋）からしか渡れません{(rules.Pieces.Exists(p => p.IgnoresGates) ? $"。ただし{Names(rules, p => p.IgnoresGates)}はどこでも越えられます" : "")}");
            if (rules.HqInitialPieceImmobile) sb.AppendLine("・最初に総司令部に置いた駒は動かせません");
            if (rules.OwnHqClosed) sb.AppendLine("・自軍の駒は自陣の総司令部に入れません（通り抜けも不可）");

            Section(sb, "戦闘");
            sb.AppendLine("・駒がぶつかると審判が勝敗を判定します（相性表を参照）。攻めても守っても判定は同じです");
            sb.AppendLine("・同じ駒同士は相討ちで、両方とも盤から除かれます。取った駒は使えません");
            var flag = rules.Pieces.Find(p => p.Ability == PieceAbility.MimicBehind);
            if (flag != null)
                sb.AppendLine($"・{flag.Name}はすぐ後ろの味方の駒と同じ強さになります。後ろが空き・相手の駒・最後列のときは必ず負けます");
            sb.AppendLine(o.MineSurvivesWin ? "・地雷は勝っても盤に残ります" : "・地雷は勝つと爆発して消えます（相討ちと同じ）");

            Section(sb, "配置");
            sb.AppendLine("・自陣のマスに自由に置けます（23枚で自陣がちょうど埋まります）");
            var gate = Names(rules, p => p.ForbiddenOnGateFront);
            if (gate.Length > 0) sb.AppendLine($"・{gate}は突入口の前に置けません");
            var back = Names(rules, p => p.ForbiddenOnBackRow);
            if (back.Length > 0) sb.AppendLine($"・{back}は最後列に置けません");
            var hq = Names(rules, p => p.ForbiddenInHq);
            if (hq.Length > 0) sb.AppendLine($"・{hq}は総司令部に置けません");
            if (rules.HqRequiresOneOf.Count > 0)
            {
                var req = new List<string>();
                foreach (var t in rules.HqRequiresOneOf) req.Add(rules.Piece(t).Name);
                sb.AppendLine($"・総司令部には{string.Join("・", req)}のどれかを置きます");
            }

            Section(sb, "ローカルルールの設定");
            foreach (var item in RuleCatalog.Items)
            {
                bool changed = !item.IsDefault(o);
                string value = changed ? $"<color={Accent}><b>{item.ValueText(o)}</b></color>" : item.ValueText(o);
                sb.AppendLine($"・{item.Label}：{value}{(changed ? $"　<color={Muted}>（標準から変更）</color>" : "")}");
            }
            sb.AppendLine($"<color={Muted}>ルールコード {RuleCodec.Encode(o)}</color>");
            return sb.ToString();
        }

        static void Section(StringBuilder sb, string title)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine($"<color={Accent}><b>{title}</b></color>");
        }

        static string Names(RuleSet rules, System.Predicate<PieceDefinition> pred)
        {
            var list = new List<string>();
            foreach (var p in rules.Pieces) if (p.Count > 0 && pred(p)) list.Add(p.Name);
            return string.Join("・", list);
        }

        static string OccupierNames(RuleSet rules)
        {
            var list = new List<string>();
            foreach (var p in rules.Pieces) if (p.Count > 0 && p.IsMobile && p.CanOccupyHQ) list.Add(p.Name);
            // 大将〜少佐がそろっていれば短く書く
            if (list.Count == 6 && list[0] == rules.Piece(StandardPieces.General).Name && list[5] == rules.Piece(StandardPieces.Major).Name)
                return "将官・佐官（大将〜少佐）";
            return string.Join("・", list);
        }

        /// <summary>動きの説明が同じ駒をまとめる（例：大将〜少尉とスパイは「前後左右に1マス」）。</summary>
        static List<KeyValuePair<string, string>> GroupByMovement(RuleSet rules)
        {
            var order = new List<string>();
            var names = new Dictionary<string, List<string>>();
            foreach (var p in rules.Pieces)
            {
                if (p.Count == 0) continue;
                var text = Movement(p);
                if (!names.TryGetValue(text, out var list))
                {
                    list = new List<string>();
                    names[text] = list;
                    order.Add(text);
                }
                list.Add(p.Name);
            }
            var result = new List<KeyValuePair<string, string>>();
            foreach (var text in order) result.Add(new KeyValuePair<string, string>(string.Join("・", names[text]), text));
            return result;
        }

        public static string Movement(PieceDefinition p)
        {
            if (!p.IsMobile) return "動けない";
            var parts = new List<string>();
            foreach (var m in p.Moves)
            {
                string steps = m.MaxSteps == 0 ? "何マスでも" : $"{m.MaxSteps}マス";
                parts.Add($"{Directions(m.Directions)}に{steps}{(m.CanJump ? "（駒を飛び越せる）" : "")}");
            }
            return string.Join("、", parts);
        }

        static string Directions(DirMask d)
        {
            if (d == DirMask.Orthogonal) return "前後左右";
            var list = new List<string>();
            if ((d & DirMask.Forward) != 0 && (d & DirMask.Back) != 0) list.Add("前後");
            else if ((d & DirMask.Forward) != 0) list.Add("前");
            else if ((d & DirMask.Back) != 0) list.Add("後ろ");
            if ((d & DirMask.Sideways) == DirMask.Sideways) list.Add("左右");
            else if ((d & DirMask.Left) != 0) list.Add("左");
            else if ((d & DirMask.Right) != 0) list.Add("右");
            return string.Join("と", list);
        }
    }
}
