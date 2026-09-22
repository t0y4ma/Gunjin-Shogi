using System;
using System.Collections.Generic;

namespace GunjinShogi.Core
{
    public static class SetupValidator
    {
        /// <summary>配置の検証。空リストなら妥当。</summary>
        public static List<string> Validate(RuleSet rules, BoardTopology topo, int player, IList<Placement> placements)
        {
            var errors = new List<string>();
            var counts = new int[rules.Pieces.Count];
            var used = new HashSet<int>();

            foreach (var pl in placements)
            {
                if (pl.TypeId < 0 || pl.TypeId >= rules.Pieces.Count)
                {
                    errors.Add($"不明な駒種 {pl.TypeId}");
                    continue;
                }
                var def = rules.Piece(pl.TypeId);
                if (pl.Node < 0 || pl.Node >= topo.NodeCount || topo.CampOf(pl.Node) != player)
                {
                    errors.Add($"{def.Name} が自陣の外（ノード {pl.Node}）に置かれています");
                    continue;
                }
                if (!used.Add(pl.Node)) errors.Add($"ノード {pl.Node} に複数の駒があります");
                if (def.ForbiddenOnGateFront && topo.IsGateFront(pl.Node, player))
                    errors.Add($"{def.Name} は突入口に置けません");
                if (def.ForbiddenOnBackRow && topo.IsBackRow(pl.Node, player))
                    errors.Add($"{def.Name} は最後列に置けません");
                if (def.ForbiddenInHq && pl.Node == topo.HqNode(player))
                    errors.Add($"{def.Name} は総司令部に置けません");
                if (pl.Node == topo.HqNode(player) && rules.HqRequiresOneOf.Count > 0 && !rules.HqRequiresOneOf.Contains(pl.TypeId))
                    errors.Add($"総司令部には{RequiredNames(rules)}のどれかを置いてください");
                counts[pl.TypeId]++;
            }

            for (int t = 0; t < counts.Length; t++)
                if (counts[t] != rules.Piece(t).Count)
                    errors.Add($"{rules.Piece(t).Name} は {rules.Piece(t).Count} 枚必要です（{counts[t]} 枚）");
            return errors;
        }

        static string RequiredNames(RuleSet rules)
        {
            var names = new List<string>();
            foreach (var t in rules.HqRequiresOneOf) names.Add(rules.Piece(t).Name);
            return string.Join("・", names);
        }
    }

    public static class RandomSetup
    {
        /// <summary>制約を満たすランダム配置。制約の強い駒から先に置く。</summary>
        public static List<Placement> Generate(RuleSet rules, BoardTopology topo, int player, Random rng)
        {
            var free = new List<int>(topo.CampNodes(player));
            var types = new List<int>();
            foreach (var p in rules.Pieces)
                for (int i = 0; i < p.Count; i++) types.Add(p.Id);

            Shuffle(types, rng);
            types.Sort((a, b) => Constraint(rules.Piece(b)).CompareTo(Constraint(rules.Piece(a))));

            var result = new List<Placement>();
            int hq = topo.HqNode(player);

            // 総司令部に置く駒の指定があれば、先にそこを埋める
            if (rules.HqRequiresOneOf.Count > 0)
            {
                var options = types.FindAll(t => rules.HqRequiresOneOf.Contains(t) && !rules.Piece(t).ForbiddenInHq);
                if (options.Count == 0) throw new InvalidOperationException("総司令部に置ける駒がありません");
                int pick = options[rng.Next(options.Count)];
                types.Remove(pick);
                free.Remove(hq);
                result.Add(new Placement(pick, hq));
            }

            foreach (var t in types)
            {
                var def = rules.Piece(t);
                var candidates = free.FindAll(n =>
                    !(def.ForbiddenOnGateFront && topo.IsGateFront(n, player)) &&
                    !(def.ForbiddenOnBackRow && topo.IsBackRow(n, player)) &&
                    !(def.ForbiddenInHq && n == hq));
                if (candidates.Count == 0)
                    throw new InvalidOperationException($"{def.Name} を置ける場所がありません");
                int node = candidates[rng.Next(candidates.Count)];
                free.Remove(node);
                result.Add(new Placement(t, node));
            }
            return result;
        }

        static int Constraint(PieceDefinition d) =>
            (d.ForbiddenOnGateFront ? 1 : 0) + (d.ForbiddenOnBackRow ? 1 : 0) + (d.ForbiddenInHq ? 1 : 0);

        static void Shuffle<T>(IList<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                var tmp = list[i]; list[i] = list[j]; list[j] = tmp;
            }
        }
    }
}
