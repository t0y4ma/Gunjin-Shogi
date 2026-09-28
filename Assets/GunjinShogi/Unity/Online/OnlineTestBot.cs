#if UNITY_EDITOR
using System;
using GunjinShogi.Core;
using GunjinShogi.Core.Ai;
using GunjinShogi.Core.Online;

namespace GunjinShogi.UnityView
{
    /// <summary>エディタの模擬サーバーに入れる対戦相手の動き方。</summary>
    public enum TestBotMode { Random, CpuEasy, CpuNormal, CpuHard, Idle }

    /// <summary>
    /// 開発用の対戦相手。「もう1人のクライアント」として振る舞い、受け取った Packet だけを頼りに動く。
    /// 準備画面では空いている席に座って準備完了を押し、自分が部屋主なら2人そろった時点で開始する。
    /// </summary>
    public sealed class OnlineTestBot
    {
        readonly Action<Packet> send;
        readonly Random rng = new Random();
        RuleSet rules;
        BoardTopology topo;
        string ruleCode;
        int myId = -1;
        RoomInfo room;
        PlayerView view;
        CpuPlayer cpu;
        CpuPlayer.CpuThinking thinking;
        float wait;
        bool setupSent;

        public TestBotMode Mode;
        /// <summary>自分の番が来てから指すまでの最低待ち時間（秒）。</summary>
        public float MoveDelay = 0.6f;
        /// <summary>準備画面で自動的に着席して準備完了を押す。</summary>
        public bool AutoReady = true;

        public int Seat => room == null ? RoomInfo.NoSeat : room.SeatOf(myId);
        public string LastAction { get; private set; } = "";

        public OnlineTestBot(Action<Packet> send, TestBotMode mode)
        {
            this.send = send;
            Mode = mode;
        }

        public void Receive(Packet p)
        {
            switch (p.Op)
            {
                case Op.RoomJoined:
                    myId = p.A;
                    break;
                case Op.RoomState:
                    room = RoomInfo.Decode(p.Data);
                    if (room.RuleCode != ruleCode)
                    {
                        ruleCode = room.RuleCode;
                        RuleCodec.TryDecode(ruleCode, out var o);
                        rules = StandardRules.Create23(o ?? new StandardRuleOptions());
                        topo = new BoardTopology(rules.Board);
                    }
                    OnRoom();
                    break;
                case Op.Snapshot:
                    view = ViewCodec.Decode(p.Data);
                    thinking = null;
                    wait = MoveDelay;
                    break;
                case Op.MoveMade:
                    view?.ApplyMove(ViewCodec.DecodeMove(p.Data), p.A, (GameResult)p.B, (EndReason)p.C);
                    thinking = null;
                    wait = MoveDelay;
                    break;
                case Op.GameOver:
                    view = null;
                    break;
            }
        }

        void OnRoom()
        {
            int seat = Seat;
            if (room.Phase == RoomPhase.Lobby)
            {
                setupSent = false;
                view = null;
                if (!AutoReady || Mode == TestBotMode.Idle) return;
                if (seat < 0)
                {
                    int free = room.SeatMember[1] == RoomInfo.NoSeat ? 1 : room.SeatMember[0] == RoomInfo.NoSeat ? 0 : -1;
                    if (free >= 0) { send(Packet.Of(Op.TakeSeat, free)); LastAction = (free == 0 ? "先手" : "後手") + "席に着いた"; }
                    return;
                }
                if (!room.Ready[seat]) { send(Packet.Of(Op.SetReady, 1)); LastAction = "準備完了"; return; }
                if (room.OwnerId == myId && room.CanStart) { send(Packet.Of(Op.StartGame)); LastAction = "対局を開始した"; }
                return;
            }
            if (room.Phase == RoomPhase.Setup && seat >= 0 && !room.SetupDone[seat] && !setupSent && Mode != TestBotMode.Idle)
            {
                setupSent = true;
                cpu = IsCpu ? new CpuPlayer(rules, seat, rng.Next(), CpuSettings.For(Level)) : null;
                var setup = cpu != null ? cpu.ChooseSetup() : RandomSetup.Generate(rules, topo, seat, rng);
                send(Packet.Of(Op.SubmitSetup, s1: SetupCodec.Encode(topo, seat, setup)));
                LastAction = "配置を提出";
            }
        }

        bool IsCpu => Mode == TestBotMode.CpuEasy || Mode == TestBotMode.CpuNormal || Mode == TestBotMode.CpuHard;
        CpuLevel Level => Mode == TestBotMode.CpuEasy ? CpuLevel.Easy : Mode == TestBotMode.CpuHard ? CpuLevel.Hard : CpuLevel.Normal;

        public void Tick(float dt)
        {
            if (Mode == TestBotMode.Idle) return;
            int seat = Seat;
            if (view == null || seat < 0 || view.Phase != GamePhase.Playing || view.CurrentPlayer != seat) return;
            wait -= dt;

            if (IsCpu)
            {
                if (cpu == null || cpu.Me != seat) cpu = new CpuPlayer(rules, seat, rng.Next(), CpuSettings.For(Level));
                if (thinking == null) thinking = cpu.BeginThink(view);
                if (!thinking.Step(8) || wait > 0) return;
                if (thinking.Resign)
                {
                    send(Packet.Of(Op.Resign));
                    LastAction = "投了（" + thinking.ResignReason + "）";
                    view = null;
                    return;
                }
                Send(thinking.Result);
                return;
            }

            if (wait > 0) return;
            var moves = view.ToMoveState(rules, topo).GetLegalMoves();
            if (moves.Count == 0) return;
            Send(moves[rng.Next(moves.Count)]);
        }

        void Send(Move m)
        {
            wait = 999; // 次の MoveMade まで待つ
            thinking = null;
            send(Packet.Of(Op.Move, m.PieceId, m.ToNode));
            LastAction = $"指した（駒 #{m.PieceId} → {m.ToNode}）";
        }
    }
}
#endif
