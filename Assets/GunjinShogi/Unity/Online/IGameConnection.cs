using System;
using GunjinShogi.Core.Online;

namespace GunjinShogi.UnityView
{
    /// <summary>
    /// 画面側から見た「サーバーとの接続」。本番は Mirror（GunjinNetworkManager）、
    /// エディタでは同じ Packet をプロセス内の模擬サーバーへ渡す EditorLoopback に差し替えられる。
    /// </summary>
    public interface IGameConnection
    {
        event Action<Packet> PacketReceived;
        event Action ClientConnected;
        event Action ClientDisconnected;

        bool IsClientConnected { get; }
        bool IsConnecting { get; }
        /// <summary>ロビーに表示する接続先の説明。</summary>
        string AddressLabel { get; }

        void Connect();
        void Disconnect();
        void SendPacket(Packet p);
    }

    /// <summary>「勝ちを申請」の待ち時間などに使う時計。エディタのデバッグで時間を進められるようにしてある。</summary>
    public static class NetClock
    {
        public static float Now => UnityEngine.Time.unscaledTime + Offset;
#if UNITY_EDITOR
        public static float Offset;
#else
        public const float Offset = 0;
#endif
    }
}
