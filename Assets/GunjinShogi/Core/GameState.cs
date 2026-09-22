using System;
using System.Collections.Generic;
using System.Text;

namespace GunjinShogi.Core
{
    /// <summary>
    /// 対局の完全な状態（審判だけが見る情報）。UI・CPU・通信は PlayerView を使うこと。
    /// 乱数を持たない決定的な状態機械なので、同じ配置と手順から常に同じ状態が再現できる。
    /// </summary>
    public sealed class GameState
    {
        public RuleSet Rules { get; private set; }
        public BoardTopology Topology { get; private set; }
        /// <summary>インデックス = 駒ID。</summary>
        public List<Piece> Pieces { get; private set; } = new List<Piece>();
        public GamePhase Phase { get; private set; } = GamePhase.Setup;
        public int CurrentPlayer { get; private set; }
        public GameResult Result { get; private set; } = GameResult.Ongoing;
        public EndReason EndReason { get; private set; } = EndReason.None;
        public List<MoveRecord> History { get; private set; } = new List<MoveRecord>();
        public int Ply => History.Count;

        int[] occupancy;
        readonly bool[] setupDone = new bool[2];
        List<Placement>[] pendingSetups = new List<Placement>[2];
        Dictionary<string, int> repetition = new Dictionary<string, int>();

