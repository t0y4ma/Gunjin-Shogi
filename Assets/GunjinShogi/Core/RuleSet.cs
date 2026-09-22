using System;
using System.Collections.Generic;

namespace GunjinShogi.Core
{
    /// <summary>
    /// 勝敗表。Set(a,b) は常に b 対 a を反転して書き込むので、矛盾した表は作れない。
    /// </summary>
    [Serializable]
    public sealed class CombatTable
    {
        public int Size { get; }
        readonly Outcome[] cells;

        public CombatTable(int size)
        {
            Size = size;
            cells = new Outcome[size * size];
            for (int i = 0; i < size; i++) cells[i * size + i] = Outcome.Both;
        }

        public Outcome Get(int a, int b) => cells[a * Size + b];

        public void Set(int a, int b, Outcome o)
        {
            cells[a * Size + b] = o;
            cells[b * Size + a] = Inverse(o);
        }

        public void SetWin(int winner, int loser) => Set(winner, loser, Outcome.Win);

        public static Outcome Inverse(Outcome o) =>
            o == Outcome.Win ? Outcome.Lose : o == Outcome.Lose ? Outcome.Win : Outcome.Both;

        public CombatTable Clone()
        {
            var c = new CombatTable(Size);
            Array.Copy(cells, c.cells, cells.Length);
            return c;
        }
    }

    /// <summary>1つの対局を支配するルール一式（すべてデータ）。</summary>
    [Serializable]
    public sealed class RuleSet
    {
        public string Name = "";
        public BoardDefinition Board;
        public List<PieceDefinition> Pieces = new List<PieceDefinition>();
        public CombatTable Combat;
        /// <summary>IsCommander の駒が倒されたら負け。</summary>
        public bool CommanderLossLoses;
        /// <summary>同一局面がこの回数現れたら引き分け。0 で無効。</summary>
        public int RepetitionDrawCount = 3;
        /// <summary>総手数の上限（到達で引き分け）。0 で無効。</summary>
        public int MaxPlies;
        /// <summary>空でなければ、自陣の総司令部にはこの中のいずれかの駒種を置かなければならない。</summary>
        public List<int> HqRequiresOneOf = new List<int>();
        /// <summary>初期配置で総司令部に置いた駒は動けない（総司令部にいて一度も動いていない駒）。</summary>
        public bool HqInitialPieceImmobile;
        /// <summary>自軍の駒は自陣の総司令部に入れない（初期配置を除く）。</summary>
        public bool OwnHqClosed;

        public PieceDefinition Piece(int typeId) => Pieces[typeId];

        public int PiecesPerPlayer
        {
            get
            {
                int n = 0;
                foreach (var p in Pieces) n += p.Count;
                return n;
            }
        }

        public RuleSet Clone()
        {
            var c = (RuleSet)MemberwiseClone();
            c.Board = Board.Clone();
            c.Combat = Combat.Clone();
            c.HqRequiresOneOf = new List<int>(HqRequiresOneOf);
            c.Pieces = new List<PieceDefinition>();
            foreach (var p in Pieces) c.Pieces.Add(p.Clone());
            return c;
        }

        /// <summary>整合性チェック。空リストなら妥当。</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            if (Board == null) { errors.Add("盤が未設定です"); return errors; }
            if (Combat == null || Combat.Size != Pieces.Count)
                errors.Add("勝敗表のサイズが駒の種類数と一致しません");
            for (int i = 0; i < Pieces.Count; i++)
                if (Pieces[i].Id != i) errors.Add($"駒ID {Pieces[i].Id} が位置 {i} と一致しません");

            BoardTopology topo = null;
            try { topo = new BoardTopology(Board); }
            catch (Exception e) { errors.Add(e.Message); }

            if (topo != null)
            {
                int campNodes = 0;
                foreach (var _ in topo.CampNodes(0)) campNodes++;
                if (PiecesPerPlayer > campNodes)
                    errors.Add($"駒数 {PiecesPerPlayer} が配置可能マス数 {campNodes} を超えています");
            }

            bool anyOccupier = false, anyMobile = false;
            foreach (var p in Pieces)
            {
                if (p.Count <= 0) continue;
                if (p.IsMobile) anyMobile = true;
                if (p.IsMobile && p.CanOccupyHQ) anyOccupier = true;
            }
            if (!anyMobile) errors.Add("動ける駒がありません");
            if (HqRequiresOneOf.Count > 0 && !HqRequiresOneOf.Exists(t => t >= 0 && t < Pieces.Count && Pieces[t].Count > 0 && !Pieces[t].ForbiddenInHq))
                errors.Add("総司令部に置ける駒がありません");
            if (!anyOccupier) errors.Add("総司令部を占領できる駒がありません");
            return errors;
        }
    }
}
