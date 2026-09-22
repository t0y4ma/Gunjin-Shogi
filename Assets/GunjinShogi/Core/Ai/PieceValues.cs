using System;
using System.Collections.Generic;

namespace GunjinShogi.Core.Ai
{
    /// <summary>
    /// 勝敗表・枚数・動き・占領能力から駒の価値を自動で導く。
    /// 駒名に依存しないので、ローカルルールやカスタムルールでもそのまま使える。
    /// </summary>
    public sealed class PieceValues
    {
        public readonly double[] Value;
        /// <summary>総司令部を守る強さ（相手の占領駒に対する勝率に近い値、0..1）。</summary>
        public readonly double[] HqDefense;

        public PieceValues(RuleSet rules)
        {
            int n = rules.Pieces.Count;
            Value = new double[n];
            HqDefense = new double[n];
            var baseStrength = new double[n];

            int total = 0, occupierTotal = 0;
            foreach (var p in rules.Pieces)
            {
                total += p.Count;
                if (p.CanOccupyHQ && p.IsMobile) occupierTotal += p.Count;
            }

            for (int t = 0; t < n; t++)
            {
                double s = 0, d = 0;
                for (int j = 0; j < n; j++)
                {
                    var pj = rules.Piece(j);
                    if (pj.Count == 0) continue;
                    double w = Score(rules.Combat.Get(t, j));
                    s += pj.Count * w;
                    if (pj.CanOccupyHQ && pj.IsMobile) d += pj.Count * w;
                }
                baseStrength[t] = total > 0 ? s / total : 0;
                HqDefense[t] = occupierTotal > 0 ? d / occupierTotal : 0;
            }

            // 天敵ボーナス：ある駒種に勝てる駒種が少ないほど、その少数派は貴重（スパイ役など）
            var bonus = new double[n];
            for (int j = 0; j < n; j++)
            {
                if (rules.Piece(j).Count == 0) continue;
                int beaters = 0;
                for (int t = 0; t < n; t++)
                    if (t != j && rules.Piece(t).Count > 0 && rules.Combat.Get(t, j) == Outcome.Win) beaters++;
                if (beaters == 0) continue;
                for (int t = 0; t < n; t++)
                    if (t != j && rules.Combat.Get(t, j) == Outcome.Win)
                        bonus[t] += 0.35 * baseStrength[j] / beaters;
            }

            for (int t = 0; t < n; t++)
            {
                var def = rules.Piece(t);
                double v = baseStrength[t] + bonus[t];
                if (def.Ability == PieceAbility.MimicBehind) v = 0.15;
                else if (!def.IsMobile) v *= 0.55;
                if (def.IsMobile && def.CanOccupyHQ) v += 0.2;
                if (def.IsMobile) v += 0.03 * Mobility(def);
                if (def.IsCommander && rules.CommanderLossLoses) v += 3;
                Value[t] = Math.Round(v * 100, 1);
                if (def.Ability == PieceAbility.MimicBehind) HqDefense[t] = 0;
            }
        }

        static double Score(Outcome o) => o == Outcome.Win ? 1 : o == Outcome.Both ? 0.5 : 0;

        static int Mobility(PieceDefinition d)
        {
            int m = 0;
            foreach (var r in d.Moves)
            {
                int dirs = 0;
                for (int b = 1; b <= 8; b <<= 1) if (((int)r.Directions & b) != 0) dirs++;
                m += dirs * (r.MaxSteps == 0 ? 3 : r.MaxSteps) + (r.CanJump ? 2 : 0);
            }
            return Math.Min(m, 16);
        }
    }
}
