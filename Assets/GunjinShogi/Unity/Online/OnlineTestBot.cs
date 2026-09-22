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
    /// 開発用の対戦相手。「もう1人のクライアント」として振る舞い、
    /// 受け取った Packet だけを頼りに配置と指し手を送る（本物のクライアントと同じ情報しか使わない）。
    /// </summary>
    public sealed class OnlineTestBot
    {
        readonly Action<Packet> send;
        readonly Random rng = new Random();
        RuleSet rules;
        BoardTopology topo;
        int seat = -1;
        PlayerView view;
        CpuPlayer cpu;
        CpuPlayer.CpuThinking thinking;
        float wait;
        bool setupSent;

        public TestBotMode Mode;
        /// <summary>自分の番が来てから指すまでの最低待ち時間（秒）。</summary>
        public float MoveDelay = 0.6f;
        /// <summary>相手の再戦申し込みに自動で応じる。</summary>
        public bool AcceptRematch = true;

        public int Seat => seat;
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
                    // 再接続（同じ席）なら続きの Snapshot を待つだけ
                    if (rules != null && seat == p.A) break;
                    seat = p.A;
                    RuleCodec.TryDecode(p.S2, out var o);
                    rules = StandardRules.Create23(o);
                    topo = new BoardTopology(rules.Board);
                    setupSent = false;
                    view = null;
                    cpu = null;
                    break;
                case Op.RoomStatus:
                    var phase = (RoomPhase)p.A;
                    var flags = (StatusFlags)p.B;
                    if (phase == RoomPhase.Setup && !setupSent && rules != null && Mode != TestBotMode.Idle)
                    {
                        setupSent = true;
                        view = null;
                        cpu = IsCpu ? new CpuPlayer(rules, seat, rng.Next(), CpuSettings.For(Level)) : null;
                        var setup = cpu != null ? cpu.ChooseSetup() : RandomSetup.Generate(rules, topo, seat, rng);
                        send(Packet.Of(Op.SubmitSetup, s1: SetupCodec.Encode(topo, seat, setup)));
                        LastAction = "配置を提出";
                    }
                    if (AcceptRematch && phase == RoomPhase.Finished
                        && (flags & StatusFlags.OpponentWantsRematch) != 0 && (flags & StatusFlags.IWantRematch) == 0)
                    {
                        send(Packet.Of(Op.Rematch));
                        LastAction = "再戦に応じた";
                    }
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
            }
        }

        bool IsCpu => Mode == TestBotMode.CpuEasy || Mode == TestBotMode.CpuNormal || Mode == TestBotMode.CpuHard;
        CpuLevel Level => Mode == TestBotMode.CpuEasy ? CpuLevel.Easy : Mode == TestBotMode.CpuHard ? CpuLevel.Hard : CpuLevel.Normal;

        public void Tick(float dt)
        {
            if (Mode == TestBotMode.Idle) return;
            if (view == null || view.Phase != GamePhase.Playing || view.CurrentPlayer != seat) return;
            wait -= dt;

            if (IsCpu)
            {
                if (cpu == null) cpu = new CpuPlayer(rules, seat, rng.Next(), CpuSettings.For(Level)); // 途中から CPU に切り替えた場合
                if (thinking == null) thinking = cpu.BeginThink(view);
                if (!thinking.Step(8) || wait > 0) return; // 1フレームに 8ms ずつ考える
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
