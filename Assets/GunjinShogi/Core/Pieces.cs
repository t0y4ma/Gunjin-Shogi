using System;
using System.Collections.Generic;

namespace GunjinShogi.Core
{
    /// <summary>戦闘結果。勝敗表の「行の駒」から見た結果。</summary>
    public enum Outcome : byte { Lose = 0, Win = 1, Both = 2 }

    /// <summary>駒の持ち主から見た方向。</summary>
    public enum MoveDir : byte { Forward, Back, Left, Right }

    [Flags]
    public enum DirMask : byte
    {
        None = 0,
        Forward = 1,
        Back = 2,
        Left = 4,
        Right = 8,
        Sideways = Left | Right,
        Vertical = Forward | Back,
        Orthogonal = Forward | Back | Left | Right,
    }

    /// <summary>勝敗表では表現できない特殊能力。</summary>
    public enum PieceAbility : byte
    {
        None = 0,
        /// <summary>すぐ後ろの味方駒と同じ強さになる（軍旗）。</summary>
        MimicBehind = 1,
    }

    /// <summary>移動ルール1本。方向の集合 × 最大歩数 × 飛び越し可否。</summary>
    [Serializable]
    public sealed class MoveRule
    {
        public DirMask Directions;
        /// <summary>0 = 無制限。</summary>
        public int MaxSteps;
        public bool CanJump;

        public MoveRule() { }

        public MoveRule(DirMask directions, int maxSteps, bool canJump = false)
        {
            Directions = directions;
            MaxSteps = maxSteps;
            CanJump = canJump;
        }

        public MoveRule Clone() => new MoveRule(Directions, MaxSteps, CanJump);
    }

    /// <summary>駒の種類の定義。ルール編集ではこれを書き換える。</summary>
    [Serializable]
    public sealed class PieceDefinition
    {
        public int Id;
        public string Name;
        public int Count;
        public List<MoveRule> Moves = new List<MoveRule>();
        /// <summary>突入口を通らずに陣をまたげる（飛行機）。</summary>
        public bool IgnoresGates;
        /// <summary>敵の総司令部に入ると占領になる。</summary>
        public bool CanOccupyHQ;
        /// <summary>「大将が倒されたら負け」ルールの対象。</summary>
        public bool IsCommander;
        public PieceAbility Ability;
        /// <summary>突入口に面したマスへの配置禁止。</summary>
        public bool ForbiddenOnGateFront;
        /// <summary>自陣最後列への配置禁止。</summary>
        public bool ForbiddenOnBackRow;
        /// <summary>自陣の総司令部への配置禁止。</summary>
        public bool ForbiddenInHq;
        /// <summary>軍旗（MimicBehind）を倒すとその場で勝ち（少尉のローカルルール）。</summary>
        public bool CapturesFlagToWin;

        public bool IsMobile => Moves != null && Moves.Count > 0;

        public PieceDefinition Clone()
        {
            var c = (PieceDefinition)MemberwiseClone();
            c.Moves = new List<MoveRule>();
            if (Moves != null) foreach (var m in Moves) c.Moves.Add(m.Clone());
            return c;
        }

        public override string ToString() => Name;
    }

    /// <summary>標準（23枚型）の駒ID。</summary>
    public static class StandardPieces
    {
        public const int General = 0;      // 大将
        public const int LtGeneral = 1;    // 中将
        public const int MajGeneral = 2;   // 少将
        public const int Colonel = 3;      // 大佐
        public const int LtColonel = 4;    // 中佐
        public const int Major = 5;        // 少佐
        public const int Captain = 6;      // 大尉
        public const int Lieutenant = 7;   // 中尉
        public const int SecondLt = 8;     // 少尉
        public const int Plane = 9;        // 飛行機
        public const int Tank = 10;        // タンク
        public const int Cavalry = 11;     // 騎兵
        public const int Engineer = 12;    // 工兵
        public const int Spy = 13;         // スパイ
        public const int Mine = 14;        // 地雷
        public const int Flag = 15;        // 軍旗
        public const int TypeCount = 16;
    }
}
