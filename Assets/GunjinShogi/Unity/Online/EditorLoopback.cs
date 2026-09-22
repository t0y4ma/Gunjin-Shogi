#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using GunjinShogi.Core.Online;
using UnityEditor;
using UnityEngine;

namespace GunjinShogi.UnityView
{
    /// <summary>
    /// エディタ専用の「模擬サーバー」。本番と同じ RoomService と同じ Packet 形式（バイト列に直してから戻す）を使い、
    /// Mirror とソケットの代わりにプロセス内の遅延付きキューで受け渡す。
    /// 本番との違い：通信路が無い・遅延は一定・サーバーはエディタと同じプロセス。
    /// そのかわり、切断・再接続・サーバー停止・時間経過などをボタン1つで起こせる（ウィンドウ「軍人将棋/オンライン デバッグ」）。
    /// </summary>
    public sealed class EditorLoopback : MonoBehaviour
    {
        const string PrefUse = "gs.debug.loopback";
        const string PrefLatency = "gs.debug.latencyMs";

        /// <summary>true ならエディタのオンライン対戦は模擬サーバーにつなぐ（既定）。false なら Mirror で実サーバーへ。</summary>
        public static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefUse, true);
            set => EditorPrefs.SetBool(PrefUse, value);
        }

        /// <summary>片道の遅延（ミリ秒）。</summary>
        public static int LatencyMs
        {
            get => EditorPrefs.GetInt(PrefLatency, 80);
            set => EditorPrefs.SetInt(PrefLatency, Mathf.Clamp(value, 0, 3000));
        }

        /// <summary>ロビーの「相手役ボットを入れる」で使う動き方。</summary>
        public static TestBotMode DefaultBotMode
        {
            get => (TestBotMode)EditorPrefs.GetInt("gs.debug.botMode", (int)TestBotMode.CpuNormal);
            set => EditorPrefs.SetInt("gs.debug.botMode", (int)value);
        }

        static EditorLoopback instance;
        public static EditorLoopback Instance
        {
            get
            {
                if (instance == null && Application.isPlaying)
                {
                    var go = new GameObject("[EditorLoopback]") { hideFlags = HideFlags.DontSave };
                    DontDestroyOnLoad(go);
                    instance = go.AddComponent<EditorLoopback>();
                }
                return instance;
            }
        }
        public static bool Exists => instance != null;

        // ───────── サーバー ─────────

        RoomService service;
        int nextConn;
        public bool ServerUp { get; private set; }
        public int ServerStarts { get; private set; }

        /// <summary>画面側が使う接続。</summary>
        public LoopbackClient Client { get; private set; }

        // 相手役（ボット）
        public OnlineTestBot Bot { get; private set; }
        public int BotConn { get; private set; } = -1;
        public string BotRoom { get; private set; } = "";
        const string BotToken = "debug-opponent";

        /// <summary>あなたが最後に入った部屋。</summary>
        public string PlayerRoom { get; private set; } = "";

        // 通信量と記録
        public long BytesUp, BytesDown;
        public int PacketsUp, PacketsDown;
        public readonly List<string> Log = new List<string>();
        const int LogMax = 200;

        struct Pending { public double At; public long Seq; public Action Run; }
        long seq; // 同じ時刻の便は送った順に届ける（TCP/WebSocket と同じく順序は保たれる）
        readonly List<Pending> queue = new List<Pending>();

        void Awake()
        {
            Client = new LoopbackClient(this);
            StartServer();
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
            NetClock.Offset = 0;
        }

        double Now => Time.unscaledTimeAsDouble;

        double lastAt;

        void Later(Action a, double extraSeconds = 0)
        {
            // 遅延を途中で変えても、追い越しは起こさない
            lastAt = Math.Max(lastAt, Now + LatencyMs / 1000.0 + extraSeconds);
            queue.Add(new Pending { At = lastAt, Seq = seq++, Run = a });
        }

        void Update()
        {
            // 遅延キューを時刻順に実行（実行中に追加されたものは次のフレーム以降）
            var due = new List<Pending>();
            for (int i = queue.Count - 1; i >= 0; i--)
                if (queue[i].At <= Now) { due.Add(queue[i]); queue.RemoveAt(i); }
            due.Sort((a, b) => a.At != b.At ? a.At.CompareTo(b.At) : a.Seq.CompareTo(b.Seq));
            foreach (var p in due) p.Run();

            Bot?.Tick(Time.unscaledDeltaTime);
        }

        public void StartServer()
        {
            if (ServerUp) return;
            service = new RoomService(ServerSend, () => Now + NetClock.Offset);
            ServerUp = true;
            ServerStarts++;
            AddLog("■ サーバー起動");
        }

        /// <summary>サーバーを止める。つながっている全員が切断され、部屋はすべて消える（プロセスが落ちたのと同じ）。</summary>
        public void StopServer()
        {
            if (!ServerUp) return;
            ServerUp = false;
            service = null;
            queue.Clear();
            AddLog("■ サーバー停止（部屋はすべて消えた）");
            Client.ServerLost();
            if (BotConn >= 0) { BotConn = -1; AddLog("相手役：切断された"); }
        }

        public void RestartServer()
        {
            StopServer();
            StartServer();
        }

        public List<string> DescribeRooms() => service != null ? service.DescribeRooms() : new List<string>();

        internal int OpenConnection()
        {
            int id = nextConn++;
            return id;
        }

        internal void ClientToServer(int conn, Packet p, string who)
        {
            var bytes = p.Encode();
            BytesUp += bytes.Length;
            PacketsUp++;
            AddLog($"{who} → サーバー  {Describe(p)}  {bytes.Length}B");
            Later(() =>
            {
                if (!ServerUp) return;
                service.Receive(conn, Packet.Decode(bytes));
            });
        }

        internal void ClientClosed(int conn)
        {
            Later(() => { if (ServerUp) service.Disconnected(conn); });
        }

        void ServerSend(int conn, Packet p)
        {
            var bytes = p.Encode();
            if (conn == BotConn)
            {
                AddLog($"サーバー → 相手役  {Describe(p)}  {bytes.Length}B");
                if (p.Op == Op.RoomJoined) BotRoom = p.S1;
                var bot = Bot;
                Later(() => { if (Bot == bot && BotConn == conn) bot.Receive(Packet.Decode(bytes)); });
                return;
            }
            if (Client.Conn == conn)
            {
                if (p.Op == Op.RoomJoined) PlayerRoom = p.S1;
                BytesDown += bytes.Length;
                PacketsDown++;
                AddLog($"サーバー → あなた  {Describe(p)}  {bytes.Length}B");
                Later(() => Client.Deliver(conn, Packet.Decode(bytes)));
            }
        }

        // ───────── 相手役 ─────────

        /// <summary>部屋に相手役を入れる（部屋番号を省略すると、あなたが今いる部屋）。</summary>
        public void AddBot(string room, TestBotMode mode)
        {
            if (!ServerUp || string.IsNullOrEmpty(room)) return;
            RemoveBot(false);
            Bot = new OnlineTestBot(p => BotSend(p), mode);
            BotRoom = room;
            BotConnect();
            AddLog($"相手役（{mode}）を部屋 {room} に入れる");
        }

        /// <summary>相手役に部屋を作らせる（あなたが「部屋に入る」側を試すため）。部屋番号は BotRoom に入る。</summary>
        public void BotCreateRoom(TestBotMode mode, string ruleCode)
        {
            if (!ServerUp) return;
            RemoveBot(false);
            Bot = new OnlineTestBot(p => BotSend(p), mode);
            BotRoom = "";
            BotConn = OpenConnection();
            ClientToServer(BotConn, Packet.Of(Op.CreateRoom, Packet.ProtocolVersion, s1: BotToken, s2: string.IsNullOrEmpty(ruleCode) ? "0" : ruleCode), "相手役");
            AddLog($"相手役（{mode}）が部屋を作る（ルール {ruleCode}）");
        }

        void BotSend(Packet p)
        {
            if (BotConn < 0) return;
            ClientToServer(BotConn, p, "相手役");
        }

        void BotConnect()
        {
            BotConn = OpenConnection();
            ClientToServer(BotConn, Packet.Of(Op.JoinRoom, Packet.ProtocolVersion, s1: BotToken, s2: BotRoom), "相手役");
        }

        /// <summary>相手役の通信を切る（席は残る。再接続で戻れる）。</summary>
        public void DisconnectBot()
        {
            if (BotConn < 0) return;
            AddLog("相手役：通信が切れた");
            ClientClosed(BotConn);
            BotConn = -1;
        }

        public void ReconnectBot()
        {
            if (Bot == null || BotConn >= 0 || !ServerUp) return;
            AddLog("相手役：同じ端末で入り直す");
            BotConnect();
        }

        public void BotResign() => BotSend(Packet.Of(Op.Resign));
        public void BotRematch() => BotSend(Packet.Of(Op.Rematch));

        /// <summary>相手役を部屋から退出させる（leave=true なら退出を送る。対局中なら投了扱い）。</summary>
        public void RemoveBot(bool leave = true)
        {
            if (Bot == null) return;
            if (leave && BotConn >= 0) { BotSend(Packet.Of(Op.LeaveRoom)); AddLog("相手役：退出"); }
            if (BotConn >= 0) ClientClosed(BotConn);
            Bot = null;
            BotConn = -1;
            BotRoom = "";
        }

        // ───────── その他 ─────────

        /// <summary>サーバーと画面の時計を進める（「勝ちを申請」の60秒待ちを飛ばす）。</summary>
        public void AdvanceTime(float seconds)
        {
            NetClock.Offset += seconds;
            AddLog($"時間を {seconds:0} 秒進めた");
        }

        public void ResetCounters()
        {
            BytesUp = BytesDown = 0;
            PacketsUp = PacketsDown = 0;
            Log.Clear();
        }

        internal void AddLog(string s)
        {
            Log.Add($"{Time.unscaledTime,7:0.0}  {s}");
            if (Log.Count > LogMax) Log.RemoveRange(0, Log.Count - LogMax);
        }

        static string Describe(Packet p)
        {
            switch (p.Op)
            {
                case Op.Move: return $"Move 駒#{p.A}→{p.B}";
                case Op.RoomStatus: return $"RoomStatus {(RoomPhase)p.A} [{(StatusFlags)p.B}]";
                case Op.RoomJoined: return $"RoomJoined 部屋{p.S1} 席{p.A}";
                case Op.Error: return $"Error {p.S1}";
                case Op.MoveMade: return $"MoveMade 次{p.A} 結果{p.B}";
                default: return p.Op.ToString();
            }
        }
    }

    /// <summary>模擬サーバーへの「あなた」側の接続。</summary>
    public sealed class LoopbackClient : IGameConnection
    {
        readonly EditorLoopback server;
        bool connecting;

        public event Action<Packet> PacketReceived;
        public event Action ClientConnected;
        public event Action ClientDisconnected;

        public int Conn { get; private set; } = -1;
        public bool IsClientConnected => Conn >= 0 && !connecting;
        public bool IsConnecting => connecting;
        public string AddressLabel => $"エディタ内の模擬サーバー・遅延 {EditorLoopback.LatencyMs}ms";

        internal LoopbackClient(EditorLoopback server) { this.server = server; }

        public void Connect()
        {
            if (Conn >= 0 || connecting) return;
            connecting = true;
            server.AddLog("あなた：接続を開始");
            // 接続の確立は1往復ぶん待つ。サーバーが落ちていれば失敗する
            server.StartCoroutine(After(EditorLoopback.LatencyMs * 2 / 1000f, () =>
            {
                connecting = false;
                if (!server.ServerUp)
                {
                    server.AddLog("あなた：接続できなかった（サーバー停止中）");
                    ClientDisconnected?.Invoke();
                    return;
                }
                Conn = server.OpenConnection();
                server.AddLog($"あなた：接続した #{Conn}");
                ClientConnected?.Invoke();
            }));
        }

        public void Disconnect()
        {
            if (Conn < 0 && !connecting) return;
            bool was = Conn >= 0;
            if (was) server.ClientClosed(Conn);
            Conn = -1;
            connecting = false;
            server.AddLog("あなた：切断した");
            ClientDisconnected?.Invoke();
        }

        /// <summary>デバッグ：通信路が突然切れた（自分から切ったのではない）。</summary>
        public void Drop()
        {
            if (Conn < 0) return;
            server.AddLog("あなた：通信が突然切れた");
            server.ClientClosed(Conn);
            Conn = -1;
            ClientDisconnected?.Invoke();
        }

        internal void ServerLost()
        {
            connecting = false;
            if (Conn < 0) return;
            Conn = -1;
            ClientDisconnected?.Invoke();
        }

        public void SendPacket(Packet p)
        {
            if (!IsClientConnected) return;
            server.ClientToServer(Conn, p, "あなた");
        }

        internal void Deliver(int conn, Packet p)
        {
            // 切断後に届いた古い接続宛ての便は捨てる
            if (conn != Conn || p == null) return;
            PacketReceived?.Invoke(p);
        }

        static System.Collections.IEnumerator After(float seconds, Action a)
        {
            yield return new WaitForSecondsRealtime(seconds);
            a();
        }
    }
}
#endif
