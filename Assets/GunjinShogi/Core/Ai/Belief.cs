using System;
using System.Collections.Generic;

namespace GunjinShogi.Core.Ai
{
    /// <summary>
    /// 相手の各駒について「あり得る駒種」の集合を追跡する推論器。
    /// PlayerView（見える情報）だけから、配置禁止・移動の形・戦闘結果・残り枚数で絞り込む。
    /// 推理アシスト表示にもそのまま使える。
    /// </summary>
    public sealed class Belief
    {
        public RuleSet Rules { get; }
        public BoardTopology Topology { get; }
        public int Me { get; }
        public int Enemy => 1 - Me;

        int pieceCount;
        int[] owners;
        int[] knownTypes;      // 自駒は駒種、相手駒は -1
        int[] nodes;           // 棋譜を再生して得た現在位置
        bool[] moved;
        bool[][] candidates;   // [pieceId][typeId]、自駒は null
        int processed;
        bool initialized;

        public Belief(RuleSet rules, BoardTopology topology, int me)
        {
            Rules = rules;
            Topology = topology;
            Me = me;
        }

        public IReadOnlyList<int> Owners => owners;
        public IReadOnlyList<int> Nodes => nodes;
        public IReadOnlyList<bool> Moved => moved;

        public bool IsCandidate(int pieceId, int typeId) =>
            candidates[pieceId] == null ? knownTypes[pieceId] == typeId : candidates[pieceId][typeId];

        public int CandidateCount(int pieceId)
        {
            if (candidates[pieceId] == null) return 1;
            int n = 0;
            foreach (var b in candidates[pieceId]) if (b) n++;
            return n;
        }

        /// <summary>新しい棋譜だけを取り込む。</summary>
        public void Update(PlayerView view)
        {
            if (!initialized) Initialize(view);
            for (; processed < view.History.Count; processed++) Apply(view.History[processed]);
        }

        void Initialize(PlayerView view)
        {
            pieceCount = view.Pieces.Count;
            owners = new int[pieceCount];
            knownTypes = new int[pieceCount];
            nodes = new int[pieceCount];
            moved = new bool[pieceCount];
            candidates = new bool[pieceCount][];

            for (int i = 0; i < pieceCount; i++)
            {
                var p = view.Pieces[i];
                owners[i] = p.Owner;
                knownTypes[i] = p.Owner == Me ? p.TypeId : -1;
                nodes[i] = InitialNode(view, i);
            }

            for (int i = 0; i < pieceCount; i++)
            {
                if (owners[i] == Me) continue;
                var c = new bool[Rules.Pieces.Count];
                for (int t = 0; t < c.Length; t++)
                {
                    var def = Rules.Piece(t);
                    c[t] = def.Count > 0
                        && !(def.ForbiddenOnGateFront && Topology.IsGateFront(nodes[i], owners[i]))
                        && !(def.ForbiddenOnBackRow && Topology.IsBackRow(nodes[i], owners[i]))
                        && !(def.ForbiddenInHq && nodes[i] == Topology.HqNode(owners[i]))
                        && !(nodes[i] == Topology.HqNode(owners[i]) && Rules.HqRequiresOneOf.Count > 0 && !Rules.HqRequiresOneOf.Contains(t));
                }
                candidates[i] = c;
            }
            processed = 0;
            initialized = true;
        }

        static int InitialNode(PlayerView view, int id)
        {
            foreach (var h in view.History)
                if (h.PieceId == id) return h.FromNode;
            foreach (var h in view.History)
                if (h.HadBattle && h.DefenderId == id) return h.ToNode;
            return view.Pieces[id].Node;
        }

        /// <summary>棋譜の時点の局面。相手駒の駒種は仮の値（0）が入る。</summary>
        GameState Snapshot(int currentPlayer, int overrideId = -1, int overrideType = 0)
        {
            var types = new int[pieceCount];
            for (int i = 0; i < pieceCount; i++) types[i] = knownTypes[i] >= 0 ? knownTypes[i] : 0;
            if (overrideId >= 0) types[overrideId] = overrideType;
            return GameState.FromSnapshot(Rules, Topology, owners, types, nodes, moved, currentPlayer);
        }

