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

        /// <summary>
        /// 1手を文章にする。hiddenOwner の駒は名前を伏せる（観戦者が「先手のみ」などを選んだとき）。
        /// 観戦者の見え方では「自/敵」の代わりに「先/後」、勝敗は攻めた側から見た結果にする。
        /// </summary>
        public static string Describe(MoveView h, PlayerView view, RuleSet rules, int hiddenOwner = -1)
        {
            int viewer = view.Viewer;
            bool spectator = viewer == Visibility.Spectator;
            string Side(int player) => spectator ? (player == 0 ? "先 " : "後 ") : (player == viewer ? "自 " : "敵 ");
            string Name(int type, int owner) => type == Visibility.HiddenType || owner == hiddenOwner ? "駒" : rules.Piece(type).Name;

            var sb = new StringBuilder();
            sb.Append("<color=#A39D88>").Append(h.Ply.ToString().PadLeft(3)).Append("</color>  ");
            if (!h.HadBattle)
            {
                // 敵の駒は、対局後の全公開（または観戦）でだけ名前が出る
                sb.Append(Side(h.Player)).Append(Name(view.Pieces[h.PieceId].TypeId, h.Player)).Append(" 移動");
                return sb.ToString();
            }

            int attackerType = h.AttackerType != Visibility.HiddenType ? h.AttackerType : view.Pieces[h.PieceId].TypeId;
            string attacker = Side(h.Player) + Name(attackerType, h.Player);
            string defender = Side(1 - h.Player) + Name(h.DefenderType, 1 - h.Player);
            sb.Append(attacker).Append(" → ").Append(defender).Append("  ");
            switch (VerdictFor(h, spectator ? h.Player : viewer))
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
