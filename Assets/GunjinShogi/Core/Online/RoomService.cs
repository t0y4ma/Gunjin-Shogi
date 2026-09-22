using System;
using System.Collections.Generic;

namespace GunjinShogi.Core.Online
{
    /// <summary>
    /// サーバーの部屋管理と審判。通信手段には依存せず、「接続番号」と Packet だけを扱う。
    /// ・各プレイヤーには、その人から見える情報（PlayerView）だけを送る。相手の駒種はサーバーの外に出ない。
    /// ・対局中に切断しても席は残し、同じ端末トークンで入り直すと続きから再開できる。
    /// </summary>
    public sealed class RoomService
    {
        /// <summary>相手がこの秒数以上切断していたら、残った側は勝ちを申請できる。</summary>
        public const double ClaimWinAfterSeconds = 60;

        sealed class Seat
        {
            public int Conn = -1;
            public string Token = "";
            public double LeftAt;
            public bool Rematch;
            public string SetupCode;  // 提出した配置（先手の向き）
            public bool Joined => Token.Length > 0;
            public bool Present => Conn >= 0;
        }

        sealed class Room
        {
            public string Code;
            public string RuleCode;
            public RuleSet Rules;
            public GameState State;
            public readonly Seat[] Seats = { new Seat(), new Seat() };

            public RoomPhase Phase =>
                State.Phase == GamePhase.Setup ? RoomPhase.Setup
                : State.Phase == GamePhase.Playing ? RoomPhase.Playing : RoomPhase.Finished;

            public int SeatOf(int conn) => Seats[0].Conn == conn ? 0 : Seats[1].Conn == conn ? 1 : -1;
        }

        readonly Action<int, Packet> send;
        readonly Func<double> clock;
        readonly Random rng;
        readonly Dictionary<string, Room> rooms = new Dictionary<string, Room>();
        readonly Dictionary<int, Room> roomOfConn = new Dictionary<int, Room>();

        public RoomService(Action<int, Packet> send, Func<double> clock, int seed = 0)
        {
            this.send = send;
            this.clock = clock;
            rng = seed == 0 ? new Random() : new Random(seed);
        }

        public int RoomCount => rooms.Count;

        /// <summary>開発用：部屋ごとの様子（席の接続状態・配置・再戦希望）。通信には使わない。</summary>
        public List<string> DescribeRooms()
        {
            var list = new List<string>();
            foreach (var r in rooms.Values)
                list.Add($"部屋 {r.Code}  {r.Phase}  ルール {r.RuleCode}  {r.State.History.Count}手\n  先手: {DescribeSeat(r.Seats[0])}\n  後手: {DescribeSeat(r.Seats[1])}");
            return list;
        }

        string DescribeSeat(Seat s)
        {
            if (!s.Joined) return "空席";
            string who = s.Present ? $"接続 #{s.Conn}" : $"切断中 {clock() - s.LeftAt:0}秒";
            string token = s.Token.Substring(0, Math.Min(6, s.Token.Length));
            return $"{who}（端末 {token}…）{(s.SetupCode != null ? " 配置済" : "")}{(s.Rematch ? " 再戦希望" : "")}";
        }

        // ───────── 受信 ─────────

        public void Receive(int conn, Packet p)
        {
            if (p == null) return;
            switch (p.Op)
            {
                case Op.CreateRoom: Create(conn, p); break;
                case Op.JoinRoom: Join(conn, p); break;
                case Op.LeaveRoom: Leave(conn); break;
                case Op.SubmitSetup: SubmitSetup(conn, p); break;
                case Op.Move: Move(conn, p); break;
                case Op.Resign: Resign(conn); break;
                case Op.Rematch: Rematch(conn); break;
                case Op.ClaimWin: ClaimWin(conn); break;
                default: Error(conn, "不明な要求です"); break;
            }
        }

        public void Disconnected(int conn)
        {
            if (!roomOfConn.TryGetValue(conn, out var room)) return;
            roomOfConn.Remove(conn);
            int seat = room.SeatOf(conn);
            if (seat < 0) return;
            room.Seats[seat].Conn = -1;
            room.Seats[seat].LeftAt = clock();
            if (!room.Seats[0].Present && !room.Seats[1].Present)
            {
                rooms.Remove(room.Code);
                return;
            }
            SendStatus(room);
        }

        // ───────── 部屋 ─────────

        bool CheckVersion(int conn, Packet p)
        {
            if (p.A == Packet.ProtocolVersion) return true;
            Error(conn, "サーバーとゲームのバージョンが違います。ページを再読み込みしてください。");
            return false;
        }

        void Create(int conn, Packet p)
        {
            if (!CheckVersion(conn, p)) return;
            if (string.IsNullOrEmpty(p.S1)) { Error(conn, "端末の識別情報がありません"); return; }
            if (!RuleCodec.TryDecode(p.S2, out var options)) { Error(conn, "ルールコードが正しくありません"); return; }
            Leave(conn);

            var room = new Room
            {
                Code = NewCode(),
                RuleCode = RuleCodec.Encode(options),
            };
            room.Rules = StandardRules.Create23(options);
            room.State = new GameState(room.Rules);
            rooms[room.Code] = room;
            Sit(room, 0, conn, p.S1);
        }

