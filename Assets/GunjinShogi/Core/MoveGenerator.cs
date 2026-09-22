using System.Collections.Generic;

namespace GunjinShogi.Core
{
    /// <summary>合法手の生成。駒の動きはすべて PieceDefinition.Moves（データ）から導く。</summary>
    public static class MoveGenerator
    {
        static readonly MoveDir[] AllDirs = { MoveDir.Forward, MoveDir.Back, MoveDir.Left, MoveDir.Right };

        static DirMask MaskOf(MoveDir d)
        {
            switch (d)
            {
                case MoveDir.Forward: return DirMask.Forward;
                case MoveDir.Back: return DirMask.Back;
                case MoveDir.Left: return DirMask.Left;
                default: return DirMask.Right;
            }
        }

        public static void GenerateAll(GameState s, int player, List<Move> result)
        {
            result.Clear();
            foreach (var p in s.Pieces)
                if (p.Owner == player && p.Alive) AppendForPiece(s, p, result);
        }

        public static List<Move> ForPiece(GameState s, Piece piece)
        {
            var list = new List<Move>();
            AppendForPiece(s, piece, list);
            return list;
        }

        public static bool HasAnyMove(GameState s, int player)
        {
            var buf = new List<Move>();
            foreach (var p in s.Pieces)
            {
                if (p.Owner != player || !p.Alive) continue;
                if (!s.Rules.Piece(p.TypeId).IsMobile) continue;
                buf.Clear();
                AppendForPiece(s, p, buf);
                if (buf.Count > 0) return true;
            }
            return false;
        }

        static void AppendForPiece(GameState s, Piece piece, List<Move> result)
        {
            if (!piece.Alive) return;
            var def = s.Rules.Piece(piece.TypeId);
            if (!def.IsMobile) return;

            var topo = s.Topology;
            int start = piece.Node;
            int ownHq = topo.HqNode(piece.Owner);
            // 初期配置で総司令部に置かれた駒（総司令部にいて一度も動いていない駒）は動けない
            if (s.Rules.HqInitialPieceImmobile && start == ownHq && !piece.HasMoved) return;
            var seen = new HashSet<int>();
            foreach (var m in result) if (m.PieceId == piece.Id) seen.Add(m.ToNode);

            foreach (var rule in def.Moves)
            foreach (var dir in AllDirs)
            {
                if ((rule.Directions & MaskOf(dir)) == 0) continue;
                foreach (var startCell in topo.CellsOf(start))
                {
                    var cur = startCell;
                    int steps = 0;
                    int prevNode = start;
                    while (true)
                    {
                        if (!topo.TryStep(cur, dir, piece.Owner, def.IgnoresGates, out var next)) break;
                        cur = next;
                        int node = topo.NodeOf(next);
                        if (node == prevNode) continue; // 総司令部（複数マス）の中を横切っているだけ
                        prevNode = node;
                        steps++;
                        if (rule.MaxSteps > 0 && steps > rule.MaxSteps) break;
                        if (s.Rules.OwnHqClosed && node == ownHq) break; // 自陣の総司令部には入れず、通り抜けもできない

                        int occ = s.OccupantOf(node);
                        if (occ < 0)
                        {
                            if (seen.Add(node)) result.Add(new Move(piece.Id, node));
                            continue;
                        }
                        bool enemy = s.Pieces[occ].Owner != piece.Owner;
                        if (enemy && seen.Add(node)) result.Add(new Move(piece.Id, node));
                        if (!rule.CanJump) break;
                    }
                }
            }
        }
    }
}
