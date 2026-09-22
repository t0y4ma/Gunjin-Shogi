using System.Text;
using GunjinShogi.Core;

namespace GunjinShogi.UnityView
{
    /// <summary>棋譜を、見る側の知っている情報だけで文章にする。</summary>
    public static class MoveLog
    {
        public enum Verdict { None, Win, Lose, Both }

        /// <summary>viewer から見た戦闘の結果。</summary>
        public static Verdict VerdictFor(MoveView h, int viewer)
        {
            if (!h.HadBattle) return Verdict.None;
            if (h.Result == BattleResult.BothRemoved) return Verdict.Both;
            bool attackerIsMine = h.Player == viewer;
            bool attackerWon = h.Result == BattleResult.AttackerSurvives;
            return attackerIsMine == attackerWon ? Verdict.Win : Verdict.Lose;
        }

        public static string Describe(MoveView h, PlayerView view, RuleSet rules)
        {
            int viewer = view.Viewer;
            bool mine = h.Player == viewer;
            string Name(int type) => type == Visibility.HiddenType ? "駒" : rules.Piece(type).Name;

            var sb = new StringBuilder();
            sb.Append("<color=#A39D88>").Append(h.Ply.ToString().PadLeft(3)).Append("</color>  ");
            if (!h.HadBattle)
            {
                string mover = (mine ? "自 " : "敵 ") + Name(view.Pieces[h.PieceId].TypeId); // 敵の駒は、対局後の全公開でだけ名前が出る
                sb.Append(mover).Append(" 移動");
                return sb.ToString();
            }

            int attackerType = h.AttackerType != Visibility.HiddenType ? h.AttackerType : view.Pieces[h.PieceId].TypeId;
            string attacker = (mine ? "自 " : "敵 ") + Name(attackerType);
            string defender = (mine ? "敵 " : "自 ") + Name(h.DefenderType);
            sb.Append(attacker).Append(" → ").Append(defender).Append("  ");
            switch (VerdictFor(h, viewer))
            {
                case Verdict.Win: sb.Append("<color=#E0705A><b>勝</b></color>"); break;
                case Verdict.Lose: sb.Append("<color=#9C978A><b>負</b></color>"); break;
                default: sb.Append("<color=#C9A46A><b>相討</b></color>"); break;
            }
            return sb.ToString();
        }

        public static string Reason(EndReason r)
        {
            switch (r)
            {
                case EndReason.HqOccupied: return "総司令部を占領";
                case EndReason.NoLegalMoves: return "動かせる駒が尽きた";
                case EndReason.CommanderLost: return "大将が倒れた";
                case EndReason.Repetition: return "千日手";
                case EndReason.MaxPlies: return "手数の上限";
                case EndReason.Resign: return "投了";
                case EndReason.FlagCaptured: return "少尉が軍旗を倒した";
                default: return "";
            }
        }
    }
}
