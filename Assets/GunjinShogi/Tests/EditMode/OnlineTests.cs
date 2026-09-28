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
            public readonly Dictionary<int, RoomInfo> Rooms = new Dictionary<int, RoomInfo>();
            public readonly Dictionary<int, int> MemberId = new Dictionary<int, int>();
            public double Now;

            public Harness()
            {
                Server = new RoomService((conn, p) =>
                {
                    // 実際と同じくバイト列を経由させる
                    var q = Packet.Decode(p.Encode());
                    if (!Inbox.TryGetValue(conn, out var list)) Inbox[conn] = list = new List<Packet>();
                    list.Add(q);
                    Apply(conn, q);
                }, () => Now, seed: 1);
            }

            void Apply(int conn, Packet p)
            {
                switch (p.Op)
                {
                    case Op.RoomJoined: MemberId[conn] = p.A; break;
                    case Op.RoomState: Rooms[conn] = RoomInfo.Decode(p.Data); break;
                    case Op.Snapshot: Views[conn] = ViewCodec.Decode(p.Data); break;
                    case Op.MoveMade: Views[conn].ApplyMove(ViewCodec.DecodeMove(p.Data), p.A, (GameResult)p.B, (EndReason)p.C); break;
                    case Op.GameOver: Views[conn] = ViewCodec.Decode(p.Data); break;
                }
            }

            public Packet Last(int conn, Op op) => Inbox.TryGetValue(conn, out var l) ? l.FindLast(p => p.Op == op) : null;
            public void Send(int conn, Packet p) => Server.Receive(conn, Packet.Decode(p.Encode()));
            public int Seat(int conn) => Rooms[conn].SeatOf(MemberId[conn]);
            public RoomInfo Room(int conn) => Rooms[conn];
        }

        static Packet Create(string token, string rule = "0", bool open = true, string name = "") =>
            Packet.Of(Op.CreateRoom, Packet.ProtocolVersion, open ? 1 : 0, s1: token, s2: rule, data: System.Text.Encoding.UTF8.GetBytes(name));

        static Packet Join(string token, string room, string name = "") =>
            Packet.Of(Op.JoinRoom, Packet.ProtocolVersion, s1: token, s2: room, data: System.Text.Encoding.UTF8.GetBytes(name));

        static string RandomSetupCode(RuleSet rules, int seat, Random rng)
        {
            var topo = new BoardTopology(rules.Board);
            return SetupCodec.Encode(topo, seat, RandomSetup.Generate(rules, topo, seat, rng));
        }

        /// <summary>1 が部屋を作り（先手席）、2 が入って後手席に着き、両者準備完了 → 開始 → 配置まで。戻り値は部屋番号。</summary>
        static string StartGame(Harness h, Random rng, string rule = "0")
        {
            h.Send(1, Create("tokenA", rule));
            string room = h.Last(1, Op.RoomJoined).S1;
            h.Send(2, Join("tokenB", room));
            h.Send(2, Packet.Of(Op.TakeSeat, 1));
            h.Send(1, Packet.Of(Op.SetReady, 1));
            h.Send(2, Packet.Of(Op.SetReady, 1));
            h.Send(1, Packet.Of(Op.StartGame));
            Assert.AreEqual(RoomPhase.Setup, h.Room(1).Phase);
            RuleCodec.TryDecode(rule, out var o);
            var rules = StandardRules.Create23(o);
            h.Send(1, Packet.Of(Op.SubmitSetup, s1: RandomSetupCode(rules, 0, rng)));
            h.Send(2, Packet.Of(Op.SubmitSetup, s1: RandomSetupCode(rules, 1, rng)));
            Assert.AreEqual(RoomPhase.Playing, h.Room(1).Phase);
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
        public void FullGame_PlayersSeeOnlyOwnTypes_SpectatorSeesAll()
        {
            var h = new Harness();
            var rng = new Random(3);
            string room = StartGame(h, rng);
            h.Send(3, Join("tokenC", room, "見る人"));
            Assert.AreEqual(RoomInfo.NoSeat, h.Seat(3), "対局中に入った人は観戦");
            Assert.AreEqual(Visibility.Spectator, h.Views[3].Viewer);
            Assert.AreEqual("見る人", h.Room(1).NameOf(h.MemberId[3]));

            var rules = StandardRules.Create23();
            for (int ply = 0; ply < 600; ply++)
            {
                var v = h.Views[1];
                if (v.Phase != GamePhase.Playing) break;
                int turnConn = v.CurrentPlayer == h.Seat(1) ? 1 : 2;
                var moves = h.Views[turnConn].ToMoveState(rules, new BoardTopology(rules.Board)).GetLegalMoves();
                var m = moves[rng.Next(moves.Count)];
                h.Send(turnConn, Packet.Of(Op.Move, m.PieceId, m.ToNode));
                Assert.IsNull(h.Inbox[turnConn].Find(p => p.Op == Op.Error), "合法手が拒否された");

                foreach (int c in new[] { 1, 2 })
                {
                    var cv = h.Views[c];
                    if (cv.Phase == GamePhase.Finished) continue;
                    foreach (var p in cv.Pieces)
                        if (p.Owner != h.Seat(c)) Assert.AreEqual(Visibility.HiddenType, p.TypeId, "相手の駒種が見えている");
                }
                if (h.Views[3].Phase == GamePhase.Playing)
                    foreach (var p in h.Views[3].Pieces) Assert.AreNotEqual(Visibility.HiddenType, p.TypeId, "観戦者には両者の駒種を送る");
            }

            var over = h.Last(3, Op.GameOver);
            Assert.IsNotNull(over, "観戦者にも終局が届く");
            Assert.AreEqual(23, over.S1.Length);
            // 終局後は感想戦：席は固定され、観戦者は座れない
            Assert.AreEqual(RoomPhase.Review, h.Room(1).Phase);
            h.Send(3, Packet.Of(Op.TakeSeat, 0));
            Assert.AreEqual(RoomInfo.NoSeat, h.Seat(3), "感想戦中は座れない");
            h.Send(1, Packet.Of(Op.FinishReview));
            Assert.AreEqual(RoomPhase.Review, h.Room(1).Phase, "相手がまだ感想戦中");
            h.Send(2, Packet.Of(Op.FinishReview));
            Assert.AreEqual(RoomPhase.Lobby, h.Room(1).Phase, "2人とも終えたら準備に戻る");
            Assert.IsFalse(h.Room(1).Ready[0] || h.Room(1).Ready[1]);
            Assert.AreEqual(0, h.Seat(1), "席はそのまま");
        }

        [Test]
        public void OnlyOwner_ChangesRules_StartsGame_AndCanTransfer()
        {
            var h = new Harness();
            h.Send(1, Create("tokenA", "A1", name: "主"));
            string room = h.Last(1, Op.RoomJoined).S1;
            h.Send(2, Join("tokenB", room));
            Assert.AreEqual("A1", h.Room(2).RuleCode);

            h.Send(2, Packet.Of(Op.SetRules, s1: "0"));
            Assert.AreEqual("部屋主だけができる操作です", h.Last(2, Op.Error).S1);
            Assert.AreEqual("A1", h.Room(1).RuleCode);

            h.Send(2, Packet.Of(Op.TakeSeat, 1));
            h.Send(1, Packet.Of(Op.SetReady, 1));
            h.Send(2, Packet.Of(Op.SetReady, 1));
            Assert.IsTrue(h.Room(1).CanStart);
            h.Send(2, Packet.Of(Op.StartGame));
            Assert.AreEqual(RoomPhase.Lobby, h.Room(1).Phase, "部屋主以外は開始できない");

            // ルールを変えると準備完了が外れる
            h.Send(1, Packet.Of(Op.SetRules, s1: "A1E2"));
            Assert.AreEqual("A1E2", h.Room(2).RuleCode);
            Assert.IsFalse(h.Room(2).Ready[0] || h.Room(2).Ready[1]);

            // 部屋主を譲る
            h.Send(1, Packet.Of(Op.TransferOwner, h.MemberId[2]));
            Assert.AreEqual(h.MemberId[2], h.Room(1).OwnerId);
            h.Send(1, Packet.Of(Op.SetRules, s1: "0"));
            Assert.AreEqual("部屋主だけができる操作です", h.Last(1, Op.Error).S1);

            // 部屋主が抜けたら残った人へ
            h.Send(2, Packet.Of(Op.LeaveRoom));
            Assert.AreEqual(h.MemberId[1], h.Room(1).OwnerId);
        }

        [Test]
        public void Seats_OwnerCanTakeAnySide_OthersOnlyEmpty()
        {
            var h = new Harness();
            h.Send(1, Create("tokenA"));
            string room = h.Last(1, Op.RoomJoined).S1;
            h.Send(2, Join("tokenB", room));
            h.Send(3, Join("tokenC", room));
            h.Send(2, Packet.Of(Op.TakeSeat, 1));
            // 観戦者は埋まった席に座れない
            h.Send(3, Packet.Of(Op.TakeSeat, 0));
            Assert.AreEqual("その席にはほかの人が座っています", h.Last(3, Op.Error).S1);
            // 部屋主は後手席へ移れる（座っていた人は先手席へ入れ替わる）
            h.Send(1, Packet.Of(Op.TakeSeat, 1));
            Assert.AreEqual(1, h.Seat(1));
            Assert.AreEqual(0, h.Seat(2));
            // 席を立つと空席になり、観戦者が座れる
            h.Send(2, Packet.Of(Op.TakeSeat, -1));
            h.Send(3, Packet.Of(Op.TakeSeat, 0));
            Assert.AreEqual(0, h.Seat(3));
        }

        [Test]
        public void AbortSetup_ReturnsEveryoneToLobby()
        {
            var h = new Harness();
            h.Send(1, Create("tokenA"));
            string room = h.Last(1, Op.RoomJoined).S1;
            h.Send(2, Join("tokenB", room));
            h.Send(2, Packet.Of(Op.TakeSeat, 1));
            h.Send(1, Packet.Of(Op.SetReady, 1));
            h.Send(2, Packet.Of(Op.SetReady, 1));
            h.Send(1, Packet.Of(Op.StartGame));
            Assert.AreEqual(RoomPhase.Setup, h.Room(2).Phase);
            h.Send(2, Packet.Of(Op.AbortSetup));
            Assert.AreEqual(RoomPhase.Lobby, h.Room(1).Phase);
            StringAssert.Contains("中断", h.Last(1, Op.Notice).S1);
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

            h.Server.Disconnected(2);
            h.Now = 10;
            Assert.GreaterOrEqual(h.Room(1).AbsentSeconds[1], 0, "相手不在が伝わる");

            h.Send(1, Packet.Of(Op.ClaimWin));
            Assert.AreEqual("まだ勝ちを申請できません", h.Last(1, Op.Error).S1);

            h.Send(3, Join("tokenB", room));
            Assert.AreEqual(1, h.Seat(3));
            Assert.AreEqual(1, h.Views[3].History.Count);
            Assert.AreEqual(-1, h.Room(1).AbsentSeconds[1]);
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
            Assert.AreEqual(RoomPhase.Review, h.Room(1).Phase);
            h.Send(1, Packet.Of(Op.FinishReview));
            Assert.AreEqual(RoomPhase.Lobby, h.Room(1).Phase, "切断中の相手は感想戦を終えた扱い");
            Assert.AreEqual(1, h.Room(1).Members.Count, "切断したままの対局者は外れる");
        }

        [Test]
        public void RoomList_ShowsOpenRooms_AndRejoinOnlyWhenSeatRemains()
        {
            var h = new Harness();
            h.Send(1, Create("tokenA", open: true, name: "公開"));
            string open = h.Last(1, Op.RoomJoined).S1;
            h.Send(2, Create("tokenB", open: false));
            string priv = h.Last(2, Op.RoomJoined).S1;

            h.Send(9, Packet.Of(Op.ListRooms, Packet.ProtocolVersion));
            var list = RoomListInfo.Decode(h.Last(9, Op.RoomList).Data);
            Assert.AreEqual(1, list.Rooms.Count);
            Assert.AreEqual(open, list.Rooms[0].Code);
            Assert.AreEqual("公開", list.Rooms[0].OwnerName);
            Assert.IsFalse(list.Rooms.Exists(r => r.Code == priv));

            // 全員いなくなった部屋は「前の対局に戻る」の対象にならない
            h.Server.Disconnected(1);
            h.Send(9, Packet.Of(Op.ListRooms, Packet.ProtocolVersion, s1: open, s2: "tokenA"));
            Assert.IsFalse(RoomListInfo.Decode(h.Last(9, Op.RoomList).Data).CanRejoin);

            // 対局中に切断した席は戻れる
            var h2 = new Harness();
            string room = StartGame(h2, new Random(8));
            h2.Server.Disconnected(2);
            h2.Send(9, Packet.Of(Op.ListRooms, Packet.ProtocolVersion, s1: room, s2: "tokenB"));
            Assert.IsTrue(RoomListInfo.Decode(h2.Last(9, Op.RoomList).Data).CanRejoin);
            h2.Send(9, Packet.Of(Op.ListRooms, Packet.ProtocolVersion, s1: room, s2: "someoneElse"));
            Assert.IsFalse(RoomListInfo.Decode(h2.Last(9, Op.RoomList).Data).CanRejoin);
        }

        [Test]
        public void IllegalMoves_AndSpectatorMoves_AreRejected()
        {
            var h = new Harness();
            string room = StartGame(h, new Random(4));
            h.Send(3, Join("tokenC", room));
            var v2 = h.Views[2];
            int piece = v2.Pieces.FindIndex(p => p.Owner == 1 && p.Alive);
            h.Send(2, Packet.Of(Op.Move, piece, v2.Pieces[piece].Node));
            Assert.IsNotNull(h.Last(2, Op.Error));
            int p0 = h.Views[1].Pieces.FindIndex(p => p.Owner == 0 && p.Alive);
            h.Send(3, Packet.Of(Op.Move, p0, 0));
            Assert.AreEqual(0, h.Views[1].History.Count, "観戦者は指せない");
        }

        [Test]
        public void CpuSeat_PlaysOnServer_AndOnlyOwnerCanPlaceIt()
        {
            var h = new Harness();
            h.Send(1, Create("tokenA"));
            string room = h.Last(1, Op.RoomJoined).S1;
            h.Send(2, Join("tokenB", room));
            h.Send(2, Packet.Of(Op.SetSeatCpu, 1, 1));
            Assert.AreEqual("部屋主だけができる操作です", h.Last(2, Op.Error).S1);

            h.Send(1, Packet.Of(Op.SetSeatCpu, 1, 0));
            var cpu = h.Room(1).Member(h.Room(1).SeatMember[1]);
            Assert.IsTrue(cpu.IsCpu);
            Assert.AreEqual(0, cpu.CpuLevel);
            Assert.IsTrue(h.Room(1).Ready[1], "CPU は常に準備完了");
            h.Send(1, Packet.Of(Op.SetSeatCpu, 1, 2));
            Assert.AreEqual(2, h.Room(1).Member(h.Room(1).SeatMember[1]).CpuLevel, "強さを変えられる");

            h.Send(1, Packet.Of(Op.SetReady, 1));
            h.Send(1, Packet.Of(Op.StartGame));
            Assert.AreEqual(RoomPhase.Setup, h.Room(1).Phase);
            Assert.IsTrue(h.Room(1).SetupDone[1], "CPU は開始と同時に配置を出す");
            var rng = new Random(11);
            h.Send(1, Packet.Of(Op.SubmitSetup, s1: RandomSetupCode(StandardRules.Create23(), 0, rng)));
            Assert.AreEqual(RoomPhase.Playing, h.Room(1).Phase);

            // 人が指す → サーバーの Tick で CPU が指し返す
            var rules = StandardRules.Create23();
            for (int turn = 0; turn < 6 && h.Views[1].Phase == GamePhase.Playing; turn++)
            {
                var moves = h.Views[1].ToMoveState(rules, new BoardTopology(rules.Board)).GetLegalMoves();
                var m = moves[rng.Next(moves.Count)];
                h.Send(1, Packet.Of(Op.Move, m.PieceId, m.ToNode));
                int before = h.Views[1].History.Count;
                for (int i = 0; i < 400 && h.Views[1].History.Count == before && h.Views[1].Phase == GamePhase.Playing; i++)
                {
                    h.Now += 0.05;
                    h.Server.Tick();
                }
                if (h.Views[1].Phase == GamePhase.Playing) Assert.AreEqual(0, h.Views[1].CurrentPlayer, "CPU が指し返した");
            }

            // 部屋主が抜けても、CPU は部屋主にならない（人が残っていれば人へ、人がいなければ部屋は閉じる）
            h.Send(1, Packet.Of(Op.LeaveRoom));
            Assert.IsFalse(h.Room(2).Member(h.Room(2).OwnerId).IsCpu);
            h.Server.Disconnected(2);
            Assert.AreEqual(0, h.Server.RoomCount, "CPU だけの部屋は閉じる");
        }

        [Test]
        public void VersionMismatch_IsReported_AndLastLeaverClosesRoom()
        {
            var h = new Harness();
            h.Send(1, Create("tokenA"));
            string room = h.Last(1, Op.RoomJoined).S1;
            h.Send(2, Join("tokenB", room));
            h.Send(1, Packet.Of(Op.LeaveRoom));
            Assert.AreEqual(1, h.Server.RoomCount);
            h.Server.Disconnected(2);
            Assert.AreEqual(0, h.Server.RoomCount);

            h.Send(3, Packet.Of(Op.CreateRoom, 999, s1: "t", s2: "0"));
            StringAssert.Contains("バージョン", h.Last(3, Op.Error).S1);
        }
    }
}
