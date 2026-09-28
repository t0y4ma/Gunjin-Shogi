using System;
using System.Collections.Generic;
using System.Text;
using GunjinShogi.Core.Ai;

namespace GunjinShogi.Core.Online
{
    /// <summary>
    /// サーバーの部屋管理と審判。通信手段には依存せず、「接続番号」と Packet だけを扱う。
    /// ・部屋には最大 MaxMembers 人が入れる。先手席・後手席に着席した2人が対局者、それ以外は観戦者。
    /// ・部屋主はルール・公開範囲の変更、席への CPU の配置、対局開始、部屋主の譲渡ができる。
    /// ・段階は 準備（Lobby）→ 配置（Setup）→ 対局（Playing）→ 感想戦（Review）→ 準備。
    ///   感想戦の間は席を固定し、着席者がそろって「準備画面へ」を押すと準備に戻る。
    /// ・対局者には自分から見える情報（PlayerView）だけを送る。観戦者には両者の駒種を送る。
    /// ・配置・対局中に対局者が切断しても席は残し、同じ端末トークンで入り直すと続きから再開できる。
    /// ・席に座った CPU はサーバーが動かす（Tick を毎フレーム呼ぶこと）。
    /// </summary>
    public sealed class RoomService
    {
        /// <summary>相手がこの秒数以上切断していたら、残った側は勝ちを申請できる。</summary>
        public const double ClaimWinAfterSeconds = 60;
        public const int MaxMembers = 10;
        public const int MaxNameLength = 12;
        public static readonly string[] CpuLevelNames = { "やさしい", "ふつう", "つよい" };
        /// <summary>CPU が指すまでの最低の間（すぐ指すと相手が盤面を追えない）。</summary>
        const double CpuMinThinkSeconds = 0.6;

        sealed class Member
        {
            public int Id;
            public int Conn = -1;
            public string Token = "";
            public string Name = "";
            public double LeftAt;
            public bool Cpu;
            public int Level;
            public CpuPlayer Brain;
            public CpuPlayer.CpuThinking Thinking;
            public double ThinkStart;
            /// <summary>通信がつながっている人。</summary>
            public bool Online => Conn >= 0;
            /// <summary>部屋にいる（CPU は常にいる）。</summary>
            public bool Present => Cpu || Conn >= 0;
        }

        sealed class Room
        {
            public string Code;
            public bool Open;
            public int OwnerId = -1;
            public string RuleCode;
            public StandardRuleOptions Options;
            public RuleSet Rules;
            public GameState State;               // 配置・対局中だけ
            public RoomPhase Phase = RoomPhase.Lobby;
            public readonly int[] Seat = { RoomInfo.NoSeat, RoomInfo.NoSeat };
            public readonly bool[] Ready = new bool[2];
            public readonly bool[] ReviewDone = new bool[2];
            public readonly string[] SetupCode = new string[2];
            public readonly List<Member> Members = new List<Member>();
            public int NextId;

            public Member Find(int id) => Members.Find(m => m.Id == id);
            public Member ByConn(int conn) => Members.Find(m => m.Conn == conn);
            public int SeatOf(int memberId) => Seat[0] == memberId ? 0 : Seat[1] == memberId ? 1 : RoomInfo.NoSeat;
            public Member SeatMember(int s) => Seat[s] == RoomInfo.NoSeat ? null : Find(Seat[s]);
            /// <summary>人がつながっているか（CPU だけの部屋は閉じる）。</summary>
            public bool AnyOnline => Members.Exists(m => m.Online);
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

        /// <summary>開発用：部屋ごとの様子。通信には使わない。</summary>
        public List<string> DescribeRooms()
        {
            var list = new List<string>();
            foreach (var r in rooms.Values)
            {
                var sb = new StringBuilder();
                sb.Append($"部屋 {r.Code}  {r.Phase}  {(r.Open ? "オープン" : "プライベート")}  ルール {r.RuleCode}  {(r.State != null ? r.State.History.Count + "手" : "")}");
                foreach (var m in r.Members)
                {
                    int s = r.SeatOf(m.Id);
                    string seat = s == 0 ? "先手" : s == 1 ? "後手" : "観戦";
                    string who = m.Cpu ? "CPU" : m.Online ? $"接続 #{m.Conn}" : $"切断中 {clock() - m.LeftAt:0}秒";
                    string token = m.Token.Substring(0, Math.Min(6, m.Token.Length));
                    sb.Append($"\n  {(m.Id == r.OwnerId ? "★" : "・")}{m.Name}（{seat}・{who}・端末 {token}…）");
                    if (s >= 0) sb.Append($"{(r.Ready[s] ? " 準備完了" : "")}{(r.SetupCode[s] != null ? " 配置済" : "")}{(r.Phase == RoomPhase.Review && r.ReviewDone[s] ? " 感想戦終了" : "")}");
                }
                list.Add(sb.ToString());
            }
            return list;
        }

