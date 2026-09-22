using System;
using System.Collections.Generic;
using NUnit.Framework;
using GunjinShogi.Core;
using GunjinShogi.Core.Ai;

namespace GunjinShogi.Core.Tests
{
    public class CpuTests
    {
        static GameState NewGame(RuleSet rules, List<Placement> s0, List<Placement> s1)
        {
            var s = new GameState(rules);
            CollectionAssert.IsEmpty(s.SubmitSetup(0, s0));
            CollectionAssert.IsEmpty(s.SubmitSetup(1, s1));
            return s;
        }

        [Test]
        public void Belief_NeverExcludesTrueType()
        {
            var rng = new Random(3);
            for (int g = 0; g < 20; g++)
            {
                var rules = StandardRules.Create23(new StandardRuleOptions { TankBeatsEngineer = g % 2 == 0 });
                var topo = new BoardTopology(rules.Board);
                var s = NewGame(rules, RandomSetup.Generate(rules, topo, 0, rng), RandomSetup.Generate(rules, topo, 1, rng));
                var belief = new Belief(rules, topo, 0);
                for (int ply = 0; ply < 300 && s.Phase == GamePhase.Playing; ply++)
                {
                    var moves = s.GetLegalMoves();
                    s.ApplyMove(moves[rng.Next(moves.Count)]);
                    belief.Update(PlayerView.From(s, 0));
                    foreach (var p in s.Pieces)
                        if (p.Owner == 1)
                            Assert.IsTrue(belief.IsCandidate(p.Id, p.TypeId),
                                $"対局{g} {ply}手目: 駒#{p.Id}（{rules.Piece(p.TypeId).Name}）が候補から外れた");
                    var nodes = belief.Nodes;
                    foreach (var p in s.Pieces) Assert.AreEqual(p.Node, nodes[p.Id], "再生した位置が一致しない");
                }
            }
        }

        [Test]
        public void Belief_SampleRespectsCounts()
        {
            var rng = new Random(5);
            var rules = StandardRules.Create23();
            var topo = new BoardTopology(rules.Board);
            var s = NewGame(rules, RandomSetup.Generate(rules, topo, 0, rng), RandomSetup.Generate(rules, topo, 1, rng));
            for (int i = 0; i < 80 && s.Phase == GamePhase.Playing; i++)
            {
                var moves = s.GetLegalMoves();
                s.ApplyMove(moves[rng.Next(moves.Count)]);
            }
            var belief = new Belief(rules, topo, 0);
            belief.Update(PlayerView.From(s, 0));
            for (int k = 0; k < 50; k++)
            {
                var types = belief.Sample(rng);
                var counts = new int[rules.Pieces.Count];
                foreach (var p in s.Pieces)
                    if (p.Owner == 1)
                    {
                        counts[types[p.Id]]++;
                        Assert.IsTrue(belief.IsCandidate(p.Id, types[p.Id]));
                    }
                for (int t = 0; t < counts.Length; t++) Assert.AreEqual(rules.Piece(t).Count, counts[t]);
            }
        }

        [Test]
        public void Cpu_SetupIsValid()
        {
            var rules = StandardRules.Create23();
            var topo = new BoardTopology(rules.Board);
            for (int p = 0; p < 2; p++)
                CollectionAssert.IsEmpty(SetupValidator.Validate(rules, topo, p, new CpuPlayer(rules, p, 1).ChooseSetup()));
        }

        [Test]
        public void Cpu_TakesImmediateHqOccupation()
        {
            var rules = StandardRules.Create23();
            var s = GameState.FromPositions(rules, new List<PositionedPiece>
            {
                new PositionedPiece(0, StandardPieces.Colonel, 3, 6, true),
                new PositionedPiece(0, StandardPieces.Captain, 5, 0),
                new PositionedPiece(1, StandardPieces.Captain, 0, 7),
            });
            var cpu = new CpuPlayer(rules, 0, 1, CpuSettings.For(CpuLevel.Normal));
            cpu.Settings.AllowResign = false;
            var move = cpu.ChooseMove(PlayerView.From(s, 0));
            s.ApplyMove(move);
            Assert.AreEqual(GameResult.Player0Win, s.Result);
        }

