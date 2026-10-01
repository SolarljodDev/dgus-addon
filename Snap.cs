using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using BizDraw.Controls;
using BizDraw.Core;
using BizDraw.Objects;
using BizDraw.Tools;

namespace DgusPlus
{
    // Магнит и размерные линии при перетаскивании с зажатым Alt: рамка выделения притягивается к линиям других элементов
    // и страницы, совпавшие линии рисуются; размерные линии — расстояние до ближайшего соседа или края страницы.
    // Подмена в потоке сообщений: у WM_MOUSEMOVE меняется координата курсора, и штатный ToolPointer сам сдвигает элементы.
    static class Snap
    {
        const int ThresholdPx = 8;

        class Seg
        {
            public Point A, B;
            public string Label;
        }

        static readonly List<Seg> segs = new List<Seg>();
        static bool haveGrab;
        static Point grab;           // положение курсора относительно левого верхнего угла рамки
        static bool moveSeen;
        static Point downDoc;

        static readonly Color Line = Color.FromArgb(240, 60, 100);

        static bool AltDown { get { return (Control.ModifierKeys & Keys.Alt) != 0; } }

        public static bool Showing { get { return segs.Count > 0; } }

        public static void Reset(DrawArea da)
        {
            haveGrab = false;
            moveSeen = false;
            if (segs.Count > 0)
            {
                segs.Clear();
                if (da != null) da.Invalidate();
            }
        }

        public static void NoteDown(DrawArea da, int clientX, int clientY)
        {
            haveGrab = false;
            moveSeen = false;
            downDoc = new Point(Convert.ToInt32(clientX / da.Zoom), Convert.ToInt32(clientY / da.Zoom));
        }

        // Возвращает true, если движение обработано здесь (штатный ToolPointer его не увидит).
        public static bool OnMove(Message m, DrawArea da)
        {
            bool left = ((int)(long)m.WParam & 0x0001) != 0;
            ToolPointer tp = da.ActiveTool as ToolPointer;
            Document doc = da.Document;
            bool moving = left && tp != null && !tp.OnClick && doc != null &&
                          Convert.ToString(R.Get(tp, "selectMode")) == "Move";
            if (!moving || !AltDown)
            {
                haveGrab = false;
                if (!moving) moveSeen = false;
                else moveSeen = true;
                if (segs.Count > 0) { segs.Clear(); da.Invalidate(); }
                return false;
            }

            List<DrawRectangle> sel = Arrange.Selected(doc);
            sel.RemoveAll(delegate(DrawRectangle o) { return !Movable(doc, o); });
            if (sel.Count == 0) return false;
            Rectangle actual = Arrange.Union(sel);

            int lp = (int)(long)m.LParam;
            float z = da.Zoom;
            Point cur = new Point(Convert.ToInt32((short)(lp & 0xFFFF) / z), Convert.ToInt32((short)((lp >> 16) & 0xFFFF) / z));

            if (!haveGrab)
            {
                // До первого сдвига рамка ещё на месте: захват берём по точке нажатия, чтобы не терять первые пиксели.
                Point basePt = moveSeen ? cur : downDoc;
                grab = new Point(basePt.X - actual.X, basePt.Y - actual.Y);
                haveGrab = true;
            }
            moveSeen = true;

            Rectangle raw = new Rectangle(cur.X - grab.X, cur.Y - grab.Y, actual.Width, actual.Height);

            List<Rectangle> others = new List<Rectangle>();
            for (int i = 0; i < doc.Items.Count; i++)
            {
                DrawRectangle o = doc.Items[i] as DrawRectangle;
                if (o != null && !o.Selected) others.Add(o.Rectangle);
            }
            Rectangle page = new Rectangle(0, 0, doc.Width, doc.Height);

            int thr = Math.Max(1, (int)Math.Round(ThresholdPx / z));
            Rectangle target = new Rectangle(raw.X + BestOffset(true, raw, others, page, thr),
                                             raw.Y + BestOffset(false, raw, others, page, thr), raw.Width, raw.Height);

            foreach (DrawRectangle o in sel) o.Move(target.X - actual.X, target.Y - actual.Y);
            // lastPoint держим на курсоре: если Alt отпустят посреди перетаскивания, DGUS продолжит без скачка.
            R.Set(tp, "lastPoint", cur);
            doc.SetDirtyFlag(true);

            BuildSegs(Arrange.Union(sel), others, page);
            da.Refresh();
            return true;
        }

        // Те же условия, что в ToolPointer.OnMouseMove: заблокированные и скрытые режимом показа не двигаются.
        static bool Movable(Document doc, DrawRectangle o)
        {
            if (o.bLocked) return false;
            return o.f13Type < 100 ? doc.Showtype == 0 || doc.Showtype == 1 : doc.Showtype == 0 || doc.Showtype == 2;
        }

        static int[] Anchors(Rectangle r, bool horizontal)
        {
            return horizontal
                ? new int[] { r.Left, r.Left + r.Width / 2, r.Right }
                : new int[] { r.Top, r.Top + r.Height / 2, r.Bottom };
        }

