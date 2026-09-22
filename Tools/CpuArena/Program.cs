using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GunjinShogi.Core;
using GunjinShogi.Core.Ai;

// CPU 同士を並列で対局させて勝率を測る。
// 使い方: dotnet run -c Release -- <games> <A> <B> [<A> <B> ...]
//   設定の書き方: Hard / Normal / Easy のあとに ,名前=値 で CpuSettings を上書き（例 Hard,Samples=200,Depth=2）
//   先頭に time を付けると、1スレッドで1手の時間を測る: dotnet run -c Release -- time 4 Hard
static class Program
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args.Length >= 3 && args[0] == "time") return Time(int.Parse(args[1]), args[2]);
        int games = int.Parse(args[0]);
        int threads = Math.Max(1, Environment.ProcessorCount * 35 / 100) // PC 全体の CPU 使用率を 4 割程度に抑える;
        for (int i = 1; i + 1 < args.Length; i += 2) Match(args[i], args[i + 1], games, threads);
        return 0;
    }

    static CpuSettings Parse(string spec)
    {
        var parts = spec.Split(',');
        var s = CpuSettings.For((CpuLevel)Enum.Parse(typeof(CpuLevel), parts[0], true));
        s.TimeBudgetMs = 1_000_000; // 並列だと時間が伸びるので、推測の数だけで強さを決める
        foreach (var kv in parts.Skip(1))
        {
            var p = kv.Split('=');
            var f = typeof(CpuSettings).GetField(p[0]) ?? throw new Exception("未知の設定 " + p[0]);
            f.SetValue(s, Convert.ChangeType(p[1], f.FieldType, System.Globalization.CultureInfo.InvariantCulture));
        }
        return s;
    }

    static void Match(string aSpec, string bSpec, int games, int threads)
    {
        var a = Parse(aSpec);
        var b = Parse(bSpec);
        int aw = 0, bw = 0, dr = 0, done = 0;
        long plies = 0, aMs = 0, aMoves = 0;
        var reasons = new Dictionary<string, int>();
        var sw = Stopwatch.StartNew();
        Parallel.For(0, games, new ParallelOptions { MaxDegreeOfParallelism = threads }, g =>
        {
            var r = PlayOne(a.Clone(), b.Clone(), g);
            lock (reasons)
            {
                done++;
                plies += r.plies;
                aMs += r.aMs; aMoves += r.aMoves;
                if (r.winner < 0) dr++; else if (r.winner == 0) aw++; else bw++;
                var key = (r.winner < 0 ? "分:" : r.winner == 0 ? "A勝:" : "B勝:") + r.reason; reasons[key] = reasons.TryGetValue(key, out int c) ? c + 1 : 1;
            }
        });
        double rate = 100.0 * aw / games;
        // 勝率の95%区間（ウィルソン）
        double z = 1.96, n = games, ph = (double)aw / games;
        double center = (ph + z * z / (2 * n)) / (1 + z * z / n);
        double half = z * Math.Sqrt(ph * (1 - ph) / n + z * z / (4 * n * n)) / (1 + z * z / n);
        Console.WriteLine($"{aSpec}  vs  {bSpec}: {games}局 A {aw}勝 B {bw}勝 {dr}分  A勝率 {rate:0.0}% (95%: {100 * (center - half):0}-{100 * (center + half):0}%)  平均{plies / games}手  A平均{(aMoves > 0 ? aMs / aMoves : 0)}ms(並列)  {sw.Elapsed.TotalSeconds:0}秒  決着 " +
            string.Join(" ", reasons.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value)));
    }

    static (int winner, string reason, int plies, long aMs, long aMoves) PlayOne(CpuSettings a, CpuSettings b, int g)
    {
        var rules = StandardRules.Create23(new StandardRuleOptions());
        int aSide = g % 2;
        var cpus = new CpuPlayer[2];
        cpus[aSide] = new CpuPlayer(rules, aSide, 1000 + g * 2, a);
        cpus[1 - aSide] = new CpuPlayer(rules, 1 - aSide, 1001 + g * 2, b);
        var s = new GameState(rules);
        s.SubmitSetup(0, cpus[0].ChooseSetup());
        s.SubmitSetup(1, cpus[1].ChooseSetup());
        var sw = new Stopwatch();
        long aMs = 0, aMoves = 0;
        while (s.Phase == GamePhase.Playing)
        {
            int p = s.CurrentPlayer;
            sw.Restart();
            var t = cpus[p].BeginThink(PlayerView.From(s, p));
            while (!t.Step(1000)) { }
            if (p == aSide) { aMs += sw.ElapsedMilliseconds; aMoves++; }
            if (t.Resign)
            {
                int w = 1 - p;
                return (w == aSide ? 0 : 1, "投了", s.History.Count, aMs, aMoves);
            }
            s.ApplyMove(t.Result);
        }
        int winner = s.Result == GameResult.Player0Win ? 0 : s.Result == GameResult.Player1Win ? 1 : -1;
        return (winner < 0 ? -1 : winner == aSide ? 0 : 1, s.EndReason.ToString(), s.History.Count, aMs, aMoves);
    }

    static int Time(int games, string spec)
    {
        var a = Parse(spec);
        var all = new List<long>();
        for (int g = 0; g < games; g++)
        {
            var rules = StandardRules.Create23(new StandardRuleOptions());
            var cpus = new[] { new CpuPlayer(rules, 0, g, a.Clone()), new CpuPlayer(rules, 1, g + 99, a.Clone()) };
            var s = new GameState(rules);
            s.SubmitSetup(0, cpus[0].ChooseSetup());
            s.SubmitSetup(1, cpus[1].ChooseSetup());
            while (s.Phase == GamePhase.Playing)
            {
                var sw = Stopwatch.StartNew();
                var t = cpus[s.CurrentPlayer].BeginThink(PlayerView.From(s, s.CurrentPlayer));
                while (!t.Step(1000)) { }
                all.Add(sw.ElapsedMilliseconds);
                if (t.Resign) break;
                s.ApplyMove(t.Result);
            }
        }
        all.Sort();
        Console.WriteLine($"{spec}: {all.Count}手 平均 {all.Average():0}ms 中央 {all[all.Count / 2]}ms 95% {all[(int)(all.Count * 0.95)]}ms 最大 {all.Last()}ms");
        return 0;
    }
}
