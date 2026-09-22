using System;
using System.Collections.Generic;
using NUnit.Framework;
using GunjinShogi.Core;
using static GunjinShogi.Core.StandardPieces;

namespace GunjinShogi.Core.Tests
{
    public class GameRulesTests
    {
        // 盤：横6×縦8。プレイヤー0の陣は y=0..3（前方 +y）、プレイヤー1は y=4..7。
        // 突入口は x=1 と x=4。総司令部は (2,0)(3,0) と (2,7)(3,7)。

        static PositionedPiece P0(int type, int x, int y) => new PositionedPiece(0, type, x, y);
        static PositionedPiece P1(int type, int x, int y) => new PositionedPiece(1, type, x, y);

        /// <summary>両者に遠くで動ける駒を1枚ずつ足して「動ける駒なし」で即終局しないようにする。</summary>
        static GameState Make(StandardRuleOptions o, params PositionedPiece[] pieces)
        {
            var list = new List<PositionedPiece>(pieces)
            {
                P0(Captain, 5, 0),
                P1(Captain, 0, 7),
            };
            return GameState.FromPositions(StandardRules.Create23(o), list);
        }

        static GameState Make(params PositionedPiece[] pieces) => Make(null, pieces);

        static int N(GameState s, int x, int y) => s.Topology.NodeOf(new Coord(x, y));

        static int IdAt(GameState s, int x, int y) => s.OccupantOf(N(s, x, y));

        static MoveRecord MoveTo(GameState s, int fromX, int fromY, int toX, int toY) =>
            s.ApplyMove(new Move(IdAt(s, fromX, fromY), N(s, toX, toY)));

        static HashSet<int> Targets(GameState s, int x, int y)
        {
            var set = new HashSet<int>();
            foreach (var m in MoveGenerator.ForPiece(s, s.Pieces[IdAt(s, x, y)])) set.Add(m.ToNode);
            return set;
        }

        // ───────── 軍旗 ─────────

        [Test]
        public void Flag_BackedByMajGeneral_BeatsTank()
        {
            var s = Make(P0(Tank, 0, 4), P1(Flag, 0, 5), P1(MajGeneral, 0, 6));
            var r = MoveTo(s, 0, 4, 0, 5);
            Assert.AreEqual(BattleResult.DefenderSurvives, r.Battle.Result);
            Assert.AreEqual(MajGeneral, r.Battle.DefenderEffectiveType);
            Assert.AreEqual(Flag, s.Pieces[IdAt(s, 0, 5)].TypeId);
        }

        [Test]
        public void Flag_BackedByColonel_LosesToTank_ColonelStays()
        {
            var s = Make(P0(Tank, 0, 4), P1(Flag, 0, 5), P1(Colonel, 0, 6));
            var r = MoveTo(s, 0, 4, 0, 5);
            Assert.AreEqual(BattleResult.AttackerSurvives, r.Battle.Result);
            Assert.AreEqual(Tank, s.Pieces[IdAt(s, 0, 5)].TypeId);
            Assert.AreEqual(Colonel, s.Pieces[IdAt(s, 0, 6)].TypeId);
        }

        [Test]
        public void Flag_BackedByMine_TradesWithTank_MineStays()
        {
            var s = Make(P0(Tank, 0, 4), P1(Flag, 0, 5), P1(Mine, 0, 6));
            var r = MoveTo(s, 0, 4, 0, 5);
            Assert.AreEqual(BattleResult.BothRemoved, r.Battle.Result);
            Assert.AreEqual(-1, IdAt(s, 0, 5));
            Assert.AreEqual(Mine, s.Pieces[IdAt(s, 0, 6)].TypeId);
        }

        [Test]
        public void Flag_WithNothingBehind_OrOnBackRow_AlwaysLoses()
        {
            var s = Make(P0(Spy, 1, 4), P1(Flag, 1, 5));
            Assert.AreEqual(BattleResult.AttackerSurvives, MoveTo(s, 1, 4, 1, 5).Battle.Result);

            var s2 = Make(P0(Spy, 4, 6), P1(Flag, 4, 7));
            Assert.AreEqual(BattleResult.AttackerSurvives, MoveTo(s2, 4, 6, 4, 7).Battle.Result);
        }

        [Test]
        public void Flag_BackedByEnemy_AlwaysLoses()
        {
            var s = Make(P0(Spy, 1, 4), P1(Flag, 1, 5), P0(General, 1, 6));
            Assert.AreEqual(BattleResult.AttackerSurvives, MoveTo(s, 1, 4, 1, 5).Battle.Result);
        }