        // ───────── 受信 ─────────

        public void Receive(int conn, Packet p)
        {
            if (p == null) return;
            switch (p.Op)
            {
                case Op.CreateRoom: Create(conn, p); break;
                case Op.JoinRoom: Join(conn, p); break;
                case Op.ListRooms: ListRooms(conn, p); break;
                case Op.LeaveRoom: Leave(conn, voluntary: true); break;
                case Op.SetName: SetName(conn, p); break;
                case Op.TakeSeat: TakeSeat(conn, p); break;
                case Op.SetReady: SetReady(conn, p); break;
                case Op.SetRules: SetRules(conn, p); break;
                case Op.SetVisibility: SetVisibility(conn, p); break;
                case Op.TransferOwner: TransferOwner(conn, p); break;
                case Op.SetSeatCpu: SetSeatCpu(conn, p); break;
                case Op.StartGame: StartGame(conn); break;
                case Op.AbortSetup: AbortSetup(conn); break;
                case Op.SubmitSetup: SubmitSetup(conn, p); break;
                case Op.Move: Move(conn, p); break;
                case Op.Resign: Resign(conn); break;
                case Op.ClaimWin: ClaimWin(conn); break;
                case Op.FinishReview: FinishReview(conn); break;
                default: Error(conn, "不明な要求です"); break;
            }
        }

        public void Disconnected(int conn) => Leave(conn, voluntary: false);

        /// <summary>席に座った CPU を進める。サーバーのフレームごとに呼ぶ（1回あたり数ミリ秒だけ考える）。</summary>
        public void Tick()
        {
            foreach (var room in new List<Room>(rooms.Values))
            {
                if (room.Phase != RoomPhase.Playing || room.State == null) continue;
                int seat = room.State.CurrentPlayer;
                var m = room.SeatMember(seat);
                if (m == null || !m.Cpu) continue;
                if (m.Brain == null || m.Brain.Me != seat)
                    m.Brain = new CpuPlayer(room.Rules, seat, rng.Next(), CpuSettings.For((CpuLevel)m.Level));
                if (m.Thinking == null)
                {
                    m.Thinking = m.Brain.BeginThink(PlayerView.From(room.State, seat));
                    m.ThinkStart = clock();
                }
                if (!m.Thinking.Step(15)) continue;
                if (clock() - m.ThinkStart < CpuMinThinkSeconds) continue;
                var t = m.Thinking;
                m.Thinking = null;
                if (t.Resign)
                {
                    NoticeAll(room, $"{m.Name}が投了しました（{t.ResignReason}）");
                    room.State.Resign(seat);
                    FinishGame(room);
                }
                else ApplyAndBroadcast(room, t.Result);
            }
        }

        // ───────── 部屋に入る・出る ─────────

        bool CheckVersion(int conn, Packet p)
        {
            if (p.A == Packet.ProtocolVersion) return true;
            Error(conn, "サーバーとゲームのバージョンが違います。ページを再読み込みしてください。");
            return false;
        }

        static string CleanName(string name)
        {
            name = (name ?? "").Replace("\n", " ").Replace("\r", " ").Replace("<", "＜").Replace(">", "＞").Trim();
            return name.Length > MaxNameLength ? name.Substring(0, MaxNameLength) : name;
        }

        static string NameFrom(Packet p) => CleanName(p.Data.Length > 0 && p.Data.Length < 200 ? Encoding.UTF8.GetString(p.Data) : "");

