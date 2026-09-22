using System;
using System.Collections.Generic;
using System.Text;

namespace GunjinShogi.Core
{
    /// <summary>
    /// ローカルルール1項目。選択肢の index 0 が必ず既定値。Key はルールコードで使う英字。
    /// 一度公開した Key は変えないこと（共有済みのコードが別の意味になるため）。
    /// </summary>
    public sealed class RuleField
    {
        public char Key;
        public int ChoiceCount;
        public Func<StandardRuleOptions, int> Get;
        public Action<StandardRuleOptions, int> Set;
    }

    public static class RuleFields
    {
        public static readonly IReadOnlyList<RuleField> All = Build();

        public static RuleField ByKey(char key)
        {
            key = char.ToUpperInvariant(key);
            foreach (var f in All) if (f.Key == key) return f;
            return null;
        }

        static RuleField F(char key, int count, Func<StandardRuleOptions, int> get, Action<StandardRuleOptions, int> set) =>
            new RuleField { Key = key, ChoiceCount = count, Get = get, Set = set };

        static RuleField Toggle(char key, Func<StandardRuleOptions, bool> get, Action<StandardRuleOptions, bool> set, bool defaultValue = false) =>
            F(key, 2, o => get(o) == defaultValue ? 0 : 1, (o, v) => set(o, v == 0 ? defaultValue : !defaultValue));

        static List<RuleField> Build()
        {
            return new List<RuleField>
            {
                // 勝敗
                Toggle('A', o => o.TankBeatsEngineer, (o, v) => o.TankBeatsEngineer = v, defaultValue: true),
                Toggle('B', o => o.MineSurvivesWin, (o, v) => o.MineSurvivesWin = v),
                Toggle('C', o => o.PlaneLosesToEngineer, (o, v) => o.PlaneLosesToEngineer = v),
                Toggle('D', o => o.CavalryAsMine, (o, v) => o.CavalryAsMine = v),
                // 移動（既定を index 0 にするために並べ替えている）
                F('E', 3,
                    o => o.PlaneSideways == PlaneSideways.OneStep ? 0 : o.PlaneSideways == PlaneSideways.None ? 1 : 2,
                    (o, v) => o.PlaneSideways = v == 0 ? PlaneSideways.OneStep : v == 1 ? PlaneSideways.None : PlaneSideways.Unlimited),
                F('F', 3, o => (int)o.TankCavalryMove, (o, v) => o.TankCavalryMove = (TankCavalryMove)v),
                // 軍旗
                Toggle('G', o => o.FlagCanMove, (o, v) => o.FlagCanMove = v),
                Toggle('H', o => o.AllowFlagOnBackRow, (o, v) => o.AllowFlagOnBackRow = v),
                // 勝利条件
                F('I', 2, o => (int)o.HqOccupiers, (o, v) => o.HqOccupiers = (HqOccupiers)v),
                Toggle('J', o => o.CommanderLossLoses, (o, v) => o.CommanderLossLoses = v),
                Toggle('K', o => o.SecondLtFlagWin, (o, v) => o.SecondLtFlagWin = v),
                F('L', 3,
                    o => o.RepetitionDrawCount == 5 ? 1 : o.RepetitionDrawCount == 0 ? 2 : 0,
                    (o, v) => o.RepetitionDrawCount = v == 0 ? 3 : v == 1 ? 5 : 0),
                // 総司令部
                Toggle('M', o => o.NoMineOrPlaneInHq, (o, v) => o.NoMineOrPlaneInHq = v),
                Toggle('N', o => o.GeneralRequiredInHq, (o, v) => o.GeneralRequiredInHq = v),
                Toggle('O', o => o.HqPieceImmobile, (o, v) => o.HqPieceImmobile = v),
                Toggle('P', o => o.OwnHqClosed, (o, v) => o.OwnHqClosed = v),
            };
        }
    }

    /// <summary>
    /// ローカルルール設定を短い共有コードにする。
    /// すべて既定なら "0"。変えた項目だけを「英字＋選んだ選択肢の番号」で連ねる（例: "A1K1"）。
    /// </summary>
    public static class RuleCodec
    {
        public const string DefaultCode = "0";

