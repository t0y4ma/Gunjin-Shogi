using System;
using System.Collections.Generic;
using System.Text;

namespace GunjinShogi.Core.Online
{
    /// <summary>
    /// 通信メッセージの種類。1〜99 はクライアント→サーバー、100〜 はサーバー→クライアント。
    /// 番号を変えたらサーバーとクライアントを必ず一緒に配置し直すこと（ProtocolVersion も上げる）。
    /// </summary>
    public enum Op : byte
    {
        // クライアント → サーバー
        CreateRoom = 1,     // A=バージョン B=1ならオープン S1=端末トークン S2=ルールコード Data=名前(UTF-8)
        JoinRoom = 2,       // A=バージョン S1=端末トークン S2=部屋番号 Data=名前(UTF-8)
        LeaveRoom = 3,
        SubmitSetup = 4,    // S1=配置コード（先手の向き）
        Move = 5,           // A=駒ID B=移動先ノード
        Resign = 6,
        ClaimWin = 8,       // 相手が長く切断しているときに勝ちを申請
        ListRooms = 9,      // A=バージョン S1=前に入っていた部屋番号 S2=端末トークン
        TakeSeat = 10,      // A=0 先手席 / 1 後手席 / -1 席を立つ
        SetReady = 11,      // A=1 準備完了 / 0 取り消し
        SetRules = 12,      // S1=ルールコード（部屋主のみ）
        SetVisibility = 13, // A=1 オープン / 0 プライベート（部屋主のみ）
        TransferOwner = 14, // A=譲る相手の参加者ID（部屋主のみ）
        StartGame = 15,     // 部屋主のみ。2人が着席して準備完了のとき
        AbortSetup = 16,    // 配置中に着席者が中断 → 全員が準備画面へ
        SetName = 17,       // S1=名前

        // サーバー → クライアント
        Error = 100,        // S1=メッセージ A=1 なら部屋から外れた
        RoomJoined = 101,   // S1=部屋番号 A=自分の参加者ID
        RoomState = 102,    // Data=RoomInfo（部屋の様子すべて）
        Snapshot = 103,     // Data=PlayerView（自分から見える情報。観戦者は Viewer=2）
        MoveMade = 104,     // Data=MoveView A=次の手番 B=GameResult C=EndReason
        GameOver = 105,     // Data=全公開の PlayerView S1/S2=先手・後手の配置コード A=GameResult B=EndReason
        RoomList = 106,     // Data=RoomListInfo
        Notice = 107,       // S1=お知らせ（「○○さんが配置を中断しました」など）
    }

    /// <summary>部屋の段階。対局が終わると準備に戻る。</summary>
    public enum RoomPhase : byte { Lobby = 0, Setup = 1, Playing = 2 }

    /// <summary>
    /// 1つのメッセージ。すべての種類で同じ入れ物を使い、使わない欄は空のまま送る。
    /// 整数は可変長（小さい値は1バイト）なので、指し手1つは7バイト程度になる。
    /// </summary>
    public sealed class Packet
    {
        public const int ProtocolVersion = 2;

        public Op Op;
        public int A, B, C;
        public string S1 = "", S2 = "";
        public byte[] Data = Array.Empty<byte>();

        public Packet() { }
        public Packet(Op op) { Op = op; }

        public static Packet Of(Op op, int a = 0, int b = 0, int c = 0, string s1 = "", string s2 = "", byte[] data = null) =>
            new Packet { Op = op, A = a, B = b, C = c, S1 = s1 ?? "", S2 = s2 ?? "", Data = data ?? Array.Empty<byte>() };

        public byte[] Encode()
        {
            var w = new ByteWriter();
            w.Byte((byte)Op);
            w.Int(A); w.Int(B); w.Int(C);
            w.String(S1); w.String(S2);
            w.Bytes(Data);
            return w.ToArray();
        }

        /// <summary>壊れたデータなら null。</summary>
        public static Packet Decode(byte[] data)
        {
            try
            {
                var r = new ByteReader(data);
                var p = new Packet
                {
                    Op = (Op)r.Byte(),
                    A = r.Int(), B = r.Int(), C = r.Int(),
                    S1 = r.String(), S2 = r.String(),
                    Data = r.Bytes(),
                };
                return r.AtEnd ? p : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public override string ToString() => $"{Op} A={A} B={B} C={C} S1={S1} S2={S2} Data={Data.Length}B";
    }

    // ───────── バイト列の読み書き ─────────

    public sealed class ByteWriter
    {
        readonly List<byte> buf = new List<byte>(32);

        public void Byte(byte b) => buf.Add(b);

        /// <summary>ジグザグ符号化した可変長整数（-1 も1バイト）。</summary>
        public void Int(int v)
        {
            uint u = (uint)((v << 1) ^ (v >> 31));
            while (u >= 0x80) { buf.Add((byte)(u | 0x80)); u >>= 7; }
            buf.Add((byte)u);
        }

        public void String(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s ?? "");
            Int(bytes.Length);
            buf.AddRange(bytes);
        }

        public void Bytes(byte[] b)
        {
            Int(b?.Length ?? 0);
            if (b != null) buf.AddRange(b);
        }

        public byte[] ToArray() => buf.ToArray();
    }