        // ───────── 移動 ─────────

        [Test]
        public void Tank_MovesTwoForward_ThroughGate()
        {
            var s = Make(P0(Tank, 1, 2));
            var t = Targets(s, 1, 2);
            Assert.IsTrue(t.Contains(N(s, 1, 3)));
            Assert.IsTrue(t.Contains(N(s, 1, 4)), "突入口経由で2マス前進");
            Assert.IsTrue(t.Contains(N(s, 1, 1)));
            Assert.IsTrue(t.Contains(N(s, 0, 2)));
            Assert.IsTrue(t.Contains(N(s, 2, 2)));
            Assert.AreEqual(5, t.Count);
        }

        [Test]
        public void NonPlane_CannotCrossRiverOutsideGate()
        {
            var s = Make(P0(Tank, 0, 2), P0(Engineer, 3, 1));
            Assert.IsFalse(Targets(s, 0, 2).Contains(N(s, 0, 4)));
            var eng = Targets(s, 3, 1);
            Assert.IsTrue(eng.Contains(N(s, 3, 3)));
            Assert.IsFalse(eng.Contains(N(s, 3, 4)));
        }

        [Test]
        public void Tank_BlockedByOwnPiece()
        {
            var s = Make(P0(Tank, 1, 2), P0(Major, 1, 3));
            var t = Targets(s, 1, 2);
            Assert.IsFalse(t.Contains(N(s, 1, 3)));
            Assert.IsFalse(t.Contains(N(s, 1, 4)));
        }

        [Test]
        public void Plane_JumpsOverPieces_AndIgnoresGates()
        {
            var s = Make(P0(Plane, 0, 1), P0(Major, 0, 2), P1(Major, 0, 5));
            var t = Targets(s, 0, 1);
            Assert.IsFalse(t.Contains(N(s, 0, 2)), "味方の上には止まれない");
            Assert.IsTrue(t.Contains(N(s, 0, 3)));
            Assert.IsTrue(t.Contains(N(s, 0, 4)), "突入口以外で川を越えられる");
            Assert.IsTrue(t.Contains(N(s, 0, 5)), "敵を攻撃できる");
            Assert.IsTrue(t.Contains(N(s, 0, 6)), "敵を飛び越せる");
            Assert.IsTrue(t.Contains(N(s, 1, 1)), "横に1マス（既定）");
            Assert.IsFalse(t.Contains(N(s, 2, 1)), "横は1マスまで");
        }

        [Test]
        public void Plane_SidewaysOption_None()
        {
            var s = Make(new StandardRuleOptions { PlaneSideways = PlaneSideways.None }, P0(Plane, 0, 1));
            Assert.IsFalse(Targets(s, 0, 1).Contains(N(s, 1, 1)));
        }

        [Test]
        public void Engineer_SlidesUntilBlocked()
        {
            var s = Make(P0(Engineer, 0, 3), P0(Major, 3, 3));
            var t = Targets(s, 0, 3);
            Assert.IsTrue(t.Contains(N(s, 1, 3)));
            Assert.IsTrue(t.Contains(N(s, 2, 3)));
            Assert.IsFalse(t.Contains(N(s, 3, 3)));
            Assert.IsFalse(t.Contains(N(s, 4, 3)));
            Assert.IsTrue(t.Contains(N(s, 0, 1)));
        }

        [Test]
        public void MineAndFlag_CannotMove_ByDefault()
        {
            var s = Make(P0(Mine, 0, 2), P0(Flag, 1, 1));
            Assert.AreEqual(0, Targets(s, 0, 2).Count);
            Assert.AreEqual(0, Targets(s, 1, 1).Count);

            var s2 = Make(new StandardRuleOptions { FlagCanMove = true }, P0(Flag, 1, 1));
            Assert.Greater(Targets(s2, 1, 1).Count, 0);
        }

        // ───────── 勝利条件 ─────────

        [Test]
        public void Colonel_OccupiesHq_Wins()
        {
            var s = Make(P0(Colonel, 3, 6));
            MoveTo(s, 3, 6, 3, 7);
            Assert.AreEqual(GameResult.Player0Win, s.Result);
            Assert.AreEqual(EndReason.HqOccupied, s.EndReason);
        }

        [Test]
        public void Cavalry_EnteringHq_DoesNotWin()
        {
            var s = Make(P0(Cavalry, 2, 6));
            MoveTo(s, 2, 6, 2, 7);
            Assert.AreEqual(GameResult.Ongoing, s.Result);
        }

