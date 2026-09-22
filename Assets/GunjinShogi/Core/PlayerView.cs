using System.Collections.Generic;
using static GunjinShogi.Core.Visibility;

namespace GunjinShogi.Core
{
    public static class Visibility
    {
        /// <summary>駒種が見えないことを表す値。</summary>
        public const int HiddenType = -1;
    }

    public sealed class PieceView
    {
        public int Id;
        public int Owner;
        public int Node;
        public bool HasMoved;
        /// <summary>見えない駒は HiddenType。</summary>
        public int TypeId;
        public bool Alive => Node >= 0;
    }

    public sealed class MoveView
    {
        public int Ply;
        public int Player;
        public int PieceId;
        public int FromNode;
        public int ToNode;
        public bool HadBattle;
        public int AttackerId;
        public int DefenderId;
        /// <summary>自分の駒だけ駒種が入る。相手の駒は HiddenType。</summary>
        public int AttackerType;
        public int DefenderType;
        public BattleResult Result;
    }

    /// <summary>
    /// あるプレイヤーから見える情報だけを含む射影。UI・CPU・通信はこれだけを参照する。
    /// 相手の駒は ID・位置・動いたかどうかだけが分かり、駒種は分からない。
    /// </summary>
    public sealed class PlayerView
    {
        public int Viewer;
        public GamePhase Phase;
        public int CurrentPlayer;
        public GameResult Result;
        public EndReason EndReason;
        public List<PieceView> Pieces = new List<PieceView>();
        public List<MoveView> History = new List<MoveView>();

        /// <summary>revealAll = true は終局後の感想戦用。</summary>
        public static PlayerView From(GameState s, int viewer, bool revealAll = false)
        {
            bool reveal = revealAll && s.Phase == GamePhase.Finished;
            var v = new PlayerView
            {
                Viewer = viewer,
                Phase = s.Phase,
                CurrentPlayer = s.CurrentPlayer,
                Result = s.Result,
                EndReason = s.EndReason,
            };
            foreach (var p in s.Pieces)
            {
                v.Pieces.Add(new PieceView
                {
                    Id = p.Id,
                    Owner = p.Owner,
                    Node = p.Node,
                    HasMoved = p.HasMoved,
                    TypeId = reveal || p.Owner == viewer ? p.TypeId : HiddenType,
                });
            }
            foreach (var r in s.History)
            {
                var mv = new MoveView
                {
                    Ply = r.Ply,
                    Player = r.Player,
                    PieceId = r.Move.PieceId,
                    FromNode = r.FromNode,
                    ToNode = r.Move.ToNode,
                    HadBattle = r.Battle != null,
                    AttackerType = HiddenType,
                    DefenderType = HiddenType,
                };
                if (r.Battle != null)
                {
                    var b = r.Battle;
                    mv.AttackerId = b.AttackerId;
                    mv.DefenderId = b.DefenderId;
                    mv.Result = b.Result;
                    if (reveal || s.Pieces[b.AttackerId].Owner == viewer) mv.AttackerType = b.AttackerType;
                    if (reveal || s.Pieces[b.DefenderId].Owner == viewer) mv.DefenderType = b.DefenderType;
                }
                v.History.Add(mv);
            }
            return v;
        }
    

/// <summary>1手分を viewer から見える形にする（サーバーが差分を送るときに使う）。</summary>
        public static MoveView ProjectMove(GameState s, MoveRecord r, int viewer)
        {
            var mv = new MoveView
            {
                Ply = r.Ply,
                Player = r.Player,
                PieceId = r.Move.PieceId,
                FromNode = r.FromNode,
                ToNode = r.Move.ToNode,
                HadBattle = r.Battle != null,
                AttackerType = Visibility.HiddenType,
                DefenderType = Visibility.HiddenType,
            };
            if (r.Battle != null)
            {
                var b = r.Battle;
                mv.AttackerId = b.AttackerId;
                mv.DefenderId = b.DefenderId;
                mv.Result = b.Result;
                if (s.Pieces[b.AttackerId].Owner == viewer) mv.AttackerType = b.AttackerType;
                if (s.Pieces[b.DefenderId].Owner == viewer) mv.DefenderType = b.DefenderType;
            }
            return mv;
        }

        /// <summary>
        /// サーバーから届いた1手を、手元の見え方に反映する。盤面全体を送らずに済ませるためのもの。
        /// </summary>
        public void ApplyMove(MoveView m, int currentPlayer, GameResult result, EndReason reason)
        {
            History.Add(m);
            var mover = Pieces[m.PieceId];
            mover.HasMoved = true;
            if (!m.HadBattle)
            {
                mover.Node = m.ToNode;
            }
            else
            {
                var defender = Pieces[m.DefenderId];
                switch (m.Result)
                {
                    case BattleResult.AttackerSurvives:
                        defender.Node = -1;
                        mover.Node = m.ToNode;
                        break;
                    case BattleResult.DefenderSurvives:
                        mover.Node = -1;
                        break;
                    default:
                        defender.Node = -1;
                        mover.Node = -1;
                        break;
                }
            }
            CurrentPlayer = currentPlayer;
            Result = result;
            EndReason = reason;
            if (result != GameResult.Ongoing) Phase = GamePhase.Finished;
        }

        /// <summary>
        /// 合法手を調べるための局面。相手の駒種は分からないので仮の値だが、自分の駒の動きは正しく求まる。
        /// </summary>
        public GameState ToMoveState(RuleSet rules, BoardTopology topo)
        {
            int n = Pieces.Count;
            var owners = new int[n];
            var types = new int[n];
            var nodes = new int[n];
            var moved = new bool[n];
            for (int i = 0; i < n; i++)
            {
                var p = Pieces[i];
                owners[i] = p.Owner;
                types[i] = p.TypeId >= 0 ? p.TypeId : 0;
                nodes[i] = p.Node;
                moved[i] = p.HasMoved;
            }
            return GameState.FromSnapshot(rules, topo, owners, types, nodes, moved, CurrentPlayer);
        }
}
}
