namespace GunjinShogi.Core
{
    /// <summary>審判。攻め・守りで判定は変わらない（勝敗表は対称）。</summary>
    public static class BattleResolver
    {
        public const int AlwaysLoses = -1;

        /// <summary>
        /// 駒の実効駒種。MimicBehind（軍旗）は atNode のすぐ後ろにある味方駒の駒種になる。
        /// 後ろが空・敵駒・盤外、または最後列にいる場合は AlwaysLoses。
        /// </summary>
        public static int EffectiveType(GameState s, Piece piece, int atNode)
        {
            int type = piece.TypeId;
            int node = atNode;
            int owner = piece.Owner;
            int guard = s.Rules.Pieces.Count + 1;

            while (s.Rules.Piece(type).Ability == PieceAbility.MimicBehind)
            {
                if (--guard < 0) return AlwaysLoses;
                if (s.Topology.IsBackRow(node, owner)) return AlwaysLoses;
                int behind = s.Topology.BehindNode(node, owner);
                if (behind < 0) return AlwaysLoses;
                int occ = s.OccupantOf(behind);
                if (occ < 0) return AlwaysLoses;
                var bp = s.Pieces[occ];
                if (bp.Owner != owner) return AlwaysLoses;
                type = bp.TypeId;
                node = behind;
            }
            return type;
        }

        public static BattleResult Resolve(RuleSet rules, int attackerEffective, int defenderEffective)
        {
            if (attackerEffective < 0 && defenderEffective < 0) return BattleResult.BothRemoved;
            if (attackerEffective < 0) return BattleResult.DefenderSurvives;
            if (defenderEffective < 0) return BattleResult.AttackerSurvives;
            switch (rules.Combat.Get(attackerEffective, defenderEffective))
            {
                case Outcome.Win: return BattleResult.AttackerSurvives;
                case Outcome.Lose: return BattleResult.DefenderSurvives;
                default: return BattleResult.BothRemoved;
            }
        }
    }
}