        [Test]
        public void Cavalry_CanOccupy_WithAllExceptPlaneOption()
        {
            var s = Make(new StandardRuleOptions { HqOccupiers = HqOccupiers.AllExceptPlane }, P0(Cavalry, 2, 6));
            MoveTo(s, 2, 6, 2, 7);
            Assert.AreEqual(GameResult.Player0Win, s.Result);
        }

        [Test]
        public void MineInHq_StopsOccupation()
        {
            var s = Make(P0(LtGeneral, 2, 6), P1(Mine, 2, 7));
            var r = MoveTo(s, 2, 6, 2, 7);
            Assert.AreEqual(BattleResult.BothRemoved, r.Battle.Result);
            Assert.AreEqual(GameResult.Ongoing, s.Result);
        }

        [Test]
        public void EliminatingAllMovablePieces_Wins()
        {
            var s = GameState.FromPositions(StandardRules.Create23(), new List<PositionedPiece>
            {
                P0(General, 1, 4), P0(Captain, 5, 0),
                P1(Major, 1, 5), P1(Mine, 4, 6),
            });
            MoveTo(s, 1, 4, 1, 5);
            Assert.AreEqual(GameResult.Player0Win, s.Result);
            Assert.AreEqual(EndReason.NoLegalMoves, s.EndReason);
        }

        [Test]
        public void CommanderLoss_Option()
        {
            var s = Make(new StandardRuleOptions { CommanderLossLoses = true }, P0(Spy, 1, 4), P1(General, 1, 5));
            MoveTo(s, 1, 4, 1, 5);
            Assert.AreEqual(GameResult.Player0Win, s.Result);
            Assert.AreEqual(EndReason.CommanderLost, s.EndReason);
        }

        [Test]
        public void Repetition_IsDraw()
        {
            var s = Make(P0(Major, 0, 1), P1(Major, 5, 6));
            for (int i = 0; i < 2 && s.Result == GameResult.Ongoing; i++)
            {
                MoveTo(s, 0, 1, 0, 2);
                MoveTo(s, 5, 6, 5, 5);
                MoveTo(s, 0, 2, 0, 1);
                MoveTo(s, 5, 5, 5, 6);
            }
            Assert.AreEqual(GameResult.Draw, s.Result);
            Assert.AreEqual(EndReason.Repetition, s.EndReason);
        }

        // ───────── 配置 ─────────

        [Test]
        public void RandomSetups_AreAlwaysValid()
        {
            var rules = StandardRules.Create23();
            var topo = new BoardTopology(rules.Board);
            var rng = new Random(1);
            for (int i = 0; i < 200; i++)
            for (int p = 0; p < 2; p++)
                CollectionAssert.IsEmpty(SetupValidator.Validate(rules, topo, p, RandomSetup.Generate(rules, topo, p, rng)));
        }

        [Test]
        public void MineOnGate_And_FlagOnBackRow_AreRejected()
        {
            var rules = StandardRules.Create23();
            var topo = new BoardTopology(rules.Board);
            var setup = RandomSetup.Generate(rules, topo, 0, new Random(3));
            int gateNode = topo.NodeOf(new Coord(1, 3));
            int backNode = topo.NodeOf(new Coord(0, 0));

            var withMineOnGate = Swap(setup, Mine, gateNode);
            Assert.IsNotEmpty(SetupValidator.Validate(rules, topo, 0, withMineOnGate));

            var withFlagOnBack = Swap(setup, Flag, backNode);
            Assert.IsNotEmpty(SetupValidator.Validate(rules, topo, 0, withFlagOnBack));

            var allowed = StandardRules.Create23(new StandardRuleOptions { AllowFlagOnBackRow = true });
            CollectionAssert.IsEmpty(SetupValidator.Validate(allowed, topo, 0, withFlagOnBack));
        }

        static List<Placement> Swap(List<Placement> setup, int type, int node)
        {
            var list = new List<Placement>(setup);
            int a = list.FindIndex(p => p.TypeId == type);
            int b = list.FindIndex(p => p.Node == node);
            var pa = list[a];
            var pb = list[b];
            list[a] = new Placement(pa.TypeId, pb.Node);
            list[b] = new Placement(pb.TypeId, pa.Node);
            return list;
        }

        [Test]
        public void PieceIds_FollowNodeOrder_NotType()
        {
            var s = NewRandomGame(new Random(5));
            int prev = -1;
            foreach (var p in s.Pieces)
            {
                if (p.Owner != 0) break;
                Assert.Greater(p.Node, prev);
                prev = p.Node;
            }
        }

