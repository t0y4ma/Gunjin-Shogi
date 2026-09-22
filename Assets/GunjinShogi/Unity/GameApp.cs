using System.Collections;
using System.Collections.Generic;
using System.Text;
using GunjinShogi.Core;
using GunjinShogi.Core.Ai;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GunjinShogi.UnityView
{
    public enum SeatKind { Human, Cpu }

    /// <summary>
    /// ローカル対戦（CPU対戦・ホットシート）の画面と進行。
    /// 画面はすべて実行時にコードで組み立てる。ゲームの判定は Core の GameState に任せ、
    /// 表示は PlayerView（見える情報）だけから作る。
    /// </summary>
    public sealed partial class GameApp : MonoBehaviour
    {
        [SerializeField] TMP_FontAsset boldFont;
        [SerializeField] TMP_FontAsset regularFont;

        static readonly string[] LevelName = { "やさしい", "ふつう", "つよい" };
        static readonly string[] MemoLabels = { "将", "佐", "尉", "飛", "タ", "騎", "工", "ス", "地", "旗", "？", "消す" };
        const string HintMove = "動かす駒を選んでください。";
        const string HintSetup = "駒を2つ選ぶと入れ替わります。相手からは見えません。";
        const string HintMemo = "メモする敵の駒を選んでください。";

        Canvas canvas;
        CanvasScaler scaler;
        RectTransform canvasRect, boardArea, panelArea, panelEdge;
        BoardView board;
        Button inviteButton;
        LayoutElement infoButtonsLayout;
        TextMeshProUGUI headerText, teamsText, statusText, subText, logText, titleRulesText, assistText;
        GameObject logFrame, memoPalette;
        GameObject setupRow, playRow, resultRow, replayRow, titleScreen, handoffScreen;
        TextMeshProUGUI handoffTitle, handoffBody;
        Button memoButton;
        RulesScreen rulesScreen;
        PresetPanel presetPanel;
        InfoScreens info;
        DisplaySettings display = new DisplaySettings();
        ScrollRect logScroll;
        string cpuResignReason;
        Button cpuStartButton;
        Button[] levelButtons;
        TextMeshProUGUI levelNote;
        static readonly string[] LevelNotes =
        {
            "推理はざっくりで、指し手にムラがあります。取られそうな駒もあまり気にしません。はじめての人向け。",
            "戦闘の結果と動きから相手の駒を推理し、取られそうな駒は逃がします。ルールを覚えた人向け。",
            "推理の試行を増やしてほとんど迷わず指します。防御も固く、勝てないと見ると投了します。",
        };
        StandardRuleOptions currentOptions;
        GameObject infoButtons;
        bool? lastLandscape;

        // 対局
        RuleSet rules;
        GameState state;
        readonly SeatKind[] seats = new SeatKind[2];
        readonly CpuPlayer[] cpus = new CpuPlayer[2];
        readonly Belief[] beliefs = new Belief[2];
        readonly List<Placement>[] submittedSetups = new List<Placement>[2];
        readonly Dictionary<int, string>[] memos = { new Dictionary<int, string>(), new Dictionary<int, string>() };
        CpuLevel level;
        int viewer;
        bool Hotseat => seats[0] == SeatKind.Human && seats[1] == SeatKind.Human;
        Coroutine matchRoutine;

        // 入力
        enum InputMode { None, Setup, Move, Replay }
        InputMode inputMode;
        int setupPlayer;
        List<Placement> setupPlacements;
        int selectedNode = -1;
        bool memoMode;
        int memoTarget = -1;
        readonly BoardView.Marks marks = new BoardView.Marks();
        Move? pendingMove;
        bool setupConfirmed, resignRequested, handoffAccepted;

        // 感想戦（ローカルでもオンラインでも「配置＋手順＋全公開の見え方」から再生する）
        int replayPly;
        readonly List<Placement>[] replaySetups = new List<Placement>[2];
        readonly List<Move> replayMoves = new List<Move>();
        PlayerView replayFinalView;
        BoardTopology topo;

        void Awake()
        {
#if UNITY_SERVER
            // 専用サーバーでは画面を作らない（通信は GunjinNetworkManager だけが動く）
            enabled = false;
#else
            Setup();
#endif
        }

        void Setup()
        {
            Ui.Bold = boldFont;
            Ui.Regular = regularFont != null ? regularFont : boldFont;
            Application.targetFrameRate = 60;
            EnsureEventSystem();
            BuildCanvas();
            ShowTitle();
            // 招待リンク（?room=1234）から開かれたら、そのまま部屋に入る
            var invited = WebBridge.RoomFromPage();
            if (invited != null) { OpenLobby(); JoinRoom(invited); }
        }

        static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        // ───────── 画面の組み立て ─────────

        void BuildCanvas()
        {
            var go = new GameObject("GameCanvas", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1;
            go.AddComponent<GraphicRaycaster>();
            canvasRect = (RectTransform)go.transform;

            var bg = Ui.Image("Background", canvasRect, Theme.Background);
            Ui.Stretch(bg.rectTransform);

            boardArea = Ui.Rect("BoardArea", canvasRect);
            board = boardArea.gameObject.AddComponent<BoardView>();
            board.NodeClicked += OnNodeClicked;

            BuildPanel();
            BuildTitle();
            BuildHandoff();
            rulesScreen = new RulesScreen(canvasRect);
            presetPanel = new PresetPanel(canvasRect, new PlayerPrefsPresetStorage());
            info = new InfoScreens(canvasRect);
            BuildOnline();
            ApplyOrientation(true);
        }

        void BuildPanel()
        {
            var panel = Ui.Image("Panel", canvasRect, Theme.Panel);
            panelArea = panel.rectTransform;
            panelEdge = Ui.Image("Edge", panelArea, Theme.PanelLine).rectTransform;

            var content = Ui.Stretch(Ui.Rect("Content", panelArea), 40, 32, 40, 32);
            var v = content.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 14;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            // 見出し行：軍人将棋（ルール概要）＋相性表・ルールボタン
            var headerRow = Ui.Rect("HeaderRow", content);
            Ui.RowGroup(headerRow, 12);
            Ui.Size(headerRow, 56);
            headerText = Ui.Text("Header", headerRow, "軍人将棋", 30, Theme.TextMuted, bold: true);
            headerText.characterSpacing = 12;
            headerText.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.AutoSize(headerText, 30);
            Ui.Size(headerText, -1, 200, 1);
            infoButtons = Ui.Rect("InfoButtons", headerRow).gameObject;
            Ui.RowGroup(infoButtons.transform as RectTransform, 12);
            infoButtonsLayout = Ui.Size(infoButtons.transform as RectTransform, -1, 330, 0);
            var tableButton = Ui.Button("CombatTable", infoButtons.transform, "相性表", () => { if (rules != null) info.OpenTable(rules, currentOptions); }, fontSize: 26);
            Ui.Size(tableButton, -1, 160);
            var rulesButton = Ui.Button("RulesView", infoButtons.transform, "ルール", () => { if (rules != null) info.OpenRules(rules, currentOptions); }, fontSize: 26);
            Ui.Size(rulesButton, -1, 160);
            inviteButton = Ui.Button("Invite", infoButtons.transform, "招待", CopyInvite, Ui.ButtonStyle.Primary, fontSize: 26);
            Ui.Size(inviteButton, -1, 160);
            SetInviteVisible(false);
            teamsText = Row(Ui.Text("Teams", content, "", 28, Theme.Text), 40);
            statusText = Row(Ui.Text("Status", content, "", 64, Theme.Text, bold: true), 84);
            Ui.AutoSize(statusText, 64);
            statusText.textWrappingMode = TextWrappingModes.NoWrap;
            subText = Row(Ui.Text("Sub", content, "", 28, Theme.TextMuted), 72);
            Ui.AutoSize(subText, 28);

            var logImage = Ui.Image("LogFrame", content, Theme.Hex("20251F"));
            logFrame = logImage.gameObject;
            var le = logFrame.AddComponent<LayoutElement>();
            le.flexibleHeight = 1;
            le.minHeight = 0;
            // 棋譜はスクロールできるようにしておき、設定で「最新の数手だけ」にもできる
            var logContent = Ui.ScrollArea("LogScroll", logFrame.transform, out logScroll);
            Ui.Stretch((RectTransform)logContent.parent.parent, 20, 14, 12, 14);
            Ui.Column(logContent, 0);
            logText = Ui.Text("Log", logContent, "", 27, Theme.Text);
            logText.alignment = TextAlignmentOptions.TopLeft;
            logText.lineSpacing = 8;
            logText.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            BuildMemoPalette(content);

            setupRow = ButtonRow(content,
                Ui.Button("Random", null, "ランダム", () => RandomizeSetup()),
                Ui.Button("Presets", null, "保存・読込", OpenPresets),
                Ui.Button("Confirm", null, "この配置で決定", OnConfirmSetup, Ui.ButtonStyle.Primary));
            memoButton = Ui.Button("Memo", null, "メモ", ToggleMemo);
            playRow = ButtonRow(content,
                memoButton,
                Ui.Button("Resign", null, "投了", OnResign, Ui.ButtonStyle.Danger));
            resultRow = ButtonRow(content,
                Ui.Button("Again", null, "もう一度", OnAgain, Ui.ButtonStyle.Primary),
                Ui.Button("Replay", null, "感想戦", StartReplay),
                Ui.Button("Title", null, "タイトルへ", OnLeaveToTitle));
            replayRow = ButtonRow(content,
                Ui.Button("First", null, "|◀", () => SetReplayPly(0)),
                Ui.Button("Prev", null, "◀", () => SetReplayPly(replayPly - 1)),
                Ui.Button("Next", null, "▶", () => SetReplayPly(replayPly + 1)),
                Ui.Button("Last", null, "▶|", () => SetReplayPly(replayMoves.Count)),
                Ui.Button("EndReplay", null, "戻る", EndReplay, Ui.ButtonStyle.Primary));
        }

        void BuildMemoPalette(Transform content)
        {
            var frame = Ui.Image("MemoPalette", content, Theme.Hex("20251F"));
            memoPalette = frame.gameObject;
            var le = memoPalette.AddComponent<LayoutElement>();
            le.flexibleHeight = 1;
            le.minHeight = 0;
            var col = Ui.Stretch(Ui.Rect("Column", frame.transform), 16, 14, 16, 14);
            Ui.Column(col, 10);
            assistText = Ui.Text("Assist", col, "", 24, Theme.TextMuted);
            Ui.Size(assistText, 70);
            Ui.AutoSize(assistText, 24);
            for (int r = 0; r < 2; r++)
            {
                var row = Ui.Rect("Row" + r, col);
                var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                h.spacing = 10;
                h.childControlWidth = h.childControlHeight = true;
                h.childForceExpandWidth = h.childForceExpandHeight = true;
                var rle = row.gameObject.AddComponent<LayoutElement>();
                rle.flexibleHeight = 1;
                rle.minHeight = 50;
                for (int i = 0; i < 6; i++)
                {
                    string label = MemoLabels[r * 6 + i];
                    Ui.Button("Memo" + label, row, label, () => ApplyMemo(label), fontSize: 34);
                }
            }
            memoPalette.SetActive(false);
        }

        static TextMeshProUGUI Row(TextMeshProUGUI t, float height)
        {
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height * 0.6f;
            return t;
        }

        static GameObject ButtonRow(Transform parent, params Button[] buttons)
        {
            var row = Ui.Rect("Buttons", parent);
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 92;
            le.minHeight = 72;
            le.flexibleHeight = 0;
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 16;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = true;
            foreach (var b in buttons) b.transform.SetParent(row, false);
            row.gameObject.SetActive(false);
            return row.gameObject;
        }

        void BuildTitle()
        {
            var screen = Ui.Image("TitleScreen", canvasRect, Theme.Background, raycast: true);
            Ui.Stretch(screen.rectTransform);
            titleScreen = screen.gameObject;

            var col = Ui.Rect("Column", screen.transform);
            Ui.Anchors(col, 0.5f, 0.5f, 0.5f, 0.5f);
            col.sizeDelta = new Vector2(860, 1060);
            var v = col.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 18;
            v.childAlignment = TextAnchor.MiddleCenter;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            var title = Row(Ui.Text("Title", col, "軍人将棋", 124, Theme.Text, TextAlignmentOptions.Center, bold: true), 150);
            title.characterSpacing = 18;
            Row(Ui.Text("Lead", col, "裏向きの駒で戦う。勝ち負けは審判だけが知っている。", 30, Theme.TextMuted, TextAlignmentOptions.Center), 56);
            Spacer(col, 8);

            // CPUの強さ：3択のボタン＋説明文。対戦ボタンは1つ
            Row(Ui.Text("LevelTitle", col, "CPUの強さ", 26, Theme.TextMuted, TextAlignmentOptions.Center, bold: true), 34);
            var levelRow = Ui.Rect("LevelRow", col);
            var lh = levelRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            lh.spacing = 10;
            lh.childControlWidth = lh.childControlHeight = true;
            lh.childForceExpandWidth = lh.childForceExpandHeight = true;
            Ui.Size(levelRow, 64);
            levelButtons = new Button[3];
            for (int i = 0; i < 3; i++)
            {
                int lv = i;
                levelButtons[i] = Ui.Button("Level" + i, levelRow, "CPUの強さ：" + LevelName[i], () => { AppSettings.CpuLevel = lv; RefreshTitle(); }, fontSize: 28);
            }
            levelNote = Ui.Text("LevelNote", col, "", 24, Theme.TextMuted, TextAlignmentOptions.Center);
            Ui.Size(levelNote, 64);
            Ui.AutoSize(levelNote, 24);
            var cpuButton = Ui.Button("PlayCpu", col, "CPUと対戦", () => StartMatch(SeatKind.Human, SeatKind.Cpu, (CpuLevel)AppSettings.CpuLevel), Ui.ButtonStyle.Secondary, 36);
            Ui.Size(cpuButton, 88);
            cpuStartButton = cpuButton;
            Spacer(col, 6);
            MenuButton(col, "ふたりで対戦（1台を交互に）", () => StartMatch(SeatKind.Human, SeatKind.Human, CpuLevel.Normal), false);
            MenuButton(col, "オンライン対戦", OpenLobby, false);
            Spacer(col, 6);
            MenuButton(col, "設定・ルール", () => rulesScreen.Open(RefreshTitle), false);
            Spacer(col, 10);
            titleRulesText = Row(Ui.Text("Rules", col, "", 24, Theme.TextMuted, TextAlignmentOptions.Center), 40);
        }

void RefreshTitle()
        {
            var o = AppSettings.Rules;
            titleRulesText.text = $"23枚型　／　ルール：{RulesScreen.Summary(o)}（コード {RuleCodec.Encode(o)}）";
            int lv = AppSettings.CpuLevel;
            for (int i = 0; i < levelButtons.Length; i++)
            {
                Ui.SetSelected(levelButtons[i], i == lv);
                Ui.SetLabel(levelButtons[i], LevelName[i]);
            }
            levelNote.text = $"<color=#E2B23C>{LevelName[lv]}</color>　{LevelNotes[lv]}";
            Ui.SetLabel(cpuStartButton, $"CPUと対戦（{LevelName[lv]}）");
        }

        static void Spacer(Transform parent, float h)
        {
            var le = Ui.Rect("Spacer", parent).gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = h;
        }

        static void MenuButton(Transform parent, string label, System.Action onClick, bool primary)
        {
            var b = Ui.Button(label, parent, label, onClick, primary ? Ui.ButtonStyle.Primary : Ui.ButtonStyle.Secondary, 36);
            var le = b.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 88;
        }

        void BuildHandoff()
        {
            var screen = Ui.Image("Handoff", canvasRect, Theme.Background, raycast: true);
            Ui.Stretch(screen.rectTransform);
            handoffScreen = screen.gameObject;
            var col = Ui.Rect("Column", screen.transform);
            Ui.Anchors(col, 0.5f, 0.5f, 0.5f, 0.5f);
            col.sizeDelta = new Vector2(820, 520);
            var v = col.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 20;
            v.childAlignment = TextAnchor.MiddleCenter;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            handoffTitle = Row(Ui.Text("Title", col, "", 88, Theme.Text, TextAlignmentOptions.Center, bold: true), 130);
            handoffBody = Row(Ui.Text("Body", col, "", 30, Theme.TextMuted, TextAlignmentOptions.Center), 120);
            handoffBody.lineSpacing = 30;
            MenuButton(col, "準備できた", () => handoffAccepted = true, true);
            handoffScreen.SetActive(false);
        }

        // ───────── 縦横の切り替え ─────────

        void LateUpdate()
        {
            ApplyOrientation(false);
        }

        void ApplyOrientation(bool force)
        {
            bool landscape = Screen.width >= Screen.height;
            if (!force && lastLandscape == landscape) return;
            lastLandscape = landscape;
            if (landscape)
            {
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 1;
                Ui.Anchors(boardArea, 0, 0, 0.64f, 1);
                boardArea.offsetMin = new Vector2(24, 24);
                boardArea.offsetMax = new Vector2(-24, -24);
                Ui.Anchors(panelArea, 0.64f, 0, 1, 1);
                Ui.Anchors(panelEdge, 0, 0, 0, 1);
                panelEdge.offsetMax = new Vector2(3, 0);
            }
            else
            {
                scaler.referenceResolution = new Vector2(1080, 1920);
                scaler.matchWidthOrHeight = 0;
                Ui.Anchors(boardArea, 0, 0.34f, 1, 1);
                boardArea.offsetMin = new Vector2(16, 8);
                boardArea.offsetMax = new Vector2(-16, -16);
                Ui.Anchors(panelArea, 0, 0, 1, 0.34f);
                Ui.Anchors(panelEdge, 0, 1, 1, 1);
                panelEdge.offsetMin = new Vector2(0, -3);
            }
            rulesScreen.ApplyOrientation(landscape);
            presetPanel.ApplyOrientation(landscape);
            info.ApplyOrientation(landscape);
            ApplyOnlineOrientation(landscape);
        }

        // ───────── 対局の進行 ─────────

        void ShowTitle()
        {
            if (matchRoutine != null) StopCoroutine(matchRoutine);
            matchRoutine = null;
            inputMode = InputMode.None;
            presetPanel.Close();
            info.CloseAll();
            titleScreen.SetActive(true);
            handoffScreen.SetActive(false);
            SetInviteVisible(false);
            RefreshTitle();
        }

        void StartMatch(SeatKind s0, SeatKind s1, CpuLevel cpuLevel)
        {
            if (matchRoutine != null) StopCoroutine(matchRoutine);
            seats[0] = s0;
            seats[1] = s1;
            level = cpuLevel;
            titleScreen.SetActive(false);
            matchRoutine = StartCoroutine(Match());
        }

        IEnumerator Match()
        {
            var options = AppSettings.Rules;
            currentOptions = options;
            display = AppSettings.Display;
            cpuResignReason = null;
            rules = StandardRules.Create23(options);
            state = new GameState(rules);
            topo = state.Topology;
            online = false;
            int seed = System.Environment.TickCount;
            for (int p = 0; p < 2; p++)
            {
                cpus[p] = seats[p] == SeatKind.Cpu ? new CpuPlayer(rules, p, seed + p, CpuSettings.For(level)) : null;
                beliefs[p] = null;
                submittedSetups[p] = null;
                memos[p].Clear();
            }
            viewer = seats[0] == SeatKind.Human ? 0 : 1;
            board.Init(rules, viewer);
            ClearMarks();
            SetMemoMode(false);
            teamsText.text = TeamsLine();
            headerText.text = $"軍人将棋　<size=70%>{RulesScreen.Summary(options)}</size>";
            logText.text = "";

            for (int p = 0; p < 2; p++)
            {
                if (seats[p] == SeatKind.Cpu)
                {
                    submittedSetups[p] = cpus[p].ChooseSetup();
                    state.SubmitSetup(p, submittedSetups[p]);
                    continue;
                }
                if (Hotseat && p == 1) yield return Handoff(p, "配置をします");
                yield return HumanSetup(p);
            }

            while (state.Phase == GamePhase.Playing)
            {
                int p = state.CurrentPlayer;
                if (seats[p] == SeatKind.Human)
                {
                    if (Hotseat && viewer != p) yield return Handoff(p, "あなたの番です");
                    yield return HumanMove(p);
                }
                else
                {
                    yield return CpuMove(p);
                }

                if (state.History.Count > 0 && !resignRequested)
                {
                    var last = PlayerView.From(state, viewer).History[state.History.Count - 1];
                    RenderPlay();
                    var verdict = MoveLog.VerdictFor(last, viewer);
                    if (verdict != MoveLog.Verdict.None)
                    {
                        board.ShowStamp(last.ToNode, ToStamp(verdict));
                        yield return new WaitForSecondsRealtime(Hotseat ? 1.3f : 0.9f);
                    }
                    else if (Hotseat && state.Phase == GamePhase.Playing)
                    {
                        yield return new WaitForSecondsRealtime(0.5f);
                    }
                }
                resignRequested = false;
            }
            ShowResult();
        }

        static BoardView.StampKind ToStamp(MoveLog.Verdict v) =>
            v == MoveLog.Verdict.Win ? BoardView.StampKind.Win
            : v == MoveLog.Verdict.Lose ? BoardView.StampKind.Lose : BoardView.StampKind.Both;

        string TeamsLine()
        {
            string Seat(int p)
            {
                if (seats[p] == SeatKind.Cpu) return $"CPU（{LevelName[(int)level]}）";
                return Hotseat ? (p == 0 ? "先手" : "後手") : "あなた";
            }
            return $"<color=#{ColorUtility.ToHtmlStringRGB(Color.Lerp(Theme.Team[0], Color.white, 0.35f))}>■ 朱</color> {Seat(0)}　　" +
                   $"<color=#{ColorUtility.ToHtmlStringRGB(Color.Lerp(Theme.Team[1], Color.white, 0.45f))}>■ 藍</color> {Seat(1)}";
        }

        IEnumerator Handoff(int player, string body)
        {
            inputMode = InputMode.None;
            SetMemoMode(false);
            handoffAccepted = false;
            handoffTitle.text = $"{Theme.TeamName[player]}の番";
            handoffTitle.color = Color.Lerp(Theme.Team[player], Color.white, player == 0 ? 0.3f : 0.45f);
            handoffBody.text = $"{body}。\n相手に画面が見えないように渡してから押してください。";
            handoffScreen.SetActive(true);
            handoffScreen.transform.SetAsLastSibling();
            while (!handoffAccepted) yield return null;
            handoffScreen.SetActive(false);
            viewer = player;
            board.SetViewer(player);
            if (state.Phase == GamePhase.Playing) RenderPlay();
        }

        // ───── 配置 ─────

        IEnumerator HumanSetup(int player)
        {
            setupPlayer = player;
            viewer = player;
            board.SetViewer(player);
            // 初期配置は CPU と同じ考え方（総司令部まわりを固める）で作っておく
            setupPlacements = new CpuPlayer(rules, player, System.Environment.TickCount + player).ChooseSetup();
            ShowRow(setupRow);
            statusText.text = "駒を配置する";
            subText.text = HintSetup;
            logText.text = "";
            setupConfirmed = false;
            inputMode = InputMode.Setup;
            selectedNode = -1;
            RenderSetup();

            while (true)
            {
                while (!setupConfirmed) yield return null;
                setupConfirmed = false;
                var copy = new List<Placement>(setupPlacements);
                var errors = state.SubmitSetup(player, copy);
                if (errors.Count == 0) { submittedSetups[player] = copy; break; }
                subText.text = $"<color=#E0705A>{errors[0]}</color>";
            }
            presetPanel.Close();
            inputMode = InputMode.None;
            selectedNode = -1;
        }

        void RandomizeSetup()
        {
            if (inputMode != InputMode.Setup) return;
            setupPlacements = RandomSetup.Generate(rules, topo, setupPlayer, new System.Random());
            selectedNode = -1;
            subText.text = HintSetup;
            RenderSetup();
        }

        void OpenPresets()
        {
            if (inputMode != InputMode.Setup) return;
            presetPanel.Open(rules, topo, setupPlayer,
                () => setupPlacements,
                p =>
                {
                    setupPlacements = p;
                    selectedNode = -1;
                    RenderSetup();
                });
        }

        void RenderSetup()
        {
            var list = new List<PieceDisplay>();
            for (int i = 0; i < setupPlacements.Count; i++)
                list.Add(new PieceDisplay { Id = i, Owner = setupPlayer, Node = setupPlacements[i].Node, TypeId = setupPlacements[i].TypeId });
            ClearMarks();
            marks.SelectedNode = selectedNode;
            for (int n = 0; n < topo.NodeCount; n++)
                if (topo.CampOf(n) != setupPlayer) marks.Dimmed.Add(n);
            board.Render(list, marks);
        }

        void SetupClick(int node)
        {
            if (topo.CampOf(node) != setupPlayer) return;
            if (selectedNode < 0) { selectedNode = node; RenderSetup(); return; }
            if (selectedNode == node) { selectedNode = -1; RenderSetup(); return; }

            int a = setupPlacements.FindIndex(p => p.Node == selectedNode);
            int b = setupPlacements.FindIndex(p => p.Node == node);
            var swapped = new List<Placement>(setupPlacements);
            if (a >= 0) swapped[a] = new Placement(swapped[a].TypeId, node);
            if (b >= 0) swapped[b] = new Placement(swapped[b].TypeId, selectedNode);
            var errors = SetupValidator.Validate(rules, topo, setupPlayer, swapped);
            if (errors.Count == 0)
            {
                setupPlacements = swapped;
                subText.text = HintSetup;
            }
            else
            {
                subText.text = $"<color=#E0705A>{errors[0]}</color>";
            }
            selectedNode = -1;
            RenderSetup();
        }

        // ───── 対局 ─────

        IEnumerator HumanMove(int player)
        {
            ShowRow(playRow);
            memoButton.interactable = true;
            memoButton.gameObject.SetActive(display.MemoEnabled);
            statusText.text = Hotseat ? $"{Theme.TeamName[player]}の番" : "あなたの番";
            subText.text = memoMode ? HintMemo : HintMove;
            pendingMove = null;
            resignRequested = false;
            selectedNode = -1;
            inputMode = InputMode.Move;
            RenderPlay();
            while (pendingMove == null && !resignRequested) yield return null;
            inputMode = InputMode.None;
            selectedNode = -1;
            if (resignRequested) { state.Resign(player); yield break; }
            state.ApplyMove(pendingMove.Value);
        }

        IEnumerator CpuMove(int player)
        {
            ShowRow(playRow);
            memoButton.gameObject.SetActive(display.MemoEnabled);
            statusText.text = "CPUが考えています";
            if (!memoMode) subText.text = "";
            RenderPlay();
            float start = Time.realtimeSinceStartup;
            var thinking = cpus[player].BeginThink(PlayerView.From(state, player));
            while (!thinking.Step(8)) yield return null;
            if (thinking.Resign)
            {
                // 勝ち目がないと判断した（占領できる駒がない・詰み）
                yield return new WaitForSecondsRealtime(0.6f);
                cpuResignReason = thinking.ResignReason;
                state.Resign(player);
                yield break;
            }
            while (Time.realtimeSinceStartup - start < 0.6f)
            {
                if (resignRequested) break;
                yield return null;
            }
            if (resignRequested) { state.Resign(1 - player); yield break; }
            state.ApplyMove(thinking.Result);
        }

void MoveClick(int node)
        {
            var ms = MoveState();
            int occ = ms.OccupantOf(node);
            if (memoMode)
            {
                bool enemyHidden = occ >= 0 && ms.Pieces[occ].Owner != viewer;
                memoTarget = enemyHidden ? occ : -1;
                RefreshMemoPalette();
                RenderPlay();
                return;
            }

            // 相手の手番中はメモ以外の操作を受け付けない
            if (inputMode != InputMode.Move) return;
            bool ownPiece = occ >= 0 && ms.Pieces[occ].Owner == viewer;
            if (selectedNode >= 0 && (marks.MoveTargets.Contains(node) || marks.AttackTargets.Contains(node)))
            {
                var move = new Move(ms.OccupantOf(selectedNode), node);
                if (online) SendOnlineMove(move);
                else pendingMove = move;
                return;
            }
            if (ownPiece && node != selectedNode)
            {
                selectedNode = node;
                var moves = MoveGenerator.ForPiece(ms, ms.Pieces[occ]);
                subText.text = moves.Count == 0 ? "この駒は動かせません。" : "移動先を選んでください。";
            }
            else
            {
                selectedNode = -1;
                subText.text = HintMove;
            }
            RenderPlay();
        }

        /// <summary>今の対局の見え方。ローカルは手元の GameState から、オンラインはサーバーから届いたもの。</summary>
        PlayerView CurrentView() =>
            online ? onlineView : PlayerView.From(state, viewer, revealAll: state.Phase == GamePhase.Finished);

        /// <summary>合法手・駒の位置を調べるための局面。</summary>
        GameState MoveState() => online ? onlineMoveState : state;

void RenderPlay()
        {
            var view = CurrentView();
            if (view == null) return;
            var ms = MoveState();
            var list = new List<PieceDisplay>(view.Pieces.Count);
            var myMemos = memos[viewer];
            foreach (var p in view.Pieces)
            {
                myMemos.TryGetValue(p.Id, out var memo);
                list.Add(new PieceDisplay { Id = p.Id, Owner = p.Owner, Node = p.Node, TypeId = p.TypeId, HasMoved = p.HasMoved, Memo = memo });
            }

            ClearMarks();
            marks.SelectedNode = selectedNode;
            if (memoMode && memoTarget >= 0) marks.SelectedNode = view.Pieces[memoTarget].Node;
            if (selectedNode >= 0 && inputMode == InputMode.Move && !memoMode)
            {
                int occ = ms.OccupantOf(selectedNode);
                if (occ >= 0)
                    foreach (var m in MoveGenerator.ForPiece(ms, ms.Pieces[occ]))
                    {
                        if (ms.OccupantOf(m.ToNode) >= 0) marks.AttackTargets.Add(m.ToNode);
                        else marks.MoveTargets.Add(m.ToNode);
                    }
            }
            if (view.History.Count > 0 && display.HighlightLastMove)
            {
                var last = view.History[view.History.Count - 1];
                marks.LastFrom = last.FromNode;
                marks.LastTo = last.ToNode;
            }
            board.Render(list, marks);
            logText.text = BuildLog(view, view.History.Count);
        }

/// <summary>新しい手が上。設定で「すべて」なら全手を出してスクロール、そうでなければ最新14手。</summary>
        string BuildLog(PlayerView view, int upTo)
        {
            int limit = display.ScrollableLog ? int.MaxValue : 14;
            var sb = new StringBuilder();
            for (int i = upTo - 1, n = 0; i >= 0 && n < limit; i--, n++)
                sb.AppendLine(MoveLog.Describe(view.History[i], view, rules));
            logScroll.vertical = display.ScrollableLog;
            logScroll.verticalNormalizedPosition = 1;
            return sb.ToString();
        }

        void ShowResult()
        {
            Ui.SetLabel(againButton, "もう一度");
            againButton.interactable = true;
            inputMode = InputMode.None;
            selectedNode = -1;
            SetMemoMode(false);
            ShowRow(resultRow);
            RenderPlay();
            string reason = MoveLog.Reason(state.EndReason);
            if (state.Result == GameResult.Draw)
            {
                statusText.text = "引き分け";
            }
            else
            {
                int winner = state.Result == GameResult.Player0Win ? 0 : 1;
                if (Hotseat) statusText.text = $"{Theme.TeamName[winner]}の勝ち";
                else statusText.text = seats[winner] == SeatKind.Human ? "勝利" : "敗北";
            }
            if (state.EndReason == EndReason.Resign && cpuResignReason != null) reason = $"CPUが投了（{cpuResignReason}）";
            subText.text = $"{reason}（{state.History.Count}手）。全ての駒を表にしています。";
        }

        // ───── メモと推理アシスト ─────

void ToggleMemo()
        {
            var v = CurrentView();
            if (v == null || v.Phase != GamePhase.Playing) return;
            SetMemoMode(!memoMode);
            selectedNode = -1;
            if (memoMode) subText.text = HintMemo;
            else if (inputMode == InputMode.Move) subText.text = HintMove;
            RenderPlay();
        }

        void SetMemoMode(bool on)
        {
            memoMode = on;
            memoTarget = -1;
            Ui.SetLabel(memoButton, on ? "メモを終える" : "メモ");
            RefreshMemoPalette();
        }

void RefreshMemoPalette()
        {
            bool show = memoMode && memoTarget >= 0;
            memoPalette.SetActive(show);
            logFrame.SetActive(!show);
            if (!show) return;

            if (!display.Assist)
            {
                assistText.text = "印を選んでください。推理アシストは設定でオンにできます。";
                return;
            }
            if (beliefs[viewer] == null) beliefs[viewer] = new Belief(rules, topo, viewer);
            var belief = beliefs[viewer];
            belief.Update(online ? onlineView : PlayerView.From(state, viewer));
            var names = new List<string>();
            for (int t = 0; t < rules.Pieces.Count; t++)
                if (belief.IsCandidate(memoTarget, t)) names.Add(rules.Piece(t).Name);
            assistText.text = $"<color=#E2B23C>推理</color>　この駒は {string.Join("・", names)}（{names.Count}種）";
        }

        void ApplyMemo(string label)
        {
            if (memoTarget < 0) return;
            if (label == "消す") memos[viewer].Remove(memoTarget);
            else memos[viewer][memoTarget] = label;
            memoTarget = -1;
            RefreshMemoPalette();
            RenderPlay();
        }

        // ───── 感想戦 ─────

void StartReplay()
        {
            if (!online)
            {
                if (state == null || state.Phase != GamePhase.Finished) return;
                replaySetups[0] = submittedSetups[0];
                replaySetups[1] = submittedSetups[1];
                replayMoves.Clear();
                foreach (var r in state.History) replayMoves.Add(r.Move);
                replayFinalView = PlayerView.From(state, viewer, revealAll: true);
            }
            if (replaySetups[0] == null || replaySetups[1] == null || replayFinalView == null) return;
            inputMode = InputMode.Replay;
            ShowRow(replayRow);
            replayPly = 0;
            SetReplayPly(0);
        }

void EndReplay()
        {
            inputMode = InputMode.None;
            if (online) ShowOnlineResult();
            else ShowResult();
        }

        /// <summary>配置と棋譜を最初から k 手目まで再生した局面。</summary>
GameState StateAt(int k)
        {
            var s = new GameState(rules);
            s.SubmitSetup(0, replaySetups[0]);
            s.SubmitSetup(1, replaySetups[1]);
            for (int i = 0; i < k && s.Phase == GamePhase.Playing; i++) s.ApplyMove(replayMoves[i]);
            return s;
        }

        void SetReplayPly(int k)
        {
            int total = replayMoves.Count;
            k = Mathf.Clamp(k, 0, total);
            bool forwardOne = k == replayPly + 1;
            board.ClearStamps();
            replayPly = k;
            var s = StateAt(k);

            var list = new List<PieceDisplay>(s.Pieces.Count);
            foreach (var p in s.Pieces)
                list.Add(new PieceDisplay { Id = p.Id, Owner = p.Owner, Node = p.Node, TypeId = p.TypeId, HasMoved = p.HasMoved });
            ClearMarks();
            var finalView = replayFinalView;
            if (k > 0)
            {
                var h = finalView.History[k - 1];
                marks.LastFrom = h.FromNode;
                marks.LastTo = h.ToNode;
                if (forwardOne)
                {
                    var verdict = MoveLog.VerdictFor(h, viewer);
                    if (verdict != MoveLog.Verdict.None) board.ShowStamp(h.ToNode, ToStamp(verdict));
                }
            }
            board.Render(list, marks);

            statusText.text = "感想戦";
            subText.text = k == 0
                ? $"初期配置（0 / {total}手）"
                : $"{k} / {total}手　{MoveLog.Describe(finalView.History[k - 1], finalView, rules)}";
            logText.text = BuildLog(finalView, k);
        }

        // ───────── 入力 ─────────

        void OnNodeClicked(int node)
        {
            if (inputMode == InputMode.Setup) SetupClick(node);
            else if (memoMode || inputMode == InputMode.Move) MoveClick(node);
        }

        void ClearMarks()
        {
            marks.SelectedNode = -1;
            marks.MoveTargets.Clear();
            marks.AttackTargets.Clear();
            marks.Dimmed.Clear();
            marks.LastFrom = marks.LastTo = -1;
        }

        void ShowRow(GameObject row)
        {
            setupRow.SetActive(row == setupRow);
            playRow.SetActive(row == playRow);
            resultRow.SetActive(row == resultRow);
            replayRow.SetActive(row == replayRow);
            if (waitRow != null) waitRow.SetActive(row == waitRow);
            if (row != playRow && claimWinButton != null) claimWinButton.gameObject.SetActive(false);
        }
    }
}
