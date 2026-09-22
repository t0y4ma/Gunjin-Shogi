using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GunjinShogi.EditorTools
{
    /// <summary>
    /// 配布用ビルド。メニュー「軍人将棋/ビルド」から実行する。
    /// ・サーバー：Linux 専用サーバー（.x86_64、Dedicated Server サブターゲット、IL2CPP）→ Builds/Server と転送用の Builds/GunjinShogiServer.zip
    /// ・クライアント：WebGL → Builds/WebGL（リポジトリで追跡し、Cloudflare Pages が push のたびに公開する）
    /// 配置の手順は Deploy/README.md。サーバーとクライアントは同じ版を一緒に配置する。
    /// </summary>
    public static class GunjinBuild
    {
        const string ServerDir = "Builds/Server";
        const string ServerExe = "GunjinShogiServer.x86_64";
        const string ServerZip = "Builds/GunjinShogiServer.zip";
        const string WebGLDir = "Builds/WebGL";
        const string ResultFile = "Library/GunjinBuild/last-result.txt";

        static string[] Scenes => EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

        [MenuItem("軍人将棋/ビルド/サーバー（Linux .x86_64）")]
        public static void BuildServerMenu() => Run(server: true, web: false);

        [MenuItem("軍人将棋/ビルド/クライアント（WebGL）")]
        public static void BuildWebGLMenu() => Run(server: false, web: true);

        [MenuItem("軍人将棋/ビルド/両方（サーバー＋WebGL）")]
        public static void BuildBothMenu() => Run(server: true, web: true);

        /// <summary>ビルドして、最後に作業ターゲットを元に戻す。結果は Library/GunjinBuild/last-result.txt にも書く。</summary>
        public static bool Run(bool server, bool web)
        {
            // Linux の IL2CPP ビルドの前に作業ターゲットを Linux にしておくことがあるので、終わったら Windows に戻す
            var restoreTarget = BuildTarget.StandaloneWindows64;
            var log = new System.Text.StringBuilder();
            bool ok = true;
            try
            {
                if (server) ok &= BuildServer(log);
                if (web && ok) ok &= BuildWebGL(log);
            }
            catch (Exception e)
            {
                ok = false;
                log.AppendLine("例外: " + e);
            }
            finally
            {
                // Server のままだとエディタ内でも Mirror がヘッドレス扱いになり、再生すると勝手にサーバーが立つ
                EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
                if (EditorUserBuildSettings.activeBuildTarget != restoreTarget)
                    EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, restoreTarget);
            }
            log.Insert(0, (ok ? "成功" : "失敗") + $"  {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
            Directory.CreateDirectory(Path.GetDirectoryName(ResultFile));
            File.WriteAllText(ResultFile, log.ToString());
            Debug.Log("[GunjinBuild] " + log);
            return ok;
        }

        static bool BuildServer(System.Text.StringBuilder log)
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64))
            {
                log.AppendLine("サーバー: Linux のビルドモジュールがありません（Unity Hub で Linux Dedicated Server Build Support を追加）");
                return false;
            }
            // Unity 6 の Linux サーバーは Mono だと起動後に落ちることがあるため IL2CPP にする（Nine の運用で確認済み）
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Server, ScriptingImplementation.IL2CPP);
            if (Directory.Exists(ServerDir)) Directory.Delete(ServerDir, true);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = Path.Combine(ServerDir, ServerExe),
                target = BuildTarget.StandaloneLinux64,
                subtarget = (int)StandaloneBuildSubtarget.Server,
                options = BuildOptions.None,
            });
            if (!Report("サーバー", report, log)) return false;

            // VM へ送る zip（配布しないデバッグ用フォルダは除く）
            if (File.Exists(ServerZip)) File.Delete(ServerZip);
            using (var zip = ZipFile.Open(ServerZip, ZipArchiveMode.Create))
            {
                foreach (var file in Directory.GetFiles(ServerDir, "*", SearchOption.AllDirectories))
                {
                    var rel = file.Substring(ServerDir.Length + 1).Replace('\\', '/');
                    if (rel.Contains("_DoNotShip") || rel.Contains("_BackUpThisFolder_ButDontShipItWithYourGame")) continue;
                    zip.CreateEntryFromFile(file, rel, System.IO.Compression.CompressionLevel.Optimal);
                }
            }
            log.AppendLine($"  転送用 zip: {ServerZip}（{new FileInfo(ServerZip).Length / (1024f * 1024f):0.0} MB）");
            return true;
        }

        static bool BuildWebGL(System.Text.StringBuilder log)
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
            {
                log.AppendLine("WebGL: ビルドモジュールがありません（Unity Hub で Web Build Support を追加）");
                return false;
            }
            // どの Web サーバーでもそのまま動くよう、圧縮ファイルはブラウザ側で展開できる形にする
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.runInBackground = true;
            if (Directory.Exists(WebGLDir)) Directory.Delete(WebGLDir, true);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = WebGLDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            });
            return Report("WebGL", report, log);
        }

        static bool Report(string label, BuildReport report, System.Text.StringBuilder log)
        {
            var s = report.summary;
            if (s.result == BuildResult.Succeeded)
            {
                log.AppendLine($"{label}: 成功 {s.outputPath}（{s.totalSize / (1024f * 1024f):0.0} MB, {s.totalTime.TotalSeconds:0}秒）");
                return true;
            }
            log.AppendLine($"{label}: 失敗 {s.result}（エラー {s.totalErrors} 件）");
            foreach (var step in report.steps)
                foreach (var m in step.messages)
                    if (m.type == LogType.Error || m.type == LogType.Exception) log.AppendLine("  " + m.content);
            return false;
        }
    }
}
