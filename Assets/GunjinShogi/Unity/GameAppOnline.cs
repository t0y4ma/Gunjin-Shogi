using System;
using System.Collections.Generic;
using System.Text;
using GunjinShogi.Core;
using GunjinShogi.Core.Online;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GunjinShogi.UnityView
{
    /// <summary>
    /// オンライン対戦の画面と進行（GameApp の続き）。盤・パネルはローカル対戦と共通。
    /// 部屋に入ると準備画面（RoomScreen）→ 部屋主の開始で配置 → 対局 → 終局後は準備画面へ戻る。
    /// 着席していない人は観戦者で、両者の駒種を受け取り「先手のみ／後手のみ／両方」を切り替えて見る。
    /// </summary>
    public sealed partial class GameApp
    {
        const string TokenKey = "gs.clientToken";
        const string LastRoomKey = "gs.lastRoom";
        const string OpenKey = "gs.roomOpen";
        const float ListInterval = 3f;

        // サーバーから届いたもの
        bool online;
        string roomCode = "";
        int myId = -1;
        RoomInfo roomInfo;
        PlayerView onlineView;
        GameState onlineMoveState;
        bool onlineSetupActive;
        bool showingResult;             // 終局の盤面・感想戦を見ている（準備画面へ戻るまで）
        GameResult onlineResult;
        EndReason onlineReason;
        float opponentLeftAt = -1;      // 相手の対局者が切断した時刻（つながっていれば -1）
        int spectatorShow = 2;          // 観戦者の表示：0=先手のみ 1=後手のみ 2=両方

        // 通信
        IGameConnection net;
        Action onConnected;
        bool leavingOnline;
        bool reconnecting;
        float reconnectAt;
        int reconnectTries;
        bool devBotPending;
        bool rejoinPending;
        float nextListAt;

        // 画面
        GameObject lobbyRoot, waitRow, spectateRow;
        TextMeshProUGUI lobbyStatus, lobbyMessage, lobbyListNote, debugBadge;
        TMP_InputField lobbyCodeInput, lobbyNameInput;
        Button lobbyCreate, lobbyJoin, lobbyRejoin, lobbyDev, lobbyOpenButton, lobbyPrivateButton;
        Button claimWinButton, againButton, spectateToggle, waitAbortButton;
        RectTransform roomListContent;
        readonly List<GameObject> roomListRows = new List<GameObject>();
        RoomScreen roomScreen;

        int MySeat => roomInfo == null ? RoomInfo.NoSeat : roomInfo.SeatOf(myId);
        bool IsOwner => roomInfo != null && roomInfo.OwnerId == myId;
        bool IsSpectator => online && MySeat == RoomInfo.NoSeat;

        IGameConnection Net
        {
            get
            {
                if (net != null) return net;
#if UNITY_EDITOR
                if (EditorLoopback.Enabled) net = EditorLoopback.Instance.Client;
                else
#endif
                net = GunjinNetworkManager.Instance != null ? GunjinNetworkManager.Instance : FindFirstObjectByType<GunjinNetworkManager>();
                if (net != null)
                {
                    net.PacketReceived += OnPacket;
                    net.ClientConnected += OnClientConnected;
                    net.ClientDisconnected += OnClientDisconnected;
                }
                return net;
            }
        }

        bool IsLoopback
        {
            get
            {
#if UNITY_EDITOR
                return Net is LoopbackClient;
#else
                return false;
#endif
            }
        }

        /// <summary>端末ごとの識別子。再読み込みしても同じ参加者に戻れる。デバッグモードでは使い捨て。</summary>
        static string ClientToken
        {
            get
            {
                if (DebugSession.Enabled) return DebugSession.Token;
                var t = PlayerPrefs.GetString(TokenKey, "");
                if (string.IsNullOrEmpty(t))
                {
                    t = Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString(TokenKey, t);
                    PlayerPrefs.Save();
                }
                return t;
            }
        }

        static string LastRoom
        {
            get => DebugSession.Enabled ? "" : PlayerPrefs.GetString(LastRoomKey, "");
            set
            {
                if (DebugSession.Enabled) return;
                if (string.IsNullOrEmpty(value)) PlayerPrefs.DeleteKey(LastRoomKey);
                else PlayerPrefs.SetString(LastRoomKey, value);
                PlayerPrefs.Save();
            }
        }

        static byte[] NameBytes => Encoding.UTF8.GetBytes(PlayerName.Value ?? "");

        // ───────── 画面の組み立て ─────────

        void BuildOnline()
        {
            var content = setupRow.transform.parent;
            waitAbortButton = Ui.Button("AbortWait", null, "中断", OnAbortSetup, Ui.ButtonStyle.Danger);
            waitRow = ButtonRow(content, waitAbortButton, Ui.Button("LeaveRoom", null, "部屋を出る", LeaveOnline, Ui.ButtonStyle.Danger));
            spectateToggle = Ui.Button("SpectateShow", null, "", CycleSpectatorShow);
            spectateRow = ButtonRow(content, spectateToggle, Ui.Button("LeaveSpectate", null, "部屋を出る", LeaveOnline, Ui.ButtonStyle.Danger));
            claimWinButton = Ui.Button("ClaimWin", playRow.transform, "勝ちを申請", () => Net?.SendPacket(Packet.Of(Op.ClaimWin)), Ui.ButtonStyle.Primary);
            claimWinButton.gameObject.SetActive(false);
            againButton = resultRow.transform.Find("Again").GetComponent<Button>();

            BuildLobby();

            roomScreen = new RoomScreen(canvasRect)
            {
                TakeSeat = s => Net?.SendPacket(Packet.Of(Op.TakeSeat, s)),
                SetReady = r => Net?.SendPacket(Packet.Of(Op.SetReady, r ? 1 : 0)),
                StartGame = () => Net?.SendPacket(Packet.Of(Op.StartGame)),
                SetOpen = o => Net?.SendPacket(Packet.Of(Op.SetVisibility, o ? 1 : 0)),
                TransferOwner = id => Net?.SendPacket(Packet.Of(Op.TransferOwner, id)),
                SetSeatCpu = (seat, level) => Net?.SendPacket(Packet.Of(Op.SetSeatCpu, seat, level)),
                EditRules = OpenRoomRules,
                OpenDisplaySettings = () => rulesScreen.OpenFor(currentOptions ?? AppSettings.Rules, false, null, () => display = AppSettings.Display, false),
                Rename = name =>
                {
                    PlayerName.Value = name;
                    if (!string.IsNullOrEmpty(PlayerName.Value)) Net?.SendPacket(Packet.Of(Op.SetName, s1: PlayerName.Value));
                },
                Invite = CopyInvite,
                Leave = LeaveOnline,
            };

            debugBadge = Ui.Text("DebugBadge", canvasRect, "", 26, Theme.Hex("E0705A"), bold: true);
            Ui.Anchors(debugBadge.rectTransform, 0, 1, 0, 1);
            debugBadge.rectTransform.pivot = new Vector2(0, 1);
            debugBadge.rectTransform.anchoredPosition = new Vector2(16, -10);
            debugBadge.rectTransform.sizeDelta = new Vector2(1400, 40);
            debugBadge.textWrappingMode = TextWrappingModes.NoWrap;
            debugBadge.gameObject.SetActive(false);
        }

        void BuildLobby()
        {
            var (root, card) = Ui.Modal("Lobby", canvasRect, 0.5f, 0.92f);
            lobbyRoot = root;
            Ui.Column(card, 10);
            Ui.Size(Ui.Text("Title", card, "オンライン対戦", 44, Theme.Text, bold: true), 58);
            lobbyStatus = Ui.Text("Status", card, "", 24, Theme.TextMuted);
            Ui.Size(lobbyStatus, 34);

            var nameRow = Ui.Rect("NameRow", card);
            Ui.RowGroup(nameRow, 12);
            Ui.Size(nameRow, 60);
            var nameLabel = Ui.Text("NameLabel", nameRow, "名前", 26, Theme.TextMuted);
            Ui.Size(nameLabel, -1, 90);
            lobbyNameInput = Ui.InputField("Name", nameRow, "あなたの名前（12文字まで）", 28);
            lobbyNameInput.characterLimit = RoomService.MaxNameLength;
            lobbyNameInput.onEndEdit.AddListener(v => PlayerName.Value = v);
            Ui.Size(lobbyNameInput, -1, 300, 1);

            Section(card, "部屋を作る");
            var createRow = Ui.Rect("CreateRow", card);
            Ui.RowGroup(createRow, 12);
            Ui.Size(createRow, 70);
            lobbyOpenButton = Ui.Button("Open", createRow, "オープン", () => SetCreateOpen(true), fontSize: 26);
            Ui.Size(lobbyOpenButton, -1, 170);
            lobbyPrivateButton = Ui.Button("Private", createRow, "プライベート", () => SetCreateOpen(false), fontSize: 26);
            Ui.Size(lobbyPrivateButton, -1, 200);
            lobbyCreate = Ui.Button("Create", createRow, "部屋を作る", CreateRoom, Ui.ButtonStyle.Primary, 30);
            Ui.Size(lobbyCreate, -1, 200, 1);
            var createNote = Ui.Text("CreateNote", card, "オープン：下の一覧に出て誰でも入れます。プライベート：部屋番号を知っている人だけが入れます。ルールは部屋の中で決めます。", 21, Theme.TextMuted);
            Ui.Size(createNote, 54);
            Ui.AutoSize(createNote, 21);

            Section(card, "部屋番号で入る");
            var joinRow = Ui.Rect("JoinRow", card);
            Ui.RowGroup(joinRow, 12);
            Ui.Size(joinRow, 66);
            lobbyCodeInput = Ui.InputField("Code", joinRow, "部屋番号（4桁）", 30);
            lobbyCodeInput.contentType = TMP_InputField.ContentType.IntegerNumber;
            lobbyCodeInput.characterLimit = 4;
            Ui.Size(lobbyCodeInput, -1, 240, 1);
            lobbyJoin = Ui.Button("Join", joinRow, "入る", () => JoinRoom(lobbyCodeInput.text), fontSize: 30);
            Ui.Size(lobbyJoin, -1, 180);
            lobbyRejoin = Ui.Button("Rejoin", card, "", () => JoinRoom(LastRoom), Ui.ButtonStyle.Primary, 26);
            Ui.Size(lobbyRejoin, 62);
            lobbyRejoin.gameObject.SetActive(false);

            Section(card, "オープンの部屋");
            lobbyListNote = Ui.Text("ListNote", card, "", 22, Theme.TextMuted);
            Ui.Size(lobbyListNote, 30);
            var listHost = Ui.Image("List", card, Theme.Hex("20251F"));
            var le = Ui.Size(listHost);
            le.flexibleHeight = 1;
            le.minHeight = 120;
            roomListContent = Ui.ScrollArea("Scroll", listHost.transform, out _);
            Ui.Stretch((RectTransform)roomListContent.parent.parent, 10, 8, 10, 8);
            Ui.Column(roomListContent, 6);

#if UNITY_EDITOR
            lobbyDev = Ui.Button("DevHost", card, "（エディタ）部屋を作り、相手役ボットを入れる", StartDevHost, fontSize: 22);
            Ui.Size(lobbyDev, 52);
#endif
            lobbyMessage = Ui.Text("Message", card, "", 24, Theme.TextMuted);
            Ui.Size(lobbyMessage, 40);
            Ui.AutoSize(lobbyMessage, 24);
            var close = Ui.Button("CloseLobby", card, "閉じる", CloseLobby, fontSize: 28);
            Ui.Size(close, 66);
        }

        static void Section(Transform parent, string text)
        {
            var h = Ui.Text("Section", parent, text, 24, Theme.Brass, bold: true);
            Ui.Size(h, 36);
            h.alignment = TextAlignmentOptions.BottomLeft;
        }

        void ApplyOnlineOrientation(bool landscape)
        {
            roomScreen?.ApplyOrientation(landscape);
            if (lobbyRoot == null) return;
            var card = (RectTransform)lobbyRoot.transform.GetChild(0);
            if (landscape) Ui.Anchors(card, 0.22f, 0.03f, 0.78f, 0.97f);
            else Ui.Anchors(card, 0.03f, 0.06f, 0.97f, 0.94f);
        }

        // ───────── ロビー（部屋の外） ─────────

        void OpenLobby()
        {
            if (Net == null)
            {
                titleRulesText.text = "<color=#E0705A>通信の設定（GunjinNetworkManager）がシーンにありません</color>";
                return;
            }
            lobbyMessage.text = "";
            lobbyNameInput.text = PlayerName.Value;
            if (lobbyDev != null) lobbyDev.gameObject.SetActive(IsLoopback);
            lobbyRejoin.gameObject.SetActive(false);
            SetCreateOpen(PlayerPrefs.GetInt(OpenKey, 1) != 0);
            ClearRoomList("サーバーに接続しています…");
            lobbyRoot.SetActive(true);
            lobbyRoot.transform.SetAsLastSibling();
            EnsureConnected(RequestRoomList);
            RefreshLobby();
        }

        void CloseLobby()
        {
            lobbyRoot.SetActive(false);
            onConnected = null;
            if (!online && Net != null) Net.Disconnect();
        }

        void SetCreateOpen(bool open)
        {
            PlayerPrefs.SetInt(OpenKey, open ? 1 : 0);
            PlayerPrefs.Save();
            Ui.SetSelected(lobbyOpenButton, open);
            Ui.SetSelected(lobbyPrivateButton, !open);
        }

        void RefreshLobby()
        {
            if (lobbyRoot == null || !lobbyRoot.activeSelf || Net == null) return;
            lobbyStatus.text = Net.IsClientConnected ? $"<color=#8FBF7A>●</color> サーバーに接続しています（{Net.AddressLabel}）"
                : Net.IsConnecting ? "サーバーに接続中…"
                : "<color=#E0705A>●</color> サーバーにつながっていません。ボタンを押すと接続し直します。";
        }

        void EnsureConnected(Action then)
        {
            onConnected = then;
            if (Net.IsClientConnected)
            {
                var a = onConnected;
                onConnected = null;
                a?.Invoke();
                return;
            }
            if (!Net.IsConnecting)
            {
                leavingOnline = false;
                Net.Connect();
            }
            RefreshLobby();
        }

        void RequestRoomList()
        {
            nextListAt = Time.unscaledTime + ListInterval;
            Net?.SendPacket(Packet.Of(Op.ListRooms, Packet.ProtocolVersion, s1: LastRoom, s2: ClientToken));
        }

        void ClearRoomList(string note)
        {
            foreach (var r in roomListRows) Destroy(r);
            roomListRows.Clear();
            lobbyListNote.text = note;
        }

        void OnRoomList(Packet p)
        {
            RoomListInfo list;
            try { list = RoomListInfo.Decode(p.Data); } catch (Exception) { return; }

            // 前の部屋：サーバーに残っているときだけ「戻る」を出す（消えた部屋は記録ごと消す）
            string last = LastRoom;
            bool canRejoin = list.CanRejoin && !string.IsNullOrEmpty(last);
            if (!list.CanRejoin && !string.IsNullOrEmpty(last)) LastRoom = "";
            lobbyRejoin.gameObject.SetActive(canRejoin);
            if (canRejoin) Ui.SetLabel(lobbyRejoin, $"前の対局に戻る（部屋 {last}）");

            if (lobbyRoot == null || !lobbyRoot.activeSelf) return;
            ClearRoomList(list.Rooms.Count == 0 ? "いまオープンの部屋はありません。部屋を作ってみましょう。" : "押すと入ります。対局中の部屋は観戦になります。");
            foreach (var e in list.Rooms)
            {
                var code = e.Code;
                string players = e.First.Length + e.Second.Length == 0 ? "対局者なし" : $"{Or(e.First)} 対 {Or(e.Second)}";
                RuleCodec.TryDecode(e.RuleCode, out var o);
                string phase = e.Phase == RoomPhase.Lobby ? "<color=#8FBF7A>準備中</color>" : "<color=#E3B341>対局中</color>";
                string label = $"<b>{code}</b>　{phase}　{players}　<color=#A39D88>部屋主 {e.OwnerName}・{RulesScreen.Summary(o ?? new StandardRuleOptions())}・{e.Members}人</color>";
                var b = Ui.Button("Room" + code, roomListContent, label, () => JoinRoom(code), fontSize: 24);
                b.GetComponentInChildren<TextMeshProUGUI>().alignment = TextAlignmentOptions.MidlineLeft;
                Ui.Size(b, 60);
                roomListRows.Add(b.gameObject);
            }
        }

        static string Or(string name) => string.IsNullOrEmpty(name) ? "（空席）" : name;

        void CreateRoom()
        {
            PlayerName.Value = lobbyNameInput.text;
            lobbyMessage.text = "部屋を作っています…";
            bool open = PlayerPrefs.GetInt(OpenKey, 1) != 0;
            var code = RuleCodec.Encode(AppSettings.Rules);
            EnsureConnected(() => Net.SendPacket(Packet.Of(Op.CreateRoom, Packet.ProtocolVersion, open ? 1 : 0, s1: ClientToken, s2: code, data: NameBytes)));
        }

        void JoinRoom(string code)
        {
            code = (code ?? "").Trim();
            if (code.Length != 4) { lobbyMessage.text = "<color=#E0705A>部屋番号は4桁の数字です</color>"; return; }
            if (lobbyNameInput != null && lobbyRoot.activeSelf) PlayerName.Value = lobbyNameInput.text;
            lobbyMessage.text = "部屋に入っています…";
            EnsureConnected(() => Net.SendPacket(Packet.Of(Op.JoinRoom, Packet.ProtocolVersion, s1: ClientToken, s2: code, data: NameBytes)));
        }

        void StartDevHost()
        {
            // 模擬サーバーで部屋を作り、入れたら相手役を呼ぶ（OnRoomJoined で）
            devBotPending = true;
            CreateRoom();
        }

        // ───────── 通信イベント ─────────

        void OnClientConnected()
        {
            reconnectTries = 0;
            if (reconnecting)
            {
                // 切れた場合は、同じ部屋に同じトークンで入り直す（サーバーが席と盤面を返す）
                reconnecting = false;
                rejoinPending = true;
                Net.SendPacket(Packet.Of(Op.JoinRoom, Packet.ProtocolVersion, s1: ClientToken, s2: roomCode, data: NameBytes));
            }
            var a = onConnected;
            onConnected = null;
            a?.Invoke();
            RefreshLobby();
        }

        void OnClientDisconnected()
        {
            RefreshLobby();
            if (lobbyRoot.activeSelf && onConnected != null)
                lobbyMessage.text = "<color=#E0705A>サーバーに接続できませんでした。時間をおいてもう一度押してください。</color>";
            onConnected = null;
            if (online && !leavingOnline)
            {
                reconnecting = true;
                reconnectAt = Time.unscaledTime + 2f;
                inputMode = InputMode.None;
                statusText.text = "接続が切れました";
                subText.text = "再接続しています…";
                roomScreen.ShowNotice("<color=#E0705A>接続が切れました。再接続しています…</color>");
            }
        }

        void Update()
        {
            CheckDebugHotkey();
            roomScreen?.Tick();

            if (reconnecting && Time.unscaledTime >= reconnectAt && Net != null && !Net.IsConnecting && !Net.IsClientConnected)
            {
                if (++reconnectTries > 20)
                {
                    reconnecting = false;
                    subText.text = "再接続できませんでした。タイトルの「オンライン対戦」の「前の対局に戻る」から入り直せます。";
                    ShowRow(waitRow);
                }
                else
                {
                    reconnectAt = Time.unscaledTime + 3f;
                    Net.Connect();
                }
            }

            // 部屋の一覧はロビーを開いている間だけ、数秒ごとに取り直す
            if (lobbyRoot != null && lobbyRoot.activeSelf && Net != null && Net.IsClientConnected && Time.unscaledTime >= nextListAt)
                RequestRoomList();

            // 相手が長く切断していたら「勝ちを申請」を出す
            if (online && onlineView != null && onlineView.Phase == GamePhase.Playing && claimWinButton != null)
            {
                bool canClaim = MySeat >= 0 && opponentLeftAt >= 0 && NetClock.Now - opponentLeftAt >= RoomService.ClaimWinAfterSeconds;
                if (claimWinButton.gameObject.activeSelf != canClaim) claimWinButton.gameObject.SetActive(canClaim);
            }

            if (debugBadge != null && debugBadge.gameObject.activeSelf && debugBadge.transform.GetSiblingIndex() != debugBadge.transform.parent.childCount - 1)
                debugBadge.transform.SetAsLastSibling();
        }

        /// <summary>Ctrl+Shift+F8：デバッグモードの切り替え（部屋に入っていないときだけ）。</summary>
        void CheckDebugHotkey()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null || !kb.f8Key.wasPressedThisFrame) return;
            if (!(kb.ctrlKey.isPressed && kb.shiftKey.isPressed)) return;
#else
            if (!(Input.GetKeyDown(KeyCode.F8) && Input.GetKey(KeyCode.LeftControl) && Input.GetKey(KeyCode.LeftShift))) return;
#endif
            if (online)
            {
                roomScreen.ShowNotice("デバッグモードは部屋を出てから切り替えてください");
                return;
            }
            DebugSession.Toggle();
            debugBadge.text = DebugSession.Enabled
                ? $"DEBUG　この画面は別の人（{PlayerName.Value}）として入ります　Ctrl+Shift+F8 で解除"
                : "";
            debugBadge.gameObject.SetActive(DebugSession.Enabled);
            if (lobbyRoot.activeSelf)
            {
                lobbyNameInput.text = PlayerName.Value;
                if (Net != null && Net.IsClientConnected) RequestRoomList();
            }
        }

        void OnPacket(Packet p)
        {
            switch (p.Op)
            {
                case Op.Error: OnServerError(p); break;
                case Op.Notice: OnNotice(p.S1); break;
                case Op.RoomJoined: OnRoomJoined(p); break;
                case Op.RoomState: OnRoomState(p); break;
                case Op.Snapshot: OnSnapshot(ViewCodec.Decode(p.Data)); break;
                case Op.MoveMade: OnMoveMade(p); break;
                case Op.GameOver: OnGameOver(p); break;
                case Op.RoomList: OnRoomList(p); break;
            }
        }

        void OnServerError(Packet p)
        {
            string msg = $"<color=#E0705A>{p.S1}</color>";
            if (lobbyRoot.activeSelf) lobbyMessage.text = msg;
            else if (roomScreen.IsOpen) roomScreen.ShowNotice(msg);
            else subText.text = msg;

            if (p.A == 1) { LeaveOnline(); titleRulesText.text = msg; return; }
            if (p.S1.Contains("部屋はありません"))
            {
                LastRoom = "";
                lobbyRejoin.gameObject.SetActive(false);
                if (online && rejoinPending)
                {
                    // 再接続したら部屋が消えていた（サーバーの再起動など）
                    rejoinPending = false;
                    bool canReplay = replayFinalView != null;
                    online = false;
                    roomScreen.Hide();
                    inputMode = InputMode.None;
                    claimWinButton.gameObject.SetActive(false);
                    if (!canReplay) { LeaveOnline(); titleRulesText.text = "<color=#E0705A>サーバーが再起動したなどの理由で、部屋が閉じられました。</color>"; return; }
                    statusText.text = "部屋がなくなりました";
                    subText.text = "サーバーが再起動したなどの理由で、この部屋は閉じられました。感想戦は見られます。";
                    againButton.interactable = false;
                    ShowRow(resultRow);
                }
                return;
            }
            // 配置の提出が拒否されたら並べ直せるようにする
            if (online && onlineSetupActive && roomInfo != null && roomInfo.Phase == RoomPhase.Setup && MySeat >= 0 && !roomInfo.SetupDone[MySeat])
                inputMode = InputMode.Setup;
        }

        void OnNotice(string text)
        {
            if (roomScreen.IsOpen) roomScreen.ShowNotice(text);
            else if (online) subText.text = text;
        }

        // ───────── 部屋 ─────────

        void OnRoomJoined(Packet p)
        {
            if (matchRoutine != null) StopCoroutine(matchRoutine);
            matchRoutine = null;
            bool sameRoom = online && roomCode == p.S1 && myId == p.A;
            rejoinPending = false;
            roomCode = p.S1;
            myId = p.A;
            LastRoom = roomCode;

            lobbyRoot.SetActive(false);
            titleScreen.SetActive(false);
            matchSetup.Close();
            online = true;
            leavingOnline = false;
            if (!sameRoom)
            {
                roomInfo = null;
                onlineView = null;
                onlineMoveState = null;
                onlineSetupActive = false;
                showingResult = false;
                spectatorShow = 2;
                opponentLeftAt = -1;
                replaySetups[0] = replaySetups[1] = null;
                replayFinalView = null;
                display = AppSettings.Display;
                for (int i = 0; i < 2; i++) { memos[i].Clear(); beliefs[i] = null; }
                board.ClearStamps();
                ClearMarks();
                SetMemoMode(false);
                logText.text = "";
            }

            if (devBotPending)
            {
                devBotPending = false;
#if UNITY_EDITOR
                if (IsLoopback) EditorLoopback.Instance.AddBot(roomCode, EditorLoopback.DefaultBotMode);
#endif
            }
        }

        void OnRoomState(Packet p)
        {
            if (!online) return;
            RoomInfo info;
            try { info = RoomInfo.Decode(p.Data); } catch (Exception) { return; }
            var prev = roomInfo;
            roomInfo = info;

            if (prev == null || prev.RuleCode != info.RuleCode || rules == null)
            {
                RuleCodec.TryDecode(info.RuleCode, out var options);
                currentOptions = options ?? new StandardRuleOptions();
                rules = StandardRules.Create23(currentOptions);
                topo = new BoardTopology(rules.Board);
                board.Init(rules, Math.Max(0, MySeat));
            }

            // 相手の対局者の切断
            int seat = MySeat;
            opponentLeftAt = seat >= 0 && info.AbsentSeconds[1 - seat] >= 0 ? NetClock.Now - info.AbsentSeconds[1 - seat] : -1;

            bool newGame = info.Phase == RoomPhase.Setup && (prev == null || prev.Phase != RoomPhase.Setup);
            if (newGame)
            {
                showingResult = false;
                onlineView = null;
                onlineMoveState = null;
                onlineSetupActive = false;
                for (int i = 0; i < 2; i++) { memos[i].Clear(); beliefs[i] = null; }
                board.Init(rules, Math.Max(0, seat));
                board.ClearStamps();
                logText.text = "";
                spectatorShow = 2;
            }

            headerText.text = $"部屋 <color=#E3B341>{info.Code}</color>";
            teamsText.text = TeamsLineOnline();
            UpdateOnlineScreen();
        }

        /// <summary>今の部屋の段階と自分の立場から、出す画面を決める。</summary>
        void UpdateOnlineScreen()
        {
            if (!online || roomInfo == null) return;
            int seat = MySeat;
            switch (roomInfo.Phase)
            {
                case RoomPhase.Lobby:
                case RoomPhase.Review:
                    onlineSetupActive = false;
                    presetPanel.Close();
                    if (showingResult)
                    {
                        if (inputMode != InputMode.Replay) ShowOnlineResult();
                        roomScreen.Hide();
                    }
                    else ShowRoomScreen();
                    break;

                case RoomPhase.Setup:
                    if (seat < 0) { ShowRoomScreen(); break; }
                    roomScreen.Hide();
                    if (!roomInfo.SetupDone[seat])
                    {
                        if (!onlineSetupActive) BeginOnlineSetup();
                        statusText.text = "駒を配置する";
                        string other = roomInfo.SetupDone[1 - seat] ? "相手は配置を終えました。" : "";
                        subText.text = other + HintSetup;
                    }
                    else
                    {
                        onlineSetupActive = false;
                        inputMode = InputMode.None;
                        presetPanel.Close();
                        ShowRow(waitRow);
                        waitAbortButton.gameObject.SetActive(true);
                        statusText.text = "相手の配置を待っています";
                        subText.text = "配置は決定しました。";
                    }
                    break;

                case RoomPhase.Playing:
                    roomScreen.Hide();
                    onlineSetupActive = false;
                    if (onlineView != null) UpdateOnlineTurn();
                    break;
            }
        }

        void ShowRoomScreen()
        {
            inputMode = InputMode.None;
            SetMemoMode(false);
            presetPanel.Close();
            roomScreen.Refresh(roomInfo, myId, RulesScreen.Summary(currentOptions));
            if (!roomScreen.IsOpen) roomScreen.Show(string.IsNullOrEmpty(PlayerName.Value) ? roomInfo.NameOf(myId) : PlayerName.Value);
        }

        void OpenRoomRules()
        {
            if (roomInfo == null) return;
            if (IsOwner && roomInfo.Phase == RoomPhase.Lobby)
                rulesScreen.OpenFor(currentOptions, true,
                    o => Net?.SendPacket(Packet.Of(Op.SetRules, s1: RuleCodec.Encode(o))),
                    () => display = AppSettings.Display, true);
            else if (rules != null)
                info.OpenRules(rules, currentOptions);
        }

        string TeamsLineOnline()
        {
            string Name(int s)
            {
                int id = roomInfo.SeatMember[s];
                if (id == RoomInfo.NoSeat) return "（空席）";
                return roomInfo.NameOf(id) + (id == myId ? "（あなた）" : "");
            }
            return $"<color=#{ColorUtility.ToHtmlStringRGB(Color.Lerp(Theme.Team[0], Color.white, 0.35f))}>■ 朱</color> {Name(0)}　" +
                   $"<color=#{ColorUtility.ToHtmlStringRGB(Color.Lerp(Theme.Team[1], Color.white, 0.45f))}>■ 藍</color> {Name(1)}" +
                   (IsSpectator ? "　<color=#A39D88>観戦中</color>" : "");
        }

        // ───────── 配置 ─────────

        void BeginOnlineSetup()
        {
            onlineSetupActive = true;
            onlineView = null;
            onlineMoveState = null;
            board.ClearStamps();
            setupPlayer = MySeat;
            viewer = MySeat;
            board.SetViewer(viewer);
            // ローカルと同じく、総司令部まわりを固めた配置を初期値にする
            setupPlacements = new Core.Ai.CpuPlayer(rules, setupPlayer, Environment.TickCount).ChooseSetup();
            selectedNode = -1;
            inputMode = InputMode.Setup;
            ShowRow(setupRow);
            logText.text = "";
            RenderSetup();
        }

        void SubmitOnlineSetup()
        {
            if (inputMode != InputMode.Setup) return;
            inputMode = InputMode.None;
            subText.text = "配置を送っています…";
            Net.SendPacket(Packet.Of(Op.SubmitSetup, s1: SetupCodec.Encode(topo, setupPlayer, setupPlacements)));
        }

        // ───────── 対局 ─────────

        void OnSnapshot(PlayerView view)
        {
            if (!online) return;
            onlineView = view;
            onlineMoveState = view.ToMoveState(rules, topo);
            onlineSetupActive = false;
            showingResult = false;
            presetPanel.Close();
            roomScreen.Hide();
            selectedNode = -1;
            viewer = MySeat >= 0 ? MySeat : 0;
            board.SetViewer(viewer);
            UpdateOnlineTurn();
        }

        void OnMoveMade(Packet p)
        {
            if (!online || onlineView == null) return;
            var mv = ViewCodec.DecodeMove(p.Data);
            onlineView.ApplyMove(mv, p.A, (GameResult)p.B, (EndReason)p.C);
            onlineMoveState = onlineView.ToMoveState(rules, topo);
            selectedNode = -1;
            // 観戦者は攻めた側から見た結果の判を押す
            var verdict = MoveLog.VerdictFor(mv, IsSpectator ? mv.Player : MySeat);
            if (verdict != MoveLog.Verdict.None) board.ShowStamp(mv.ToNode, ToStamp(verdict));
            if (onlineView.Phase == GamePhase.Playing) UpdateOnlineTurn();
            else RenderPlay(); // 終局の全公開は GameOver で届く
        }

        void UpdateOnlineTurn()
        {
            if (onlineView == null || onlineView.Phase != GamePhase.Playing) return;
            teamsText.text = TeamsLineOnline();
            if (IsSpectator)
            {
                inputMode = InputMode.None;
                selectedNode = -1;
                ShowRow(spectateRow);
                RefreshSpectateToggle();
                int turn = onlineView.CurrentPlayer;
                statusText.text = $"観戦中　{Theme.TeamName[turn]}の番";
                subText.text = $"{roomInfo.NameOf(roomInfo.SeatMember[turn])}さんが考えています。";
                RenderPlay();
                return;
            }

            ShowRow(playRow);
            memoButton.gameObject.SetActive(display.MemoEnabled);
            bool myTurn = onlineView.CurrentPlayer == MySeat;
            bool opponentAway = opponentLeftAt >= 0;
            if (myTurn)
            {
                if (inputMode != InputMode.Move) selectedNode = -1;
                inputMode = InputMode.Move;
                statusText.text = "あなたの番";
                if (!memoMode) subText.text = opponentAway ? "相手の接続が切れています（戻るまで待てます）。" + HintMove : HintMove;
            }
            else
            {
                inputMode = InputMode.None;
                selectedNode = -1;
                statusText.text = "相手の番";
                if (!memoMode)
                    subText.text = opponentAway
                        ? $"相手の接続が切れています。{(int)RoomService.ClaimWinAfterSeconds}秒たっても戻らなければ勝ちを申請できます。"
                        : "相手が考えています。";
            }
            RenderPlay();
        }

        void SendOnlineMove(Move move)
        {
            inputMode = InputMode.None;
            selectedNode = -1;
            Net.SendPacket(Packet.Of(Op.Move, move.PieceId, move.ToNode));
        }

        // ───── 観戦者の表示 ─────

        void CycleSpectatorShow()
        {
            spectatorShow = spectatorShow == 2 ? 0 : spectatorShow == 0 ? 1 : 2;
            RefreshSpectateToggle();
            RenderPlay();
        }

        void RefreshSpectateToggle()
        {
            Ui.SetLabel(spectateToggle, spectatorShow == 2 ? "表示：両方の駒" : spectatorShow == 0 ? "表示：先手の駒だけ" : "表示：後手の駒だけ");
        }

        /// <summary>盤に出す駒種。観戦中に片方だけを表示しているときは、もう片方を伏せる。</summary>
        int DisplayType(Core.PieceView p)
        {
            if (online && IsSpectator && onlineView != null && onlineView.Phase == GamePhase.Playing && spectatorShow != 2 && p.Owner != spectatorShow)
                return Visibility.HiddenType;
            return p.TypeId;
        }

        int LogHiddenOwner(PlayerView view)
        {
            if (!online || !IsSpectator || view.Phase != GamePhase.Playing || spectatorShow == 2) return -1;
            return 1 - spectatorShow;
        }

        // ───── 終局 ─────

        void OnGameOver(Packet p)
        {
            if (!online) return;
            onlineView = ViewCodec.Decode(p.Data);
            onlineMoveState = onlineView.ToMoveState(rules, topo);
            onlineResult = (GameResult)p.A;
            onlineReason = (EndReason)p.B;
            showingResult = true;
            onlineSetupActive = false;
            roomScreen.Hide();

            // 感想戦用：双方の配置と手順
            replaySetups[0] = SetupCodec.Decode(rules, topo, 0, p.S1);
            replaySetups[1] = SetupCodec.Decode(rules, topo, 1, p.S2);
            replayMoves.Clear();
            foreach (var h in onlineView.History) replayMoves.Add(new Move(h.PieceId, h.ToNode));
            replayFinalView = onlineView;
            if (inputMode != InputMode.Replay) ShowOnlineResult();
        }

        void ShowOnlineResult()
        {
            inputMode = InputMode.None;
            selectedNode = -1;
            SetMemoMode(false);
            claimWinButton.gameObject.SetActive(false);
            if (!resultRow.activeSelf) againButton.interactable = true;
            ShowRow(resultRow);
            RenderPlay();
            int seat = MySeat;
            if (onlineResult == GameResult.Draw) statusText.text = "引き分け";
            else
            {
                int winner = onlineResult == GameResult.Player0Win ? 0 : 1;
                statusText.text = seat < 0 || roomInfo == null ? $"{Theme.TeamName[winner]}の勝ち" : winner == seat ? "勝利" : "敗北";
            }
            string reason = MoveLog.Reason(onlineReason);
            if (onlineReason == EndReason.Resign) reason = "投了・退出";
            subText.text = $"{reason}（{onlineView.History.Count}手）。全ての駒を表にしています。「準備画面へ」で次の対局の準備に戻ります。";
            Ui.SetLabel(againButton, "準備画面へ");
            Ui.SetLabel(resultRow.transform.Find("Title").GetComponent<Button>(), "部屋を出る");
        }

        // ───────── 招待 ─────────

        void SetInviteVisible(bool visible)
        {
            // 招待は準備画面にある。見出しの招待ボタンは使わない
            if (inviteButton == null) return;
            inviteButton.gameObject.SetActive(false);
            infoButtonsLayout.preferredWidth = 330;
        }

        /// <summary>部屋番号（ブラウザなら直接入れるリンクも）をクリップボードへ。</summary>
        void CopyInvite()
        {
            if (!online || string.IsNullOrEmpty(roomCode)) return;
            string url = WebBridge.InviteUrl(roomCode);
            string text = url != null
                ? $"軍人将棋で対戦しましょう。部屋番号 {roomCode}\n{url}"
                : $"軍人将棋で対戦しましょう。部屋番号 {roomCode}";
            bool ok = WebBridge.Copy(text);
            roomScreen.ShowNotice(ok ? "招待の文面をコピーしました。相手に送ってください。" : $"<color=#E0705A>コピーできませんでした。部屋番号 {roomCode} を相手に伝えてください。</color>");
        }

        void LeaveOnline()
        {
            leavingOnline = true;
            reconnecting = false;
            if (Net != null && Net.IsClientConnected) Net.SendPacket(Packet.Of(Op.LeaveRoom));
            LastRoom = "";
            online = false;
            roomInfo = null;
            myId = -1;
            onlineView = null;
            onlineMoveState = null;
            showingResult = false;
            roomScreen.Hide();
            claimWinButton.gameObject.SetActive(false);
            if (Net != null) Net.Disconnect();
            ShowTitle();
        }

        // ───────── ボタン（ローカルとオンラインで行き先を分ける） ─────────

        void OnConfirmSetup()
        {
            if (online) SubmitOnlineSetup();
            else setupConfirmed = true;
        }

        void OnAbortSetup()
        {
            if (online) { Net?.SendPacket(Packet.Of(Op.AbortSetup)); return; }
            ShowTitle();
        }

        void OnResign()
        {
            if (online) Net?.SendPacket(Packet.Of(Op.Resign));
            else resignRequested = true;
        }

        void OnAgain()
        {
            if (online)
            {
                // 着席者が「準備画面へ」を押すと感想戦を終えたことをサーバーに伝える（2人そろうと準備段階へ）
                if (roomInfo != null && roomInfo.Phase == RoomPhase.Review && MySeat >= 0) Net?.SendPacket(Packet.Of(Op.FinishReview));
                showingResult = false;
                board.ClearStamps();
                UpdateOnlineScreen();
                return;
            }
            StartMatch(seats[0], seats[1], level);
        }

        void OnLeaveToTitle()
        {
            if (online) LeaveOnline();
            else ShowTitle();
        }
    }
}