        void Create(int conn, Packet p)
        {
            if (!CheckVersion(conn, p)) return;
            if (string.IsNullOrEmpty(p.S1)) { Error(conn, "端末の識別情報がありません"); return; }
            if (!RuleCodec.TryDecode(p.S2, out var options)) { Error(conn, "ルールコードが正しくありません"); return; }
            Leave(conn, voluntary: true);

            var room = new Room { Code = NewCode(), Open = p.B != 0 };
            ApplyRules(room, options);
            rooms[room.Code] = room;
            var m = AddMember(room, conn, p.S1, NameFrom(p));
            room.OwnerId = m.Id;
            room.Seat[0] = m.Id; // 作った人はまず先手席に（あとで自由に移れる）
            Welcome(room, m);
            BroadcastState(room);
        }

        void ApplyRules(Room room, StandardRuleOptions options)
        {
            room.Options = options;
            room.RuleCode = RuleCodec.Encode(options);
            room.Rules = StandardRules.Create23(options);
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

        Member AddMember(Room room, int conn, string token, string name)
        {
            var m = new Member { Id = room.NextId++, Conn = conn, Token = token };
            m.Name = string.IsNullOrEmpty(name) ? $"参加者{m.Id + 1}" : name;
            room.Members.Add(m);
            roomOfConn[conn] = room;
            return m;
        }

        void Join(int conn, Packet p)
        {
            if (!CheckVersion(conn, p)) return;
            if (string.IsNullOrEmpty(p.S1)) { Error(conn, "端末の識別情報がありません"); return; }
            if (!rooms.TryGetValue(p.S2.Trim(), out var room)) { Error(conn, "その番号の部屋はありません"); return; }

            // 同じ端末からの入り直し（再読み込み・再接続）なら、同じ参加者として戻す
            var same = room.Members.Find(x => !x.Cpu && x.Token == p.S1);
            if (same != null)
            {
                if (same.Conn == conn) { Welcome(room, same); return; }
                if (same.Online)
                {
                    roomOfConn.Remove(same.Conn);
                    send(same.Conn, Packet.Of(Op.Error, a: 1, s1: "別の画面からこの部屋に入り直したため、こちらは切断しました"));
                }
                if (roomOfConn.TryGetValue(conn, out var cur) && cur != room) Leave(conn, voluntary: true);
                same.Conn = conn;
                roomOfConn[conn] = room;
                var name = NameFrom(p);
                if (name.Length > 0) same.Name = name;
                Welcome(room, same);
                BroadcastState(room);
                return;
            }

            if (room.Members.Count >= MaxMembers) { Error(conn, "この部屋は満員です"); return; }
            Leave(conn, voluntary: true);
            var m = AddMember(room, conn, p.S1, NameFrom(p));
            Welcome(room, m);
            BroadcastState(room);
            if (room.Phase == RoomPhase.Setup || room.Phase == RoomPhase.Playing) Notice(conn, "対局中なので観戦で入りました");
        }

        /// <summary>入った（戻った）人に、自分のIDと今の盤面を送る。</summary>
        void Welcome(Room room, Member m)
        {
            send(m.Conn, Packet.Of(Op.RoomJoined, a: m.Id, s1: room.Code));
            send(m.Conn, Packet.Of(Op.RoomState, data: Info(room).Encode()));
            if (room.Phase == RoomPhase.Playing) SendSnapshot(room, m);
        }

        void Leave(int conn, bool voluntary)
        {
            if (!roomOfConn.TryGetValue(conn, out var room)) return;
            roomOfConn.Remove(conn);
            var m = room.ByConn(conn);
            if (m == null) return;
            int seat = room.SeatOf(m.Id);

            if (seat >= 0 && !voluntary && (room.Phase == RoomPhase.Setup || room.Phase == RoomPhase.Playing))
            {
                // 配置・対局中の切断：席（と提出済みの配置）を残して戻ってくるのを待つ
                m.Conn = -1;
                m.LeftAt = clock();
            }
            else if (seat >= 0 && room.Phase == RoomPhase.Playing)
            {
                // 対局中に自分から抜けたら投了
                room.State.Resign(seat);
                RemoveMember(room, m);
                FinishGame(room);
                return;
            }
            else
            {
                if (seat >= 0 && room.Phase == RoomPhase.Setup)
                {
                    BackToLobby(room);
                    NoticeAll(room, $"{m.Name}さんが退出したため、配置を中断しました");
                }
                RemoveMember(room, m);
                if (room.Phase == RoomPhase.Review) CheckReviewEnd(room);
            }

            if (!room.AnyOnline) { CloseRoom(room); return; }
            BroadcastState(room);
        }

        void RemoveMember(Room room, Member m)
        {
            int seat = room.SeatOf(m.Id);
            if (seat >= 0)
            {
                room.Seat[seat] = RoomInfo.NoSeat;
                room.Ready[seat] = false;
            }
            room.Members.Remove(m);
            if (m.Conn >= 0) roomOfConn.Remove(m.Conn);
            if (room.OwnerId == m.Id) PassOwnership(room);
        }

        /// <summary>部屋主がいなくなったら、つながっている人に引き継ぐ（着席者を優先）。CPU は部屋主にならない。</summary>
        void PassOwnership(Room room)
        {
            Member next = null;
            for (int s = 0; s < 2 && next == null; s++)
            {
                var sm = room.SeatMember(s);
                if (sm != null && sm.Online) next = sm;
            }
            if (next == null) next = room.Members.Find(x => x.Online);
            room.OwnerId = next?.Id ?? -1;
            if (next != null) NoticeAll(room, $"{next.Name}さんが部屋主になりました");
        }

        void CloseRoom(Room room)
        {
            rooms.Remove(room.Code);
            foreach (var m in room.Members) if (m.Conn >= 0) roomOfConn.Remove(m.Conn);
        }

        void ListRooms(int conn, Packet p)
        {
            if (!CheckVersion(conn, p)) return;
            var list = new RoomListInfo();
            foreach (var r in rooms.Values)
            {
                if (!r.Open || !r.AnyOnline) continue;
                list.Rooms.Add(new RoomListInfo.Entry
                {
                    Code = r.Code,
                    Phase = r.Phase,
                    OwnerName = r.Find(r.OwnerId)?.Name ?? "",
                    RuleCode = r.RuleCode,
                    First = r.SeatMember(0)?.Name ?? "",
                    Second = r.SeatMember(1)?.Name ?? "",
                    Members = r.Members.Count,
                });
            }
            list.Rooms.Sort((a, b) => a.Phase != b.Phase ? a.Phase.CompareTo(b.Phase) : string.CompareOrdinal(a.Code, b.Code));
            if (list.Rooms.Count > 50) list.Rooms.RemoveRange(50, list.Rooms.Count - 50);

            // 前の部屋：まだあり、この端末が参加者として残っている（対局中に切断した席など）
            if (!string.IsNullOrEmpty(p.S1) && !string.IsNullOrEmpty(p.S2) && rooms.TryGetValue(p.S1, out var prev))
                list.CanRejoin = prev.Members.Exists(x => !x.Cpu && x.Token == p.S2);
            send(conn, Packet.Of(Op.RoomList, data: list.Encode()));
        }

        // ───────── 準備画面の操作 ─────────

        bool TryMember(int conn, out Room room, out Member m)
        {
            m = null;
            if (!roomOfConn.TryGetValue(conn, out room)) { Error(conn, "部屋に入っていません"); return false; }
            m = room.ByConn(conn);
            return m != null;
        }

        bool TryOwner(int conn, out Room room, out Member m)
        {
            if (!TryMember(conn, out room, out m)) return false;
            if (room.OwnerId == m.Id) return true;
            Error(conn, "部屋主だけができる操作です");
            return false;
        }

        bool RequireLobby(int conn, Room room)
        {
            if (room.Phase == RoomPhase.Lobby) return true;
            Error(conn, room.Phase == RoomPhase.Review ? "対局者が感想戦を終えるまで待ってください" : "対局中はできません");
            return false;
        }

        void SetName(int conn, Packet p)
        {
            if (!TryMember(conn, out var room, out var m)) return;
            var name = CleanName(p.S1);
            if (name.Length == 0) return;
            m.Name = name;
            BroadcastState(room);
        }

        void TakeSeat(int conn, Packet p)
        {
            if (!TryMember(conn, out var room, out var m)) return;
            if (!RequireLobby(conn, room)) return;
            int target = p.A;
            int current = room.SeatOf(m.Id);
            if (target == current) return;
            if (target == RoomInfo.NoSeat)
            {
                room.Seat[current] = RoomInfo.NoSeat;
                room.Ready[current] = false;
                BroadcastState(room);
                return;
            }
            if (target != 0 && target != 1) return;

            var occupant = room.SeatMember(target);
            if (occupant != null)
            {
                // 埋まっている席に座れるのは部屋主だけ。座っていた人は部屋主の元の席へ（観戦からなら観戦へ、CPU なら外す）
                if (room.OwnerId != m.Id) { Error(conn, "その席にはほかの人が座っています"); return; }
                if (current != RoomInfo.NoSeat) room.Seat[current] = occupant.Id;
                else if (occupant.Cpu) room.Members.Remove(occupant);
            }
            else if (current != RoomInfo.NoSeat)
            {
                room.Seat[current] = RoomInfo.NoSeat;
            }
            room.Seat[target] = m.Id;
            ResetReady(room); // 席が変わったら準備完了は取り直し
            BroadcastState(room);
        }

        void SetReady(int conn, Packet p)
        {
            if (!TryMember(conn, out var room, out var m)) return;
            int seat = room.SeatOf(m.Id);
            if (seat < 0 || room.Phase != RoomPhase.Lobby) return;
            room.Ready[seat] = p.A != 0;
            BroadcastState(room);
        }

        void SetRules(int conn, Packet p)
        {
            if (!TryOwner(conn, out var room, out _)) return;
            if (!RequireLobby(conn, room)) return;
            if (!RuleCodec.TryDecode(p.S1, out var options)) { Error(conn, "ルールコードが正しくありません"); return; }
            if (RuleCodec.Encode(options) == room.RuleCode) return;
            ApplyRules(room, options);
            ResetReady(room);
            BroadcastState(room);
            NoticeAll(room, "ルールが変更されました。準備完了を押し直してください");
        }

        void SetVisibility(int conn, Packet p)
        {
            if (!TryOwner(conn, out var room, out _)) return;
            room.Open = p.A != 0;
            BroadcastState(room);
        }

        void TransferOwner(int conn, Packet p)
        {
            if (!TryOwner(conn, out var room, out var m)) return;
            var target = room.Find(p.A);
            if (target == null || target.Cpu || !target.Online || target == m) { Error(conn, "その人には譲れません"); return; }
            room.OwnerId = target.Id;
            BroadcastState(room);
            NoticeAll(room, $"{target.Name}さんが部屋主になりました");
        }

        /// <summary>部屋主が席に CPU を座らせる（B=強さ）・強さを変える・外す（B=-1）。</summary>
        void SetSeatCpu(int conn, Packet p)
        {
            if (!TryOwner(conn, out var room, out _)) return;
            if (!RequireLobby(conn, room)) return;
            int seat = p.A;
            if (seat != 0 && seat != 1) return;
            var occupant = room.SeatMember(seat);
            if (p.B < 0)
            {
                if (occupant != null && occupant.Cpu) RemoveMember(room, occupant);
                BroadcastState(room);
                return;
            }
            int level = Math.Max(0, Math.Min(CpuLevelNames.Length - 1, p.B));
            string moved = null;
            if (occupant != null && !occupant.Cpu)
            {
                // 人が座っている席は、その人を観戦に移して CPU と交代する
                moved = occupant.Name;
                room.Seat[seat] = RoomInfo.NoSeat;
                occupant = null;
            }
            if (occupant == null)
            {
                if (room.Members.Count >= MaxMembers) { Error(conn, "この部屋は満員です"); return; }
                occupant = new Member { Id = room.NextId++, Cpu = true, Token = "cpu" };
                room.Members.Add(occupant);
                room.Seat[seat] = occupant.Id;
            }
            occupant.Level = level;
            occupant.Name = $"CPU（{CpuLevelNames[level]}）";
            room.Ready[seat] = true;
            if (moved != null) ResetReady(room);
            BroadcastState(room);
            if (moved != null) NoticeAll(room, $"{moved}さんの席に CPU が座りました（{moved}さんは観戦になります）");
        }

        /// <summary>準備完了を外す（CPU は常に準備完了）。</summary>
        static void ResetReady(Room room)
        {
            for (int s = 0; s < 2; s++) room.Ready[s] = room.SeatMember(s)?.Cpu ?? false;
        }

        void StartGame(int conn)
        {
            if (!TryOwner(conn, out var room, out _)) return;
            if (!Info(room).CanStart) { Error(conn, "2人が着席して準備完了になると開始できます"); return; }
            room.State = new GameState(room.Rules);
            room.SetupCode[0] = room.SetupCode[1] = null;
            room.Phase = RoomPhase.Setup;
            // CPU は開始と同時に配置を出す
            for (int s = 0; s < 2; s++)
            {
                var m = room.SeatMember(s);
                if (m == null || !m.Cpu) continue;
                m.Brain = new CpuPlayer(room.Rules, s, rng.Next(), CpuSettings.For((CpuLevel)m.Level));
                m.Thinking = null;
                var placements = m.Brain.ChooseSetup();
                if (room.State.SubmitSetup(s, placements).Count == 0)
                    room.SetupCode[s] = SetupCodec.Encode(room.State.Topology, s, placements);
            }
            AfterSetupChanged(room);
        }

        void AbortSetup(int conn)
        {
            if (!TryMember(conn, out var room, out var m)) return;
            if (room.Phase != RoomPhase.Setup || room.SeatOf(m.Id) < 0) return;
            BackToLobby(room);
            BroadcastState(room);
            NoticeAll(room, $"{m.Name}さんが配置を中断しました");
        }

        void BackToLobby(Room room)
        {
            room.Phase = RoomPhase.Lobby;
            room.State = null;
            room.SetupCode[0] = room.SetupCode[1] = null;
            room.ReviewDone[0] = room.ReviewDone[1] = false;
            ResetReady(room);
            // 戻ってこなかった対局者は外す
            foreach (var m in room.Members.ToArray())
                if (!m.Present) RemoveMember(room, m);
        }

        // ───────── 対局 ─────────

        bool TrySeat(int conn, out Room room, out int seat)
        {
            seat = RoomInfo.NoSeat;
            if (!TryMember(conn, out room, out var m)) return false;
            seat = room.SeatOf(m.Id);
            return seat >= 0 && room.State != null;
        }

        void SubmitSetup(int conn, Packet p)
        {
            if (!TrySeat(conn, out var room, out int seat)) return;
            if (room.Phase != RoomPhase.Setup) { Error(conn, "配置の時間ではありません"); return; }
            if (room.SetupCode[seat] != null) { Error(conn, "配置は提出済みです"); return; }
            var placements = SetupCodec.Decode(room.Rules, room.State.Topology, seat, p.S1);
            if (placements == null) { Error(conn, "配置の形式が正しくありません"); return; }
            var errors = room.State.SubmitSetup(seat, placements);
            if (errors.Count > 0) { Error(conn, errors[0]); return; }
            room.SetupCode[seat] = SetupCodec.Encode(room.State.Topology, seat, placements);
            AfterSetupChanged(room);
        }

        /// <summary>配置が出そろっていれば対局を始め、全員に盤面を送る。</summary>
        void AfterSetupChanged(Room room)
        {
            if (room.State.Phase == GamePhase.Playing)
            {
                room.Phase = RoomPhase.Playing;
                BroadcastState(room);
                foreach (var m in room.Members) SendSnapshot(room, m);
            }
            else BroadcastState(room);
        }

        void Move(int conn, Packet p)
        {
            if (!TrySeat(conn, out var room, out int seat)) return;
            var move = new Move(p.A, p.B);
            if (room.Phase != RoomPhase.Playing || room.State.CurrentPlayer != seat || !room.State.IsLegal(move))
            {
                Error(conn, "その手は指せません");
                if (room.Phase == RoomPhase.Playing) SendSnapshot(room, room.ByConn(conn)); // 画面を正しい状態に戻す
                return;
            }
            ApplyAndBroadcast(room, move);
        }

        void ApplyAndBroadcast(Room room, Move move)
        {
            var state = room.State;
            var record = state.ApplyMove(move);
            foreach (var m in room.Members)
            {
                if (!m.Online) continue;
                var mv = PlayerView.ProjectMove(state, record, ViewerOf(room, m));
                send(m.Conn, Packet.Of(Op.MoveMade, state.CurrentPlayer, (int)state.Result, (int)state.EndReason,
                    data: ViewCodec.EncodeMove(mv)));
            }
            if (state.Phase == GamePhase.Finished) FinishGame(room);
        }

        void Resign(int conn)
        {
            if (!TrySeat(conn, out var room, out int seat)) return;
            if (room.Phase != RoomPhase.Playing) return;
            room.State.Resign(seat);
            FinishGame(room);
        }

        void ClaimWin(int conn)
        {
            if (!TrySeat(conn, out var room, out int seat)) return;
            var other = room.SeatMember(1 - seat);
            if (room.Phase != RoomPhase.Playing || other == null || other.Present || clock() - other.LeftAt < ClaimWinAfterSeconds)
            {
                Error(conn, "まだ勝ちを申請できません");
                return;
            }
            room.State.Resign(1 - seat);
            FinishGame(room);
        }

        /// <summary>終局：全員に全公開の盤面と配置を送り、感想戦の段階へ（席は固定したまま）。</summary>
        void FinishGame(Room room)
        {
            var state = room.State;
            foreach (var m in room.Members)
            {
                if (!m.Online) continue;
                var view = PlayerView.From(state, ViewerOf(room, m), revealAll: true);
                send(m.Conn, Packet.Of(Op.GameOver, (int)state.Result, (int)state.EndReason,
                    s1: room.SetupCode[0] ?? "", s2: room.SetupCode[1] ?? "", data: ViewCodec.Encode(view)));
            }
            room.Phase = RoomPhase.Review;
            room.ReviewDone[0] = room.ReviewDone[1] = false;
            foreach (var m in room.Members) m.Thinking = null;
            if (!room.AnyOnline) { CloseRoom(room); return; }
            CheckReviewEnd(room);
            BroadcastState(room);
        }

        void FinishReview(int conn)
        {
            if (!TryMember(conn, out var room, out var m)) return;
            if (room.Phase != RoomPhase.Review) return;
            int seat = room.SeatOf(m.Id);
            if (seat < 0) return; // 観戦者はいつでも準備画面を見られる（席は動かせない）
            room.ReviewDone[seat] = true;
            CheckReviewEnd(room);
            BroadcastState(room);
        }

        /// <summary>着席者がみな感想戦を終えた（または不在・CPU）なら準備段階に戻す。</summary>
        void CheckReviewEnd(Room room)
        {
            if (room.Phase != RoomPhase.Review) return;
            for (int s = 0; s < 2; s++)
            {
                var sm = room.SeatMember(s);
                if (sm == null || sm.Cpu || !sm.Online) room.ReviewDone[s] = true;
                if (!room.ReviewDone[s]) return;
            }
            BackToLobby(room);
        }

        // ───────── 送信 ─────────

        static int ViewerOf(Room room, Member m)
        {
            int s = room.SeatOf(m.Id);
            return s >= 0 ? s : Visibility.Spectator;
        }

        void Error(int conn, string message) => send(conn, Packet.Of(Op.Error, s1: message));
        void Notice(int conn, string message) => send(conn, Packet.Of(Op.Notice, s1: message));

        void NoticeAll(Room room, string message)
        {
            foreach (var m in room.Members) if (m.Online) Notice(m.Conn, message);
        }

        RoomInfo Info(Room room)
        {
            var info = new RoomInfo
            {
                Code = room.Code,
                Phase = room.Phase,
                Open = room.Open,
                OwnerId = room.OwnerId,
                RuleCode = room.RuleCode,
            };
            for (int s = 0; s < 2; s++)
            {
                info.SeatMember[s] = room.Seat[s];
                info.Ready[s] = room.Ready[s];
                info.SetupDone[s] = room.SetupCode[s] != null;
                info.ReviewDone[s] = room.ReviewDone[s];
                var sm = room.SeatMember(s);
                info.AbsentSeconds[s] = sm != null && !sm.Present ? (int)(clock() - sm.LeftAt) : -1;
            }
            foreach (var m in room.Members)
                info.Members.Add(new MemberInfo { Id = m.Id, Name = m.Name, Present = m.Present, IsCpu = m.Cpu, CpuLevel = m.Level });
            return info;
        }

        void BroadcastState(Room room)
        {
            var data = Info(room).Encode();
            foreach (var m in room.Members) if (m.Online) send(m.Conn, Packet.Of(Op.RoomState, data: data));
        }

        void SendSnapshot(Room room, Member m)
        {
            if (m == null || !m.Online || room.State == null || room.State.Phase == GamePhase.Setup) return;
            send(m.Conn, Packet.Of(Op.Snapshot, data: ViewCodec.Encode(PlayerView.From(room.State, ViewerOf(room, m)))));
        }
    }
}
