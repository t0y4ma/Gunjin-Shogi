using GunjinShogi.UnityView;
using UnityEditor;
using UnityEngine;

namespace GunjinShogi.EditorTools
{
    /// <summary>
    /// エディタでオンライン対戦を試すための操作盤。メニュー「軍人将棋/オンライン デバッグ」。
    /// 再生中は模擬サーバー（EditorLoopback）の部屋・通信量・通信記録を表示し、
    /// 相手役の追加・切断・再接続、自分の通信断、サーバー停止、時間を進める、などを起こせる。
    /// </summary>
    public sealed class OnlineDebugWindow : EditorWindow
    {
        Vector2 scroll, logScroll;
        string botRuleCode = "0";
        double nextRepaint;

        [MenuItem("軍人将棋/オンライン デバッグ")]
        static void Open() => GetWindow<OnlineDebugWindow>("オンライン デバッグ");

        void Update()
        {
            if (!Application.isPlaying || EditorApplication.timeSinceStartup < nextRepaint) return;
            nextRepaint = EditorApplication.timeSinceStartup + 0.25;
            Repaint();
        }

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.LabelField("接続先", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                bool use = EditorGUILayout.ToggleLeft("エディタ内の模擬サーバーを使う（オフなら Mirror で GunjinNetworkManager の接続先へ）", EditorLoopback.Enabled);
                if (use != EditorLoopback.Enabled) EditorLoopback.Enabled = use;
            }
            if (Application.isPlaying) EditorGUILayout.HelpBox("接続先の切り替えは、再生を止めてから行ってください。", MessageType.None);
            EditorLoopback.LatencyMs = EditorGUILayout.IntSlider("片道の遅延 (ms)", EditorLoopback.LatencyMs, 0, 1500);
            EditorLoopback.DefaultBotMode = (TestBotMode)EditorGUILayout.EnumPopup("相手役の動き（ロビーのボタン）", EditorLoopback.DefaultBotMode);

            if (!Application.isPlaying)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(
                    "再生して「オンライン対戦」を開くと、模擬サーバーに接続します。\n" +
                    "・「（エディタ）部屋を作り、相手役ボットを入れる」で、すぐ対局を始められます。\n" +
                    "・「部屋を作る」だけなら、ここから好きなタイミングで相手役を入れられます。\n" +
                    "サーバーの処理（RoomService）と通信の形式（Packet）は本番と同じものを使います。",
                    MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            if (!EditorLoopback.Exists)
            {
                EditorGUILayout.HelpBox(EditorLoopback.Enabled ? "まだ模擬サーバーは起動していません（「オンライン対戦」を開くと起動します）。" : "Mirror で接続中のため、この操作盤は使いません。", MessageType.None);
                EditorGUILayout.EndScrollView();
                return;
            }

            var lb = EditorLoopback.Instance;

            // ───── サーバー ─────
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"サーバー：{(lb.ServerUp ? "稼働中" : "停止中")}（起動 {lb.ServerStarts} 回）", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (lb.ServerUp ? GUILayout.Button("サーバーを止める") : GUILayout.Button("サーバーを起動")) { if (lb.ServerUp) lb.StopServer(); else lb.StartServer(); }
                if (GUILayout.Button("再起動（部屋が消える）")) lb.RestartServer();
                if (GUILayout.Button("時間を +60秒")) lb.AdvanceTime(60);
            }
            var rooms = lb.DescribeRooms();
            if (rooms.Count == 0) EditorGUILayout.LabelField("部屋はありません");
            foreach (var r in rooms) EditorGUILayout.HelpBox(r, MessageType.None);

            // ───── あなた ─────
            EditorGUILayout.Space();
            var c = lb.Client;
            string state = c.IsClientConnected ? $"接続中 #{c.Conn}" : c.IsConnecting ? "接続中…" : "未接続";
            EditorGUILayout.LabelField($"あなた：{state}　部屋 {(string.IsNullOrEmpty(lb.PlayerRoom) ? "-" : lb.PlayerRoom)}", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!c.IsClientConnected))
                    if (GUILayout.Button("通信を突然切る（再接続を試す）")) c.Drop();
            }

            // ───── 相手役 ─────
            EditorGUILayout.Space();
            var bot = lb.Bot;
            string botState = bot == null ? "いない" : $"{bot.Mode}・部屋 {lb.BotRoom}・{(lb.BotConn >= 0 ? "接続中" : "切断中")}・席 {(bot.Seat < 0 ? "-" : bot.Seat == 0 ? "先手" : "後手")}";
            EditorGUILayout.LabelField($"相手役：{botState}", EditorStyles.boldLabel);
            if (bot != null && !string.IsNullOrEmpty(bot.LastAction)) EditorGUILayout.LabelField("　最後の動作：" + bot.LastAction);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(lb.PlayerRoom) || !lb.ServerUp))
                    if (GUILayout.Button(bot == null ? "あなたの部屋に入れる" : "入れ直す")) lb.AddBot(lb.PlayerRoom, EditorLoopback.DefaultBotMode);
                using (new EditorGUI.DisabledScope(bot == null))
                    if (GUILayout.Button("退出させる")) lb.RemoveBot();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                botRuleCode = EditorGUILayout.TextField("相手役が作る部屋のルール", botRuleCode);
                using (new EditorGUI.DisabledScope(!lb.ServerUp))
                    if (GUILayout.Button("相手役に部屋を作らせる", GUILayout.Width(160))) lb.BotCreateRoom(EditorLoopback.DefaultBotMode, botRuleCode);
            }
            if (bot != null && !string.IsNullOrEmpty(lb.BotRoom) && lb.BotRoom != lb.PlayerRoom)
                EditorGUILayout.HelpBox($"相手役の部屋は {lb.BotRoom} です。ゲームの「部屋に入る」にこの番号を入れてください。", MessageType.Info);
            using (new EditorGUI.DisabledScope(bot == null))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(lb.BotConn < 0))
                        if (GUILayout.Button("通信を切る")) lb.DisconnectBot();
                    using (new EditorGUI.DisabledScope(lb.BotConn >= 0))
                        if (GUILayout.Button("再接続")) lb.ReconnectBot();
                    if (GUILayout.Button("投了")) lb.BotResign();
                    if (GUILayout.Button("再戦を申し込む")) lb.BotRematch();
                }
                if (bot != null)
                {
                    bot.Mode = (TestBotMode)EditorGUILayout.EnumPopup("動き（途中で変更可）", bot.Mode);
                    bot.MoveDelay = EditorGUILayout.Slider("指すまでの待ち (秒)", bot.MoveDelay, 0, 5);
                    bot.AcceptRematch = EditorGUILayout.Toggle("再戦に自動で応じる", bot.AcceptRematch);
                }
            }

            // ───── 通信量と記録 ─────
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("あなたの通信量（本番の Mirror の枠組み分は含まない）", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"　送信 {lb.PacketsUp} 件 / {lb.BytesUp} B　　受信 {lb.PacketsDown} 件 / {lb.BytesDown} B");
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("通信の記録（新しい順）", EditorStyles.boldLabel);
                if (GUILayout.Button("消去", GUILayout.Width(60))) lb.ResetCounters();
            }
            logScroll = EditorGUILayout.BeginScrollView(logScroll, GUILayout.MinHeight(200));
            for (int i = lb.Log.Count - 1; i >= 0; i--) EditorGUILayout.LabelField(lb.Log[i], EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndScrollView();
        }
    }
}
