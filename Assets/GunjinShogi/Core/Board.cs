using System;
using System.Collections.Generic;

namespace GunjinShogi.Core
{
    public struct Coord : IEquatable<Coord>
    {
        public int X;
        public int Y;

        public Coord(int x, int y) { X = x; Y = y; }

        public bool Equals(Coord o) => X == o.X && Y == o.Y;
        public override bool Equals(object obj) => obj is Coord c && Equals(c);
        public override int GetHashCode() => (X * 397) ^ Y;
        public override string ToString() => $"({X},{Y})";
    }

    /// <summary>
    /// 盤の定義（データ）。プレイヤー0の陣が y = 0..CampRows-1、プレイヤー1の陣が残り。
    /// 陣の境目は「川」で、GateColumns の列だけ突入口として通れる。
    /// </summary>
    [Serializable]
    public sealed class BoardDefinition
    {
        public int Width = 6;
        public int Height = 8;
        public int CampRows = 4;
        public int[] GateColumns = { 1, 4 };
        /// <summary>[player] → 総司令部を構成するマス（複数マスで1ノード）。</summary>
        public Coord[][] HqCells;

        public BoardDefinition Clone()
        {
            var c = (BoardDefinition)MemberwiseClone();
            c.GateColumns = (int[])GateColumns.Clone();
            c.HqCells = new Coord[HqCells.Length][];
            for (int i = 0; i < HqCells.Length; i++) c.HqCells[i] = (Coord[])HqCells[i].Clone();
            return c;
        }
    }

    /// <summary>
    /// 盤の位相。マス(Coord)とノード(int)の対応を持つ。総司令部の複数マスは1ノードに集約される。
    /// </summary>
    public sealed class BoardTopology
    {
        public BoardDefinition Definition { get; }
        public int NodeCount => nodeCells.Count;

        readonly int[] cellToNode;
        readonly List<Coord[]> nodeCells = new List<Coord[]>();
        readonly int[] hqNode = { -1, -1 };
        readonly HashSet<int> gateSet;

        public BoardTopology(BoardDefinition def)
        {
            Definition = def ?? throw new ArgumentNullException(nameof(def));
            gateSet = new HashSet<int>(def.GateColumns);
            cellToNode = new int[def.Width * def.Height];
            var building = new List<List<Coord>>();

            for (int y = 0; y < def.Height; y++)
            for (int x = 0; x < def.Width; x++)
            {
                var c = new Coord(x, y);
                int hqOwner = HqOwnerOfCell(c);
                int node;
                if (hqOwner >= 0 && hqNode[hqOwner] >= 0)
                {
                    node = hqNode[hqOwner];
                    building[node].Add(c);
                }
                else
                {
                    node = building.Count;
                    building.Add(new List<Coord> { c });
                    if (hqOwner >= 0) hqNode[hqOwner] = node;
                }
                cellToNode[y * def.Width + x] = node;
            }

            foreach (var l in building) nodeCells.Add(l.ToArray());
            if (hqNode[0] < 0 || hqNode[1] < 0) throw new ArgumentException("両プレイヤーの総司令部が必要です");
        }

        int HqOwnerOfCell(Coord c)
        {
            var hq = Definition.HqCells;
            for (int p = 0; p < 2; p++)
                foreach (var h in hq[p])
                    if (h.Equals(c)) return p;
            return -1;
        }

        public bool InBounds(Coord c) =>
            c.X >= 0 && c.Y >= 0 && c.X < Definition.Width && c.Y < Definition.Height;

        public int NodeOf(Coord c) => InBounds(c) ? cellToNode[c.Y * Definition.Width + c.X] : -1;

        public IReadOnlyList<Coord> CellsOf(int node) => nodeCells[node];

        /// <summary>ノードの代表マス（先頭のマス）。</summary>
        public Coord PrimaryCell(int node) => nodeCells[node][0];

        public int HqNode(int player) => hqNode[player];

        /// <summary>ノードがどちらの陣にあるか（0 or 1）。</summary>
        public int CampOf(int node) => PrimaryCell(node).Y < Definition.CampRows ? 0 : 1;

        public bool IsBackRow(int node, int player)
        {
            int backY = player == 0 ? 0 : Definition.Height - 1;
            foreach (var c in nodeCells[node]) if (c.Y == backY) return true;
            return false;
        }

        /// <summary>自陣のうち突入口に面したマスか（地雷・軍旗の配置禁止判定用）。</summary>
        public bool IsGateFront(int node, int player)
        {
            int frontY = player == 0 ? Definition.CampRows - 1 : Definition.CampRows;
            foreach (var c in nodeCells[node])
                if (c.Y == frontY && gateSet.Contains(c.X)) return true;
            return false;
        }

        /// <summary>プレイヤーから見て「すぐ後ろ」のノード。無ければ -1。</summary>
        public int BehindNode(int node, int player)
        {
            var c = PrimaryCell(node);
            var b = new Coord(c.X, c.Y + (player == 0 ? -1 : 1));
            return NodeOf(b);
        }

        public static Coord Delta(MoveDir dir, int player)
        {
            int f = player == 0 ? 1 : -1;
            switch (dir)
            {
                case MoveDir.Forward: return new Coord(0, f);
                case MoveDir.Back: return new Coord(0, -f);
                case MoveDir.Left: return new Coord(-f, 0);
                default: return new Coord(f, 0);
            }
        }

        /// <summary>1マス進む。盤外、または突入口以外で川を渡る場合は false。</summary>
        public bool TryStep(Coord from, MoveDir dir, int player, bool ignoresGates, out Coord to)
        {
            var d = Delta(dir, player);
            to = new Coord(from.X + d.X, from.Y + d.Y);
            if (!InBounds(to)) return false;
            if (ignoresGates || d.Y == 0) return true;
            int lo = Math.Min(from.Y, to.Y);
            bool crossesRiver = lo == Definition.CampRows - 1;
            return !crossesRiver || gateSet.Contains(from.X);
        }

        public IEnumerable<int> CampNodes(int player)
        {
            for (int n = 0; n < NodeCount; n++)
                if (CampOf(n) == player) yield return n;
        }
    }
}
