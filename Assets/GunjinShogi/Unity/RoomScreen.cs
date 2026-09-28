using System;
using System.Collections.Generic;
using GunjinShogi.Core;
using GunjinShogi.Core.Online;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GunjinShogi.UnityView
{
    /// <summary>
    /// オンラインの部屋の準備画面（対局座）。先手席・後手席のカード、参加者の一覧、ルール、準備完了・対局開始。
    /// 表示は RoomInfo からそのまま作り直す（状態は持たない）。操作はすべてコールバックで GameApp に渡す。
    /// </summary>
    public sealed class RoomScreen
    {
        public Action<int> TakeSeat;            // 0/1、-1 で席を立つ
        public Action<bool> SetReady;
        public Action StartGame;
        public Action<bool> SetOpen;
        public Action<int> TransferOwner;
        public Action<int, int> SetSeatCpu;     // 席, 強さ（-1 で外す）
        public Action EditRules;                // 部屋主：変更 / それ以外：確認
        public Action OpenDisplaySettings;
        public Action<string> Rename;
        public Action Invite;
        public Action Leave;

        readonly GameObject root;
        readonly RectTransform column, seatsRow;
        readonly TextMeshProUGUI titleText, noticeText, statusText, rulesText, membersTitle;
        readonly Button openButton, privateButton, inviteButton, rulesButton, readyButton, startButton;
        readonly SeatCard[] seats = new SeatCard[2];
        readonly RectTransform memberList;
        readonly TMP_InputField nameInput;
        readonly List<GameObject> memberRows = new List<GameObject>();
        RoomInfo info;
        int myId;
        float noticeUntil;

        sealed class SeatCard
        {
            public Image Frame;
            public TextMeshProUGUI Title, Name, State;
            public Button Action, Cpu;
        }

        public bool IsOpen => root.activeSelf;

        public RoomScreen(Transform canvas)
        {
            var bg = Ui.Image("RoomScreen", canvas, Theme.Background, raycast: true);
            Ui.Stretch(bg.rectTransform);
            root = bg.gameObject;
            column = Ui.Rect("Column", bg.transform);
            Ui.Column(column, 12);

            // 見出し：部屋番号・公開範囲・招待
            var head = Ui.Rect("Head", column);
            Ui.RowGroup(head, 12);
            Ui.Size(head, 76);
            titleText = Ui.Text("Title", head, "", 50, Theme.Text, bold: true);
            titleText.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(titleText, 50);
            Ui.Size(titleText, -1, 300, 1);
            openButton = Ui.Button("Open", head, "オープン", () => SetOpen?.Invoke(true), fontSize: 24);
            Ui.Size(openButton, -1, 150);
            privateButton = Ui.Button("Private", head, "プライベート", () => SetOpen?.Invoke(false), fontSize: 24);
            Ui.Size(privateButton, -1, 180);
            inviteButton = Ui.Button("Invite", head, "招待", () => Invite?.Invoke(), Ui.ButtonStyle.Primary, 26);
            Ui.Size(inviteButton, -1, 130);

            noticeText = Ui.Text("Notice", column, "", 26, Theme.Brass);
            Ui.Size(noticeText, 36);
            Ui.AutoSize(noticeText, 26);
            statusText = Ui.Text("Status", column, "", 28, Theme.TextMuted);
            Ui.Size(statusText, 44);
            Ui.AutoSize(statusText, 28);

            // 対局座：先手席・後手席
            seatsRow = Ui.Rect("Seats", column);
            var h = seatsRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 18;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = true;
            Ui.Size(seatsRow, 290);
            for (int s = 0; s < 2; s++) seats[s] = BuildSeat(seatsRow, s);

            // 参加者
            membersTitle = Ui.Text("MembersTitle", column, "参加者", 26, Theme.Brass, bold: true);
            Ui.Size(membersTitle, 40);
            var listHost = Ui.Image("Members", column, Theme.Hex("20251F"));
            var le = Ui.Size(listHost);
            le.flexibleHeight = 1;
            le.minHeight = 120;
            memberList = Ui.ScrollArea("Scroll", listHost.transform, out _);
            Ui.Stretch((RectTransform)memberList.parent.parent, 12, 8, 12, 8);
            Ui.Column(memberList, 6);

            // ルール・表示設定・名前
            var ruleRow = Ui.Rect("RuleRow", column);
            Ui.RowGroup(ruleRow, 12);
            Ui.Size(ruleRow, 66);
            rulesText = Ui.Text("Rules", ruleRow, "", 26, Theme.Text);
            Ui.AutoSize(rulesText, 26);
            Ui.Size(rulesText, -1, 300, 1);
            rulesButton = Ui.Button("RulesBtn", ruleRow, "ルール", () => EditRules?.Invoke(), fontSize: 26);
            Ui.Size(rulesButton, -1, 200);
            var disp = Ui.Button("Display", ruleRow, "表示設定", () => OpenDisplaySettings?.Invoke(), fontSize: 26);
            Ui.Size(disp, -1, 170);

            var nameRow = Ui.Rect("NameRow", column);
            Ui.RowGroup(nameRow, 12);
            Ui.Size(nameRow, 62);
            var nameLabel = Ui.Text("NameLabel", nameRow, "あなたの名前", 24, Theme.TextMuted);
            Ui.Size(nameLabel, -1, 170);
            nameInput = Ui.InputField("Name", nameRow, "名前（12文字まで）", 26);
            nameInput.characterLimit = RoomService.MaxNameLength;
            Ui.Size(nameInput, -1, 300, 1);
            var rename = Ui.Button("Rename", nameRow, "名前を変える", () => Rename?.Invoke(nameInput.text), fontSize: 24);
            Ui.Size(rename, -1, 200);

            // 下段：準備完了・対局開始・退出
            var bottom = Ui.Rect("Bottom", column);
            var bh = bottom.gameObject.AddComponent<HorizontalLayoutGroup>();
            bh.spacing = 16;
            bh.childControlWidth = bh.childControlHeight = true;
            bh.childForceExpandWidth = bh.childForceExpandHeight = true;
            Ui.Size(bottom, 90);
            readyButton = Ui.Button("Ready", bottom, "準備完了", () => ToggleReady(), fontSize: 32);
            startButton = Ui.Button("Start", bottom, "対局開始", () => StartGame?.Invoke(), Ui.ButtonStyle.Primary, 32);
            Ui.Button("Leave", bottom, "部屋を出る", () => Leave?.Invoke(), Ui.ButtonStyle.Danger, 32);

            root.SetActive(false);
        }

        SeatCard BuildSeat(Transform parent, int seat)
        {
            var frame = Ui.Image("Seat" + seat, parent, Theme.PanelLine);
            var face = Ui.Image("Face", frame.transform, Theme.Panel);
            Ui.Stretch(face.rectTransform, 3, 3, 3, 3);
            var col = Ui.Stretch(Ui.Rect("Col", face.transform), 20, 14, 20, 14);
            Ui.Column(col, 6);
            var card = new SeatCard { Frame = frame };
            var teamColor = Color.Lerp(Theme.Team[seat], Color.white, seat == 0 ? 0.3f : 0.45f);
            card.Title = Ui.Text("Title", col, $"{(seat == 0 ? "先手" : "後手")}（{Theme.TeamName[seat]}）", 26, teamColor, bold: true);
            Ui.Size(card.Title, 36);
            card.Name = Ui.Text("Name", col, "", 40, Theme.Text, bold: true);
            card.Name.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(card.Name, 40);
            Ui.Size(card.Name, 56);
            card.State = Ui.Text("State", col, "", 24, Theme.TextMuted);
            Ui.Size(card.State, 34);
            var spacer = Ui.Size(Ui.Rect("Spacer", col));
            spacer.flexibleHeight = 1;
            var buttons = Ui.Rect("Buttons", col);
            var bh = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
            bh.spacing = 10;
            bh.childControlWidth = bh.childControlHeight = true;
            bh.childForceExpandWidth = bh.childForceExpandHeight = true;
            Ui.Size(buttons, 60);
            card.Action = Ui.Button("Action", buttons, "", () => OnSeatAction(seat), fontSize: 24);
            card.Cpu = Ui.Button("Cpu", buttons, "", () => OnCpuAction(seat), fontSize: 24);
            return card;
        }

        public void ApplyOrientation(bool landscape)
        {
            if (landscape)
            {
                Ui.Anchors(column, 0.5f, 0, 0.5f, 1);
                column.offsetMin = new Vector2(-600, 24);
                column.offsetMax = new Vector2(600, -24);
            }
            else
            {
                Ui.Anchors(column, 0, 0, 1, 1);
                column.offsetMin = new Vector2(24, 40);
                column.offsetMax = new Vector2(-24, -60);
            }
        }

        public void Show(string myName)
        {
            nameInput.text = myName;
            root.SetActive(true);
            root.transform.SetAsLastSibling();
        }

        public void Hide() => root.SetActive(false);

        public void ShowNotice(string text)
        {
            noticeText.text = text;
            noticeUntil = Time.unscaledTime + 8;
        }

        /// <summary>毎フレーム呼ぶ（お知らせを時間で消す）。</summary>
        public void Tick()
        {
            if (noticeUntil > 0 && Time.unscaledTime > noticeUntil) { noticeText.text = ""; noticeUntil = 0; }
        }

        bool IsOwner => info != null && info.OwnerId == myId;
        int MySeat => info == null ? RoomInfo.NoSeat : info.SeatOf(myId);

        void ToggleReady()
        {
            int s = MySeat;
            if (s < 0) return;
            SetReady?.Invoke(!info.Ready[s]);
        }

        void OnCpuAction(int seat)
        {
            if (info == null || !IsOwner) return;
            var m = info.Member(info.SeatMember[seat]);
            if (m == null) SetSeatCpu?.Invoke(seat, AppSettings.CpuLevel);
            else if (m.IsCpu) SetSeatCpu?.Invoke(seat, (m.CpuLevel + 1) % RoomService.CpuLevelNames.Length); // 強さを順に切り替え
        }

        void OnSeatAction(int seat)
        {
            if (info == null) return;
            var occupant = info.Member(info.SeatMember[seat]);
            if (occupant != null && occupant.IsCpu && IsOwner) { SetSeatCpu?.Invoke(seat, -1); return; }
            if (MySeat == seat) TakeSeat?.Invoke(RoomInfo.NoSeat);
            else TakeSeat?.Invoke(seat);
        }

        public void Refresh(RoomInfo room, int me, string ruleSummary)
        {
            info = room;
            myId = me;
            bool owner = IsOwner;
            int mySeat = MySeat;
            bool lobby = room.Phase == RoomPhase.Lobby;

            titleText.text = $"部屋 <color=#E3B341>{room.Code}</color>";
            openButton.gameObject.SetActive(owner || room.Open);
            privateButton.gameObject.SetActive(owner || !room.Open);
            openButton.interactable = privateButton.interactable = owner;
            Ui.SetSelected(openButton, room.Open);
            Ui.SetSelected(privateButton, !room.Open);

            for (int s = 0; s < 2; s++)
            {
                var c = seats[s];
                int id = room.SeatMember[s];
                var m = room.Member(id);
                bool mine = id == myId;
                c.Frame.color = mine ? Theme.Brass : Theme.PanelLine;
                if (m == null)
                {
                    c.Name.text = "<color=#6E6A58>空席</color>";
                    c.State.text = "";
                }
                else
                {
                    c.Name.text = (id == room.OwnerId ? "<color=#E3B341>★</color> " : "") + m.Name + (mine ? "<size=70%>（あなた）</size>" : "");
                    c.State.text = !m.Present ? "<color=#E0705A>切断中</color>"
                        : room.Phase == RoomPhase.Review ? (room.ReviewDone[s] ? "感想戦を終えました" : "感想戦中")
                        : room.Phase == RoomPhase.Setup ? (room.SetupDone[s] ? "配置済み" : "配置中")
                        : room.Phase == RoomPhase.Playing ? "対局中"
                        : room.Ready[s] ? "<color=#8FBF7A>準備完了</color>" : "準備中";
                }
                string label;
                bool enabled = lobby;
                bool cpuSeat = m != null && m.IsCpu;
                if (mine) label = "席を立つ（観戦へ）";
                else if (cpuSeat && owner) label = "CPUを外す";
                else if (m == null) label = "ここに座る";
                else if (owner) label = mySeat >= 0 ? "席を入れ替える" : "代わりに座る";
                else { label = "着席中"; enabled = false; }
                Ui.SetLabel(c.Action, label);
                c.Action.interactable = enabled;
                // 部屋主だけ：空席に CPU を座らせる・座っている CPU の強さを切り替える
                bool showCpu = owner && (m == null || cpuSeat);
                c.Cpu.gameObject.SetActive(showCpu);
                if (showCpu)
                {
                    Ui.SetLabel(c.Cpu, cpuSeat ? $"強さ：{RoomService.CpuLevelNames[m.CpuLevel]} ›" : "CPUを座らせる");
                    c.Cpu.interactable = lobby;
                }
            }

            // 参加者一覧
            foreach (var r in memberRows) UnityEngine.Object.Destroy(r);
            memberRows.Clear();
            membersTitle.text = $"参加者 {room.Members.Count} / {RoomService.MaxMembers}　<size=80%><color=#A39D88>着席していない人は観戦になります</color></size>";
            foreach (var m in room.Members) memberRows.Add(BuildMemberRow(room, m, owner));

            rulesText.text = $"ルール：{ruleSummary}（コード {room.RuleCode}）";
            Ui.SetLabel(rulesButton, owner && lobby ? "ルールを変更" : "ルールを確認");

            readyButton.gameObject.SetActive(mySeat >= 0);
            if (mySeat >= 0)
            {
                bool ready = room.Ready[mySeat];
                Ui.SetLabel(readyButton, ready ? "準備完了を取り消す" : "準備完了");
                Ui.SetSelected(readyButton, ready);
                readyButton.interactable = lobby;
            }
            startButton.gameObject.SetActive(owner);
            startButton.interactable = room.CanStart;
            inviteButton.gameObject.SetActive(lobby);

            statusText.text = Status(room, owner, mySeat);
        }

        static string Status(RoomInfo room, bool owner, int mySeat)
        {
            if (room.Phase == RoomPhase.Setup) return mySeat >= 0 ? "配置中です" : "対局者が駒を配置しています。対局が始まると観戦できます。";
            if (room.Phase == RoomPhase.Playing) return "対局中です";
            if (room.Phase == RoomPhase.Review)
                return mySeat >= 0 && room.ReviewDone[mySeat] ? "相手が感想戦を終えるのを待っています。終わると席を移ったり次の対局を始めたりできます。"
                    : "対局者が感想戦をしています。終わると席を移ったり次の対局を始めたりできます。";
            if (!room.BothSeated) return "先手席と後手席に1人ずつ座ってください。座っていない人は観戦になります。";
            if (!(room.Ready[0] && room.Ready[1])) return mySeat >= 0 && !room.Ready[mySeat] ? "準備ができたら「準備完了」を押してください。" : "対局者の準備完了を待っています。";
            if (!room.CanStart) return "対局者の接続を待っています。";
            return owner ? "そろいました。「対局開始」を押してください。" : "部屋主の開始を待っています。";
        }

        GameObject BuildMemberRow(RoomInfo room, MemberInfo m, bool owner)
        {
            var row = Ui.Image("Member", memberList, Theme.Hex("262B26"));
            Ui.Size(row, 54);
            var h = Ui.RowGroup(row, 10);
            h.padding = new RectOffset(14, 8, 4, 4);
            int seat = room.SeatOf(m.Id);
            string role = (seat == 0 ? "先手" : seat == 1 ? "後手" : "観戦") + (m.IsCpu ? "・CPU" : "");
            string text = $"{(m.Id == room.OwnerId ? "<color=#E3B341>★</color>" : "　")} {m.Name}{(m.Id == myId ? "（あなた）" : "")}　<color=#A39D88>{role}{(m.Present ? "" : "・切断中")}{(m.Id == room.OwnerId ? "・部屋主" : "")}</color>";
            var t = Ui.Text("Name", row.transform, text, 24, Theme.Text);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(t, 24);
            Ui.Size(t, -1, 300, 1);
            if (owner && m.Id != myId && m.Present && !m.IsCpu)
            {
                int id = m.Id;
                var give = Ui.Button("Give", row.transform, "部屋主を譲る", () => TransferOwner?.Invoke(id), fontSize: 22);
                Ui.Size(give, -1, 190);
            }
            return row.gameObject;
        }
    }
}