        // ───────── 情報秘匿 ─────────

        [Test]
        public void PlayerView_HidesEnemyTypes()
        {
            var rng = new Random(7);
            var s = NewRandomGame(rng);
            PlayRandom(s, rng, 60);

            var v = PlayerView.From(s, 0);
            foreach (var p in v.Pieces)
                Assert.AreEqual(p.Owner == 0, p.TypeId != Visibility.HiddenType);
            foreach (var h in v.History)
            {
                if (!h.HadBattle) continue;
                Assert.AreEqual(s.Pieces[h.AttackerId].Owner == 0, h.AttackerType != Visibility.HiddenType);
                Assert.AreEqual(s.Pieces[h.DefenderId].Owner == 0, h.DefenderType != Visibility.HiddenType);
            }

            var notRevealed = PlayerView.From(s, 0, revealAll: true);
            if (s.Phase != GamePhase.Finished)
                foreach (var p in notRevealed.Pieces)
                    if (p.Owner == 1) Assert.AreEqual(Visibility.HiddenType, p.TypeId, "対局中は revealAll でも公開しない");
        }

        // ───────── ランダム対局（不変条件・決定性） ─────────

        [Test]
        public void RandomGames_KeepInvariants_AndTerminate()
        {
            var o = new StandardRuleOptions();
            var rng = new Random(11);
            int finished = 0;
            for (int g = 0; g < 200; g++)
            {
                var rules = StandardRules.Create23(o);
                rules.MaxPlies = 800;
                var s = NewRandomGame(rng, rules);
                PlayRandom(s, rng, 2000, checkInvariants: true);
                Assert.AreEqual(GamePhase.Finished, s.Phase, $"対局 {g} が終わらない");
                finished++;
            }
            Assert.AreEqual(200, finished);
        }

        [Test]
        public void Replay_IsDeterministic()
        {
            var rules = StandardRules.Create23();
            var topo = new BoardTopology(rules.Board);
            var rng = new Random(21);
            var s0 = RandomSetup.Generate(rules, topo, 0, rng);
            var s1 = RandomSetup.Generate(rules, topo, 1, rng);

            var a = new GameState(rules);
            a.SubmitSetup(0, s0);
            a.SubmitSetup(1, s1);
            PlayRandom(a, rng, 300);

            var b = new GameState(rules);
            b.SubmitSetup(1, s1);
            b.SubmitSetup(0, s0);
            foreach (var r in a.History) b.ApplyMove(r.Move);

            Assert.AreEqual(a.Result, b.Result);
            for (int i = 0; i < a.Pieces.Count; i++)
                Assert.AreEqual(a.Pieces[i].Node, b.Pieces[i].Node);
        }

        // ───────── ヘルパー ─────────

        static GameState NewRandomGame(Random rng, RuleSet rules = null)
        {
            rules = rules ?? StandardRules.Create23();
            var s = new GameState(rules);
            for (int p = 0; p < 2; p++)
                CollectionAssert.IsEmpty(s.SubmitSetup(p, RandomSetup.Generate(rules, s.Topology, p, rng)));
            Assert.AreEqual(GamePhase.Playing, s.Phase);
            return s;
        }

        static void PlayRandom(GameState s, Random rng, int maxPlies, bool checkInvariants = false)
        {
            for (int i = 0; i < maxPlies && s.Phase == GamePhase.Playing; i++)
            {
                var moves = s.GetLegalMoves();
                Assert.IsNotEmpty(moves, "対局中に合法手が無い");
                s.ApplyMove(moves[rng.Next(moves.Count)]);
                if (checkInvariants) CheckInvariants(s);
            }
        }

        static void CheckInvariants(GameState s)
        {
            var seen = new HashSet<int>();
            foreach (var p in s.Pieces)
            {
                if (!p.Alive) continue;
                Assert.IsTrue(seen.Add(p.Node), "1マスに複数の駒");
                Assert.AreEqual(p.Id, s.OccupantOf(p.Node), "占有表と駒位置が不一致");
                if (!s.Rules.Piece(p.TypeId).IsMobile) Assert.IsFalse(p.HasMoved, "動けない駒が動いた");
            }
            for (int n = 0; n < s.Topology.NodeCount; n++)
            {
                int occ = s.OccupantOf(n);
                if (occ >= 0) Assert.AreEqual(n, s.Pieces[occ].Node);
            }
        }
    }
}
