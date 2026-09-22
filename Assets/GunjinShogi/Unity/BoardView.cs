using System;
using System.Collections.Generic;
using GunjinShogi.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GunjinShogi.UnityView
{
    internal enum MarkKind { None, Move, Attack }

    /// <summary>盤に表示する駒1枚分の情報（配置中と対局中で共通）。</summary>
    public struct PieceDisplay
    {
        public int Id;
        public int Owner;
        public int Node;       // -1 = 盤上にない
        public int TypeId;     // Visibility.HiddenType = 裏向き
        public bool HasMoved;
        public string Memo;    // 見えない敵駒に付けたメモ（無ければ null）
    }

    /// <summary>
    /// 盤の表示。コンテナの大きさからマスの大きさを決め、見る側（viewer）の陣が常に手前になるよう描く。
    /// マスのクリックを NodeClicked で通知する。ゲームの状態は持たない。
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        const float RiverGap = 0.62f; // マス何個分の幅の川を挟むか

        public event Action<int> NodeClicked;

        RuleSet rules;
        BoardTopology topo;
        int viewer;
        RectTransform boardRoot;
        internal RectTransform cellLayer, markLayer, pieceLayer, fxLayer;
        Image boardFrame, river;
        readonly List<Image> bridges = new List<Image>();
        readonly List<NodeCell> cells = new List<NodeCell>();
        readonly List<PieceView> pieces = new List<PieceView>();
        readonly List<BoardStamp> stamps = new List<BoardStamp>();
        Vector2 lastSize;
        internal float cell;
        Vector2 origin;

        public int Viewer => viewer;
        public float CellSize => cell;

        public void Init(RuleSet rules, int viewer)
        {
            this.rules = rules;
            topo = new BoardTopology(rules.Board);
            this.viewer = viewer;
            Build();
            lastSize = Vector2.zero;
        }

        public void SetViewer(int v)
        {
            if (viewer == v) return;
            viewer = v;
            lastSize = Vector2.zero;
        }

        void Build()
        {
            foreach (Transform c in transform) Destroy(c.gameObject);
            cells.Clear(); pieces.Clear(); bridges.Clear(); stamps.Clear();

            boardRoot = Ui.Rect("Board", transform);
            boardFrame = Ui.Image("Frame", boardRoot, Theme.Ink);
            Ui.Stretch(boardFrame.rectTransform);
            river = Ui.Image("River", boardRoot, Theme.River);
            cellLayer = Ui.Stretch(Ui.Rect("Cells", boardRoot));
            markLayer = Ui.Stretch(Ui.Rect("Marks", boardRoot));
            pieceLayer = Ui.Stretch(Ui.Rect("Pieces", boardRoot));
            fxLayer = Ui.Stretch(Ui.Rect("Fx", boardRoot));

            foreach (var g in rules.Board.GateColumns)
                bridges.Add(Ui.Image("Bridge" + g, boardRoot, Theme.Bridge));

            for (int n = 0; n < topo.NodeCount; n++)
                cells.Add(NodeCell.Create(this, n, topo.HqNode(0) == n || topo.HqNode(1) == n));
        }

        void LateUpdate()
        {
            if (rules == null) return;
            var size = ((RectTransform)transform).rect.size;
            if ((size - lastSize).sqrMagnitude > 1f) Layout(size);
            foreach (var p in pieces) p.Tick();
            for (int i = stamps.Count - 1; i >= 0; i--)
                if (!stamps[i].Tick()) { Destroy(stamps[i].gameObject); stamps.RemoveAt(i); }
        }

        // ───────── 配置計算 ─────────

        void Layout(Vector2 size)
        {
            lastSize = size;
            var def = rules.Board;
            float units = def.Height + RiverGap;
            cell = Mathf.Floor(Mathf.Min(size.x / (def.Width + 0.3f), size.y / (units + 0.3f)));
            if (cell < 4) { lastSize = Vector2.zero; return; } // まだ大きさが決まっていない
            var boardSize = new Vector2(cell * def.Width, cell * units);
            float pad = Mathf.Max(3, cell * 0.06f);
            Ui.Place(boardRoot, size * 0.5f, boardSize + Vector2.one * pad * 2);
            origin = new Vector2(pad, pad);

            float riverY = origin.y + cell * def.CampRows; // 盤は上下対称（CampRows = Height / 2）
            Ui.Place(river.rectTransform, new Vector2(origin.x + boardSize.x / 2, riverY + cell * RiverGap / 2),
                new Vector2(boardSize.x, cell * RiverGap));
            for (int i = 0; i < bridges.Count; i++)
            {
                int col = DisplayX(def.GateColumns[i]);
                Ui.Place(bridges[i].rectTransform,
                    new Vector2(origin.x + (col + 0.5f) * cell, riverY + cell * RiverGap / 2),
                    new Vector2(cell * 0.62f, cell * RiverGap + 2));
            }

            for (int n = 0; n < cells.Count; n++)
            {
                var r = NodeRect(n);
                cells[n].Layout(r, cell);
            }
            foreach (var p in pieces) p.Snap();
        }

        int DisplayX(int x) => viewer == 0 ? x : rules.Board.Width - 1 - x;
        int DisplayRow(int y) => viewer == 0 ? y : rules.Board.Height - 1 - y;

        Rect CellRect(Coord c)
        {
            int row = DisplayRow(c.Y);
            float y = origin.y + row * cell + (row >= rules.Board.CampRows ? cell * RiverGap : 0);
            return new Rect(origin.x + DisplayX(c.X) * cell, y, cell, cell);
        }

        public Rect NodeRect(int node)
        {
            Rect r = default;
            bool first = true;
            foreach (var c in topo.CellsOf(node))
            {
                var cr = CellRect(c);
                if (first) { r = cr; first = false; }
                else r = Rect.MinMaxRect(Mathf.Min(r.xMin, cr.xMin), Mathf.Min(r.yMin, cr.yMin),
                    Mathf.Max(r.xMax, cr.xMax), Mathf.Max(r.yMax, cr.yMax));
            }
            return r;
        }

        internal void OnCellClicked(int node) => NodeClicked?.Invoke(node);

        // ───────── 表示の更新 ─────────

        public sealed class Marks
        {
            public int SelectedNode = -1;
            public readonly HashSet<int> MoveTargets = new HashSet<int>();
            public readonly HashSet<int> AttackTargets = new HashSet<int>();
            public int LastFrom = -1, LastTo = -1;
            public readonly HashSet<int> Dimmed = new HashSet<int>(); // 配置中の相手陣など
        }

        public void Render(IReadOnlyList<PieceDisplay> list, Marks marks)
        {
            while (pieces.Count < list.Count) pieces.Add(PieceView.Create(pieceLayer, this));
            for (int i = 0; i < pieces.Count; i++)
            {
                if (i >= list.Count || list[i].Node < 0) { pieces[i].Hide(); continue; }
                var d = list[i];
                bool selected = marks != null && marks.SelectedNode == d.Node;
                pieces[i].Show(d, d.Owner != viewer, selected, rules);
            }
            for (int n = 0; n < cells.Count; n++)
            {
                var kind = MarkKind.None;
                if (marks != null)
                {
                    if (marks.AttackTargets.Contains(n)) kind = MarkKind.Attack;
                    else if (marks.MoveTargets.Contains(n)) kind = MarkKind.Move;
                }
                bool last = marks != null && (marks.LastFrom == n || marks.LastTo == n);
                bool dim = marks != null && marks.Dimmed.Contains(n);
                cells[n].SetState(kind, last, dim);
            }
            lastSize = Vector2.zero; // 次のフレームで位置を確定
        }

        public enum StampKind { Win, Lose, Both }

        /// <summary>審判の判定をハンコで見せる。</summary>
        public void ShowStamp(int node, StampKind kind)
        {
            var r = NodeRect(node);
            stamps.Add(BoardStamp.Create(fxLayer, r.center, cell, kind));
        }

        public Vector2 NodeCenter(int node) => NodeRect(node).center;

        public void ClearStamps()
        {
            foreach (var s in stamps) Destroy(s.gameObject);
            stamps.Clear();
        }

    }
}
