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
    /// オンライン対戦の画面と進行（GameApp の続き）。盤・パネルはローカル対戦と共通で、
    /// 違いは「見え方（PlayerView）をサーバーから受け取る」「自分の操作をサーバーへ送る」ことだけ。
    /// </summary>
    public sealed partial class GameApp
    {
        const string TokenKey = "gs.clientToken";
        const string LastRoomKey = "gs.lastRoom";

        // 対局の状態（サーバーから届いたもの）
        bool online;
        PlayerView onlineView;
        GameState onlineMoveState;
        string roomCode = "";
        int onlineSeat;
        RoomPhase onlinePhase;
        StatusFlags onlineFlags;
        float opponentLeftAt = -1;   // 相手が切断した時刻（つながっていれば -1）
        bool onlineSetupActive;
        GameResult onlineResult;
        EndReason onlineReason;

        // 通信
        IGameConnection net;
        Action onConnected;          // 接続できたら実行する操作（部屋を作る・入る）
        bool leavingOnline;          // 自分から抜けたときは再接続しない
        bool reconnecting;
        float reconnectAt;
        int reconnectTries;
        bool devBotPending;
        bool rejoinPending;          // 再接続で同じ部屋に入り直している途中

        // 画面
        GameObject lobbyRoot, waitRow;
        TextMeshProUGUI lobbyStatus, lobbyRules, lobbyMessage;
        TMP_InputField lobbyCodeInput;
        Button lobbyCreate, lobbyJoin, lobbyRejoin, claimWinButton, againButton, lobbyDev;

        /// <summary>サーバーとの接続。エディタでは既定で模擬サーバー（EditorLoopback）、それ以外は Mirror。</summary>
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

        static string ClientToken
        {
            get
            {
                // ブラウザ（端末）ごとの識別子。再読み込みしても同じ席に戻れるようにする
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

        // ───────── 画面の組み立て ─────────

        void BuildOnline()
        {
            var content = setupRow.transform.parent;
            waitRow = ButtonRow(content, Ui.Button("LeaveRoom", null, "部屋を出る", LeaveOnline, Ui.ButtonStyle.Danger));
            claimWinButton = Ui.Button("ClaimWin", playRow.transform, "勝ちを申請", () => Net?.SendPacket(Packet.Of(Op.ClaimWin)), Ui.ButtonStyle.Primary);
            claimWinButton.gameObject.SetActive(false);
            againButton = resultRow.transform.Find("Again").GetComponent<Button>();

            var (root, card) = Ui.Modal("Lobby", canvasRect, 0.5f, 0.86f);
            lobbyRoot = root;
            Ui.Column(card, 14);
            Ui.Size(Ui.Text("Title", card, "オンライン対戦", 46, Theme.Text, bold: true), 64);
            lobbyStatus = Ui.Text("Status", card, "", 26, Theme.TextMuted);
            Ui.Size(lobbyStatus, 40);

            Section(card, "部屋を作る");
            lobbyRules = Ui.Text("Rules", card, "", 24, Theme.TextMuted);
            Ui.Size(lobbyRules, 64);
            Ui.AutoSize(lobbyRules, 24);
            lobbyCreate = Ui.Button("Create", card, "部屋を作る", CreateRoom, Ui.ButtonStyle.Primary, 32);
            Ui.Size(lobbyCreate, 80);

            Section(card, "部屋に入る");
            var joinRow = Ui.Rect("JoinRow", card);
            Ui.RowGroup(joinRow, 14);
            Ui.Size(joinRow, 76);
            lobbyCodeInput = Ui.InputField("Code", joinRow, "部屋番号（4桁）", 34);
            lobbyCodeInput.contentType = TMP_InputField.ContentType.IntegerNumber;
            lobbyCodeInput.characterLimit = 4;
            Ui.Size(lobbyCodeInput, -1, 240, 1);
            lobbyJoin = Ui.Button("Join", joinRow, "入る", () => JoinRoom(lobbyCodeInput.text), fontSize: 32);
            Ui.Size(lobbyJoin, -1, 200);

            lobbyRejoin = Ui.Button("Rejoin", card, "", () => JoinRoom(PlayerPrefs.GetString(LastRoomKey, "")), fontSize: 28);
            Ui.Size(lobbyRejoin, 70);

#if UNITY_EDITOR
            lobbyDev = Ui.Button("DevHost", card, "（エディタ）部屋を作り、相手役ボットを入れる", StartDevHost, fontSize: 24);
            Ui.Size(lobbyDev, 60);
#endif
            lobbyMessage = Ui.Text("Message", card, "", 26, Theme.TextMuted);
            Ui.Size(lobbyMessage, 70);
            Ui.AutoSize(lobbyMessage, 26);

            var spacer = Ui.Size(Ui.Rect("Spacer", card));
            spacer.flexibleHeight = 1;
            var close = Ui.Button("CloseLobby", card, "閉じる", CloseLobby, fontSize: 30);
            Ui.Size(close, 76);
        }

        static void Section(Transform parent, string text)
        {
            var h = Ui.Text("Section", parent, text, 26, Theme.Brass, bold: true);
            Ui.Size(h, 44);
            h.alignment = TextAlignmentOptions.BottomLeft;
        }

        void ApplyOnlineOrientation(bool landscape)
        {
            if (lobbyRoot == null) return;
            var card = (RectTransform)lobbyRoot.transform.GetChild(0);
            if (landscape) Ui.Anchors(card, 0.25f, 0.05f, 0.75f, 0.95f);
            else Ui.Anchors(card, 0.03f, 0.12f, 0.97f, 0.88f);
        }

        // ───────── ロビー ─────────

        void OpenLobby()
        {
            if (Net == null)
            {
                titleRulesText.text = "<color=#E0705A>通信の設定（GunjinNetworkManager）がシーンにありません</color>";
                return;
            }
            var o = AppSettings.Rules;
            lobbyRules.text = $"今のルール（{RulesScreen.Summary(o)}・コード {RuleCodec.Encode(o)}）で部屋を作ります。表示される部屋番号を相手に伝えてください。";
            lobbyMessage.text = "";
            string last = PlayerPrefs.GetString(LastRoomKey, "");
            lobbyRejoin.gameObject.SetActive(!string.IsNullOrEmpty(last));
            Ui.SetLabel(lobbyRejoin, $"前の対局に戻る（部屋 {last}）");
            if (lobbyDev != null) lobbyDev.gameObject.SetActive(IsLoopback);
            lobbyRoot.SetActive(true);
            lobbyRoot.transform.SetAsLastSibling();
            EnsureConnected(null);
            RefreshLobby();
        }

        void CloseLobby()
        {
            lobbyRoot.SetActive(false);
            onConnected = null;
            if (!online && Net != null) Net.Disconnect();
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

        void CreateRoom()
        {
            lobbyMessage.text = "部屋を作っています…";
            var code = RuleCodec.Encode(AppSettings.Rules);
            EnsureConnected(() => Net.SendPacket(Packet.Of(Op.CreateRoom, Packet.ProtocolVersion, s1: ClientToken, s2: code)));
        }

        void JoinRoom(string code)
        {
            code = (code ?? "").Trim();
            if (code.Length != 4) { lobbyMessage.text = "<color=#E0705A>部屋番号は4桁の数字です</color>"; return; }
            lobbyMessage.text = "部屋に入っています…";
            EnsureConnected(() => Net.SendPacket(Packet.Of(Op.JoinRoom, Packet.ProtocolVersion, s1: ClientToken, s2: code)));
        }

void StartDevHost()
        {
            // 模擬サーバーで部屋を作り、入れたら相手役を呼ぶ（EnterOnlineRoom で）
            devBotPending = true;
            CreateRoom();
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

        // ───────── 通信イベント ─────────

        void OnClientConnected()
        {
            reconnectTries = 0;
            if (reconnecting)
            {
                // 対局中に切れた場合は、同じ部屋に同じトークンで入り直す（サーバーが席と盤面を返す）
                reconnecting = false;
                rejoinPending = true;
                Net.SendPacket(Packet.Of(Op.JoinRoom, Packet.ProtocolVersion, s1: ClientToken, s2: roomCode));
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
            }
        }

        void Update()
        {
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

            // 相手が長く切断していたら「勝ちを申請」を出す
            if (online && onlineView != null && onlineView.Phase == GamePhase.Playing && claimWinButton != null)
            {
                bool canClaim = opponentLeftAt >= 0 && NetClock.Now - opponentLeftAt >= RoomService.ClaimWinAfterSeconds;
                if (claimWinButton.gameObject.activeSelf != canClaim) claimWinButton.gameObject.SetActive(canClaim);
            }
        }

        void OnPacket(Packet p)
        {
            switch (p.Op)
            {
                case Op.Error: OnServerError(p); break;
                case Op.RoomJoined: EnterOnlineRoom(p.S1, p.A, p.S2); break;
                case Op.RoomStatus: OnRoomStatus(p); break;
                case Op.Snapshot: OnSnapshot(ViewCodec.Decode(p.Data)); break;
                case Op.MoveMade: OnMoveMade(p); break;
                case Op.GameOver: OnGameOver(p); break;
            }
        }

        void OnServerError(Packet p)
        {
            string msg = $"<color=#E0705A>{p.S1}</color>";
            if (lobbyRoot.activeSelf) lobbyMessage.text = msg;
            else subText.text = msg;

            if (p.A == 1) { LeaveOnline(); titleRulesText.text = msg; return; }
            // 部屋が無くなっていた場合は「前の対局に戻る」を消す
            if (p.S1.Contains("部屋はありません")) { PlayerPrefs.DeleteKey(LastRoomKey); lobbyRejoin.gameObject.SetActive(false); }
            if (online && rejoinPending && p.S1.Contains("部屋はありません"))
            {
                // 再接続したら部屋が消えていた（サーバーの再起動など）。対局はもう続けられない
                rejoinPending = false;
                inputMode = InputMode.None;
                claimWinButton.gameObject.SetActive(false);
                statusText.text = "部屋がなくなりました";
                subText.text = "サーバーが再起動したなどの理由で、この部屋は閉じられました。" + (replayFinalView != null ? "感想戦は見られます。" : "タイトルに戻って部屋を作り直してください。");
                if (replayFinalView != null) { againButton.interactable = false; ShowRow(resultRow); }
                else ShowRow(waitRow);
                return;
            }
            // 配置の提出が拒否されたら並べ直せるようにする
            if (online && onlinePhase == RoomPhase.Setup && onlineSetupActive && (onlineFlags & StatusFlags.MySetupDone) == 0)
                inputMode = InputMode.Setup;
        }

        void EnterOnlineRoom(string code, int seat, string ruleCode)
        {
            if (matchRoutine != null) StopCoroutine(matchRoutine);
            matchRoutine = null;
            bool sameGame = rejoinPending && online && roomCode == code && onlineSeat == seat && rules != null;
            rejoinPending = false;
            roomCode = code;
            onlineSeat = seat;
            PlayerPrefs.SetString(LastRoomKey, code);
            PlayerPrefs.Save();

            lobbyRoot.SetActive(false);
            titleScreen.SetActive(false);
            online = true;
            leavingOnline = false;
            if (sameGame)
            {
                // 再接続：対局中の盤面はこの後の Snapshot で揃う。配置中なら並べかけのまま続ける
                if (onlineSetupActive) { inputMode = InputMode.Setup; ShowRow(setupRow); }
                return;
            }

            RuleCodec.TryDecode(ruleCode, out var options);
            currentOptions = options ?? new StandardRuleOptions();
            display = AppSettings.Display;
            rules = StandardRules.Create23(currentOptions);
            topo = new BoardTopology(rules.Board);
            viewer = seat;
            onlineView = null;
            onlineMoveState = null;
            onlineSetupActive = false;
            onlinePhase = RoomPhase.Setup;
            onlineFlags = StatusFlags.None;
            onlineResult = GameResult.Ongoing;
            onlineReason = EndReason.None;
            opponentLeftAt = -1;
            replaySetups[0] = replaySetups[1] = null;
            replayFinalView = null;
            for (int p = 0; p < 2; p++) { memos[p].Clear(); beliefs[p] = null; }
            board.Init(rules, seat);
            board.ClearStamps();
            ClearMarks();
            SetMemoMode(false);
            logText.text = "";
            headerText.text = $"部屋 <color=#E3B341>{code}</color>";
            SetInviteVisible(true);
            string me = "あなた", them = "相手";
            teamsText.text = $"<color=#{ColorUtility.ToHtmlStringRGB(Color.Lerp(Theme.Team[0], Color.white, 0.35f))}>■ 朱</color> {(seat == 0 ? me : them)}（先手）　　" +
                             $"<color=#{ColorUtility.ToHtmlStringRGB(Color.Lerp(Theme.Team[1], Color.white, 0.45f))}>■ 藍</color> {(seat == 1 ? me : them)}（後手）";
            statusText.text = "部屋に入りました";
            subText.text = "";

            if (devBotPending)
            {
                devBotPending = false;
#if UNITY_EDITOR
                if (IsLoopback) EditorLoopback.Instance.AddBot(code, EditorLoopback.DefaultBotMode);
#endif
            }
        }

        void OnRoomStatus(Packet p)
        {
            onlinePhase = (RoomPhase)p.A;
            onlineFlags = (StatusFlags)p.B;
            bool opponentAway = (onlineFlags & StatusFlags.OpponentJoined) != 0 && (onlineFlags & StatusFlags.OpponentPresent) == 0;
            opponentLeftAt = opponentAway ? NetClock.Now - p.C : -1;
            RefreshOnlineStatus();
        }

        void RefreshOnlineStatus()
        {
            if (!online) return;
            bool opponentJoined = (onlineFlags & StatusFlags.OpponentJoined) != 0;
            // 途中から入れるのは配置中だけなので、招待は相手が来るまで
            SetInviteVisible(onlinePhase == RoomPhase.Setup && !opponentJoined);
            switch (onlinePhase)
            {
                case RoomPhase.Setup:
                    if ((onlineFlags & StatusFlags.MySetupDone) == 0)
                    {
                        if (!onlineSetupActive) BeginOnlineSetup();
                        statusText.text = "駒を配置する";
                        // 標準と違うルールの部屋なら、配置の前に気づけるようにする
                        string ruleNote = RuleCodec.DiffCount(currentOptions) > 0 ? $"<color=#E3B341>この部屋のルール：{RulesScreen.Summary(currentOptions)}（「ルール」で確認）</color>\n" : "";
                        subText.text = ruleNote + (opponentJoined
                            ? ((onlineFlags & StatusFlags.OpponentSetupDone) != 0 ? "相手は配置を終えました。" : "対戦相手がそろいました。") + HintSetup
                            : $"相手を待つ間に配置できます。部屋番号 <b><size=130%>{roomCode}</size></b> を相手に伝えてください（「招待」でコピーできます）。");
                    }
                    else
                    {
                        onlineSetupActive = false;
                        inputMode = InputMode.None;
                        presetPanel.Close();
                        ShowRow(waitRow);
                        statusText.text = opponentJoined ? "相手の配置を待っています" : "相手の参加を待っています";
                        subText.text = opponentJoined ? "配置は決定しました。" : $"部屋番号 <b><size=130%>{roomCode}</size></b> を相手に伝えてください（「招待」でコピーできます）。";
                    }
                    break;
                case RoomPhase.Playing:
                    UpdateOnlineTurn();
                    break;
                case RoomPhase.Finished:
                    if (onlineView != null && onlineView.Phase == GamePhase.Finished && inputMode != InputMode.Replay) ShowOnlineResult();
                    break;
            }
        }

        void BeginOnlineSetup()
        {
            onlineSetupActive = true;
            onlineView = null;
            onlineMoveState = null;
            board.ClearStamps();
            setupPlayer = onlineSeat;
            // ローカルと同じく、総司令部まわりを固めた配置を初期値にする
            setupPlacements = new Core.Ai.CpuPlayer(rules, onlineSeat, Environment.TickCount).ChooseSetup();
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
            Net.SendPacket(Packet.Of(Op.SubmitSetup, s1: SetupCodec.Encode(topo, onlineSeat, setupPlacements)));
        }

        void OnSnapshot(PlayerView view)
        {
            if (!online) return;
            onlineView = view;
            onlineMoveState = view.ToMoveState(rules, topo);
            onlineSetupActive = false;
            presetPanel.Close();
            selectedNode = -1;
            if (view.Phase == GamePhase.Playing) onlinePhase = RoomPhase.Playing;
            UpdateOnlineTurn();
        }

        void OnMoveMade(Packet p)
        {
            if (!online || onlineView == null) return;
            var mv = ViewCodec.DecodeMove(p.Data);
            onlineView.ApplyMove(mv, p.A, (GameResult)p.B, (EndReason)p.C);
            onlineMoveState = onlineView.ToMoveState(rules, topo);
            selectedNode = -1;
            var verdict = MoveLog.VerdictFor(mv, onlineSeat);
            if (verdict != MoveLog.Verdict.None) board.ShowStamp(mv.ToNode, ToStamp(verdict));
            if (onlineView.Phase == GamePhase.Playing) UpdateOnlineTurn();
            else RenderPlay(); // 終局の全公開は GameOver で届く
        }

        void UpdateOnlineTurn()
        {
            if (onlineView == null || onlineView.Phase != GamePhase.Playing) return;
            ShowRow(playRow);
            memoButton.gameObject.SetActive(display.MemoEnabled);
            bool myTurn = onlineView.CurrentPlayer == onlineSeat;
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

        void OnGameOver(Packet p)
        {
            if (!online) return;
            onlineView = ViewCodec.Decode(p.Data);
            onlineMoveState = onlineView.ToMoveState(rules, topo);
            onlineResult = (GameResult)p.A;
            onlineReason = (EndReason)p.B;
            onlinePhase = RoomPhase.Finished;

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
            ShowRow(resultRow);
            RenderPlay();
            if (onlineResult == GameResult.Draw) statusText.text = "引き分け";
            else
            {
                int winner = onlineResult == GameResult.Player0Win ? 0 : 1;
                statusText.text = winner == onlineSeat ? "勝利" : "敗北";
            }
            string reason = MoveLog.Reason(onlineReason);
            if (onlineReason == EndReason.Resign)
                reason = (onlineResult == GameResult.Player0Win) == (onlineSeat == 0) ? "相手の投了・切断" : "投了";
            bool iWant = (onlineFlags & StatusFlags.IWantRematch) != 0;
            bool theyWant = (onlineFlags & StatusFlags.OpponentWantsRematch) != 0;
            bool opponentHere = (onlineFlags & StatusFlags.OpponentPresent) != 0;
            string rematch = iWant ? "　再戦を申し込みました。相手を待っています。"
                : theyWant ? "　相手が再戦を希望しています。"
                : !opponentHere ? "　相手は退出しました。" : "";
            subText.text = $"{reason}（{onlineView.History.Count}手）。全ての駒を表にしています。{rematch}";
            Ui.SetLabel(againButton, iWant ? "再戦待ち" : "再戦（先後交代）");
            againButton.interactable = !iWant && opponentHere;
        }

        // ───────── 招待 ─────────

        void SetInviteVisible(bool visible)
        {
            if (inviteButton == null || inviteButton.gameObject.activeSelf == visible) return;
            inviteButton.gameObject.SetActive(visible);
            infoButtonsLayout.preferredWidth = visible ? 504 : 330;
            Ui.SetLabel(inviteButton, "招待");
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
            Ui.SetLabel(inviteButton, ok ? "コピー済" : "失敗");
            if (!ok) subText.text = $"コピーできませんでした。部屋番号 <b><size=130%>{roomCode}</size></b> を相手に伝えてください（「招待」でコピーできます）。";
            StopCoroutine(nameof(ResetInviteLabel));
            StartCoroutine(nameof(ResetInviteLabel));
        }

        System.Collections.IEnumerator ResetInviteLabel()
        {
            yield return new WaitForSecondsRealtime(2f);
            if (inviteButton != null) Ui.SetLabel(inviteButton, "招待");
        }

        void LeaveOnline()
        {
            leavingOnline = true;
            reconnecting = false;
            if (Net != null && Net.IsClientConnected) Net.SendPacket(Packet.Of(Op.LeaveRoom));
            PlayerPrefs.DeleteKey(LastRoomKey);
            online = false;
            onlineView = null;
            onlineMoveState = null;
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

        void OnResign()
        {
            if (online) Net?.SendPacket(Packet.Of(Op.Resign));
            else resignRequested = true;
        }

        void OnAgain()
        {
            if (online) { Net?.SendPacket(Packet.Of(Op.Rematch)); return; }
            StartMatch(seats[0], seats[1], level);
        }

        void OnLeaveToTitle()
        {
            if (online) LeaveOnline();
            else ShowTitle();
        }
    }
}