        [Test]
        public void Cpu_ResignsWhenNoOccupierLeft()
        {
            var rules = StandardRules.Create23();
            // CPU（先手）には尉官と工兵しかなく、相手には何か分からない駒がいる
            var s = GameState.FromPositions(rules, new List<PositionedPiece>
            {
                new PositionedPiece(0, StandardPieces.Captain, 0, 1),
                new PositionedPiece(0, StandardPieces.Engineer, 5, 1),
                new PositionedPiece(1, StandardPieces.Major, 0, 6),
            });
            var thinking = new CpuPlayer(rules, 0, 1).BeginThink(PlayerView.From(s, 0));
            while (!thinking.Step(1000)) { }
            Assert.IsTrue(thinking.Resign);

            var keep = new CpuPlayer(rules, 0, 1, new CpuSettings { AllowResign = false }).BeginThink(PlayerView.From(s, 0));
            while (!keep.Step(1000)) { }
            Assert.IsFalse(keep.Resign);
        }

[Test]
        public void Cpu_ResignsWhenHqCannotBeDefended_OnlyWhenConfident()
        {
            var rules = StandardRules.Create23();
            // 後手（CPU）の総司令部 (2,7)(3,7) は空。相手の駒が (3,6) の大尉を倒して総司令部の隣に来る
            GameState Setup()
            {
                var s = GameState.FromPositions(rules, new List<PositionedPiece>
                {
                    new PositionedPiece(0, StandardPieces.Colonel, 3, 5, true),
                    new PositionedPiece(0, StandardPieces.Captain, 0, 0),
                    new PositionedPiece(1, StandardPieces.Captain, 3, 6),
                    new PositionedPiece(1, StandardPieces.Major, 5, 4, true),
                });
                s.ApplyMove(new Move(s.OccupantOf(s.Topology.NodeOf(new Coord(3, 5))), s.Topology.NodeOf(new Coord(3, 6))));
                Assert.AreEqual(GamePhase.Playing, s.Phase);
                return s;
            }

            // 大尉に勝った駒は「将官・佐官・飛行機・タンク」のどれか。占領できるのは6割ほどなので、
            // 既定（9割）では投了せず、基準を5割に下げれば投了する
            var careful = new CpuPlayer(rules, 1, 3, new CpuSettings { Samples = 40, TimeBudgetMs = 5000 }).BeginThink(PlayerView.From(Setup(), 1));
            while (!careful.Step(1000)) { }
            Assert.IsFalse(careful.Resign, "確信がないのに投了した");

            var eager = new CpuPlayer(rules, 1, 3, new CpuSettings { Samples = 40, TimeBudgetMs = 5000, ResignLossRate = 0.5 }).BeginThink(PlayerView.From(Setup(), 1));
            while (!eager.Step(1000)) { }
            Assert.IsTrue(eager.Resign, "防げないのに投了しなかった");
            Assert.AreEqual("総司令部の占領を防げない", eager.ResignReason);
        }

        [Test]
        public void Cpu_BeatsRandomPlayer()
        {
            var rng = new Random(9);
            int wins = 0, losses = 0, games = 10;
            var settings = new CpuSettings { Samples = 6, TimeBudgetMs = 200, Noise = 1, AllowResign = false };
            for (int g = 0; g < games; g++)
            {
                var rules = StandardRules.Create23();
                rules.MaxPlies = 400;
                int cpuSide = g % 2;
                var cpu = new CpuPlayer(rules, cpuSide, g, settings);
                var topo = new BoardTopology(rules.Board);
                var setups = new List<Placement>[2];
                setups[cpuSide] = cpu.ChooseSetup();
                setups[1 - cpuSide] = RandomSetup.Generate(rules, topo, 1 - cpuSide, rng);
                var s = NewGame(rules, setups[0], setups[1]);
                while (s.Phase == GamePhase.Playing)
                {
                    Move m;
                    if (s.CurrentPlayer == cpuSide) m = cpu.ChooseMove(PlayerView.From(s, cpuSide));
                    else { var ms = s.GetLegalMoves(); m = ms[rng.Next(ms.Count)]; }
                    s.ApplyMove(m);
                }
                bool cpuWon = (s.Result == GameResult.Player0Win && cpuSide == 0) || (s.Result == GameResult.Player1Win && cpuSide == 1);
                bool cpuLost = s.Result != GameResult.Draw && !cpuWon;
                if (cpuWon) wins++;
                if (cpuLost) losses++;
            }
            UnityEngine.Debug.Log($"CPU vs ランダム: {wins}勝 {losses}敗 {games - wins - losses}分");
            Assert.GreaterOrEqual(wins, 8, $"CPUの勝ち {wins}/{games}");
        }
    }
}
