using System;
using System.Collections.Generic;
using static GunjinShogi.Core.StandardPieces;

namespace GunjinShogi.Core
{
    public enum PlaneSideways : byte { None, OneStep, Unlimited }
    public enum TankCavalryMove : byte { Standard, OneStepAll, TwoStepAll }
    public enum HqOccupiers : byte { OfficersOnly, AllExceptPlane }

    /// <summary>
    /// ローカルルールのオン・オフ（第2層）。値を変えると StandardRules.Create23 が
    /// 基本データにパッチを当てた RuleSet を作る。
    /// </summary>
    [Serializable]
    public sealed class StandardRuleOptions
    {
        /// <summary>タンクが工兵に勝つ（TVゲームで事実上の標準）。既定ON。</summary>
        public bool TankBeatsEngineer = true;
        /// <summary>地雷が勝っても盤に残る。</summary>
        public bool MineSurvivesWin;
        /// <summary>軍旗も1マス動ける。</summary>
        public bool FlagCanMove;
        /// <summary>軍旗を自陣最後列に置ける。</summary>
        public bool AllowFlagOnBackRow;
        public PlaneSideways PlaneSideways = PlaneSideways.OneStep;
        public TankCavalryMove TankCavalryMove = TankCavalryMove.Standard;
        public HqOccupiers HqOccupiers = HqOccupiers.OfficersOnly;
        /// <summary>大将が倒されたら負け。</summary>
        public bool CommanderLossLoses;
        public int RepetitionDrawCount = 3;

        // ─── 詳細ルール（既定はすべてオフ） ───
        /// <summary>飛行機は工兵に負ける。</summary>
        public bool PlaneLosesToEngineer;
        /// <summary>騎兵の強さを地雷と同じにする（動ける地雷）。</summary>
        public bool CavalryAsMine;
        /// <summary>少尉で軍旗を倒したら勝ち。</summary>
        public bool SecondLtFlagWin;
        /// <summary>総司令部に地雷・飛行機を置けない。</summary>
        public bool NoMineOrPlaneInHq;
        /// <summary>総司令部には将官を置かなければならない。</summary>
        public bool GeneralRequiredInHq;
        /// <summary>初期配置で総司令部に置いた駒は動かせない。</summary>
        public bool HqPieceImmobile;
        /// <summary>自軍の駒は自陣の総司令部に入れない（内側から守れない）。</summary>
        public bool OwnHqClosed;

        public StandardRuleOptions Clone() => (StandardRuleOptions)MemberwiseClone();
    }

    public static class StandardRules
    {
        /// <summary>変形8×6（横6×縦8）、突入口2つ、総司令部は最後列中央2マス。</summary>
        public static BoardDefinition CreateBoard23()
        {
            return new BoardDefinition
            {
                Width = 6,
                Height = 8,
                CampRows = 4,
                GateColumns = new[] { 1, 4 },
                HqCells = new[]
                {
                    new[] { new Coord(2, 0), new Coord(3, 0) },
                    new[] { new Coord(2, 7), new Coord(3, 7) },
                },
            };
        }

        public static RuleSet Create23(StandardRuleOptions o = null)
        {
            o = o ?? new StandardRuleOptions();
            var rules = new RuleSet
            {
                Name = "23枚型",
                Board = CreateBoard23(),
                Pieces = CreatePieces(o),
                Combat = CreateCombatTable(o),
                CommanderLossLoses = o.CommanderLossLoses,
                RepetitionDrawCount = o.RepetitionDrawCount,
                HqInitialPieceImmobile = o.HqPieceImmobile,
                OwnHqClosed = o.OwnHqClosed,
            };
            if (o.GeneralRequiredInHq) rules.HqRequiresOneOf.AddRange(new[] { General, LtGeneral, MajGeneral });
            return rules;
        }