        void Apply(MoveView h)
        {
            if (h.Player != Me)
            {
                var c = candidates[h.PieceId];
                var move = new Move(h.PieceId, h.ToNode);
                for (int t = 0; t < c.Length; t++)
                {
                    if (!c[t]) continue;
                    var s = Snapshot(h.Player, h.PieceId, t);
                    if (!MoveGenerator.ForPiece(s, s.Pieces[h.PieceId]).Contains(move)) c[t] = false;
                }
            }

            if (h.HadBattle)
            {
                int enemyId = owners[h.AttackerId] == Me ? h.DefenderId : h.AttackerId;
                int myId = owners[h.AttackerId] == Me ? h.AttackerId : h.DefenderId;
                var snap = Snapshot(h.Player);
                int myNode = myId == h.AttackerId ? h.FromNode : h.ToNode;
                int enemyNode = enemyId == h.AttackerId ? h.FromNode : h.ToNode;
                int myEff = BattleResolver.EffectiveType(snap, snap.Pieces[myId], myNode);
                var c = candidates[enemyId];
                for (int t = 0; t < c.Length; t++)
                {
                    if (!c[t]) continue;
                    if (!TryEnemyEffective(snap, enemyId, enemyNode, t, out int enemyEff)) continue; // 軍旗の背後が不明
                    var r = myId == h.AttackerId
                        ? BattleResolver.Resolve(Rules, myEff, enemyEff)
                        : BattleResolver.Resolve(Rules, enemyEff, myEff);
                    if (r != h.Result) c[t] = false;
                }
            }

            // 局面を進める
            int mover = h.PieceId;
            moved[mover] = true;
            if (!h.HadBattle)
            {
                nodes[mover] = h.ToNode;
                return;
            }
            switch (h.Result)
            {
                case BattleResult.AttackerSurvives:
                    nodes[h.DefenderId] = -1;
                    nodes[mover] = h.ToNode;
                    break;
                case BattleResult.DefenderSurvives:
                    nodes[mover] = -1;
                    break;
                default:
                    nodes[h.DefenderId] = -1;
                    nodes[mover] = -1;
                    break;
            }
        }

        /// <summary>相手駒が駒種 t だった場合の実効駒種。軍旗の背後が相手の未知の駒なら false。</summary>
        bool TryEnemyEffective(GameState snap, int enemyId, int node, int t, out int eff)
        {
            eff = t;
            if (Rules.Piece(t).Ability != PieceAbility.MimicBehind) return true;
            int owner = owners[enemyId];
            eff = BattleResolver.AlwaysLoses;
            if (Topology.IsBackRow(node, owner)) return true;
            int behind = Topology.BehindNode(node, owner);
            if (behind < 0) return true;
            int occ = snap.OccupantOf(behind);
            if (occ < 0 || owners[occ] != owner) return true;
            return false;
        }

        // ───────── 決定化（推測配置のサンプリング） ─────────

        /// <summary>
        /// 候補と枚数の制約を満たす相手駒種の割り当てを1つ作る。
        /// 戻り値は全駒の駒種（自駒は本物）。見つからなければ制約を緩めた割り当てを返す。
        /// </summary>
        public int[] Sample(Random rng)
        {
            var result = new int[pieceCount];
            var enemyIds = new List<int>();
            for (int i = 0; i < pieceCount; i++)
            {
                if (owners[i] == Me) result[i] = knownTypes[i];
                else enemyIds.Add(i);
            }

            for (int attempt = 0; attempt < 40; attempt++)
                if (TryGreedy(enemyIds, result, rng, strict: true)) return result;
            if (Backtrack(enemyIds, result, rng)) return result;
            TryGreedy(enemyIds, result, rng, strict: false);
            return result;
        }

        int[] RemainingCounts()
        {
            var remaining = new int[Rules.Pieces.Count];
            for (int t = 0; t < remaining.Length; t++) remaining[t] = Rules.Piece(t).Count;
            return remaining;
        }

        bool TryGreedy(List<int> ids, int[] result, Random rng, bool strict)
        {
            var remaining = RemainingCounts();
            var order = new List<int>(ids);
            Shuffle(order, rng);
            order.Sort((a, b) => CandidateCount(a).CompareTo(CandidateCount(b)));

            foreach (var id in order)
            {
                int total = 0;
                for (int t = 0; t < remaining.Length; t++)
                    if (remaining[t] > 0 && candidates[id][t]) total += remaining[t];
                if (total == 0)
                {
                    if (strict) return false;
                    for (int t = 0; t < remaining.Length; t++) total += remaining[t];
                    int pick = rng.Next(Math.Max(1, total));
                    for (int t = 0; t < remaining.Length; t++)
                    {
                        if (remaining[t] <= 0) continue;
                        if (pick < remaining[t]) { result[id] = t; remaining[t]--; break; }
                        pick -= remaining[t];
                    }
                    continue;
                }
                int r = rng.Next(total);
                for (int t = 0; t < remaining.Length; t++)
                {
                    if (remaining[t] <= 0 || !candidates[id][t]) continue;
                    if (r < remaining[t]) { result[id] = t; remaining[t]--; break; }
                    r -= remaining[t];
                }
            }
            return true;
        }

        bool Backtrack(List<int> ids, int[] result, Random rng)
        {
            var remaining = RemainingCounts();
            var order = new List<int>(ids);
            order.Sort((a, b) => CandidateCount(a).CompareTo(CandidateCount(b)));
            int budget = 20000;
            return Dfs(0);

            bool Dfs(int k)
            {
                if (k == order.Count) return true;
                if (--budget < 0) return false;
                int id = order[k];
                int start = rng.Next(remaining.Length);
                for (int j = 0; j < remaining.Length; j++)
                {
                    int t = (start + j) % remaining.Length;
                    if (remaining[t] <= 0 || !candidates[id][t]) continue;
                    remaining[t]--;
                    result[id] = t;
                    if (Dfs(k + 1)) return true;
                    remaining[t]++;
                }
                return false;
            }
        }

        static void Shuffle<T>(IList<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                var tmp = list[i]; list[i] = list[j]; list[j] = tmp;
            }
        }
    }
}
