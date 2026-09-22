using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace GunjinShogi.Core.Ai
{
    public enum CpuLevel : byte { Easy, Normal, Hard }

    [Serializable]
    public sealed class CpuSettings
    {
        /// <summary>推測配置のサンプル数。</summary>
        public int Samples = 16;
        /// <summary>1手に使う最大時間（ミリ秒）。サンプル数に届かなくても打ち切る。</summary>
        public int TimeBudgetMs = 700;
        /// <summary>評価値に加えるノイズの大きさ（弱さの調整）。</summary>
        public double Noise = 4;
        /// <summary>相手の次の一手による損失をどれだけ恐れるか。</summary>
        public double ThreatFactor = 0.6;
        /// <summary>勝ち目がないと判断したら投了する。</summary>
        public bool AllowResign = true;
        /// <summary>どの手を指しても、推測配置のうちこの割合以上で次の手に負けるなら投了。</summary>
        public double ResignLossRate = 0.9;
        /// <summary>
        /// 読みの深さ。1 = 自分の手＋相手の最善の取り返しを見積もる。
        /// 2 = 相手の取る手・総司令部に入る手を実際に指してみて、その後の自分の取り返しまで読む。
        /// </summary>
        public int Depth = 1;
        /// <summary>占領できる駒が相手総司令部に1歩近づくごとの点。</summary>
        public double HqDistWeight = 3;
        /// <summary>自分の総司令部のそばにいる守りの駒の点（HqDefense × 重み）。</summary>
        public double DefenseWeight = 0;
        /// <summary>2手読みで、相手の手の後の自分の取り返しをどれだけ見込むか。</summary>
        public double FollowUp = 0.5;
        /// <summary>配置を選ぶときに比べるランダム配置の数。</summary>
        public int SetupCandidates = 60;

        public CpuSettings Clone() => (CpuSettings)MemberwiseClone();

        public static CpuSettings For(CpuLevel level)
        {
            switch (level)
            {
                // やさしい：推測が少なく、評価のゆらぎが大きく、取られる危険をほとんど気にしない
                case CpuLevel.Easy: return new CpuSettings { Samples = 4, Noise = 45, ThreatFactor = 0.1 };
                // つよい：推測を増やし、ゆらぎなし、取られる危険を重く見る
                case CpuLevel.Hard: return new CpuSettings { Samples = 80, TimeBudgetMs = 850, Noise = 0, ThreatFactor = 0.75, Depth = 2 };
                default: return new CpuSettings();
            }
        }
    }

    /// <summary>
    /// CPUのひな型。PlayerView（見える情報）だけを使う。
    /// 推論（Belief）→ 推測配置を複数サンプル → 各合法手を1手先＋相手の最善反撃で評価 → 平均が最大の手。
    /// </summary>
    public sealed class CpuPlayer
    {
        const double WinScore = 10000;

        public int Me { get; }
        public RuleSet Rules { get; }
        public BoardTopology Topology { get; }
        public CpuSettings Settings { get; }
        public Belief Belief { get; }
        public PieceValues Values { get; }

        readonly Random rng;
        readonly int[][] distToHq = new int[2][]; // [攻める側] → ノードから相手総司令部までの歩数
        readonly int maxDist;

        public CpuPlayer(RuleSet rules, int me, int seed, CpuSettings settings = null)
        {
            Rules = rules;
            Me = me;
            Settings = settings ?? new CpuSettings();
            Topology = new BoardTopology(rules.Board);
            Belief = new Belief(rules, Topology, me);
            Values = new PieceValues(rules);
            rng = new Random(seed);
            for (int p = 0; p < 2; p++) distToHq[p] = Bfs(Topology.HqNode(1 - p));
            foreach (var d in distToHq[0]) if (d < int.MaxValue) maxDist = Math.Max(maxDist, d);
        }

        int[] Bfs(int target)
        {
            var dist = new int[Topology.NodeCount];
            for (int i = 0; i < dist.Length; i++) dist[i] = int.MaxValue;
            var q = new Queue<int>();
            dist[target] = 0;
            q.Enqueue(target);
            var dirs = new[] { MoveDir.Forward, MoveDir.Back, MoveDir.Left, MoveDir.Right };
            while (q.Count > 0)
            {
                int n = q.Dequeue();
                foreach (var cell in Topology.CellsOf(n))
                foreach (var d in dirs)
                {
                    if (!Topology.TryStep(cell, d, 0, false, out var to)) continue;
                    int m = Topology.NodeOf(to);
                    if (dist[m] != int.MaxValue) continue;
                    dist[m] = dist[n] + 1;
                    q.Enqueue(m);
                }
            }
            return dist;
        }

        // ───────── 配置 ─────────

        /// <summary>ランダム配置を多数作り、総司令部まわりの守りが堅いものを選ぶ。</summary>
        public List<Placement> ChooseSetup(int candidates = -1)
        {
            if (candidates < 0) candidates = Settings.SetupCandidates;
            List<Placement> best = null;
            double bestScore = double.MinValue;
            for (int i = 0; i < candidates; i++)
            {
                var setup = RandomSetup.Generate(Rules, Topology, Me, rng);
                double s = ScoreSetup(setup) + rng.NextDouble() * 0.3;
                if (s > bestScore) { bestScore = s; best = setup; }
            }
            return best;
        }

        double ScoreSetup(List<Placement> setup)
        {
            int hq = Topology.HqNode(Me);
            var typeAt = new Dictionary<int, int>();
            foreach (var p in setup) typeAt[p.Node] = p.TypeId;

            double score = 0;
            foreach (var p in setup)
            {
                var def = Rules.Piece(p.TypeId);
                double d = Values.HqDefense[p.TypeId];
                int dist = distToHq[1 - Me][p.Node]; // 自陣総司令部までの距離
                if (p.Node == hq) score += 3 * d;
                else if (dist == 1) score += 1.0 * d;
                if (Topology.IsGateFront(p.Node, Me)) score += 0.8 * d;
                // 軍旗は後ろの駒が強いほど良い
                if (def.Ability == PieceAbility.MimicBehind)
                {
                    int behind = Topology.BehindNode(p.Node, Me);
                    if (behind >= 0 && typeAt.TryGetValue(behind, out int bt)) score += 0.8 * Values.HqDefense[bt];
                }
            }
            return score;
        }

        // ───────── 思考 ─────────

        public CpuThinking BeginThink(PlayerView view) => new CpuThinking(this, view);

        /// <summary>同期で最後まで考える（テスト・サーバー用）。</summary>
        public Move ChooseMove(PlayerView view)
        {
            var t = BeginThink(view);
            while (!t.Step(1000)) { }
            return t.Result;
        }

        public sealed class CpuThinking
        {
            readonly CpuPlayer cpu;
            readonly PlayerView view;
            readonly Stopwatch total = Stopwatch.StartNew();
            readonly int[] owners;
            readonly int[] nodes;
            readonly bool[] moved;
            List<Move> moves;
            double[] sums;
            int[] losses; // その手を指した直後に負けが確定するサンプルの数
            int samples;
            MoveView lastOwnMove;
            readonly Dictionary<string, int> seen = new Dictionary<string, int>(); // 最後の戦闘以降に現れた局面と回数

            public bool IsDone { get; private set; }
            public Move Result { get; private set; }
            public int SamplesUsed => samples;
            /// <summary>投了する場合 true（Result は使わない）。</summary>
            public bool Resign { get; private set; }
            public string ResignReason { get; private set; }

            internal CpuThinking(CpuPlayer cpu, PlayerView view)
            {
                this.cpu = cpu;
                this.view = view;
                cpu.Belief.Update(view);
                int n = view.Pieces.Count;
                owners = new int[n];
                nodes = new int[n];
                moved = new bool[n];
                for (int i = 0; i < n; i++)
                {
                    owners[i] = view.Pieces[i].Owner;
                    nodes[i] = view.Pieces[i].Node;
                    moved[i] = view.Pieces[i].HasMoved;
                }
                for (int i = view.History.Count - 1; i >= 0; i--)
                    if (view.History[i].Player == cpu.Me) { lastOwnMove = view.History[i]; break; }
                CollectPositions();

                if (cpu.Settings.AllowResign && cpu.CannotOccupyAnymore(view))
                {
                    Resign = true;
                    ResignReason = "総司令部を占領できる駒がなくなった";
                    IsDone = true;
                }
            }

            /// <summary>
            /// 千日手の判定と同じ数え方で、最後の戦闘以降の局面を集める（推測した局面には履歴がないため、ここで補う）。
            /// 盤上の位置は見えているので、戦闘のない手を後ろから巻き戻せば過去の局面が正確に分かる。
            /// </summary>
            void CollectPositions()
            {
                var n = (int[])nodes.Clone();
                Count(PositionKey(n, view.CurrentPlayer));
                for (int i = view.History.Count - 1; i > 0; i--)
                {
                    var h = view.History[i];
                    if (h.HadBattle) break;
                    n[h.PieceId] = h.FromNode;
                    Count(PositionKey(n, h.Player));
                }
            }

            void Count(string key) => seen[key] = seen.TryGetValue(key, out int c) ? c + 1 : 1;

            static string PositionKey(int[] n, int toMove)
            {
                var sb = new System.Text.StringBuilder(n.Length * 3 + 2);
                sb.Append(toMove).Append('|');
                foreach (var x in n) sb.Append(x).Append(',');
                return sb.ToString();
            }

            /// <summary>その手を指した後の局面が、これまでに何回現れたか（戦闘になる手は 0）。</summary>
            int RepeatsAfter(Move m)
            {
                if (cpu.Rules.RepetitionDrawCount <= 0) return 0;
                foreach (var node in nodes) if (node == m.ToNode) return 0; // 相手の駒がいる＝戦闘
                var n = (int[])nodes.Clone();
                n[m.PieceId] = m.ToNode;
                return seen.TryGetValue(PositionKey(n, 1 - cpu.Me), out int c) ? c : 0;
            }

            /// <summary>最大 sliceMs だけ考える。終わったら true。</summary>
            public bool Step(int sliceMs)
            {
                if (IsDone) return true;
                var slice = Stopwatch.StartNew();
                do
                {
                    var types = cpu.Belief.Sample(cpu.rng);
                    var state = GameState.FromSnapshot(cpu.Rules, cpu.Topology, owners, types, nodes, moved, cpu.Me);
                    if (moves == null)
                    {
                        moves = state.GetLegalMoves();
                        sums = new double[moves.Count];
                        losses = new int[moves.Count];
                        if (moves.Count <= 1) { Finish(); return true; }
                    }
                    for (int i = 0; i < moves.Count; i++)
                    {
                        var s = state.Clone();
                        s.ApplyMove(moves[i]);
                        bool lost;
                        sums[i] += cpu.Settings.Depth >= 2 ? cpu.EvaluateDeep(s, out lost) : cpu.Evaluate(s, out lost);
                        if (lost) losses[i]++;
                    }
                    samples++;
                    if (samples >= cpu.Settings.Samples || total.ElapsedMilliseconds >= cpu.Settings.TimeBudgetMs)
                    {
                        Finish();
                        return true;
                    }
                } while (slice.ElapsedMilliseconds < sliceMs);
                return false;
            }

            void Finish()
            {
                IsDone = true;
                if (moves == null || moves.Count == 0) return;

                // どの手を指してもほぼ確実に次の手で負けるなら投了
                if (cpu.Settings.AllowResign && samples >= 4 && losses != null)
                {
                    int minLoss = int.MaxValue;
                    foreach (var l in losses) minLoss = Math.Min(minLoss, l);
                    if (minLoss >= samples * cpu.Settings.ResignLossRate)
                    {
                        Resign = true;
                        ResignReason = "総司令部の占領を防げない";
                        return;
                    }
                }

                int best = 0;
                double bestScore = double.MinValue;
                for (int i = 0; i < moves.Count; i++)
                {
                    double s = samples > 0 ? sums[i] / samples : 0;
                    if (lastOwnMove != null && moves[i].PieceId == lastOwnMove.PieceId && moves[i].ToNode == lastOwnMove.FromNode)
                        s -= 6; // 往復で千日手に向かうのを避ける
                    int rep = RepeatsAfter(moves[i]);
                    if (rep > 0)
                    {
                        if (rep + 1 >= cpu.Rules.RepetitionDrawCount) s = 0; // この手で千日手（引き分け＝0点）
                        else s -= rep * (3 + 0.3 * Math.Max(0, s)); // 優勢なほど同じ局面に戻るのを嫌う
                    }
                    s += cpu.Gaussian() * cpu.Settings.Noise;
                    if (s > bestScore) { bestScore = s; best = i; }
                }
                Result = moves[best];
            }
        }

        double Gaussian()
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }

        // ───────── 評価 ─────────

        /// <summary>自分から見た局面の評価。相手が手番の局面を想定し、相手の最善の取り返しを差し引く。</summary>
        public double Evaluate(GameState s) => Evaluate(s, out _);

        /// <summary>lost: この局面ですでに負けているか、相手が次の手で総司令部を占領できる。</summary>
        public double Evaluate(GameState s, out bool lost)
        {
            int enemy = 1 - Me;
            lost = false;
            if (s.Phase == GamePhase.Finished)
            {
                if (s.Result == GameResult.Draw) return 0;
                bool win = (s.Result == GameResult.Player0Win) == (Me == 0);
                lost = !win;
                return win ? WinScore : -WinScore;
            }

            double score = StaticScore(s);

            if (s.CurrentPlayer == enemy)
            {
                score -= Settings.ThreatFactor * BestGainFor(s, enemy, out bool canOccupy);
                lost = canOccupy;
            }
            return score;
        }

        readonly List<Move> moveBuffer = new List<Move>();
        readonly List<Move> replyBuffer = new List<Move>();

        /// <summary>駒の価値と総司令部への距離だけの評価（次の手の脅威は含めない）。</summary>
        double StaticScore(GameState s)
        {
            if (s.Phase == GamePhase.Finished)
            {
                if (s.Result == GameResult.Draw) return 0;
                return (s.Result == GameResult.Player0Win) == (Me == 0) ? WinScore : -WinScore;
            }
            int enemy = 1 - Me;
            double score = 0;
            int bestOwn = maxDist + 1, bestEnemy = maxDist + 1;
            foreach (var p in s.Pieces)
            {
                if (!p.Alive) continue;
                var def = Rules.Piece(p.TypeId);
                bool occupier = def.IsMobile && def.CanOccupyHQ;
                if (p.Owner == Me)
                {
                    score += Values.Value[p.TypeId];
                    if (occupier) bestOwn = Math.Min(bestOwn, distToHq[Me][p.Node]);
                }
                else
                {
                    score -= Values.Value[p.TypeId];
                    if (occupier) bestEnemy = Math.Min(bestEnemy, distToHq[enemy][p.Node]);
                }
            }
            score += Settings.HqDistWeight * (maxDist - Math.Min(bestOwn, maxDist));
            score -= Settings.HqDistWeight * (maxDist - Math.Min(bestEnemy, maxDist));
            if (Settings.DefenseWeight != 0) score += Settings.DefenseWeight * (Defense(s, Me) - Defense(s, enemy));
            return score;
        }

        /// <summary>player の総司令部から2歩以内にいる、占領を止められる駒の強さの合計（近いほど重い）。</summary>
        double Defense(GameState s, int player)
        {
            var dist = distToHq[1 - player]; // 相手から見た「player の総司令部までの距離」
            double d = 0;
            foreach (var p in s.Pieces)
            {
                if (!p.Alive || p.Owner != player) continue;
                int k = dist[p.Node];
                if (k <= 2) d += Values.HqDefense[p.TypeId] / (1 + k);
            }
            return d;
        }

        /// <summary>
        /// 2手読み（つよい用）。自分が指した後の局面 s で、相手の「取る手・総司令部に入る手」をすべて実際に指し、
        /// その後の自分の最善の取り返しも加味する。相手が静かな手を指す場合も候補に入れ、最も悪い値を採る。
        /// </summary>
        public double EvaluateDeep(GameState s, out bool lost)
        {
            lost = false;
            if (s.Phase == GamePhase.Finished)
            {
                double t = StaticScore(s);
                lost = t < 0;
                return t;
            }
            int enemy = 1 - Me;
            double ownFollowUp = Settings.FollowUp; // 次の自分の取りは相手の駒が推測なので割り引いて見る

            // 相手が静かな手を指した場合
            double worst = StaticScore(s) + ownFollowUp * BestGainFor(s, Me, out _);

            MoveGenerator.GenerateAll(s, enemy, replyBuffer);
            int targetHq = Topology.HqNode(Me);
            var replies = new List<Move>(replyBuffer);
            foreach (var r in replies)
            {
                bool forcing = s.OccupantOf(r.ToNode) >= 0 || r.ToNode == targetHq;
                if (!forcing) continue;
                var s2 = s.Clone();
                s2.ApplyMove(r);
                double v;
                if (s2.Phase == GamePhase.Finished)
                {
                    v = StaticScore(s2);
                    if (v < 0) lost = true;
                }
                else
                {
                    v = StaticScore(s2) + ownFollowUp * BestGainFor(s2, Me, out _);
                }
                // 相手はこちらの駒を知らないので、最善手を必ず指すとは限らない。損の分は脅威係数で割り引く
                double quiet = StaticScore(s);
                v = quiet + (v - quiet) * (v < quiet ? Settings.ThreatFactor : 1);
                if (v < worst) worst = v;
            }
            return worst;
        }

        /// <summary>
        /// 自分には総司令部を占領できる駒が残っておらず、相手には残っている可能性がある。
        /// （相手の駒種は分からないので、推論で「占領できる駒であり得る」駒がいれば残っているとみなす）
        /// </summary>
        internal bool CannotOccupyAnymore(PlayerView view)
        {
            bool enemyMayHave = false;
            foreach (var p in view.Pieces)
            {
                if (!p.Alive) continue;
                if (p.Owner == Me)
                {
                    var def = Rules.Piece(p.TypeId);
                    if (def.IsMobile && def.CanOccupyHQ) return false;
                }
                else if (!enemyMayHave)
                {
                    for (int t = 0; t < Rules.Pieces.Count; t++)
                    {
                        var def = Rules.Piece(t);
                        if (def.IsMobile && def.CanOccupyHQ && Belief.IsCandidate(p.Id, t)) { enemyMayHave = true; break; }
                    }
                }
            }
            return enemyMayHave;
        }

        /// <summary>player が次の1手で得られる最大の得（駒得・総司令部占領）。</summary>
        double BestGainFor(GameState s, int player, out bool canOccupy)
        {
            canOccupy = false;
            MoveGenerator.GenerateAll(s, player, moveBuffer);
            int targetHq = Topology.HqNode(1 - player);
            double best = 0;
            foreach (var m in moveBuffer)
            {
                var mover = s.Pieces[m.PieceId];
                var def = Rules.Piece(mover.TypeId);
                int occ = s.OccupantOf(m.ToNode);
                bool attackerSurvives = true;
                double gain = 0;
                if (occ >= 0)
                {
                    var target = s.Pieces[occ];
                    int a = BattleResolver.EffectiveType(s, mover, mover.Node);
                    int d = BattleResolver.EffectiveType(s, target, target.Node);
                    var r = BattleResolver.Resolve(Rules, a, d);
                    if (r != BattleResult.DefenderSurvives) gain += Values.Value[target.TypeId];
                    if (r != BattleResult.AttackerSurvives) { gain -= Values.Value[mover.TypeId]; attackerSurvives = false; }
                }
                if (attackerSurvives && m.ToNode == targetHq && def.CanOccupyHQ) { gain = WinScore * 0.5; canOccupy = true; }
                if (gain > best) best = gain;
            }
            return best;
        }
    }
}
