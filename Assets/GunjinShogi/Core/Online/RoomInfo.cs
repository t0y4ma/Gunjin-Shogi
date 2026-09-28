using System;
using System.Collections.Generic;

namespace GunjinShogi.Core.Online
{
    // ───────── 部屋の様子（準備画面・部屋一覧） ─────────

    public sealed class MemberInfo
    {
        public int Id;
        public string Name = "";
        public bool Present;
    }

    /// <summary>部屋の様子。変化があるたびに部屋の全員へ送る（数十〜百バイト程度）。</summary>
    public sealed class RoomInfo
    {
        public const int NoSeat = -1;

        public string Code = "";
        public RoomPhase Phase;
        public bool Open;
        public int OwnerId = -1;
        public string RuleCode = "0";
        public readonly int[] SeatMember = { NoSeat, NoSeat };
        public readonly bool[] Ready = new bool[2];
        public readonly bool[] SetupDone = new bool[2];
        /// <summary>着席者が切断してからの秒数（つながっていれば -1）。</summary>
        public readonly int[] AbsentSeconds = { -1, -1 };
        public readonly List<MemberInfo> Members = new List<MemberInfo>();

        public MemberInfo Member(int id) => Members.Find(m => m.Id == id);
        public string NameOf(int id) => Member(id)?.Name ?? "";
        public int SeatOf(int memberId) => SeatMember[0] == memberId ? 0 : SeatMember[1] == memberId ? 1 : NoSeat;
        public bool BothSeated => SeatMember[0] != NoSeat && SeatMember[1] != NoSeat;
        public bool CanStart => Phase == RoomPhase.Lobby && BothSeated && Ready[0] && Ready[1]
                                && (Member(SeatMember[0])?.Present ?? false) && (Member(SeatMember[1])?.Present ?? false);
        public int SpectatorCount
        {
            get { int n = 0; foreach (var m in Members) if (SeatOf(m.Id) == NoSeat) n++; return n; }
        }

        public byte[] Encode()
        {
            var w = new ByteWriter();
            w.String(Code);
            w.Byte((byte)Phase);
            w.Byte((byte)(Open ? 1 : 0));
            w.Int(OwnerId);
            w.String(RuleCode);
            for (int s = 0; s < 2; s++)
            {
                w.Int(SeatMember[s]);
                w.Byte((byte)((Ready[s] ? 1 : 0) | (SetupDone[s] ? 2 : 0)));
                w.Int(AbsentSeconds[s]);
            }
            w.Int(Members.Count);
            foreach (var m in Members)
            {
                w.Int(m.Id);
                w.String(m.Name);
                w.Byte((byte)(m.Present ? 1 : 0));
            }
            return w.ToArray();
        }

        public static RoomInfo Decode(byte[] data)
        {
            var r = new ByteReader(data);
            var info = new RoomInfo
            {
                Code = r.String(),
                Phase = (RoomPhase)r.Byte(),
                Open = r.Byte() != 0,
                OwnerId = r.Int(),
                RuleCode = r.String(),
            };
            for (int s = 0; s < 2; s++)
            {
                info.SeatMember[s] = r.Int();
                byte f = r.Byte();
                info.Ready[s] = (f & 1) != 0;
                info.SetupDone[s] = (f & 2) != 0;
                info.AbsentSeconds[s] = r.Int();
            }
            int n = r.Int();
            if (n < 0 || n > 64) throw new FormatException("参加者の数が不正です");
            for (int i = 0; i < n; i++)
                info.Members.Add(new MemberInfo { Id = r.Int(), Name = r.String(), Present = r.Byte() != 0 });
            if (!r.AtEnd) throw new FormatException("余分なデータがあります");
            return info;
        }
    }

    /// <summary>ロビーの部屋一覧（オープンの部屋だけ）と、「前の対局に戻る」ができるかどうか。</summary>
    public sealed class RoomListInfo
    {
        public sealed class Entry
        {
            public string Code = "";
            public RoomPhase Phase;
            public string OwnerName = "";
            public string RuleCode = "0";
            public string First = "", Second = "";  // 着席者の名前（空席は空）
            public int Members;
        }

        public readonly List<Entry> Rooms = new List<Entry>();
        /// <summary>前に入っていた部屋がまだあり、この端末の席が残っている。</summary>
        public bool CanRejoin;

        public byte[] Encode()
        {
            var w = new ByteWriter();
            w.Byte((byte)(CanRejoin ? 1 : 0));
            w.Int(Rooms.Count);
            foreach (var e in Rooms)
            {
                w.String(e.Code);
                w.Byte((byte)e.Phase);
                w.String(e.OwnerName);
                w.String(e.RuleCode);
                w.String(e.First);
                w.String(e.Second);
                w.Int(e.Members);
            }
            return w.ToArray();
        }

        public static RoomListInfo Decode(byte[] data)
        {
            var r = new ByteReader(data);
            var list = new RoomListInfo { CanRejoin = r.Byte() != 0 };
            int n = r.Int();
            if (n < 0 || n > 1000) throw new FormatException("部屋の数が不正です");
            for (int i = 0; i < n; i++)
                list.Rooms.Add(new Entry
                {
                    Code = r.String(), Phase = (RoomPhase)r.Byte(), OwnerName = r.String(), RuleCode = r.String(),
                    First = r.String(), Second = r.String(), Members = r.Int(),
                });
            if (!r.AtEnd) throw new FormatException("余分なデータがあります");
            return list;
        }
    }
}
