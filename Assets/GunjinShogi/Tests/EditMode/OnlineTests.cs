using System;
using System.Collections.Generic;
using NUnit.Framework;
using GunjinShogi.Core;
using GunjinShogi.Core.Online;

namespace GunjinShogi.Core.Tests
{
    /// <summary>サーバーの部屋管理と通信の中身を、通信なしで確かめる。</summary>
    public class OnlineTests
    {
        /// <summary>接続番号ごとの受信箱と、クライアント側の見え方を持つ小さなテスト用クライアント群。</summary>
        sealed class Harness
        {
            public readonly RoomService Server;
            public readonly Dictionary<int, List<Packet>> Inbox = new Dictionary<int, List<Packet>>();
            public readonly Dictionary<int, PlayerView> Views = new Dictionary<int, PlayerView>();
            public readonly Dictionary<int, int> Seat = new Dictionary<int, int>();
            public double Now;
            public int BytesSent;

            public Harness()
            {
                Server = new RoomService((conn, p) =>
                {
                    // 実際と同じくバイト列を経由させる
                    var bytes = p.Encode();
                    BytesSent += bytes.Length;
                    var q = Packet.Decode(bytes);
                    if (!Inbox.TryGetValue(conn, out var list)) Inbox[conn] = list = new List<Packet>();
                    list.Add(q);
                    Apply(conn, q);
                }, () => Now, seed: 1);
            }

            void Apply(int conn, Packet p)
            {
                switch (p.Op)
                {
                    case Op.RoomJoined: Seat[conn] = p.A; break;
                    case Op.Snapshot: Views[conn] = ViewCodec.Decode(p.Data); break;
                    case Op.MoveMade:
                        Views[conn].ApplyMove(ViewCodec.DecodeMove(p.Data), p.A, (GameResult)p.B, (EndReason)p.C);
                        break;
                    case Op.GameOver: Views[conn] = ViewCodec.Decode(p.Data); break;
                }
            }

            public Packet Last(int conn, Op op) => Inbox[conn].FindLast(p => p.Op == op);
            public void Send(int conn, Packet p) => Server.Receive(conn, Packet.Decode(p.Encode()));
        }

        static Packet Create(string token, string rule = "0") =>
            Packet.Of(Op.CreateRoom, Packet.ProtocolVersion, s1: token, s2: rule);

        static Packet Join(string token, string room) =>
            Packet.Of(Op.JoinRoom, Packet.ProtocolVersion, s1: token, s2: room);

        static string RandomSetupCode(RuleSet rules, int seat, Random rng)
        {
            var topo = new BoardTopology(rules.Board);
            return SetupCodec.Encode(topo, seat, RandomSetup.Generate(rules, topo, seat, rng));
        }

        /// <summary>2人が部屋に入って配置を終えたところまで進める。戻り値は部屋番号。</summary>
        static string StartGame(Harness h, Random rng, string rule = "0")
        {
            h.Send(1, Create("tokenA", rule));
            string room = h.Last(1, Op.RoomJoined).S1;
            h.Send(2, Join("tokenB", room));
            RuleCodec.TryDecode(rule, out var o);
            var rules = StandardRules.Create23(o);
            h.Send(1, Packet.Of(Op.SubmitSetup, s1: RandomSetupCode(rules, 0, rng)));
            h.Send(2, Packet.Of(Op.SubmitSetup, s1: RandomSetupCode(rules, 1, rng)));
            return room;
        }

        [Test]
        public void Packet_RoundTrips()
        {
            var p = Packet.Of(Op.MoveMade, -1, 300, 70000, "部屋", "A1K1", new byte[] { 1, 2, 3 });
            var q = Packet.Decode(p.Encode());
            Assert.AreEqual(p.ToString(), q.ToString());
            Assert.IsNull(Packet.Decode(new byte[] { 5, 0xFF }));
            Assert.LessOrEqual(Packet.Of(Op.Move, 45, 30).Encode().Length, 8, "指し手は小さく送る");
        }

        [Test]
        public void FullGame_ClientViewsMatchServer_AndHideEnemyTypes()
        {
            var h = new Harness();
            var rng = new Random(3);
            StartGame(h, rng);
            Assert.AreEqual(0, h.Seat[1]);
            Assert.AreEqual(1, h.Seat[2]);

            // 両者の見え方を差分だけで更新しながら最後まで指す
            for (int ply = 0; ply < 600; ply++)
            {
                var v = h.Views[1];
                if (v.Phase != GamePhase.Playing) break;
                int turnConn = v.CurrentPlayer == h.Seat[1] ? 1 : 2;
                var myView = h.Views[turnConn];
                var rules = StandardRules.Create23();
                var moves = myView.ToMoveState(rules, new BoardTopology(rules.Board)).GetLegalMoves();
                Assert.IsNotEmpty(moves);
                var m = moves[rng.Next(moves.Count)];
                h.Send(turnConn, Packet.Of(Op.Move, m.PieceId, m.ToNode));
                Assert.IsNull(h.Inbox[turnConn].Find(p => p.Op == Op.Error), "合法手が拒否された");

                // 差分で更新した見え方に、相手の駒種が一切含まれない
                foreach (int c in new[] { 1, 2 })
                {
                    var cv = h.Views[c];
                    if (cv.Phase == GamePhase.Finished) continue;
                    foreach (var p in cv.Pieces)
                        if (p.Owner != h.Seat[c]) Assert.AreEqual(Visibility.HiddenType, p.TypeId);
                    foreach (var mv in cv.History)
                    {
                        if (!mv.HadBattle) continue;
                        if (cv.Pieces[mv.AttackerId].Owner != h.Seat[c]) Assert.AreEqual(Visibility.HiddenType, mv.AttackerType);
                        if (cv.Pieces[mv.DefenderId].Owner != h.Seat[c]) Assert.AreEqual(Visibility.HiddenType, mv.DefenderType);
                    }
                }
            }

            var over = h.Last(1, Op.GameOver);
            Assert.IsNotNull(over, "終局しなかった");
            // 終局後は全公開
            foreach (var p in h.Views[1].Pieces) Assert.AreNotEqual(Visibility.HiddenType, p.TypeId);
            Assert.AreEqual(23, over.S1.Length);
            Assert.AreEqual(23, over.S2.Length);
        }

