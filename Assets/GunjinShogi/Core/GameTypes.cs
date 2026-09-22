using System;

namespace GunjinShogi.Core
{
    public enum GamePhase : byte { Setup, Playing, Finished }
    public enum GameResult : byte { Ongoing, Player0Win, Player1Win, Draw }
    public enum EndReason : byte { None, HqOccupied, NoLegalMoves, CommanderLost, Repetition, MaxPlies, Resign, FlagCaptured }
    public enum BattleResult : byte { AttackerSurvives, DefenderSurvives, BothRemoved }

    public sealed class Piece
    {
        public int Id;
        public int Owner;
        public int TypeId;
        /// <summary>-1 = 盤上から除去済み。</summary>
        public int Node = -1;
        public bool HasMoved;

        public bool Alive => Node >= 0;
        public Piece Clone() => (Piece)MemberwiseClone();
    }

    public struct Placement
    {
        public int TypeId;
        public int Node;
        public Placement(int typeId, int node) { TypeId = typeId; Node = node; }
    }

    /// <summary>任意局面を作るときの駒指定（テスト・詰め問題・解析用）。</summary>
    public struct PositionedPiece
    {
        public int Owner;
        public int TypeId;
        public Coord Cell;
        public bool HasMoved;
        public PositionedPiece(int owner, int typeId, int x, int y, bool hasMoved = false)
        {
            Owner = owner; TypeId = typeId; Cell = new Coord(x, y); HasMoved = hasMoved;
        }
    }

    public struct Move : IEquatable<Move>
    {
        public int PieceId;
        public int ToNode;

        public Move(int pieceId, int toNode) { PieceId = pieceId; ToNode = toNode; }

        public bool Equals(Move o) => PieceId == o.PieceId && ToNode == o.ToNode;
        public override bool Equals(object obj) => obj is Move m && Equals(m);
        public override int GetHashCode() => (PieceId * 397) ^ ToNode;
        public override string ToString() => $"#{PieceId}->{ToNode}";
    }

    public sealed class BattleRecord
    {
        public int AttackerId;
        public int DefenderId;
        public int AttackerType;
        public int DefenderType;
        /// <summary>軍旗を解決した後の実効駒種（-1 = 必ず負ける）。</summary>
        public int AttackerEffectiveType;
        public int DefenderEffectiveType;
        public BattleResult Result;
    }

    public sealed class MoveRecord
    {
        public int Ply;
        public int Player;
        public Move Move;
        public int FromNode;
        /// <summary>戦闘が無ければ null。</summary>
        public BattleRecord Battle;
        public GameResult ResultAfter;
        public EndReason EndReasonAfter;
    }
}