        string NewCode()
        {
            for (int i = 0; i < 1000; i++)
            {
                var code = rng.Next(1000, 10000).ToString();
                if (!rooms.ContainsKey(code)) return code;
            }
            throw new InvalidOperationException("部屋番号が足りません");
        }

        void Join(int conn, Packet p)
        {
            if (!CheckVersion(conn, p)) return;
            if (string.IsNullOrEmpty(p.S1)) { Error(conn, "端末の識別情報がありません"); return; }
            if (!rooms.TryGetValue(p.S2.Trim(), out var room)) { Error(conn, "その番号の部屋はありません"); return; }

            // 同じ端末からの入り直し（再読み込み・再接続）なら元の席に戻す
            for (int s = 0; s < 2; s++)
            {
                if (room.Seats[s].Token != p.S1) continue;
                if (roomOfConn.TryGetValue(conn, out var current) && current == room && room.SeatOf(conn) == s) return;
                int old = room.Seats[s].Conn;
                if (old >= 0 && old != conn)
                {
                    roomOfConn.Remove(old);
                    send(old, Packet.Of(Op.Error, a: 1, s1: "別の画面からこの対局に入り直したため、こちらは切断しました"));
                }
                Leave(conn);
                Sit(room, s, conn, p.S1);
                return;
            }

            int free = !room.Seats[1].Joined ? 1 : !room.Seats[0].Joined ? 0 : -1;
            if (free < 0 || room.Phase != RoomPhase.Setup) { Error(conn, "この部屋はすでに2人そろっています"); return; }
            Leave(conn);
            Sit(room, free, conn, p.S1);
        }

        void Sit(Room room, int seat, int conn, string token)
        {
            var s = room.Seats[seat];
            s.Conn = conn;
            s.Token = token;
            roomOfConn[conn] = room;
            send(conn, Packet.Of(Op.RoomJoined, a: seat, s1: room.Code, s2: room.RuleCode));
            SendStatus(room);
            if (room.Phase != RoomPhase.Setup) SendSnapshot(room, seat);
            if (room.Phase == RoomPhase.Finished) SendGameOver(room, seat);
        }

        void Leave(int conn)
        {
            if (!roomOfConn.TryGetValue(conn, out var room)) return;
            int seat = room.SeatOf(conn);
            // 対局中に自分から抜けたら投了
            if (room.Phase == RoomPhase.Playing && seat >= 0)
            {
                room.State.Resign(seat);
                BroadcastGameOver(room);
            }
            roomOfConn.Remove(conn);
            if (seat >= 0)
            {
                var s = room.Seats[seat];
                s.Conn = -1;
                s.LeftAt = clock();
                // 対局前・対局後なら席ごと空ける（別の人が入れる）
                if (room.Phase != RoomPhase.Playing)
                {
                    s.Token = "";
                    s.SetupCode = null;
                    s.Rematch = false;
                    if (room.Phase == RoomPhase.Setup) ResetGame(room, keepSetupsOf: 1 - seat);
                }
            }
            if (!room.Seats[0].Present && !room.Seats[1].Present) rooms.Remove(room.Code);
            else SendStatus(room);
        }

        /// <summary>配置中に片方が抜けたときは、残った側の提出済み配置を活かしたまま盤を作り直す。</summary>
        void ResetGame(Room room, int keepSetupsOf)
        {
            room.State = new GameState(room.Rules);
            for (int s = 0; s < 2; s++)
            {
                if (s != keepSetupsOf) { room.Seats[s].SetupCode = null; continue; }
                var code = room.Seats[s].SetupCode;
                if (code == null) continue;
                var placements = SetupCodec.Decode(room.Rules, room.State.Topology, s, code);
                if (placements == null || room.State.SubmitSetup(s, placements).Count > 0) room.Seats[s].SetupCode = null;
            }
        }

        // ───────── 対局 ─────────

        bool TryGetSeat(int conn, out Room room, out int seat)
        {
            seat = -1;
            if (!roomOfConn.TryGetValue(conn, out room)) { Error(conn, "部屋に入っていません"); return false; }
            seat = room.SeatOf(conn);
            return seat >= 0;
        }

        void SubmitSetup(int conn, Packet p)
        {
            if (!TryGetSeat(conn, out var room, out int seat)) return;
            if (room.Phase != RoomPhase.Setup) { Error(conn, "配置の時間は終わっています"); return; }
            if (room.Seats[seat].SetupCode != null) { Error(conn, "配置は提出済みです"); return; }
            var placements = SetupCodec.Decode(room.Rules, room.State.Topology, seat, p.S1);
            if (placements == null) { Error(conn, "配置の形式が正しくありません"); return; }
            var errors = room.State.SubmitSetup(seat, placements);
            if (errors.Count > 0) { Error(conn, errors[0]); return; }
            room.Seats[seat].SetupCode = SetupCodec.Encode(room.State.Topology, seat, placements);

            if (room.Phase == RoomPhase.Playing)
                for (int s = 0; s < 2; s++) SendSnapshot(room, s);
            SendStatus(room);
        }

