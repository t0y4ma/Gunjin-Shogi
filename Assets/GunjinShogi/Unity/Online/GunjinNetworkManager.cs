using System;
using GunjinShogi.Core.Online;
using Mirror;
using Mirror.SimpleWeb;
using UnityEngine;

namespace GunjinShogi.UnityView
{
    /// <summary>Mirror で運ぶのはこのメッセージ1種類だけ。中身は Core の Packet をバイト列にしたもの。</summary>
    public struct GsPacketMessage : NetworkMessage
    {
        public byte[] data;
    }

    /// <summary>
    /// 通信の入口。SyncVar・NetworkIdentity・プレイヤーオブジェクトは使わず、メッセージのやり取りだけで対局する。
    /// サーバーでは RoomService（部屋管理と審判）を動かし、クライアントでは受け取った Packet をイベントで渡す。
    /// 専用サーバー（LinuxServer ビルド）では起動と同時にサーバーを始める。
    /// </summary>
    public sealed class GunjinNetworkManager : NetworkManager, IGameConnection
    {
        [Header("サーバーの待ち受け")]
        [Tooltip("サーバーが WebSocket を待ち受けるポート。起動引数 -port で上書きできる")]
        public ushort serverPort = 7778;

        [Header("クライアントの接続先")]
        [Tooltip("サーバーのホスト名。エディタで試すときは localhost")]
        public string serverHost = "localhost";
        [Tooltip("TLS の逆プロキシ（Caddy など）越しに wss で接続する。https で配信する WebGL では必須")]
        public bool useWss;
        [Tooltip("クライアントが接続するポート。逆プロキシ越しなら 443。0 ならサーバーと同じ")]
        public ushort clientPort;
        [Tooltip("WebGL ビルドでは、ページを配信しているホストへ接続する（https なら wss・同じポート）。逆プロキシが WebSocket をゲームサーバーへ振り分ける前提")]
        public bool followPageHost = true;

        public static GunjinNetworkManager Instance => singleton as GunjinNetworkManager;

        public event Action<Packet> PacketReceived;
        public event Action ClientConnected;
        public event Action ClientDisconnected;

        RoomService service;

        public bool IsClientConnected => NetworkClient.isConnected;
        public bool IsConnecting => NetworkClient.active && !NetworkClient.isConnected;
        public string AddressLabel => networkAddress;

        public override void Awake()
        {
            autoCreatePlayer = false;
            ApplyCommandLine();
            base.Awake();
            ConfigureTransport();
        }

        void ApplyCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-port" && ushort.TryParse(args[i + 1], out var port)) serverPort = port;
        }

        void ConfigureTransport()
        {
            if (!(transport is SimpleWebTransport web)) return;
            web.port = serverPort;
            web.clientUseWss = useWss;
            web.sslEnabled = false; // TLS は逆プロキシで終端する
            if (clientPort != 0 && clientPort != serverPort)
            {
                web.clientWebsocketSettings.ClientPortOption = WebsocketPortOption.SpecifyPort;
                web.clientWebsocketSettings.CustomClientPort = clientPort;
            }
        }

        // ───────── サーバー ─────────

        public override void OnStartServer()
        {
            base.OnStartServer();
            service = new RoomService(SendToClient, () => Time.unscaledTimeAsDouble);
            NetworkServer.RegisterHandler<GsPacketMessage>(OnServerPacket, false);
            if (Utils.IsHeadless())
            {
                // ターン制なので高いフレームレートは要らない（VMのCPUを節約）
                Application.targetFrameRate = 15;
                Debug.Log($"[Gunjin] サーバー起動 ポート {serverPort}");
            }
        }

        void SendToClient(int connectionId, Packet p)
        {
            if (NetworkServer.connections.TryGetValue(connectionId, out var conn))
                conn.Send(new GsPacketMessage { data = p.Encode() });
        }

        void OnServerPacket(NetworkConnectionToClient conn, GsPacketMessage msg)
        {
            var p = Packet.Decode(msg.data);
            if (p == null) return; // 壊れたデータは無視
            service.Receive(conn.connectionId, p);
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            service?.Disconnected(conn.connectionId);
            base.OnServerDisconnect(conn);
        }


        // ───────── クライアント ─────────

        public void Connect()
        {
            if (NetworkClient.active) return;
            networkAddress = serverHost;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (followPageHost) UsePageHost();
#endif
            ConfigureTransport();
            StartClient();
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        /// <summary>ページの URL（例 https://example.com/）から接続先を決める。ファイル配信と WebSocket を同じホスト・ポートで受ける構成用。</summary>
        void UsePageHost()
        {
            if (!Uri.TryCreate(Application.absoluteURL, UriKind.Absolute, out var page)) return;
            if (page.Scheme != "http" && page.Scheme != "https") return;
            networkAddress = page.Host;
            useWss = page.Scheme == "https";
            clientPort = (ushort)page.Port;
            ConfigureTransport();
        }
#endif

        public void Disconnect()
        {
            if (NetworkServer.active && NetworkClient.active) StopHost();
            else if (NetworkClient.active) StopClient();
        }

        public void SendPacket(Packet p)
        {
            if (NetworkClient.isConnected) NetworkClient.Send(new GsPacketMessage { data = p.Encode() });
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            NetworkClient.RegisterHandler<GsPacketMessage>(msg =>
            {
                var p = Packet.Decode(msg.data);
                if (p != null) PacketReceived?.Invoke(p);
            }, false);
        }

        public override void OnClientConnect()
        {
            base.OnClientConnect();
            ClientConnected?.Invoke();
        }

        public override void OnClientDisconnect()
        {
            base.OnClientDisconnect();
            ClientDisconnected?.Invoke();
        }
    }
}
