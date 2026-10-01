using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DgusPlus
{
    // Стрелки NumericUpDown рисует система светлыми даже в тёмной теме — перерисовываем.
    class SpinPainter : NativeWindow
    {
        const int WM_PAINT = 0x000F;
        readonly Control owner;

        public static void Attach(Control buttons)
        {
            SpinPainter sp = new SpinPainter(buttons);
            if (buttons.IsHandleCreated) sp.AssignHandle(buttons.Handle);
            buttons.HandleCreated += delegate { sp.AssignHandle(buttons.Handle); };
            buttons.HandleDestroyed += delegate { sp.ReleaseHandle(); };
        }

        SpinPainter(Control c) { owner = c; }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_PAINT) Plus.Guard(Paint);
        }

        void Paint()
        {
            if (owner.IsDisposed || !owner.Visible) return;
            Rectangle r = owner.ClientRectangle;
            if (r.Width <= 0 || r.Height <= 0) return;
            Point mouse = owner.PointToClient(Control.MousePosition);
            bool down = (Control.MouseButtons & MouseButtons.Left) != 0;
            int half = r.Height / 2;
            Rectangle up = new Rectangle(r.X, r.Y, r.Width, half);
            Rectangle dn = new Rectangle(r.X, r.Y + half, r.Width, r.Height - half);
            using (Graphics g = owner.CreateGraphics())
            {
                Part(g, up, true, up.Contains(mouse), down);
                Part(g, dn, false, dn.Contains(mouse), down);
                using (Pen p = new Pen(Theme.Border)) g.DrawLine(p, r.X, r.Y + half, r.Right, r.Y + half);
            }
        }

        static void Part(Graphics g, Rectangle r, bool up, bool hot, bool pressed)
        {
            Color back = hot ? (pressed ? Theme.Selection : Theme.ButtonHover) : Theme.Button;
            using (Brush b = new SolidBrush(back)) g.FillRectangle(b, r);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, w = Math.Min(3.5f, r.Width / 3f);
            PointF[] tri = up
                ? new PointF[] { new PointF(cx - w, cy + w / 2), new PointF(cx + w, cy + w / 2), new PointF(cx, cy - w / 2) }
                : new PointF[] { new PointF(cx - w, cy - w / 2), new PointF(cx + w, cy - w / 2), new PointF(cx, cy + w / 2) };
            using (Brush b = new SolidBrush(Theme.Text)) g.FillPolygon(b, tri);
            g.SmoothingMode = SmoothingMode.None;
        }
    }
}