        public GameState(RuleSet rules)
        {
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            var errors = rules.Validate();
            if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors));
            Topology = new BoardTopology(rules.Board);
            occupancy = new int[Topology.NodeCount];
            for (int i = 0; i < occupancy.Length; i++) occupancy[i] = -1;
        }

        GameState() { }

        /// <summary>
        /// 駒IDを保ったまま任意の局面を作る（CPUの推論・決定化用）。
        /// nodes[i] が -1 の駒は除去済み。types[i] は推測値でもよい。
        /// </summary>
        public static GameState FromSnapshot(RuleSet rules, BoardTopology topo, IReadOnlyList<int> owners,
            IReadOnlyList<int> types, IReadOnlyList<int> nodes, IReadOnlyList<bool> hasMoved, int currentPlayer)
        {
            var s = new GameState
            {
                Rules = rules,
                Topology = topo,
                Phase = GamePhase.Playing,
                CurrentPlayer = currentPlayer,
                occupancy = new int[topo.NodeCount],
            };
            for (int i = 0; i < s.occupancy.Length; i++) s.occupancy[i] = -1;
            s.setupDone[0] = s.setupDone[1] = true;
            for (int i = 0; i < owners.Count; i++)
            {
                var p = new Piece { Id = i, Owner = owners[i], TypeId = types[i], Node = nodes[i], HasMoved = hasMoved[i] };
                s.Pieces.Add(p);
                if (p.Node >= 0) s.occupancy[p.Node] = i;
            }
            return s;
        }

        /// <summary>
        /// 任意局面から対局を始める（テスト・詰め問題・解析用）。配置ルールの検証は行わない。
        /// </summary>
        public static GameState FromPositions(RuleSet rules, IList<PositionedPiece> pieces, int currentPlayer = 0)
        {
            var s = new GameState(rules);
            foreach (var pp in pieces)
            {
                int node = s.Topology.NodeOf(pp.Cell);
                if (node < 0) throw new ArgumentException($"盤外のマス {pp.Cell}");
                if (s.occupancy[node] >= 0) throw new ArgumentException($"マス {pp.Cell} が重複しています");
                var piece = new Piece { Id = s.Pieces.Count, Owner = pp.Owner, TypeId = pp.TypeId, Node = node, HasMoved = pp.HasMoved };
                s.Pieces.Add(piece);
                s.occupancy[node] = piece.Id;
            }
            s.setupDone[0] = s.setupDone[1] = true;
            s.Phase = GamePhase.Playing;
            s.CurrentPlayer = currentPlayer;
            s.RecordPosition();
            return s;
        }

        public int OccupantOf(int node) => occupancy[node];
        public bool IsSetupDone(int player) => setupDone[player];

        // ───────── 配置 ─────────

        /// <summary>
        /// 配置を提出する。両者が揃った時点で駒IDを割り当てて対局開始。
        /// 駒IDは「プレイヤー → ノード番号順」で振るので、IDから駒種は推測できない。
        /// </summary>
        public List<string> SubmitSetup(int player, IList<Placement> placements)
        {
            if (Phase != GamePhase.Setup) return new List<string> { "配置フェーズではありません" };
            if (setupDone[player]) return new List<string> { "配置は提出済みです" };
            var errors = SetupValidator.Validate(Rules, Topology, player, placements);
            if (errors.Count > 0) return errors;

            pendingSetups[player] = new List<Placement>(placements);
            setupDone[player] = true;
            if (setupDone[0] && setupDone[1]) StartGame();
            return errors;
        }

        void StartGame()
        {
            for (int p = 0; p < 2; p++)
            {
                var sorted = new List<Placement>(pendingSetups[p]);
                sorted.Sort((a, b) => a.Node.CompareTo(b.Node));
                foreach (var pl in sorted)
                {
                    var piece = new Piece { Id = Pieces.Count, Owner = p, TypeId = pl.TypeId, Node = pl.Node };
                    Pieces.Add(piece);
                    occupancy[pl.Node] = piece.Id;
                }
            }
            pendingSetups = new List<Placement>[2];
            Phase = GamePhase.Playing;
            CurrentPlayer = 0;
            RecordPosition();
        }

        // ───────── 対局 ─────────

        public List<Move> GetLegalMoves()
        {
            var list = new List<Move>();
            if (Phase == GamePhase.Playing) MoveGenerator.GenerateAll(this, CurrentPlayer, list);
            return list;
        }

        public bool IsLegal(Move move)
        {
            if (Phase != GamePhase.Playing) return false;
            if (move.PieceId < 0 || move.PieceId >= Pieces.Count) return false;
            var p = Pieces[move.PieceId];
            if (p.Owner != CurrentPlayer || !p.Alive) return false;
            return MoveGenerator.ForPiece(this, p).Contains(move);
        }

        /// <summary>手を適用する。不正な手は例外。</summary>
        public MoveRecord ApplyMove(Move move)
        {
            if (!IsLegal(move)) throw new InvalidOperationException($"不正な手です: {move}");

            int player = CurrentPlayer;
            var piece = Pieces[move.PieceId];
            var record = new MoveRecord { Ply = Ply + 1, Player = player, Move = move, FromNode = piece.Node };

            int defenderId = occupancy[move.ToNode];
            bool attackerSurvives = true;

            if (defenderId >= 0)
            {
                var defender = Pieces[defenderId];
                int aEff = BattleResolver.EffectiveType(this, piece, piece.Node);
                int dEff = BattleResolver.EffectiveType(this, defender, defender.Node);
                var result = BattleResolver.Resolve(Rules, aEff, dEff);
                record.Battle = new BattleRecord
                {
                    AttackerId = piece.Id,
                    DefenderId = defender.Id,
                    AttackerType = piece.TypeId,
                    DefenderType = defender.TypeId,
                    AttackerEffectiveType = aEff,
                    DefenderEffectiveType = dEff,
                    Result = result,
                };
                if (result != BattleResult.AttackerSurvives) attackerSurvives = false;
                if (result != BattleResult.DefenderSurvives) Remove(defender);
            }

            occupancy[piece.Node] = -1;
            piece.HasMoved = true;
            if (attackerSurvives)
            {
                piece.Node = move.ToNode;
                occupancy[move.ToNode] = piece.Id;
            }
            else
            {
                piece.Node = -1;
            }

            History.Add(record);
            EvaluateEnd(player, piece, attackerSurvives, record);
            if (Phase == GamePhase.Playing) CurrentPlayer = 1 - player;
            if (Phase == GamePhase.Playing) CheckNoMovesAndRepetition(player, record);

            record.ResultAfter = Result;
            record.EndReasonAfter = EndReason;
            return record;
        }

        public void Resign(int player)
        {
            if (Phase != GamePhase.Playing) return;
            Finish(player == 0 ? GameResult.Player1Win : GameResult.Player0Win, EndReason.Resign);
        }

        void Remove(Piece p)
        {
            if (p.Node >= 0) occupancy[p.Node] = -1;
            p.Node = -1;
        }

        void EvaluateEnd(int player, Piece mover, bool moverSurvived, MoveRecord record)
        {
            if (Rules.CommanderLossLoses && record.Battle != null)
            {
                bool lost0 = CommanderLost(0), lost1 = CommanderLost(1);
                if (lost0 && lost1) { Finish(GameResult.Draw, EndReason.CommanderLost); return; }
                if (lost0) { Finish(GameResult.Player1Win, EndReason.CommanderLost); return; }
                if (lost1) { Finish(GameResult.Player0Win, EndReason.CommanderLost); return; }
            }

            // 少尉で軍旗を倒したら勝ち（攻めた側・守った側どちらでも）
            if (record.Battle != null)
            {
                var b = record.Battle;
                var attacker = Pieces[b.AttackerId];
                var defender = Pieces[b.DefenderId];
                if (FlagCapturedBy(attacker, defender)) { Finish(WinOf(attacker.Owner), EndReason.FlagCaptured); return; }
                if (FlagCapturedBy(defender, attacker)) { Finish(WinOf(defender.Owner), EndReason.FlagCaptured); return; }
            }

            if (moverSurvived && mover.Node == Topology.HqNode(1 - player) && Rules.Piece(mover.TypeId).CanOccupyHQ)
                Finish(WinOf(player), EndReason.HqOccupied);
        }

        void CheckNoMovesAndRepetition(int lastPlayer, MoveRecord record)
        {
            bool nextHas = MoveGenerator.HasAnyMove(this, CurrentPlayer);
            if (!nextHas)
            {
                bool lastHas = MoveGenerator.HasAnyMove(this, lastPlayer);
                Finish(lastHas ? WinOf(lastPlayer) : GameResult.Draw, EndReason.NoLegalMoves);
                return;
            }

            if (record.Battle != null) repetition.Clear(); // 駒が減った局面には戻れない
            if (Rules.RepetitionDrawCount > 0 && RecordPosition() >= Rules.RepetitionDrawCount)
            {
                Finish(GameResult.Draw, EndReason.Repetition);
                return;
            }
            if (Rules.MaxPlies > 0 && Ply >= Rules.MaxPlies) Finish(GameResult.Draw, EndReason.MaxPlies);
        }

        bool FlagCapturedBy(Piece capturer, Piece flag) =>
            Rules.Piece(capturer.TypeId).CapturesFlagToWin
            && Rules.Piece(flag.TypeId).Ability == PieceAbility.MimicBehind
            && !flag.Alive;

        bool CommanderLost(int player)
        {
            bool hasCommanderType = false;
            foreach (var p in Pieces)
            {
                if (p.Owner != player || !Rules.Piece(p.TypeId).IsCommander) continue;
                hasCommanderType = true;
                if (p.Alive) return false;
            }
            return hasCommanderType;
        }

        static GameResult WinOf(int player) => player == 0 ? GameResult.Player0Win : GameResult.Player1Win;

        void Finish(GameResult result, EndReason reason)
        {
            Result = result;
            EndReason = reason;
            Phase = GamePhase.Finished;
        }

        int RecordPosition()
        {
            var sb = new StringBuilder(Pieces.Count * 3 + 2);
            sb.Append(CurrentPlayer).Append('|');
            foreach (var p in Pieces) sb.Append(p.Node).Append(',');
            var key = sb.ToString();
            repetition.TryGetValue(key, out int n);
            repetition[key] = ++n;
            return n;
        }

        // ───────── 複製（CPUの先読み用） ─────────

        public GameState Clone()
        {
            var c = new GameState
            {
                Rules = Rules,
                Topology = Topology,
                Phase = Phase,
                CurrentPlayer = CurrentPlayer,
                Result = Result,
                EndReason = EndReason,
                History = new List<MoveRecord>(History),
                occupancy = (int[])occupancy.Clone(),
                repetition = new Dictionary<string, int>(repetition),
            };
            c.setupDone[0] = setupDone[0];
            c.setupDone[1] = setupDone[1];
            foreach (var p in Pieces) c.Pieces.Add(p.Clone());
            return c;
        }

        /// <summary>
        /// 駒種だけ差し替えた複製（CPUの決定化用：推測した敵配置で先読みする）。
        /// types[pieceId] が -1 の駒は元の駒種のまま。
        /// </summary>
        public GameState CloneWithTypes(IReadOnlyList<int> types)
        {
            var c = Clone();
            for (int i = 0; i < c.Pieces.Count && i < types.Count; i++)
                if (types[i] >= 0) c.Pieces[i].TypeId = types[i];
            return c;
        }
    }
}