        static List<PieceDefinition> CreatePieces(StandardRuleOptions o)
        {
            var ortho1 = new MoveRule(DirMask.Orthogonal, 1);
            var list = new List<PieceDefinition>();

            PieceDefinition Add(int id, string name, int count, params MoveRule[] moves)
            {
                var p = new PieceDefinition { Id = id, Name = name, Count = count };
                foreach (var m in moves) p.Moves.Add(m.Clone());
                list.Add(p);
                return p;
            }

            Add(General, "大将", 1, ortho1).IsCommander = true;
            Add(LtGeneral, "中将", 1, ortho1);
            Add(MajGeneral, "少将", 1, ortho1);
            Add(Colonel, "大佐", 1, ortho1);
            Add(LtColonel, "中佐", 1, ortho1);
            Add(Major, "少佐", 1, ortho1);
            Add(Captain, "大尉", 2, ortho1);
            Add(Lieutenant, "中尉", 2, ortho1);
            Add(SecondLt, "少尉", 2, ortho1);

            var plane = Add(Plane, "飛行機", 2, new MoveRule(DirMask.Vertical, 0, canJump: true));
            plane.IgnoresGates = true;
            if (o.PlaneSideways == PlaneSideways.OneStep) plane.Moves.Add(new MoveRule(DirMask.Sideways, 1));
            else if (o.PlaneSideways == PlaneSideways.Unlimited) plane.Moves.Add(new MoveRule(DirMask.Sideways, 0, canJump: true));

            MoveRule[] tankMoves;
            switch (o.TankCavalryMove)
            {
                case TankCavalryMove.OneStepAll: tankMoves = new[] { ortho1 }; break;
                case TankCavalryMove.TwoStepAll: tankMoves = new[] { new MoveRule(DirMask.Orthogonal, 2) }; break;
                default:
                    tankMoves = new[] { new MoveRule(DirMask.Forward, 2), new MoveRule(DirMask.Back | DirMask.Sideways, 1) };
                    break;
            }
            Add(Tank, "タンク", 2, tankMoves);
            Add(Cavalry, "騎兵", 1, tankMoves);
            Add(Engineer, "工兵", 2, new MoveRule(DirMask.Orthogonal, 0));
            Add(Spy, "スパイ", 1, ortho1);

            var mine = Add(Mine, "地雷", 2);
            mine.ForbiddenOnGateFront = true;

            var flag = o.FlagCanMove ? Add(Flag, "軍旗", 1, ortho1) : Add(Flag, "軍旗", 1);
            flag.Ability = PieceAbility.MimicBehind;
            flag.ForbiddenOnGateFront = true;
            flag.ForbiddenOnBackRow = !o.AllowFlagOnBackRow;

            list[SecondLt].CapturesFlagToWin = o.SecondLtFlagWin;
            if (o.NoMineOrPlaneInHq)
            {
                list[Mine].ForbiddenInHq = true;
                list[Plane].ForbiddenInHq = true;
            }

            foreach (var p in list)
            {
                bool officer = p.Id <= Major;
                p.CanOccupyHQ = o.HqOccupiers == HqOccupiers.OfficersOnly
                    ? officer
                    : p.Id != Plane && p.Id != Mine;
            }
            return list;
        }

        /// <summary>
        /// 一般的な勝敗表。地雷の「勝ち」は爆発して消えるため Both（相打ち）として表現する。
        /// </summary>
        static CombatTable CreateCombatTable(StandardRuleOptions o)
        {
            var t = new CombatTable(TypeCount);

            // 基本駒：階級の高い方が勝つ（同階級は対角線で Both）
            for (int a = General; a <= SecondLt; a++)
            for (int b = a + 1; b <= SecondLt; b++)
                t.SetWin(a, b);

            for (int r = General; r <= SecondLt; r++)
            {
                bool flagOfficer = r <= MajGeneral; // 将官
                t.Set(r, Plane, flagOfficer ? Outcome.Win : Outcome.Lose);
                t.Set(r, Tank, flagOfficer ? Outcome.Win : Outcome.Lose);
                t.SetWin(r, Cavalry);
                t.SetWin(r, Engineer);
                t.Set(r, Spy, r == General ? Outcome.Lose : Outcome.Win);
                t.Set(r, Mine, Outcome.Both);
            }

            t.SetWin(Plane, Tank);
            t.SetWin(Plane, Cavalry);
            t.SetWin(Plane, Engineer);
            t.SetWin(Plane, Spy);
            t.SetWin(Plane, Mine);

            t.SetWin(Tank, Cavalry);
            t.SetWin(Tank, Spy);
            t.Set(Tank, Mine, Outcome.Both);
            if (o.TankBeatsEngineer) t.SetWin(Tank, Engineer);
            else t.SetWin(Engineer, Tank);

            t.SetWin(Cavalry, Engineer);
            t.SetWin(Cavalry, Spy);
            t.Set(Cavalry, Mine, Outcome.Both);

            t.SetWin(Engineer, Spy);
            t.SetWin(Engineer, Mine);

            t.Set(Spy, Mine, Outcome.Both);

            // 軍旗は MimicBehind で解決されるので表は参照されない（念のため全敗にしておく）
            for (int j = 0; j < TypeCount; j++)
                if (j != Flag) t.Set(Flag, j, Outcome.Lose);

            if (o.PlaneLosesToEngineer) t.SetWin(Engineer, Plane);

            if (o.MineSurvivesWin)
                for (int j = 0; j < TypeCount; j++)
                    if (j != Mine && t.Get(Mine, j) == Outcome.Both) t.Set(Mine, j, Outcome.Win);

            // 騎兵は地雷と同じ勝敗（地雷の行を写す）。騎兵同士・騎兵対地雷は相打ち
            if (o.CavalryAsMine)
            {
                for (int j = 0; j < TypeCount; j++)
                    if (j != Cavalry && j != Mine && j != Flag) t.Set(Cavalry, j, t.Get(Mine, j));
                t.Set(Cavalry, Mine, Outcome.Both);
            }

            return t;
        }
    }
}