        public static string Encode(StandardRuleOptions o)
        {
            var d = new StandardRuleOptions();
            var sb = new StringBuilder();
            foreach (var f in RuleFields.All)
            {
                int v = f.Get(o);
                if (v != f.Get(d)) sb.Append(f.Key).Append(v);
            }
            return sb.Length == 0 ? DefaultCode : sb.ToString();
        }

        public static bool TryDecode(string code, out StandardRuleOptions o)
        {
            o = null;
            if (code == null) return false;
            code = code.Trim().ToUpperInvariant();
            if (code.Length == 0) return false;
            var r = new StandardRuleOptions();
            if (code == DefaultCode) { o = r; return true; }
            if (code.Length % 2 != 0) return false;
            var seen = new HashSet<char>();
            for (int i = 0; i < code.Length; i += 2)
            {
                var f = RuleFields.ByKey(code[i]);
                if (f == null || !seen.Add(f.Key)) return false;
                if (!char.IsDigit(code[i + 1])) return false;
                int v = code[i + 1] - '0';
                if (v <= 0 || v >= f.ChoiceCount) return false; // 0（既定）は書かない決まり
                f.Set(r, v);
            }
            o = r;
            return true;
        }

        /// <summary>既定値と違う項目の数。</summary>
        public static int DiffCount(StandardRuleOptions o)
        {
            var d = new StandardRuleOptions();
            int n = 0;
            foreach (var f in RuleFields.All) if (f.Get(o) != f.Get(d)) n++;
            return n;
        }
    }

    /// <summary>
    /// 配置を文字列にする。常に先手（プレイヤー0）の向きで保存するので、後手で読み込んでも同じ形になる。
    /// 1文字 = 1マス（自陣のノード番号順）、文字は駒種ID（0-9, A-Z）。
    /// </summary>
    public static class SetupCodec
    {
        const string Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        /// <summary>盤の形と駒構成が同じルール同士でだけ配置を使い回せるようにするためのキー。</summary>
        public static string CompositionKey(RuleSet rules)
        {
            var b = rules.Board;
            var sb = new StringBuilder();
            sb.Append(b.Width).Append('x').Append(b.Height).Append('g');
            foreach (var g in b.GateColumns) sb.Append(g);
            sb.Append('p');
            foreach (var p in rules.Pieces) sb.Append(Digits[Math.Min(p.Count, 35)]);
            return sb.ToString();
        }

        /// <summary>player の陣のノードを、先手側の同じ位置のノードに対応付ける。</summary>
        static int ToCanonical(BoardTopology topo, int node, int player)
        {
            if (player == 0) return node;
            var c = topo.PrimaryCell(node);
            var d = topo.Definition;
            return topo.NodeOf(new Coord(d.Width - 1 - c.X, d.Height - 1 - c.Y));
        }

        static int FromCanonical(BoardTopology topo, int node, int player) => ToCanonical(topo, node, player);

        public static string Encode(BoardTopology topo, int player, IList<Placement> placements)
        {
            var byNode = new Dictionary<int, int>();
            foreach (var p in placements) byNode[ToCanonical(topo, p.Node, player)] = p.TypeId;
            var sb = new StringBuilder();
            foreach (var n in topo.CampNodes(0))
                sb.Append(byNode.TryGetValue(n, out int t) ? Digits[t] : '-');
            return sb.ToString();
        }

        /// <summary>文字列から配置を作る。形式が合わなければ null（ルール上の妥当性は SetupValidator で別途確認）。</summary>
        public static List<Placement> Decode(RuleSet rules, BoardTopology topo, int player, string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            code = code.Trim().ToUpperInvariant();
            var nodes = new List<int>(topo.CampNodes(0));
            if (code.Length != nodes.Count) return null;
            var result = new List<Placement>();
            for (int i = 0; i < nodes.Count; i++)
            {
                if (code[i] == '-') continue;
                int t = Digits.IndexOf(code[i]);
                if (t < 0 || t >= rules.Pieces.Count) return null;
                result.Add(new Placement(t, FromCanonical(topo, nodes[i], player)));
            }
            return result;
        }
    }
}