        static int BestOffset(bool horizontal, Rectangle raw, List<Rectangle> others, Rectangle page, int thr)
        {
            int[] mine = Anchors(raw, horizontal);
            int best = 0, bestAbs = thr + 1;
            List<Rectangle> all = new List<Rectangle>(others);
            all.Add(page);
            foreach (Rectangle o in all)
            {
                foreach (int t in Anchors(o, horizontal))
                    foreach (int a in mine)
                    {
                        int d = t - a;
                        if (Math.Abs(d) < bestAbs) { bestAbs = Math.Abs(d); best = d; }
                    }
            }
            return bestAbs <= thr ? best : 0;
        }

        static void BuildSegs(Rectangle s, List<Rectangle> others, Rectangle page)
        {
            segs.Clear();

            List<Rectangle> all = new List<Rectangle>(others);
            all.Add(page);
            foreach (Rectangle o in all)
            {
                foreach (int a in Anchors(s, true))
                    foreach (int t in Anchors(o, true))
                        if (a == t)
                        {
                            AddGuide(true, a, Math.Min(s.Top, o.Top), Math.Max(s.Bottom, o.Bottom));
                        }
                foreach (int a in Anchors(s, false))
                    foreach (int t in Anchors(o, false))
                        if (a == t)
                        {
                            AddGuide(false, a, Math.Min(s.Left, o.Left), Math.Max(s.Right, o.Right));
                        }
            }

            Dim(s, others, page, -1, 0);
            Dim(s, others, page, 1, 0);
            Dim(s, others, page, 0, -1);
            Dim(s, others, page, 0, 1);
        }

        static void AddGuide(bool vertical, int coord, int from, int to)
        {
            Seg g = new Seg();
            g.A = vertical ? new Point(coord, from) : new Point(from, coord);
            g.B = vertical ? new Point(coord, to) : new Point(to, coord);
            segs.Add(g);
        }

        static void Dim(Rectangle s, List<Rectangle> others, Rectangle page, int dx, int dy)
        {
            bool horiz = dx != 0;
            int edge = horiz ? (dx < 0 ? s.Left : s.Right) : (dy < 0 ? s.Top : s.Bottom);
            int limit = horiz ? (dx < 0 ? page.Left : page.Right) : (dy < 0 ? page.Top : page.Bottom);
            int bestGap = Math.Abs(limit - edge);
            int bestPos = limit;
            Rectangle? hit = null;
            if ((dx < 0 || dy < 0) ? edge < limit : edge > limit) return;

            foreach (Rectangle o in others)
            {
                bool overlap = horiz ? (o.Top < s.Bottom && o.Bottom > s.Top) : (o.Left < s.Right && o.Right > s.Left);
                if (!overlap) continue;
                int pos = horiz ? (dx < 0 ? o.Right : o.Left) : (dy < 0 ? o.Bottom : o.Top);
                int gap = (dx < 0 || dy < 0) ? edge - pos : pos - edge;
                if (gap < 0 || gap >= bestGap) continue;
                bestGap = gap; bestPos = pos; hit = o;
            }
            if (bestGap <= 0) return;

            Seg g = new Seg();
            g.Label = bestGap.ToString();
            if (horiz)
            {
                int y = hit.HasValue
                    ? (Math.Max(s.Top, hit.Value.Top) + Math.Min(s.Bottom, hit.Value.Bottom)) / 2
                    : s.Top + s.Height / 2;
                g.A = new Point(edge, y); g.B = new Point(bestPos, y);
            }
            else
            {
                int x = hit.HasValue
                    ? (Math.Max(s.Left, hit.Value.Left) + Math.Min(s.Right, hit.Value.Right)) / 2
                    : s.Left + s.Width / 2;
                g.A = new Point(x, edge); g.B = new Point(x, bestPos);
            }
            segs.Add(g);
        }

        public static void Paint(Graphics g, DrawArea da)
        {
            if (segs.Count == 0) return;
            float z = da.Zoom;
            using (Pen p = new Pen(Line, Math.Max(1f, 1f / z)))
            using (Font f = new Font("Arial", 10f / z, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Brush bg = new SolidBrush(Line))
            {
                float tick = 4f / z;
                foreach (Seg s in segs)
                {
                    g.DrawLine(p, s.A, s.B);
                    if (s.Label == null) continue;
                    bool horiz = s.A.Y == s.B.Y;
                    if (horiz)
                    {
                        g.DrawLine(p, s.A.X, s.A.Y - tick, s.A.X, s.A.Y + tick);
                        g.DrawLine(p, s.B.X, s.B.Y - tick, s.B.X, s.B.Y + tick);
                    }
                    else
                    {
                        g.DrawLine(p, s.A.X - tick, s.A.Y, s.A.X + tick, s.A.Y);
                        g.DrawLine(p, s.B.X - tick, s.B.Y, s.B.X + tick, s.B.Y);
                    }
                    SizeF ts = g.MeasureString(s.Label, f);
                    float cx = (s.A.X + s.B.X) / 2f, cy = (s.A.Y + s.B.Y) / 2f;
                    float w = ts.Width, h = ts.Height;
                    RectangleF box = horiz
                        ? new RectangleF(cx - w / 2, cy - h - 2f / z, w, h)
                        : new RectangleF(cx + 3f / z, cy - h / 2, w, h);
                    g.FillRectangle(bg, box);
                    g.DrawString(s.Label, f, Brushes.White, box.X, box.Y);
                }
            }
        }
    }
}