        void Move(int conn, Packet p)
        {
            if (!TryGetSeat(conn, out var room, out int seat)) return;
            var state = room.State;
            var move = new Move(p.A, p.B);
            if (room.Phase != RoomPhase.Playing || state.CurrentPlayer != seat || !state.IsLegal(move))
            {
                Error(conn, "その手は指せません");
                SendSnapshot(room, seat); // 画面を正しい状態に戻す
                return;
            }
            var record = state.ApplyMove(move);
            for (int s = 0; s < 2; s++)
            {
                if (!room.Seats[s].Present) continue;
                var mv = PlayerView.ProjectMove(state, record, s);
                send(room.Seats[s].Conn, Packet.Of(Op.MoveMade, state.CurrentPlayer, (int)state.Result, (int)state.EndReason,
                    data: ViewCodec.EncodeMove(mv)));
            }
            if (state.Phase == GamePhase.Finished) BroadcastGameOver(room);
        }

        void Resign(int conn)
        {
            if (!TryGetSeat(conn, out var room, out int seat)) return;
            if (room.Phase != RoomPhase.Playing) return;
            room.State.Resign(seat);
            BroadcastGameOver(room);
        }

        void ClaimWin(int conn)
        {
            if (!TryGetSeat(conn, out var room, out int seat)) return;
            var other = room.Seats[1 - seat];
            if (room.Phase != RoomPhase.Playing || other.Present || clock() - other.LeftAt < ClaimWinAfterSeconds)
            {
                Error(conn, "まだ勝ちを申請できません");
                return;
            }
            room.State.Resign(1 - seat);
            BroadcastGameOver(room);
        }

        void Rematch(int conn)
        {
            if (!TryGetSeat(conn, out var room, out int seat)) return;
            if (room.Phase != RoomPhase.Finished) return;
            room.Seats[seat].Rematch = true;
            if (room.Seats[0].Rematch && room.Seats[1].Rematch && room.Seats[0].Present && room.Seats[1].Present)
            {
                // 先手と後手を入れ替えて新しい対局
                var a = room.Seats[0];
                room.Seats[0] = room.Seats[1];
                room.Seats[1] = a;
                room.State = new GameState(room.Rules);
                for (int s = 0; s < 2; s++)
                {
                    room.Seats[s].Rematch = false;
                    room.Seats[s].SetupCode = null;
                    send(room.Seats[s].Conn, Packet.Of(Op.RoomJoined, a: s, s1: room.Code, s2: room.RuleCode));
                }
            }
            SendStatus(room);
        }

        // ───────── 送信 ─────────

        void Error(int conn, string message) => send(conn, Packet.Of(Op.Error, s1: message));

        void SendStatus(Room room)
        {
            for (int s = 0; s < 2; s++)
            {
                var me = room.Seats[s];
                if (!me.Present) continue;
                var other = room.Seats[1 - s];
                var flags = StatusFlags.None;
                if (other.Joined) flags |= StatusFlags.OpponentJoined;
                if (other.Present) flags |= StatusFlags.OpponentPresent;
                if (me.SetupCode != null) flags |= StatusFlags.MySetupDone;
                if (other.SetupCode != null) flags |= StatusFlags.OpponentSetupDone;
                if (me.Rematch) flags |= StatusFlags.IWantRematch;
                if (other.Rematch) flags |= StatusFlags.OpponentWantsRematch;
                int absent = other.Joined && !other.Present ? (int)(clock() - other.LeftAt) : 0;
                send(me.Conn, Packet.Of(Op.RoomStatus, (int)room.Phase, (int)flags, absent));
            }
        }

        void SendSnapshot(Room room, int seat)
        {
            var s = room.Seats[seat];
            if (!s.Present || room.State.Phase == GamePhase.Setup) return;
            send(s.Conn, Packet.Of(Op.Snapshot, data: ViewCodec.Encode(PlayerView.From(room.State, seat))));
        }

        void BroadcastGameOver(Room room)
        {
            for (int s = 0; s < 2; s++) SendGameOver(room, s);
            SendStatus(room);
        }

        void SendGameOver(Room room, int seat)
        {
            var s = room.Seats[seat];
            if (!s.Present) return;
            var view = PlayerView.From(room.State, seat, revealAll: true);
            send(s.Conn, Packet.Of(Op.GameOver, (int)room.State.Result, (int)room.State.EndReason,
                s1: room.Seats[0].SetupCode ?? "", s2: room.Seats[1].SetupCode ?? "", data: ViewCodec.Encode(view)));
        }
    }
}
