using System;
using System.Collections.Generic;
using NUnit.Framework;
using GunjinShogi.Core;
using static GunjinShogi.Core.StandardPieces;

namespace GunjinShogi.Core.Tests
{
    /// <summary>詳細ローカルルール（既定オフ）の動作。</summary>
    public class DetailRuleTests
    {
        static PositionedPiece P0(int type, int x, int y, bool moved = false) => new PositionedPiece(0, type, x, y, moved);
        static PositionedPiece P1(int type, int x, int y, bool moved = false) => new PositionedPiece(1, type, x, y, moved);

        static GameState Make(StandardRuleOptions o, params PositionedPiece[] pieces)
        {
            var list = new List<PositionedPiece>(pieces) { P0(Captain, 5, 0), P1(Captain, 0, 7) };
            return GameState.FromPositions(StandardRules.Create23(o), list);
        }

        static int N(GameState s, int x, int y) => s.Topology.NodeOf(new Coord(x, y));
        static MoveRecord MoveTo(GameState s, int fx, int fy, int tx, int ty) =>
            s.ApplyMove(new Move(s.OccupantOf(N(s, fx, fy)), N(s, tx, ty)));

        [Test]
        public void PlaneLosesToEngineer()
        {
            var t = StandardRules.Create23(new StandardRuleOptions { PlaneLosesToEngineer = true }).Combat;
            Assert.AreEqual(Outcome.Win, t.Get(Engineer, Plane));
            Assert.AreEqual(Outcome.Win, StandardRules.Create23().Combat.Get(Plane, Engineer));
        }

        [Test]
        public void CavalryAsMine_CopiesMineRow()
        {
            var t = StandardRules.Create23(new StandardRuleOptions { CavalryAsMine = true }).Combat;
            Assert.AreEqual(Outcome.Both, t.Get(Cavalry, General));
            Assert.AreEqual(Outcome.Both, t.Get(Cavalry, Tank));
            Assert.AreEqual(Outcome.Lose, t.Get(Cavalry, Plane));
            Assert.AreEqual(Outcome.Lose, t.Get(Cavalry, Engineer));
            Assert.AreEqual(Outcome.Both, t.Get(Cavalry, Mine));
            Assert.AreEqual(Outcome.Both, t.Get(Cavalry, Cavalry));
        }

        [Test]
        public void SecondLtCapturingFlag_Wins()
        {
            var o = new StandardRuleOptions { SecondLtFlagWin = true };
            var s = Make(o, P0(SecondLt, 1, 4), P1(Flag, 1, 5));
            MoveTo(s, 1, 4, 1, 5);
            Assert.AreEqual(GameResult.Player0Win, s.Result);
            Assert.AreEqual(EndReason.FlagCaptured, s.EndReason);

            var off = Make(null, P0(SecondLt, 1, 4), P1(Flag, 1, 5));
            MoveTo(off, 1, 4, 1, 5);
            Assert.AreEqual(GameResult.Ongoing, off.Result);

            // 軍旗の後ろが少将なら少尉が負けるので勝ちにはならない
            var backed = Make(o, P0(SecondLt, 1, 4), P1(Flag, 1, 5), P1(MajGeneral, 1, 6));
            MoveTo(backed, 1, 4, 1, 5);
            Assert.AreEqual(GameResult.Ongoing, backed.Result);
        }

        [Test]
        public void HqPlacementRules()
        {
            var o = new StandardRuleOptions { NoMineOrPlaneInHq = true, GeneralRequiredInHq = true };
            var rules = StandardRules.Create23(o);
            var topo = new BoardTopology(rules.Board);
            var rng = new Random(4);
            int hq = topo.HqNode(0);
            for (int i = 0; i < 100; i++)
            {
                var setup = RandomSetup.Generate(rules, topo, 0, rng);
                CollectionAssert.IsEmpty(SetupValidator.Validate(rules, topo, 0, setup));
                int type = setup.Find(p => p.Node == hq).TypeId;
                Assert.IsTrue(type == General || type == LtGeneral || type == MajGeneral);
            }

            var bad = RandomSetup.Generate(StandardRules.Create23(), topo, 0, rng);
            int mineIdx = bad.FindIndex(p => p.TypeId == Mine);
            int hqIdx = bad.FindIndex(p => p.Node == hq);
            var m = bad[mineIdx]; var h = bad[hqIdx];
            bad[mineIdx] = new Placement(m.TypeId, h.Node);
            bad[hqIdx] = new Placement(h.TypeId, m.Node);
            Assert.IsNotEmpty(SetupValidator.Validate(rules, topo, 0, bad));
        }

        [Test]
        public void HqPieceImmobile_OnlyUntilItMoves()
        {
            var o = new StandardRuleOptions { HqPieceImmobile = true };
            var s = Make(o, P0(General, 2, 0), P1(Major, 5, 6));
            Assert.AreEqual(0, MoveGenerator.ForPiece(s, s.Pieces[s.OccupantOf(N(s, 2, 0))]).Count);

            var off = Make(null, P0(General, 2, 0), P1(Major, 5, 6));
            Assert.Greater(MoveGenerator.ForPiece(off, off.Pieces[off.OccupantOf(N(off, 2, 0))]).Count, 0);
        }

        [Test]
        public void OwnHqClosed_BlocksEnteringAndPassing()
        {
            var o = new StandardRuleOptions { OwnHqClosed = true };
            var s = Make(o, P0(Major, 2, 1), P0(Engineer, 1, 0), P1(Major, 5, 6));
            var major = MoveGenerator.ForPiece(s, s.Pieces[s.OccupantOf(N(s, 2, 1))]);
            Assert.IsFalse(major.Exists(m => m.ToNode == s.Topology.HqNode(0)), "自陣の総司令部に入れない");
            var eng = MoveGenerator.ForPiece(s, s.Pieces[s.OccupantOf(N(s, 1, 0))]);
            Assert.IsFalse(eng.Exists(m => m.ToNode == N(s, 4, 0)), "総司令部を通り抜けられない");

            // 相手の総司令部には入れる
            var s2 = Make(o, P0(Major, 2, 6), P1(Major, 5, 6));
            MoveTo(s2, 2, 6, 2, 7);
            Assert.AreEqual(GameResult.Player0Win, s2.Result);
        }

        [Test]
        public void RandomGames_WithAllDetailRules_Terminate()
        {
            var o = new StandardRuleOptions
            {
                PlaneLosesToEngineer = true, CavalryAsMine = true, SecondLtFlagWin = true,
                NoMineOrPlaneInHq = true, GeneralRequiredInHq = true, HqPieceImmobile = true, OwnHqClosed = true,
            };
            var rng = new Random(8);
            for (int g = 0; g < 60; g++)
            {
                var rules = StandardRules.Create23(o);
                rules.MaxPlies = 600;
                var s = new GameState(rules);
                for (int p = 0; p < 2; p++)
                    CollectionAssert.IsEmpty(s.SubmitSetup(p, RandomSetup.Generate(rules, s.Topology, p, rng)));
                while (s.Phase == GamePhase.Playing)
                {
                    var ms = s.GetLegalMoves();
                    Assert.IsNotEmpty(ms);
                    s.ApplyMove(ms[rng.Next(ms.Count)]);
                }
            }
        }
    }
}