    public sealed class ByteReader
    {
        const int MaxLength = 1 << 20;
        readonly byte[] data;
        int pos;

        public ByteReader(byte[] data) { this.data = data ?? Array.Empty<byte>(); }

        public bool AtEnd => pos == data.Length;

        public byte Byte()
        {
            if (pos >= data.Length) throw new FormatException("データが短すぎます");
            return data[pos++];
        }

        public int Int()
        {
            uint u = 0;
            int shift = 0;
            while (true)
            {
                byte b = Byte();
                u |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
                if (shift > 28) throw new FormatException("整数が長すぎます");
            }
            return (int)(u >> 1) ^ -(int)(u & 1);
        }

        public string String()
        {
            int n = Int();
            if (n < 0 || n > MaxLength || pos + n > data.Length) throw new FormatException("文字列の長さが不正です");
            var s = Encoding.UTF8.GetString(data, pos, n);
            pos += n;
            return s;
        }

        public byte[] Bytes()
        {
            int n = Int();
            if (n < 0 || n > MaxLength || pos + n > data.Length) throw new FormatException("データの長さが不正です");
            var b = new byte[n];
            Array.Copy(data, pos, b, 0, n);
            pos += n;
            return b;
        }
    }

    // ───────── 盤面（PlayerView）の符号化 ─────────

    public static class ViewCodec
    {
        public static byte[] Encode(PlayerView v)
        {
            var w = new ByteWriter();
            w.Byte((byte)v.Viewer);
            w.Byte((byte)v.Phase);
            w.Byte((byte)v.CurrentPlayer);
            w.Byte((byte)v.Result);
            w.Byte((byte)v.EndReason);
            w.Int(v.Pieces.Count);
            foreach (var p in v.Pieces)
            {
                w.Byte((byte)(p.Owner | (p.HasMoved ? 2 : 0)));
                w.Int(p.TypeId);
                w.Int(p.Node);
            }
            w.Int(v.History.Count);
            foreach (var m in v.History) WriteMove(w, m);
            return w.ToArray();
        }

        public static PlayerView Decode(byte[] data)
        {
            var r = new ByteReader(data);
            var v = new PlayerView
            {
                Viewer = r.Byte(),
                Phase = (GamePhase)r.Byte(),
                CurrentPlayer = r.Byte(),
                Result = (GameResult)r.Byte(),
                EndReason = (EndReason)r.Byte(),
            };
            int n = r.Int();
            if (n < 0 || n > 256) throw new FormatException("駒の数が不正です");
            for (int i = 0; i < n; i++)
            {
                byte flags = r.Byte();
                v.Pieces.Add(new PieceView
                {
                    Id = i,
                    Owner = flags & 1,
                    HasMoved = (flags & 2) != 0,
                    TypeId = r.Int(),
                    Node = r.Int(),
                });
            }
            int h = r.Int();
            if (h < 0 || h > 100000) throw new FormatException("棋譜の長さが不正です");
            for (int i = 0; i < h; i++) v.History.Add(ReadMove(r));
            if (!r.AtEnd) throw new FormatException("余分なデータがあります");
            return v;
        }

        public static byte[] EncodeMove(MoveView m)
        {
            var w = new ByteWriter();
            WriteMove(w, m);
            return w.ToArray();
        }

        public static MoveView DecodeMove(byte[] data)
        {
            var r = new ByteReader(data);
            var m = ReadMove(r);
            if (!r.AtEnd) throw new FormatException("余分なデータがあります");
            return m;
        }

        static void WriteMove(ByteWriter w, MoveView m)
        {
            w.Int(m.Ply);
            w.Byte((byte)(m.Player | (m.HadBattle ? 2 : 0)));
            w.Int(m.PieceId);
            w.Int(m.FromNode);
            w.Int(m.ToNode);
            if (!m.HadBattle) return;
            w.Int(m.AttackerId);
            w.Int(m.DefenderId);
            w.Int(m.AttackerType);
            w.Int(m.DefenderType);
            w.Byte((byte)m.Result);
        }

        static MoveView ReadMove(ByteReader r)
        {
            var m = new MoveView { Ply = r.Int() };
            byte flags = r.Byte();
            m.Player = flags & 1;
            m.HadBattle = (flags & 2) != 0;
            m.PieceId = r.Int();
            m.FromNode = r.Int();
            m.ToNode = r.Int();
            m.AttackerType = Visibility.HiddenType;
            m.DefenderType = Visibility.HiddenType;
            if (!m.HadBattle) return m;
            m.AttackerId = r.Int();
            m.DefenderId = r.Int();
            m.AttackerType = r.Int();
            m.DefenderType = r.Int();
            m.Result = (BattleResult)r.Byte();
            return m;
        }
    }
}