        [Test]
        public void IllegalMoves_AreRejected()
        {
            var h = new Harness();
            var rng = new Random(4);
            StartGame(h, rng);
            // 後手が先手の番に指す
            var v2 = h.Views[2];
            int myPiece = v2.Pieces.FindIndex(p => p.Owner == 1 && p.Alive);
            h.Send(2, Packet.Of(Op.Move, myPiece, v2.Pieces[myPiece].Node));
            Assert.IsNotNull(h.Last(2, Op.Error));
            // 先手が相手の駒を動かそうとする
            h.Send(1, Packet.Of(Op.Move, myPiece, 0));
            Assert.IsNotNull(h.Last(1, Op.Error));
            Assert.AreEqual(0, h.Views[1].History.Count);
        }

        [Test]
        public void Reconnect_WithSameToken_RestoresSeat()
        {
            var h = new Harness();
            var rng = new Random(5);
            string room = StartGame(h, rng);
            var rules = StandardRules.Create23();
            var moves = h.Views[1].ToMoveState(rules, new BoardTopology(rules.Board)).GetLegalMoves();
            h.Send(1, Packet.Of(Op.Move, moves[0].PieceId, moves[0].ToNode));

            // 後手が切断 → 先手に「相手不在」が伝わる
            h.Server.Disconnected(2);
            h.Now = 10;
            var status = h.Last(1, Op.RoomStatus);
            Assert.AreEqual(0, status.B & (int)StatusFlags.OpponentPresent);

            // 60秒経つ前の勝ち申請は拒否
            h.Send(1, Packet.Of(Op.ClaimWin));
            Assert.AreEqual("まだ勝ちを申請できません", h.Last(1, Op.Error).S1);

            // 別の接続番号・同じトークンで戻ると、同じ席と盤面が戻る
            h.Send(3, Join("tokenB", room));
            Assert.AreEqual(1, h.Seat[3]);
            Assert.AreEqual(1, h.Views[3].History.Count);
            Assert.AreNotEqual(0, h.Last(1, Op.RoomStatus).B & (int)StatusFlags.OpponentPresent);

            // 知らない人は対局中の部屋に入れない
            h.Send(4, Join("stranger", room));
            Assert.IsNotNull(h.Last(4, Op.Error));
        }

        [Test]
        public void ClaimWin_AfterLongDisconnect()
        {
            var h = new Harness();
            StartGame(h, new Random(6));
            h.Server.Disconnected(2);
            h.Now = RoomService.ClaimWinAfterSeconds + 1;
            h.Send(1, Packet.Of(Op.ClaimWin));
            var over = h.Last(1, Op.GameOver);
            Assert.IsNotNull(over);
            Assert.AreEqual((int)GameResult.Player0Win, over.A);
        }

        [Test]
        public void Rematch_SwapsFirstPlayer()
        {
            var h = new Harness();
            StartGame(h, new Random(7));
            h.Send(1, Packet.Of(Op.Resign));
            Assert.IsNotNull(h.Last(2, Op.GameOver));
            h.Send(1, Packet.Of(Op.Rematch));
            h.Send(2, Packet.Of(Op.Rematch));
            Assert.AreEqual(1, h.Seat[1]);
            Assert.AreEqual(0, h.Seat[2]);
            Assert.AreEqual((int)RoomPhase.Setup, h.Last(1, Op.RoomStatus).A);
        }

        [Test]
        public void RulesFromCreator_AreUsed_AndLastLeaverClosesRoom()
        {
            var h = new Harness();
            h.Send(1, Create("tokenA", "A1"));
            var joined = h.Last(1, Op.RoomJoined);
            Assert.AreEqual("A1", joined.S2);
            h.Send(2, Join("tokenB", joined.S1));
            Assert.AreEqual("A1", h.Last(2, Op.RoomJoined).S2);

            h.Send(1, Packet.Of(Op.LeaveRoom));
            Assert.AreEqual(1, h.Server.RoomCount);
            h.Server.Disconnected(2);
            Assert.AreEqual(0, h.Server.RoomCount);

            h.Send(3, Packet.Of(Op.CreateRoom, 999, s1: "t", s2: "0"));
            StringAssert.Contains("バージョン", h.Last(3, Op.Error).S1);
        }
    }
}
